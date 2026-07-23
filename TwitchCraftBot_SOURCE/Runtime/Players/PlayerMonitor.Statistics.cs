using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void QueueTrackedPlayerGamemodeRefreshForStatistics()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        lock (_playerGate)
        {
            if (!ContainsPlayer(_knownPlayers, playerName))
                return;
        }

        QueueTrackedPlayerGamemodeRefreshForStatistics(playerName);
    }

    private void QueueTrackedPlayerGamemodeRefreshForStatistics(string playerName)
    {
        if (!StatisticsEnabled ||
            !IsTrackingSurvivalPlayer(playerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                _ = await QueryPlayerProbeAsync<int?>(
                    playerName,
                    _spectatorProbeGate,
                    _pendingGameTypeRequests,
                    (waiter, ct) => SendInternalProbeCommandAsync($"data get entity {playerName} playerGameType", () => waiter.TrySetResult(default), ct),
                    t).ConfigureAwait(false);
            },
            () => Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 0),
            "Tracked player gamemode refresh failed",
            token: token);
    }

    private void QueueTrackedPlayerRespawnPositionRefreshForStatistics(string playerName)
    {
        if (!ShouldRefreshTrackedPlayerRespawnPositionForStatistics(playerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                if (await QueryPlayerRespawnPositionAsync(playerName, t).ConfigureAwait(false))
                    RecordTrackedPlayerRespawnPositionForStatistics(playerName);
            },
            () => Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 0),
            "Tracked player respawn position refresh failed",
            token: token);
    }

    private void QueueDeathScoreObjectiveInitialization()
    {
        if (!StatisticsEnabled ||
            Volatile.Read(ref _deathScoreObjectiveReady) != 0 ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _deathScoreObjectiveQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            EnsureDeathScoreObjectiveReadyAsync,
            () => Interlocked.Exchange(ref _deathScoreObjectiveQueued, 0),
            "Death score objective initialization failed",
            token: token);
    }

    private async Task<bool> EnsureDeathScoreObjectiveReadyAsync(CancellationToken cancellationToken)
    {
        if (!StatisticsEnabled)
            return false;

        if (Volatile.Read(ref _deathScoreObjectiveReady) != 0)
            return true;

        await _deathScoreObjectiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _deathScoreObjectiveReady) != 0)
                return true;

            TaskCompletionSource<bool> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool sent = await SendInternalProbeCommandsAsync(
                [
                    "scoreboard objectives add " + DeathScoreObjective + " deathCount"
                ],
                () => waiter.TrySetResult(true),
                cancellationToken).ConfigureAwait(false);

            if (!sent)
                return false;

            await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            ResetDeathScoreBaselineForStatistics();
            Volatile.Write(ref _deathScoreObjectiveReady, 1);
            return true;
        }
        finally
        {
            _deathScoreObjectiveGate.Release();
        }
    }

    private void QueueTrackedPlayerDeathScoreRefreshForStatistics()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        QueueTrackedPlayerDeathScoreRefreshForStatistics(playerName);
    }

    private void QueueTrackedPlayerDeathScoreRefreshForStatistics(string playerName)
    {
        if (!StatisticsEnabled ||
            !MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayerName) ||
            !IsTrackingSurvivalPlayer(normalizedPlayerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token))
        {
            return;
        }

        if (Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 1) != 0)
            return;

        RunQueuedSessionWork(
            async t =>
            {
                if (await EnsureDeathScoreObjectiveReadyAsync(t).ConfigureAwait(false))
                {
                    string command = "scoreboard players get " + normalizedPlayerName + " " + DeathScoreObjective;
                    if (RemoteControlEnabled)
                    {
                        string? response = await ExecuteRemoteServerQueryAsync(command, t).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(response))
                            HandleRemoteQueryResponse(response);
                    }
                    else
                    {
                        await SendServerCommandAsync(command, t).ConfigureAwait(false);
                    }
                }
            },
            () => Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 0),
            "Tracked player death score refresh failed",
            token: token);
    }

    private void RecordPlayerSidebarRefreshFailure(Exception ex)
    {
        DateTime now = DateTime.UtcNow;
        lock (_playerGate)
        {
            if (now - _lastPlayerSidebarRefreshErrorUtc < PlayerSidebarRefreshErrorLogInterval)
                return;

            _lastPlayerSidebarRefreshErrorUtc = now;
        }

        _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Player sidebar refresh failed", ex));
    }

    private void RecordPlayerRosterChanges(List<string> previousPlayers, List<string> currentPlayers)
    {
        int previousIndex = 0;
        int currentIndex = 0;

        while (previousIndex < previousPlayers.Count && currentIndex < currentPlayers.Count)
        {
            string previous = previousPlayers[previousIndex];
            string current = currentPlayers[currentIndex];
            int comparison = PlayerNameComparer.Compare(previous, current);
            if (comparison == 0)
            {
                previousIndex++;
                currentIndex++;
                continue;
            }

            if (comparison < 0)
            {
                RemoveSpectatorPlayer(previous);
                RecordPlayerLeaveForStatistics(previous);
                previousIndex++;
                continue;
            }

            RecordPlayerJoinForStatistics(current);
            currentIndex++;
        }

        for (; previousIndex < previousPlayers.Count; previousIndex++)
        {
            string previous = previousPlayers[previousIndex];
            RemoveSpectatorPlayer(previous);
            RecordPlayerLeaveForStatistics(previous);
        }

        for (; currentIndex < currentPlayers.Count; currentIndex++)
            RecordPlayerJoinForStatistics(currentPlayers[currentIndex]);
    }

    private static List<string> ParseOnlinePlayers(ReadOnlySpan<char> remainder)
    {
        List<string> players = [];
        if (remainder.IsEmpty)
            return players;

        int start = 0;
        for (int i = 0; i <= remainder.Length; i++)
        {
            if (i < remainder.Length && remainder[i] != ',')
                continue;

            if (MinecraftNameHelper.TryNormalizePlayerName(remainder[start..i], out string normalizedPlayer))
                players.Add(normalizedPlayer);

            start = i + 1;
        }

        SortedListHelper.SortAndDeduplicate(players, PlayerNameComparer);
        return players;
    }

    private static bool TryParsePlayerListResponse(string response, bool allowFallbackColon, out List<string> players)
    {
        players = [];
        ReadOnlySpan<char> text = (response ?? string.Empty).AsSpan().Trim();
        if (text.IsEmpty)
            return false;

        int marker = text.IndexOf("players online".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("player online".AsSpan(), StringComparison.OrdinalIgnoreCase);

        if (marker >= 0)
        {
            int colon = text[marker..].IndexOf(':');
            if (colon >= 0)
            {
                players = ParseOnlinePlayers(text[(marker + colon + 1)..]);
                return true;
            }

            return TryParseFirstInt(text[..marker], out int count) && count == 0;
        }

        if (!allowFallbackColon)
            return false;

        int fallbackColon = text.LastIndexOf(':');
        if (fallbackColon < 0)
            return false;

        players = ParseOnlinePlayers(text[(fallbackColon + 1)..]);
        return true;
    }

    private static bool TryParseFirstInt(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                continue;

            int start = i;
            while (i < text.Length && char.IsDigit(text[i]))
                i++;

            return int.TryParse(text[start..i], out value);
        }

        return false;
    }

    private static string ExtractPlayerEventName(string line, int markerIndex)
    {
        if (string.IsNullOrEmpty(line) || markerIndex <= 0)
            return string.Empty;

        return TrimPrefixAfterLastColon(line, markerIndex);
    }

    private static string TrimPrefixAfterLastColon(string value, int length)
    {
        string segment = TextSegmentHelper.TrimSegment(value, 0, length);
        if (segment.Length == 0)
            return string.Empty;

        int colon = segment.LastIndexOf(':');
        return colon < 0 || colon == segment.Length - 1
            ? segment
            : TextSegmentHelper.TrimSegment(segment, colon + 1, segment.Length - colon - 1);
    }

}
