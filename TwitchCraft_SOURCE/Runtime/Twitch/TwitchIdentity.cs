using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private async Task<TwitchCraftConfig> EnsureAuthAsync(
        TwitchCraftConfig config,
        CancellationToken cancellationToken)
    {
        if (TwitchOAuthAuthorizer.IsOAuthConfigured &&
            !string.Equals(config.Twitch.ClientID, TwitchOAuthAuthorizer.ApplicationClientID, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The saved Twitch authorization belongs to a different Twitch application. " +
                "Open Settings --> Dangerous --> Reauthorize Twitch, then start TwitchCraft again.");
        }

        string token = NormalizeToken(config.Twitch.BotToken);
        if (token.Length == 0)
            return config;

        _dataMaintenance.MarkTwitchValidated();
        try
        {
            string login = await ValidateBotAsync(token, config.Twitch.ClientID, cancellationToken).ConfigureAwait(false);
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

            if (!refreshed.IsSuccess && refreshed.RefreshToken.Length > 0) SaveAuth(config.Twitch.ClientID, token, refreshed);

            if (!refreshed.IsSuccess && TwitchOAuthAuthorizer.IsClientSecretFailure(refreshed.Error))
            {
                throw new InvalidOperationException(
                    "Twitch authorization expired, but Twitch rejected a secretless refresh. " +
                    "TwitchCraft's Twitch Developer application must be set to Client Type: Public. " + refreshed.Error, ex);
            }

            if (!refreshed.IsSuccess && TwitchOAuthAuthorizer.ShouldUseDeviceAuth(refreshed.Error))
            {
                throw new InvalidOperationException(
                    "Twitch authorization needs to be renewed. Open Settings --> Dangerous --> Reauthorize Twitch, then start TwitchCraft again. " +
                    refreshed.Error,
                    ex);
            }

            if (!refreshed.IsSuccess)
            {
                throw new InvalidOperationException(
                    "Twitch authorization expired and could not be renewed. Open Settings --> Dangerous --> Authorize Twitch. " +
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
                if (refreshed.RefreshToken.Length > 0) SaveAuth(twitch.ClientID, normalizedRejectedToken, refreshed);
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

    private TwitchConfig SaveAuth(string clientID, string expectedToken, TwitchOAuthResult refreshed)
    {
        lock (_configPersistenceGate)
        {
            TwitchCraftConfig persisted = ConfigurationStore.Update(config =>
            {
                if (!string.Equals(config.Twitch.ClientID, clientID, StringComparison.Ordinal) || !string.Equals(NormalizeToken(config.Twitch.BotToken), expectedToken, StringComparison.Ordinal))
                    return;

                if (refreshed.RefreshToken.Length > 0) config.Twitch.RefreshToken = refreshed.RefreshToken;
                if (refreshed.IsSuccess)
                {
                    config.Twitch.BotToken = refreshed.Token;
                    config.Twitch.BotName = refreshed.Login;
                }
            });

            if (_activeConfig != null &&
                string.Equals(_activeConfig.Twitch.ClientID, clientID, StringComparison.Ordinal) &&
                string.Equals(NormalizeToken(_activeConfig.Twitch.BotToken), expectedToken, StringComparison.Ordinal) &&
                string.Equals(NormalizeToken(persisted.Twitch.BotToken), refreshed.IsSuccess ? NormalizeToken(refreshed.Token) : expectedToken, StringComparison.Ordinal))
            {
                TwitchCraftConfig active = CloneConfig(_activeConfig);
                active.Twitch.RefreshToken = persisted.Twitch.RefreshToken;
                if (refreshed.IsSuccess)
                {
                    active.Twitch.BotToken = refreshed.Token;
                    active.Twitch.BotName = refreshed.Login;
                }
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
                resolvedBotName = string.Equals(NormalizeToken(twitch?.BotToken), normalizedToken, StringComparison.Ordinal) && NormalizeUser(twitch?.BotName) is { Length: > 0 } cachedBotName
                    ? cachedBotName
                    : await ValidateBotAsync(normalizedToken, twitch?.ClientID ?? string.Empty, cancellationToken).ConfigureAwait(false);
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
                TwitchCraftConfig config = ConfigurationStore.Update(configToUpdate =>
                {
                    configToUpdate.Twitch ??= new Setup.TwitchConfig();

                    if (!string.Equals(NormalizeToken(configToUpdate.Twitch.BotToken), normalizedToken, StringComparison.Ordinal)
                        || string.Equals(NormalizeUser(configToUpdate.Twitch.BotName), resolvedBotName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    configToUpdate.Twitch.BotName = resolvedBotName;
                });

                if (_activeConfig != null)
                {
                    TwitchCraftConfig activeConfig = CloneConfig(_activeConfig);
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

    private static async Task<string> ValidateBotAsync(string token, string clientID, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeToken(token);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildValidateHeader(normalizedToken));

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!TwitchOAuthAuthorizer.TryReadIdentity(document.RootElement, clientID, out string login, out string error))
            throw new InvalidOperationException(error);
        return login;
    }

    private void CloseIRCSocket(TcpClient? socketToClose = null)
        => _twitchSession.CloseSocket(socketToClose);
}
