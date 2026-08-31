using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void QueueGamemode()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        lock (_playerGate)
        {
            if (!HasPlayer(_knownPlayers, playerName))
                return;
        }

        QueueGamemode(playerName);
    }

    private void QueueGamemode(string playerName)
    {
        if (!StatisticsEnabled ||
            !ShouldTrackPlayer(playerName) ||
            !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 1) != 0)
        {
            return;
        }

        RunSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                _ = await QueryPlayerAsync<int?>(
                    playerName,
                    _spectatorProbeGate,
                    _pendingGameTypeRequests,
                    (complete, ct) => SendProbeAsync($"data get entity {playerName} playerGameType", complete, ct),
                    t).ConfigureAwait(false);
            },
            () => Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 0),
            "Tracked player gamemode refresh failed",
            token: token);
    }

    private void QueueRespawn(string playerName)
    {
        if (!NeedsRespawnRefresh(playerName) ||
            !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 1) != 0)
        {
            return;
        }

        RunSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                if (await QueryRespawnAsync(playerName, t).ConfigureAwait(false))
                    RecordRespawn(playerName);
            },
            () => Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 0),
            "Tracked player respawn position refresh failed",
            token: token);
    }

    private void QueueDeathSetup()
    {
        if (!StatisticsEnabled ||
            Volatile.Read(ref _deathScoreObjectiveReady) != 0 ||
            !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _deathScoreObjectiveQueued, 1) != 0)
        {
            return;
        }

        RunSessionWork(
            EnsureDeathScoreAsync,
            () => Interlocked.Exchange(ref _deathScoreObjectiveQueued, 0),
            "Death score objective initialization failed",
            token: token);
    }

    private async Task<bool> EnsureDeathScoreAsync(CancellationToken cancellationToken)
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
            bool sent = await SendProbesAsync(
                [
                    "scoreboard objectives add " + DeathScoreObjective + " deathCount"
                ],
                () => waiter.TrySetResult(true),
                cancellationToken).ConfigureAwait(false);

            if (!sent)
                return false;

            await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            ResetDeathBaseline();
            Volatile.Write(ref _deathScoreObjectiveReady, 1);
            return true;
        }
        finally
        {
            _deathScoreObjectiveGate.Release();
        }
    }

    private void QueueDeathScore()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        QueueDeathScore(playerName);
    }

    private void QueueDeathScore(string playerName)
    {
        if (!StatisticsEnabled ||
            !MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayerName) ||
            !ShouldTrackPlayer(normalizedPlayerName) ||
            !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token))
        {
            return;
        }

        if (Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 1) != 0)
            return;

        RunSessionWork(
            async t =>
            {
                if (await EnsureDeathScoreAsync(t).ConfigureAwait(false))
                {
                    string command = "scoreboard players get " + normalizedPlayerName + " " + DeathScoreObjective;
                    if (RemoteControlEnabled)
                    {
                        string? response = await ExecuteRconQueryAsync(command, t).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(response))
                            HandleRconResponse(response);
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

    private void LogSidebarError(Exception ex)
    {
        DateTime now = DateTime.UtcNow;
        lock (_playerGate)
        {
            if (now - _lastPlayerSidebarRefreshErrorUtc < PlayerSidebarRefreshErrorLogInterval)
                return;

            _lastPlayerSidebarRefreshErrorUtc = now;
        }

        _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Player sidebar refresh failed", ex));
    }

    private void RecordRoster(List<string> previousPlayers, List<string> currentPlayers)
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
                RemoveSpectator(previous);
                RecordPlayerLeave(previous);
                previousIndex++;
                continue;
            }

            RecordPlayerJoin(current);
            currentIndex++;
        }

        for (; previousIndex < previousPlayers.Count; previousIndex++)
        {
            string previous = previousPlayers[previousIndex];
            RemoveSpectator(previous);
            RecordPlayerLeave(previous);
        }

        for (; currentIndex < currentPlayers.Count; currentIndex++)
            RecordPlayerJoin(currentPlayers[currentIndex]);
    }

    private static List<string> ParsePlayers(ReadOnlySpan<char> remainder)
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

    private static bool TryParseList(string response, bool allowFallbackColon, out List<string> players)
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
                players = ParsePlayers(text[(marker + colon + 1)..]);
                return true;
            }

            return TryParseInt(text[..marker], out int count) && count == 0;
        }

        if (!allowFallbackColon)
            return false;

        int fallbackColon = text.LastIndexOf(':');
        if (fallbackColon < 0)
            return false;

        players = ParsePlayers(text[(fallbackColon + 1)..]);
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> text, out int value)
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

    private static string GetPlayerName(string line, int markerIndex)
    {
        if (string.IsNullOrEmpty(line) || markerIndex <= 0)
            return string.Empty;

        return AfterLastColon(line, markerIndex);
    }

    private static string AfterLastColon(string value, int length)
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
