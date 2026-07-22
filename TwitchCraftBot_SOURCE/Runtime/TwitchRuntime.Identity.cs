using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
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
        }

        if (resolvedBotName.Length == 0)
        {
            return resolvedBotName;
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
        JObject payload = JObject.Parse(JSON);
        return CommandUserHelper.NormalizeUsername(payload["login"]?.ToString());
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
