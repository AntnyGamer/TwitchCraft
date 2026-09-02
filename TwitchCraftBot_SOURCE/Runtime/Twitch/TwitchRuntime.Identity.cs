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
    private async Task<BotConfig> EnsureAuthAsync(
        BotConfig config,
        CancellationToken cancellationToken)
    {
        if (TwitchOAuthAuthorizer.TwitchCraftOAuthConfigured &&
            !string.Equals(config.Twitch.ClientID, TwitchOAuthAuthorizer.TwitchCraftClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The saved Twitch authorization belongs to a different Twitch application. " +
                "Open Settings → Dangerous → Reauthorize Twitch, then start TwitchCraft again.");
        }

        string token = NormalizeToken(config.Twitch.BotToken);
        if (token.Length == 0)
            return config;

        _dataMaintenance.MarkTwitchValidated();
        try
        {
            string login = await ValidateBotAsync(token, cancellationToken).ConfigureAwait(false);
            if (login.Length > 0)
                config.Twitch.BotName = login;
            return config;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            TwitchOAuthResult refreshed = await TwitchOAuthAuthorizer.RefreshAsync(
                config.Twitch.ClientID,
                config.Twitch.RefreshToken,
                cancellationToken).ConfigureAwait(false);

            if (!refreshed.IsSuccess && TwitchOAuthAuthorizer.IsClientSecretFailure(refreshed.Error))
            {
                throw new InvalidOperationException(
                    "Twitch authorization expired, but Twitch rejected a secretless refresh. " +
                    "TwitchCraft's Twitch Developer application must be set to Client Type: Public. " + refreshed.Error, ex);
            }

            if (!refreshed.IsSuccess && TwitchOAuthAuthorizer.ShouldUseDeviceAuth(refreshed.Error))
            {
                throw new InvalidOperationException(
                    "Twitch authorization needs to be renewed. Open Settings → Dangerous → Reauthorize Twitch, then start TwitchCraft again. " +
                    refreshed.Error,
                    ex);
            }

            if (!refreshed.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Twitch authorization expired and could not be renewed. Open Settings → Dangerous → Authorize Twitch. " +
                    refreshed.Error,
                    ex);
            }

            config.Twitch = SaveAuth(config.Twitch.ClientID, token, refreshed);
            return config;
        }
    }

    private async Task<bool> TryRefreshAuthAsync(
        string rejectedToken,
        CancellationToken cancellationToken)
    {
        string normalizedRejectedToken = NormalizeToken(rejectedToken);
        await _twitchTokenRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TwitchConfig? twitch = _activeConfig?.Twitch;
            if (twitch == null)
                return false;

            string currentToken = NormalizeToken(twitch.BotToken);
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

            SaveAuth(twitch.ClientID, normalizedRejectedToken, refreshed);
            _shellWindow?.AddChatLogLine("Twitch authorization renewed automatically.");
            return true;
        }
        finally
        {
            _twitchTokenRefreshGate.Release();
        }
    }

    private TwitchConfig SaveAuth(string clientId, string expectedToken, TwitchOAuthResult refreshed)
    {
        lock (_configPersistenceGate)
        {
            BotConfig persisted = ConfigurationStore.Update(config =>
            {
                if (!string.Equals(config.Twitch.ClientID, clientId, StringComparison.Ordinal) || !string.Equals(NormalizeToken(config.Twitch.BotToken), expectedToken, StringComparison.Ordinal))
                    return;

                config.Twitch.BotToken = refreshed.Token;
                config.Twitch.RefreshToken = refreshed.RefreshToken;
                config.Twitch.BotName = refreshed.Login;
            });

            if (_activeConfig != null &&
                string.Equals(_activeConfig.Twitch.ClientID, clientId, StringComparison.Ordinal) && string.Equals(NormalizeToken(_activeConfig.Twitch.BotToken), expectedToken, StringComparison.Ordinal) && string.Equals(NormalizeToken(persisted.Twitch.BotToken), NormalizeToken(refreshed.Token), StringComparison.Ordinal))
            {
                BotConfig active = CloneConfig(_activeConfig);
                active.Twitch.BotToken = refreshed.Token;
                active.Twitch.RefreshToken = refreshed.RefreshToken;
                active.Twitch.BotName = refreshed.Login;
                SetConfig(active);
            }
            return persisted.Twitch;
        }
    }

    private async Task<string> ResolveBotAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeToken(token);
        TwitchConfig? twitch = _activeConfig?.Twitch;
        string resolvedBotName = string.Equals(NormalizeToken(twitch?.BotToken), normalizedToken, StringComparison.Ordinal)
            ? NormalizeUser(twitch?.BotName)
            : string.Empty;

        if (resolvedBotName.Length == 0)
        {
            await _botIdentityResolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                twitch = _activeConfig?.Twitch;
                resolvedBotName = string.Equals(NormalizeToken(twitch?.BotToken), normalizedToken, StringComparison.Ordinal)
                    ? NormalizeUser(twitch?.BotName)
                    : await ValidateBotAsync(normalizedToken, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _botIdentityResolveGate.Release();
            }

            if (resolvedBotName.Length == 0)
                return string.Empty;
        }

        twitch = _activeConfig?.Twitch;
        if (!string.Equals(NormalizeUser(twitch?.BotName), resolvedBotName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(NormalizeToken(twitch?.BotToken), normalizedToken, StringComparison.Ordinal))
            SaveBot(normalizedToken, resolvedBotName);

        return resolvedBotName;
    }

    private void SaveBot(string normalizedToken, string resolvedBotName)
    {
        try
        {
            lock (_configPersistenceGate)
            {
                BotConfig config = ConfigurationStore.Update(configToUpdate =>
                {
                    configToUpdate.Twitch ??= new BotSetup.TwitchConfig();

                    if (!string.Equals(NormalizeToken(configToUpdate.Twitch.BotToken), normalizedToken, StringComparison.Ordinal)
                        || string.Equals(NormalizeUser(configToUpdate.Twitch.BotName), resolvedBotName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    configToUpdate.Twitch.BotName = resolvedBotName;
                });

                if (_activeConfig != null)
                {
                    BotConfig activeConfig = CloneConfig(_activeConfig);
                    activeConfig.Twitch = config.Twitch;
                    SetConfig(activeConfig);
                }
                else
                {
                    SetConfig(config);
                }
            }
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Failed to save bot token identity", ex));
        }
    }

    private static async Task<string> ValidateBotAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeToken(token);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildValidateHeader(normalizedToken));

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string JSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseLogin(JSON);
    }

    internal static string ParseLogin(string json)
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
                ? CommandUserHelper.NormalizeUser(reader.Value as string)
                : string.Empty;
        }

        return string.Empty;
    }

    private void CloseIrcSocket(TcpClient? socketToClose = null)
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

}
