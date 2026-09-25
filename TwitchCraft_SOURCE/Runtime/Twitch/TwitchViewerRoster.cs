using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private void ClearRoster()
    {
        List<string> emptyViewers = [];
        lock (_viewerGate)
        {
            _knownViewers = emptyViewers;
            _viewerRewardSchedule.Clear();
        }

        _shellWindow?.UpdateViewers(emptyViewers);
    }

    private async Task RunPassiveRewardsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (CurrentSettings.PassiveTokenEarningEnabled)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                List<string>? rewarded = null;

                lock (_viewerGate)
                {
                    for (int i = 0; i < _knownViewers.Count; i++)
                    {
                        string viewer = _knownViewers[i];
                        if (string.IsNullOrWhiteSpace(viewer))
                            continue;

                        if (!IsRewardEligibleNoLock(viewer, now))
                        {
                            _viewerRewardSchedule.Remove(viewer);
                            continue;
                        }

                        if (!_viewerRewardSchedule.TryGetValue(viewer, out long nextAt))
                        {
                            _viewerRewardSchedule[viewer] = now + GetPassivePayoutDelay();
                        }
                        else if (nextAt <= now)
                        {
                            _viewerRewardSchedule[viewer] = now + GetPassivePayoutDelay();
                            (rewarded ??= []).Add(viewer);
                        }
                    }
                }

                if (rewarded is { Count: > 0 })
                    Tokens.Award(rewarded, PassiveTokensPerPayout);
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

    private async Task RunViewerRosterAsync(CancellationToken cancellationToken)
    {
        TwitchConfig? twitch = _activeConfig?.Twitch;
        if (twitch == null ||
            string.IsNullOrWhiteSpace(twitch.ClientID) ||
            string.IsNullOrWhiteSpace(twitch.BotToken) ||
            string.IsNullOrWhiteSpace(twitch.StreamerName))
        {
            ClearRoster();
            return;
        }

        try
        {
            int consecutiveFailures = 0;
            string resolvedToken = string.Empty;
            string moderatorID = string.Empty;
            string broadcasterID = string.Empty;
            TimeSpan refreshDelay = TimeSpan.FromSeconds(ViewerRosterRefreshIntervalSeconds);

            while (!cancellationToken.IsCancellationRequested)
            {
                TwitchConfig? currentTwitch = _activeConfig?.Twitch;
                if (currentTwitch == null)
                {
                    ClearRoster();
                    return;
                }

                string clientID = (currentTwitch.ClientID ?? string.Empty).Trim();
                string streamerName = (currentTwitch.StreamerName ?? string.Empty).Trim();
                string botToken = NormalizeToken(currentTwitch.BotToken);
                string bearerHeader = TwitchTokenHelper.BuildBearerHeader(botToken);
                if (clientID.Length == 0 || streamerName.Length == 0 || botToken.Length == 0)
                {
                    ClearRoster();
                    return;
                }

                try
                {
                    if (!string.Equals(resolvedToken, botToken, StringComparison.Ordinal))
                    {
                        string botName = await ResolveBotAsync(botToken, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(botName))
                        {
                            ClearRoster();
                            throw new InvalidOperationException("Unable to resolve bot login for the viewer roster.");
                        }

                        string[] userIDs = await ResolveUsersAsync(
                            botName,
                            streamerName,
                            clientID,
                            botToken,
                            cancellationToken).ConfigureAwait(false);

                        if (userIDs.Length != 2)
                        {
                            ClearRoster();
                            throw new InvalidOperationException("Viewer roster setup failed: Twitch user IDs could not be resolved for the bot/channel.");
                        }

                        moderatorID = userIDs[0];
                        broadcasterID = userIDs[1];
                        resolvedToken = botToken;
                    }

                    List<string> viewers = [];
                    string? cursor = null;
                    HashSet<string>? cursors = null;

                    do
                    {
                        string url = "https://api.twitch.tv/helix/chat/chatters?broadcaster_id=" + broadcasterID
                            + "&moderator_id=" + moderatorID
                            + "&first=100";

                        if (cursor is { Length: > 0 })
                            url += "&after=" + Uri.EscapeDataString(cursor);

                        using HttpRequestMessage request = new(HttpMethod.Get, url);
                        request.Headers.TryAddWithoutValidation("Authorization", bearerHeader);
                        request.Headers.TryAddWithoutValidation("Client-Id", clientID);

                        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            resolvedToken = string.Empty;
                            throw new HttpRequestException("Viewer roster authorization failed.", null, response.StatusCode);
                        }

                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            refreshDelay = GetRetryDelay(response, TimeSpan.FromMinutes(1));
                            throw new HttpRequestException("Viewer roster was rate limited.", null, response.StatusCode);
                        }

                        response.EnsureSuccessStatusCode();
                        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        cursor = ParseRosterPage(json, viewers);
                        if (cursor is { Length: > 0 } && (!(cursors ??= new(StringComparer.Ordinal)).Add(cursor) || cursors.Count > 1000))
                            throw new InvalidDataException("Twitch viewer pagination did not terminate safely.");
                    }
                    while (cursor is { Length: > 0 });

                    ApplyViewerRoster(viewers);

                    consecutiveFailures = 0;
                    refreshDelay = TimeSpan.FromSeconds(ViewerRosterRefreshIntervalSeconds);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    consecutiveFailures++;
                    resolvedToken = string.Empty;
                    ClearRoster();
                    if (await TryRefreshAuthAsync(botToken, cancellationToken).ConfigureAwait(false))
                    {
                        consecutiveFailures = 0;
                        refreshDelay = TimeSpan.FromSeconds(1);
                    }
                    else
                    {
                        _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Viewer roster authorization failed", ex));
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                        ClearRoster();

                    _shellWindow?.AddChatLogLine("Viewer roster rate limited; retrying in " + Math.Ceiling(refreshDelay.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s.");
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                    {
                        ClearRoster();
                    }

                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Viewer roster refresh failed", ex));
                }

                await Task.Delay(refreshDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ClearRoster();
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Viewer roster setup failed", ex));
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

    private static async Task<string[]> ResolveUsersAsync(string botName, string streamerName, string clientID, string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeToken(token);

        string url = "https://api.twitch.tv/helix/users?login=" + Uri.EscapeDataString(botName) + "&login=" + Uri.EscapeDataString(streamerName);
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildBearerHeader(normalizedToken));
        request.Headers.TryAddWithoutValidation("Client-Id", clientID);

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseUserIDs(json, botName, streamerName);
    }

    internal static string? ParseRosterPage(string json, List<string> viewers)
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
                            login = NormalizeUser(reader.Value as string);
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
                        cursor = reader.Value as string;
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

    internal static string[] ParseUserIDs(string json, string botName, string streamerName)
    {
        string botID = string.Empty;
        string broadcasterID = string.Empty;
        string normalizedBotName = NormalizeUser(botName);
        string normalizedStreamerName = NormalizeUser(streamerName);

        using StringReader textReader = new(json);
        using JsonTextReader reader = new(textReader);
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.PropertyName || !string.Equals(reader.Value as string, "data", StringComparison.Ordinal) ||
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
                        login = NormalizeUser(reader.Value as string);
                    else if (string.Equals(propertyName, "id", StringComparison.Ordinal) && reader.TokenType == JsonToken.String)
                        ID = (reader.Value as string)?.Trim() ?? string.Empty;
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

    internal void ApplyViewerRoster(List<string> viewers)
    {
        string botName = NormalizeUser(_activeConfig?.Twitch.BotName);
        string streamerName = NormalizeUser(_activeConfig?.Twitch.StreamerName);
        if (botName.Length > 0 && !string.Equals(botName, streamerName, StringComparison.OrdinalIgnoreCase))
            viewers.RemoveAll(viewer => string.Equals(NormalizeUser(viewer), botName, StringComparison.OrdinalIgnoreCase));

        SortedListHelper.SortAndDeduplicate(viewers, StringComparer.OrdinalIgnoreCase);

        List<string>? viewerList = null;
        lock (_viewerGate)
        {
            if (!SortedListHelper.EqualInOrder(_knownViewers, viewers, StringComparer.OrdinalIgnoreCase))
            {
                viewerList = viewers;
                _knownViewers = viewerList;
            }

            foreach (string viewer in _viewerRewardSchedule.Keys)
                if (!SortedListHelper.Contains(viewers, viewer, StringComparer.OrdinalIgnoreCase))
                    _viewerRewardSchedule.Remove(viewer);

            long activityCutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 120 * 60L;
            foreach ((string viewer, long lastActive) in _viewerLastChatActivity)
                if (lastActive < activityCutoff) _viewerLastChatActivity.Remove(viewer);
        }

        if (viewerList != null)
            _shellWindow?.UpdateViewers(viewerList);
    }

    public List<string> GetViewerRosterSnapshot()
    {
        lock (_viewerGate)
        {
            return [.. _knownViewers];
        }
    }
}
