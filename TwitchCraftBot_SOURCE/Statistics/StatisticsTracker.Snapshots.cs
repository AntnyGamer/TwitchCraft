using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    public BotStatisticsSnapshot GetStatsSnapshot(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLoaded();

        DateTime now = DateTime.UtcNow;
        string streamerViewer = _currentStreamerName;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long versionToRefresh;
            string sessionMostUsedCommand;
            string sessionMostDangerousViewer;
            string sessionNicestViewer;
            string totalMostUsedCommand;

            lock (_statisticsGate)
            {
                if (_cachedStatisticsLeaderboardVersion == _statisticsLeaderboardVersion &&
                    string.Equals(_cachedStatisticsLeaderboardStreamer, streamerViewer, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildSnapshotNoLock(now);
                }

                versionToRefresh = _statisticsLeaderboardVersion;
                sessionMostUsedCommand = GetTopCommand(_sessionStatistics.CommandUseCounts);
                sessionMostDangerousViewer = GetTopViewer(_sessionStatistics.DangerousViewerScores, streamerViewer);
                sessionNicestViewer = GetTopViewer(_sessionStatistics.NiceViewerScores, streamerViewer);
                totalMostUsedCommand = GetTopCommand(_totalStatistics.CommandUseCounts);
            }

            (string totalMostDangerousViewer, string totalNicestViewer) = BotStatisticsStore.GetTopViewers(streamerViewer);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_statisticsGate)
            {
                if (versionToRefresh != _statisticsLeaderboardVersion)
                {
                    continue;
                }

                _cachedSessionMostUsedCommand = sessionMostUsedCommand;
                _cachedSessionMostDangerousViewer = sessionMostDangerousViewer;
                _cachedSessionNicestViewer = sessionNicestViewer;
                _cachedTotalMostUsedCommand = totalMostUsedCommand;
                _cachedTotalMostDangerousViewer = totalMostDangerousViewer;
                _cachedTotalNicestViewer = totalNicestViewer;
                _cachedStatisticsLeaderboardStreamer = streamerViewer;
                _cachedStatisticsLeaderboardVersion = versionToRefresh;
                return BuildSnapshotNoLock(now);
            }
        }
    }

    internal static void FlushForShutdown()
    {
        BotStatisticsStore.TryExportJson();
    }

    private BotStatisticsSnapshot BuildSnapshotNoLock(DateTime now)
    {
        return new BotStatisticsSnapshot
        {
            StatisticsEnabled = StatisticsEnabled,
            SessionGameCommandsRun = _sessionStatistics.GameCommandsRun,
            SessionDangerousCommandsRun = GetCommandCount(_sessionStatistics.CommandUseCounts, ChatCommandStatisticFlags.Dangerous),
            SessionNiceCommandsRun = GetCommandCount(_sessionStatistics.CommandUseCounts, ChatCommandStatisticFlags.Nice),
            SessionMostUsedCommand = _cachedSessionMostUsedCommand,
            SessionTokensSpent = _sessionStatistics.TokensSpent,
            SessionEffectsGiven = _sessionStatistics.EffectsGiven,
            SessionMostDangerousViewer = _cachedSessionMostDangerousViewer,
            SessionNicestViewer = _cachedSessionNicestViewer,
            SessionTimeSurvived = GetSessionSurvival(_sessionStatistics, now),
            SessionDeaths = _sessionStatistics.Deaths,

            TotalGameCommandsRun = _totalStatistics.GameCommandsRun,
            TotalMostUsedCommand = _cachedTotalMostUsedCommand,
            TotalTokensSpent = _totalStatistics.TokensSpent,
            TotalEffectsGiven = _totalStatistics.EffectsGiven,
            TotalMostDangerousViewer = _cachedTotalMostDangerousViewer,
            TotalNicestViewer = _cachedTotalNicestViewer,
            TotalDeaths = _totalStatistics.Deaths,
            LongestTimeSurvived = ToDuration(_totalStatistics.LongestSurvivalSeconds),
            ShortestTimeSurvived = ToDuration(_totalStatistics.ShortestSurvivalSeconds),
            SessionsStarted = _totalStatistics.SessionsStarted
        };
    }

    private long GetCommandCount(Dictionary<string, long> commandUseCounts, ChatCommandStatisticFlags flag)
    {
        long count = 0;
        foreach (KeyValuePair<string, long> pair in commandUseCounts)
        {
            if (pair.Value > 0 && (_commandRegistry.GetStatisticFlags(pair.Key) & flag) != 0)
                count += pair.Value;
        }

        return count;
    }

    private void MarkLeaderboardDirty()
    {
        _statisticsLeaderboardVersion++;
    }

    private static TimeSpan? ToDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return null;
        }

        double clampedSeconds = Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    private void PauseLifeNoLock(DateTime now)
    {
        if (_sessionStatistics.CurrentLifeStartedUtc is not DateTime startedUtc)
        {
            return;
        }

        _sessionStatistics.CurrentLifeAccumulatedSeconds += GetSurvivalSeconds(startedUtc, now);
        _sessionStatistics.CurrentLifeStartedUtc = null;
    }

    private static TimeSpan? GetSessionSurvival(BotSessionStatistics session, DateTime now)
    {
        long seconds = GetLifeSeconds(session, now);
        bool hasStartedLife = session.CurrentLifeHasStarted || session.CurrentLifeStartedUtc != null || session.CurrentLifeAccumulatedSeconds > 0;
        return hasStartedLife ? ToDuration(seconds) ?? TimeSpan.Zero : null;
    }

    private static long GetLifeSeconds(BotSessionStatistics session, DateTime now)
    {
        long seconds = Math.Max(0, session.CurrentLifeAccumulatedSeconds);
        if (session.CurrentLifeStartedUtc is DateTime startedUtc)
        {
            seconds += GetSurvivalSeconds(startedUtc, now);
        }

        return seconds;
    }

    private static long GetSurvivalSeconds(DateTime startedUtc, DateTime nowUtc)
    {
        if (nowUtc <= startedUtc)
        {
            return 0;
        }

        return (long)Math.Floor((nowUtc - startedUtc).TotalSeconds);
    }

    private static int ClampDeathScore(long deathScore)
        => (int)Math.Min(int.MaxValue, Math.Max(0L, deathScore));

    private bool ShouldTrackPlayer(string playerName)
    {
        return _currentStreamerMinecraftName.Length > 0
            && MinecraftNameHelper.TryNormalizePlayerName(playerName, out string player)
            && string.Equals(player, _currentStreamerMinecraftName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTopCommand(Dictionary<string, long> commandUseCounts)
    {
        if (commandUseCounts == null || commandUseCounts.Count == 0)
        {
            return string.Empty;
        }

        string bestCommand = string.Empty;
        long bestCount = 0;
        foreach (KeyValuePair<string, long> pair in commandUseCounts)
        {
            if (pair.Value <= 0 || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            string command = StatisticNameHelper.CleanCommandName(pair.Key);
            if (command.Length == 0)
            {
                continue;
            }

            if (pair.Value > bestCount ||
                (pair.Value == bestCount && string.Compare(command, bestCommand, StringComparison.OrdinalIgnoreCase) < 0))
            {
                bestCount = pair.Value;
                bestCommand = command;
            }
        }

        return bestCommand.Length == 0 ? string.Empty : "!" + bestCommand;
    }

    private static void AddScore(Dictionary<string, long> scores, string viewer, long amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(viewer))
        {
            return;
        }

        scores[viewer] = scores.TryGetValue(viewer, out long current)
            ? current + amount
            : amount;
    }

    private static string GetTopViewer(Dictionary<string, long> scores, string excludedViewer)
    {
        if (scores == null || scores.Count == 0)
        {
            return string.Empty;
        }

        string excluded = CommandUserHelper.NormalizeUser(excludedViewer);
        string bestViewer = string.Empty;
        long bestScore = 0;

        foreach (KeyValuePair<string, long> pair in scores)
        {
            if (pair.Value <= 0 || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            string viewer = pair.Key;
            if (excluded.Length > 0 && string.Equals(viewer, excluded, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value > bestScore ||
                (pair.Value == bestScore && string.Compare(viewer, bestViewer, StringComparison.OrdinalIgnoreCase) < 0))
            {
                bestScore = pair.Value;
                bestViewer = viewer;
            }
        }

        return bestViewer;
    }

    private static bool TryExtractDeathScore(string message, out string playerName, out int deathScore)
    {
        playerName = string.Empty;
        deathScore = 0;
        if (!message.Contains("tc_deaths", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string hasMarker = " has ";
        int hasIndex = message.IndexOf(hasMarker, StringComparison.OrdinalIgnoreCase);
        int objectiveIndex = message.IndexOf("[tc_deaths]", StringComparison.OrdinalIgnoreCase);
        if (hasIndex > 0 && objectiveIndex > hasIndex)
        {
            string candidate = message[..hasIndex].Trim();
            string scoreText = message[(hasIndex + hasMarker.Length)..objectiveIndex].Trim();
            if (MinecraftNameHelper.IsValidPlayerName(candidate) &&
                int.TryParse(scoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out deathScore))
            {
                playerName = candidate;
                return true;
            }
        }

        const string missingPrefix = "Can't get value of ";
        if (message.StartsWith(missingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int objectiveMarker = message.IndexOf(" for objective tc_deaths", missingPrefix.Length, StringComparison.OrdinalIgnoreCase);
            if (objectiveMarker > missingPrefix.Length)
            {
                string candidate = message[missingPrefix.Length..objectiveMarker].Trim();
                if (MinecraftNameHelper.IsValidPlayerName(candidate))
                {
                    playerName = candidate;
                    deathScore = 0;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryExtractPlayer(string message, out string playerName)
    {
        playerName = string.Empty;
        if (string.IsNullOrEmpty(message) || message[0] == '<')
            return false;

        int firstSpace = message.IndexOf(' ');
        if (firstSpace <= 0)
            return false;

        ReadOnlySpan<char> suffix = message.AsSpan(firstSpace);
        foreach (string phrase in DeathMessagePhrases)
        {
            if (!suffix.StartsWith(phrase.AsSpan(), StringComparison.OrdinalIgnoreCase))
                continue;

            return MinecraftNameHelper.TryNormalizePlayerName(message.AsSpan(0, firstSpace), out playerName);
        }

        return false;
    }

    private static string ExtractMessage(string line)
    {
        string trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        int bracketMessageStart = trimmed.IndexOf("]: ", StringComparison.Ordinal);
        if (bracketMessageStart >= 0 && bracketMessageStart + 3 < trimmed.Length)
        {
            return trimmed[(bracketMessageStart + 3)..].Trim();
        }

        int colon = trimmed.IndexOf(':');
        if (colon >= 0 && colon + 1 < trimmed.Length)
        {
            string afterColon = trimmed[(colon + 1)..].Trim();
            if (afterColon.Length > 0)
            {
                return afterColon;
            }
        }

        return trimmed;
    }
}
