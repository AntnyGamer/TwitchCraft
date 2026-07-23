using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void ClearViewerRoster()
    {
        List<string> emptyChatters = [];
        lock (_viewerGate)
        {
            _knownChatters = emptyChatters;
            _viewerRewardSchedule.Clear();
        }

        _shellWindow?.DisplayNormalizedViewerList(emptyChatters);
    }

    private async Task RunPassiveRewardLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!(_activeConfig?.Settings.PassiveTokenEarningEnabled ?? true))
            {
                try
                {
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            List<string>? rewarded = null;

            lock (_viewerGate)
            {
                for (int i = 0; i < _knownChatters.Count; i++)
                {
                    string chatter = _knownChatters[i];
                    if (string.IsNullOrWhiteSpace(chatter))
                        continue;

                    if (!_viewerRewardSchedule.TryGetValue(chatter, out long nextAt))
                    {
                        _viewerRewardSchedule[chatter] = now + Random.Shared.Next(30, 61);
                    }
                    else if (nextAt <= now)
                    {
                        _viewerRewardSchedule[chatter] = now + Random.Shared.Next(30, 61);
                        (rewarded ??= []).Add(chatter);
                    }
                }
            }

            if (rewarded is { Count: > 0 })
            {
                _tokenStore.AdjustBalances(rewarded, 1);
            }

            try
            {
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunViewerRosterLoopAsync(CancellationToken cancellationToken)
    {
        if (_activeConfig?.Twitch == null)
        {
            ClearViewerRoster();
            return;
        }

        string botToken = NormalizeTwitchToken(_activeConfig.Twitch.BotToken);
        string bearerHeader = TwitchTokenHelper.BuildBearerHeader(botToken);
        if (string.IsNullOrWhiteSpace(_activeConfig.Twitch.ClientID) ||
            string.IsNullOrWhiteSpace(botToken) ||
            string.IsNullOrWhiteSpace(_activeConfig.Twitch.StreamerName))
        {
            ClearViewerRoster();
            return;
        }

        try
        {
            int consecutiveFailures = 0;
            bool shouldResolveUserIds = true;
            string moderatorId = string.Empty;
            string broadcasterID = string.Empty;
            TimeSpan refreshDelay = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (shouldResolveUserIds)
                    {
                        string botName = await ResolveAndPersistBotNameAsync(botToken, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(botName))
                        {
                            ClearViewerRoster();
                            _shellWindow?.AddChatLogLine("Unable to resolve bot login for the viewer roster.");
                            return;
                        }

                        string[] userIDs = await ResolveTwitchUserIdsAsync(
                            botName,
                            _activeConfig.Twitch.StreamerName,
                            _activeConfig.Twitch.ClientID,
                            botToken,
                            cancellationToken).ConfigureAwait(false);

                        if (userIDs.Length != 2)
                        {
                            ClearViewerRoster();
                            _shellWindow?.AddChatLogLine("Viewer roster setup failed: Twitch user IDs could not be resolved for the bot/channel.");
                            return;
                        }

                        moderatorId = userIDs[0];
                        broadcasterID = userIDs[1];
                        shouldResolveUserIds = false;
                    }

                    List<string> viewers = [];
                    string? cursor = null;

                    do
                    {
                        string URL = "https://api.twitch.tv/helix/chat/chatters?broadcaster_id=" + broadcasterID
                            + "&moderator_id=" + moderatorId
                            + "&first=100";

                        if (!string.IsNullOrWhiteSpace(cursor))
                            URL += "&after=" + Uri.EscapeDataString(cursor);

                        using HttpRequestMessage request = new(HttpMethod.Get, URL);
                        request.Headers.TryAddWithoutValidation("Authorization", bearerHeader);
                        request.Headers.TryAddWithoutValidation("Client-Id", _activeConfig.Twitch.ClientID);

                        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            shouldResolveUserIds = true;
                            throw new HttpRequestException("Viewer roster authorization failed.", null, response.StatusCode);
                        }

                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            refreshDelay = GetRetryDelay(response, TimeSpan.FromMinutes(1));
                            throw new HttpRequestException("Viewer roster was rate limited.", null, response.StatusCode);
                        }

                        response.EnsureSuccessStatusCode();
                        string JSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        cursor = ParseViewerRosterPage(JSON, viewers);
                    }
                    while (!string.IsNullOrWhiteSpace(cursor));

                    SortedListHelper.SortAndDeduplicate(viewers, StringComparer.OrdinalIgnoreCase);

                    List<string>? viewerList = null;
                    lock (_viewerGate)
                    {
                        if (!SortedListHelper.EqualInOrder(_knownChatters, viewers, StringComparer.OrdinalIgnoreCase))
                        {
                            viewerList = viewers;
                            _knownChatters = viewerList;
                        }

                        List<string>? staleChatters = null;
                        foreach (string chatter in _viewerRewardSchedule.Keys)
                        {
                            if (!SortedListHelper.Contains(viewers, chatter, StringComparer.OrdinalIgnoreCase))
                                (staleChatters ??= []).Add(chatter);
                        }

                        if (staleChatters != null)
                        {
                            for (int i = 0; i < staleChatters.Count; i++)
                                _viewerRewardSchedule.Remove(staleChatters[i]);
                        }
                    }

                    consecutiveFailures = 0;
                    refreshDelay = TimeSpan.FromSeconds(30);
                    if (viewerList != null)
                        _shellWindow?.DisplayNormalizedViewerList(viewerList);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    consecutiveFailures++;
                    shouldResolveUserIds = true;
                    ClearViewerRoster();
                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Viewer roster authorization failed", ex));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                        ClearViewerRoster();

                    _shellWindow?.AddChatLogLine("Viewer roster rate limited; retrying in " + Math.Ceiling(refreshDelay.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s.");
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                    {
                        ClearViewerRoster();
                    }

                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Viewer roster refresh failed", ex));
                }

                await Task.Delay(refreshDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ClearViewerRoster();
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Viewer roster setup failed", ex));
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, TimeSpan fallback)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            return delta;

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                return delay;
        }

        return fallback;
    }

    private static async Task<string[]> ResolveTwitchUserIdsAsync(string botName, string streamerName, string clientID, string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeTwitchToken(token);

        string URL = "https://api.twitch.tv/helix/users?login=" + Uri.EscapeDataString(botName) + "&login=" + Uri.EscapeDataString(streamerName);
        using HttpRequestMessage request = new(HttpMethod.Get, URL);
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildBearerHeader(normalizedToken));
        request.Headers.TryAddWithoutValidation("Client-Id", clientID);

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string JSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseResolvedUserIds(JSON, botName, streamerName);
    }

    internal static string? ParseViewerRosterPage(string json, List<string> viewers)
    {
        using StringReader textReader = new(json);
        using JsonTextReader reader = new(textReader);
        string? cursor = null;

        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || reader.Value is not string propertyName)
                continue;

            if (string.Equals(propertyName, "data", StringComparison.Ordinal) && reader.Read() && reader.TokenType == JsonToken.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (reader.TokenType != JsonToken.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    string login = string.Empty;
                    while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                    {
                        if (reader.TokenType != JsonToken.PropertyName || reader.Value is not string itemProperty || !reader.Read())
                            continue;

                        if (string.Equals(itemProperty, "user_login", StringComparison.Ordinal) && reader.TokenType == JsonToken.String)
                            login = NormalizeUser(reader.Value?.ToString());
                        else if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
                            reader.Skip();
                    }

                    if (login.Length > 0)
                        viewers.Add(login);
                }
            }
            else if (string.Equals(propertyName, "pagination", StringComparison.Ordinal) && reader.Read() && reader.TokenType == JsonToken.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    if (reader.TokenType != JsonToken.PropertyName || reader.Value is not string paginationProperty || !reader.Read())
                        continue;

                    if (string.Equals(paginationProperty, "cursor", StringComparison.Ordinal) && reader.TokenType == JsonToken.String)
                        cursor = reader.Value?.ToString();
                    else if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
                        reader.Skip();
                }
            }
            else if (reader.Read() && reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
            {
                reader.Skip();
            }
        }

        return cursor;
    }

    internal static string[] ParseResolvedUserIds(string json, string botName, string streamerName)
    {
        string botID = string.Empty;
        string broadcasterID = string.Empty;
        string normalizedBotName = NormalizeUser(botName);
        string normalizedStreamerName = NormalizeUser(streamerName);

        using StringReader textReader = new(json);
        using JsonTextReader reader = new(textReader);
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || !string.Equals(reader.Value?.ToString(), "data", StringComparison.Ordinal) ||
                !reader.Read() || reader.TokenType != JsonToken.StartArray)
            {
                continue;
            }

            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                if (reader.TokenType != JsonToken.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                string login = string.Empty;
                string ID = string.Empty;
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    if (reader.TokenType != JsonToken.PropertyName || reader.Value is not string propertyName || !reader.Read())
                        continue;

                    if (string.Equals(propertyName, "login", StringComparison.Ordinal) && reader.TokenType == JsonToken.String)
                        login = NormalizeUser(reader.Value?.ToString());
                    else if (string.Equals(propertyName, "id", StringComparison.Ordinal) && reader.TokenType == JsonToken.String)
                        ID = reader.Value?.ToString()?.Trim() ?? string.Empty;
                    else if (reader.TokenType is JsonToken.StartArray or JsonToken.StartObject)
                        reader.Skip();
                }

                if (string.Equals(login, normalizedBotName, StringComparison.OrdinalIgnoreCase))
                    botID = ID;
                if (string.Equals(login, normalizedStreamerName, StringComparison.OrdinalIgnoreCase))
                    broadcasterID = ID;
            }

            break;
        }

        return botID.Length == 0 || broadcasterID.Length == 0 ? [] : [botID, broadcasterID];
    }

    public List<string> GetKnownChattersSnapshot()
    {
        lock (_viewerGate)
        {
            return [.. _knownChatters];
        }
    }

}
