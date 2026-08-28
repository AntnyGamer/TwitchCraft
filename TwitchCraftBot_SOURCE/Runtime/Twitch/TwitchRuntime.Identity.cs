using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task<BotConfig> EnsureTwitchAuthorizationReadyAsync(
        BotConfig config,
        CancellationToken cancellationToken)
    {
        string token = NormalizeTwitchToken(config.Twitch.BotToken);
        if (token.Length == 0)
            return config;

        try
        {
            string login = await GetValidatedBotNameAsync(token, cancellationToken).ConfigureAwait(false);
            if (login.Length > 0)
                config.Twitch.BotName = login;
            return config;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            TwitchOAuthResult refreshed = await TwitchOAuthAuthorizer.RefreshAsync(
                config.Twitch.ClientID,
                config.Twitch.RefreshToken,
                cancellationToken).ConfigureAwait(false);
            if (!refreshed.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Twitch authorization expired and could not be renewed. Open Settings → Dangerous → Authorize Twitch. " +
                    refreshed.Error,
                    ex);
            }

            config.Twitch.BotToken = refreshed.Token;
            config.Twitch.RefreshToken = refreshed.RefreshToken;
            config.Twitch.BotName = refreshed.Login;
            PersistRenewedTwitchAuthorization(config.Twitch.ClientID, refreshed);
            return config;
        }
    }

    private async Task<bool> TryRefreshTwitchCredentialsAsync(
        string rejectedToken,
        CancellationToken cancellationToken)
    {
        string normalizedRejectedToken = NormalizeTwitchToken(rejectedToken);
        await _twitchTokenRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TwitchConfig? twitch = _activeConfig?.Twitch;
            if (twitch == null)
                return false;

            string currentToken = NormalizeTwitchToken(twitch.BotToken);
            if (currentToken.Length > 0 &&
                !string.Equals(currentToken, normalizedRejectedToken, StringComparison.Ordinal))
            {
                return true;
            }

            TwitchOAuthResult refreshed = await TwitchOAuthAuthorizer.RefreshAsync(
                twitch.ClientID,
                twitch.RefreshToken,
                cancellationToken).ConfigureAwait(false);
            if (!refreshed.IsSuccess)
            {
                _shellWindow?.AddChatLogLine("Twitch authorization could not be renewed automatically: " + refreshed.Error);
                return false;
            }

            PersistRenewedTwitchAuthorization(twitch.ClientID, refreshed);
            _shellWindow?.AddChatLogLine("Twitch authorization renewed automatically.");
            return true;
        }
        finally
        {
            _twitchTokenRefreshGate.Release();
        }
    }

    private void PersistRenewedTwitchAuthorization(string clientId, TwitchOAuthResult refreshed)
    {
        lock (_configPersistenceGate)
        {
            ConfigurationStore.Update(config =>
            {
                if (!string.Equals(config.Twitch.ClientID, clientId, StringComparison.Ordinal))
                    return;

                config.Twitch.BotToken = refreshed.Token;
                config.Twitch.RefreshToken = refreshed.RefreshToken;
                config.Twitch.BotName = refreshed.Login;
            });

            if (_activeConfig != null &&
                string.Equals(_activeConfig.Twitch.ClientID, clientId, StringComparison.Ordinal))
            {
                BotConfig active = CloneConfig(_activeConfig);
                active.Twitch.BotToken = refreshed.Token;
                active.Twitch.RefreshToken = refreshed.RefreshToken;
                active.Twitch.BotName = refreshed.Login;
                SetActiveConfig(active);
            }
        }
    }

    private async Task<string> ResolveAndPersistBotNameAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeTwitchToken(token);
        string resolvedBotName;

        lock (_botIdentityCacheGate)
        {
            resolvedBotName = string.Equals(_cachedBotToken, normalizedToken, StringComparison.Ordinal)
                ? _cachedBotName
                : string.Empty;
        }

        if (resolvedBotName.Length == 0)
        {
            await _botIdentityResolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_botIdentityCacheGate)
                {
                    resolvedBotName = string.Equals(_cachedBotToken, normalizedToken, StringComparison.Ordinal)
                        ? _cachedBotName
                        : string.Empty;
                }

                if (resolvedBotName.Length == 0)
                {
                    resolvedBotName = await GetValidatedBotNameAsync(normalizedToken, cancellationToken).ConfigureAwait(false);

                    if (resolvedBotName.Length > 0)
                    {
                        lock (_botIdentityCacheGate)
                        {
                            _cachedBotToken = normalizedToken;
                            _cachedBotName = resolvedBotName;
                        }
                    }
                }
            }
            finally
            {
                _botIdentityResolveGate.Release();
            }

            if (resolvedBotName.Length == 0)
                return string.Empty;
        }

        bool configAlreadyCurrent = _activeConfig?.Twitch != null
            && string.Equals(_currentBotName, resolvedBotName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeTwitchToken(_activeConfig.Twitch.BotToken), normalizedToken, StringComparison.Ordinal);

        if (!configAlreadyCurrent)
        {
            PersistResolvedBotIdentity(normalizedToken, resolvedBotName);
        }

        return resolvedBotName;
    }

    private void PersistResolvedBotIdentity(string normalizedToken, string resolvedBotName)
    {
        try
        {
            lock (_configPersistenceGate)
            {
                BotConfig config = ConfigurationStore.Update(configToUpdate =>
                {
                    configToUpdate.Twitch ??= new BotSetup.TwitchConfig();

                    if (string.Equals(NormalizeUser(configToUpdate.Twitch.BotName), resolvedBotName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(NormalizeTwitchToken(configToUpdate.Twitch.BotToken), normalizedToken, StringComparison.Ordinal))
                    {
                        return;
                    }

                    configToUpdate.Twitch.BotName = resolvedBotName;
                    configToUpdate.Twitch.BotToken = normalizedToken;
                });

                if (_activeConfig != null)
                {
                    BotConfig activeConfig = CloneConfig(_activeConfig);
                    activeConfig.Twitch.ClientID = config.Twitch.ClientID;
                    activeConfig.Twitch.BotName = config.Twitch.BotName;
                    activeConfig.Twitch.BotToken = config.Twitch.BotToken;
                    activeConfig.Twitch.StreamerName = config.Twitch.StreamerName;
                    SetActiveConfig(activeConfig);
                }
                else
                {
                    SetActiveConfig(config);
                }
            }
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Failed to save bot token identity", ex));
        }
    }

    private static async Task<string> GetValidatedBotNameAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeTwitchToken(token);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildValidateHeader(normalizedToken));

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string JSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseValidatedBotLogin(JSON);
    }

    internal static string ParseValidatedBotLogin(string json)
    {
        using StringReader textReader = new(json);
        using JsonTextReader reader = new(textReader);

        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName
                || !string.Equals(reader.Value as string, "login", StringComparison.Ordinal)
                || !reader.Read())
            {
                continue;
            }

            return reader.TokenType == JsonToken.String
                ? CommandUserHelper.NormalizeUsername(reader.Value as string)
                : string.Empty;
        }

        return string.Empty;
    }

    private void SafeCloseIRCSocket(TcpClient? socketToClose = null)
    {
        TcpClient? socket = socketToClose ?? _IRCSocket;
        if (socket == null)
            return;

        if (ReferenceEquals(socket, _IRCSocket))
            _IRCSocket = null;

        try
        {
            socket.Dispose();
        }
        catch
        {
        }
    }

    // ===== Twitch chat output =====

}
