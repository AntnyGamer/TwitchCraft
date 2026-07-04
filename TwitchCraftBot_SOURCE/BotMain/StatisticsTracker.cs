using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static readonly string[] DeathMessagePhrases =
[
    " was shot by ",
    " was pummeled by ",
    " was fireballed by ",
    " was killed",
    " was slain by ",
    " was stung to death",
    " was impaled by ",
    " was impaled on a stalagmite",
    " was squashed by ",
    " was poked to death by ",
    " was pricked to death",
    " walked into a cactus",
    " drowned",
    " died",
    " blew up",
    " was blown up by ",
    " went off with a bang",
    " hit the ground too hard",
    " fell from a high place",
    " fell too far and was finished by ",
    " fell off ",
    " fell while ",
    " was doomed to fall",
    " went up in flames",
    " burned to death",
    " was burned to a crisp",
    " was burnt to a crisp",
    " walked into fire",
    " walked into the danger zone",
    " tried to swim in lava",
    " discovered the floor was lava",
    " suffocated in a wall",
    " was squished too much",
    " was smashed by ",
    " experienced kinetic energy",
    " starved to death",
    " withered away",
    " froze to death",
    " was frozen to death by ",
    " was roasted in dragon's breath",
    " was obliterated by ",
    " was skewered by ",
    " was struck by lightning",
    " fell out of the world",
    " left the confines of this world",
    " didn't want to live in the same world as "
];

    private readonly Lock _statisticsGate = new();
    private readonly Lock _deathStatisticsGate = new();
    private readonly Lock _statisticsLoadGate = new();
    private readonly AsyncLocal<string?> _currentStatisticCommandName = new();
    private BotSessionStatistics _sessionStatistics = new();
    private BotLifetimeStatistics _totalStatistics = new();
    private volatile bool _statisticsLoaded;
    private long _statisticsLeaderboardVersion;
    private long _cachedStatisticsLeaderboardVersion = -1;
    private string _cachedStatisticsLeaderboardStreamer = string.Empty;
    private string _cachedSessionMostUsedCommand = string.Empty;
    private string _cachedSessionMostDangerousViewer = string.Empty;
    private string _cachedSessionNicestViewer = string.Empty;
    private string _cachedTotalMostUsedCommand = string.Empty;
    private string _cachedTotalMostDangerousViewer = string.Empty;
    private string _cachedTotalNicestViewer = string.Empty;

    public bool StatisticsEnabled => _activeConfig?.Settings.StatisticsEnabled != false;

    private void EnsureStatisticsLoaded()
    {
        if (_statisticsLoaded)
        {
            return;
        }

        lock (_statisticsLoadGate)
        {
            if (_statisticsLoaded)
            {
                return;
            }

            BotLifetimeStatistics loadedStatistics = BotStatisticsStore.LoadGlobalOnly();
            lock (_statisticsGate)
            {
                _totalStatistics = loadedStatistics;
                _statisticsLoaded = true;
                MarkStatisticsLeaderboardDirtyNoLock();
            }
        }
    }

    internal void ResetStatisticsForNewSession()
    {
        lock (_statisticsGate)
        {
            _sessionStatistics = new BotSessionStatistics
            {
                DeathScoreBaselineSet = true,
                LastDeathScore = ClampDeathScore(_totalStatistics.LastDeathScore)
            };
            MarkStatisticsLeaderboardDirtyNoLock();
        }
    }

    internal void ResetCurrentSurvivalForStatistics()
    {
        if (StatisticsEnabled)
        {
            EnsureStatisticsLoaded();
            _ = BotStatisticsStore.SaveDeathScoreBaseline(0);
        }

        lock (_statisticsGate)
        {
            _sessionStatistics.CurrentLifeAccumulatedSeconds = 0;
            _sessionStatistics.CurrentLifeStartedUtc = null;
            _sessionStatistics.CurrentLifeHasStarted = false;
            _sessionStatistics.CurrentPlayerIsSpectator = false;
            _sessionStatistics.CurrentLifeWaitingForRespawn = false;
            _sessionStatistics.DeathScoreBaselineSet = true;
            _sessionStatistics.LastDeathScore = 0;
            _totalStatistics.LastDeathScore = 0;
        }
    }

    internal void ResetDeathScoreBaselineForStatistics()
    {
        EnsureStatisticsLoaded();
        lock (_statisticsGate)
        {
            _sessionStatistics.DeathScoreBaselineSet = true;
            _sessionStatistics.LastDeathScore = ClampDeathScore(_totalStatistics.LastDeathScore);
        }
    }

    public Task ResetAllStatisticsAsync()
        => Task.Run(ResetAllStatistics);

    private void ResetAllStatistics()
    {
        EnsureStatisticsLoaded();

        DateTime now = DateTime.UtcNow;
        bool trackedPlayerIsOnline = false;
        bool trackedPlayerIsSpectator = false;
        string trackedPlayer = _currentStreamerMinecraftName;

        if (trackedPlayer.Length > 0)
        {
            foreach (string knownPlayer in GetKnownPlayersList())
            {
                if (string.Equals(knownPlayer, trackedPlayer, StringComparison.OrdinalIgnoreCase))
                {
                    trackedPlayerIsOnline = true;
                    break;
                }
            }

            lock (_spectatorProbeGate)
            {
                trackedPlayerIsSpectator = _spectatorPlayers.Contains(trackedPlayer);
            }
        }

        long preservedLifeSeconds;
        DateTime? preservedLifeStartedUtc;
        bool preservedLifeHasStarted;
        bool preservedPlayerIsSpectator;
        bool preservedLifeWaitingForRespawn;
        int? preservedLastDeathScore;

        lock (_statisticsGate)
        {
            preservedLifeSeconds = Math.Max(0, _sessionStatistics.CurrentLifeAccumulatedSeconds);
            preservedLifeStartedUtc = _sessionStatistics.CurrentLifeStartedUtc;
            preservedLifeHasStarted = _sessionStatistics.CurrentLifeHasStarted;
            preservedPlayerIsSpectator = _sessionStatistics.CurrentPlayerIsSpectator;
            preservedLifeWaitingForRespawn = _sessionStatistics.CurrentLifeWaitingForRespawn;
            preservedLastDeathScore = _sessionStatistics.LastDeathScore;
        }

        if (trackedPlayerIsOnline)
        {
            preservedLifeHasStarted = true;

            if (trackedPlayerIsSpectator)
            {
                if (preservedLifeStartedUtc is DateTime startedUtc)
                {
                    preservedLifeSeconds += CalculateElapsedSurvivalSeconds(startedUtc, now);
                    preservedLifeStartedUtc = null;
                }

                preservedPlayerIsSpectator = true;
            }
            else
            {
                preservedPlayerIsSpectator = false;
            }
        }

        lock (_deathStatisticsGate)
        {
            if (!BotStatisticsStore.ClearAll())
                throw new IOException("Statistics could not be reset because the statistics database could not be cleared.");

            int deathScoreBaseline = Math.Max(0, preservedLastDeathScore ?? 0);
            if (!BotStatisticsStore.SaveDeathScoreBaseline(deathScoreBaseline))
                throw new IOException("Statistics could not be reset because the death score baseline could not be saved.");

            lock (_statisticsGate)
            {
                _sessionStatistics = new BotSessionStatistics
                {
                    CurrentLifeAccumulatedSeconds = preservedLifeSeconds,
                    CurrentLifeStartedUtc = preservedLifeStartedUtc,
                    CurrentLifeHasStarted = preservedLifeHasStarted,
                    CurrentPlayerIsSpectator = preservedPlayerIsSpectator,
                    CurrentLifeWaitingForRespawn = preservedLifeWaitingForRespawn,
                    DeathScoreBaselineSet = true,
                    LastDeathScore = deathScoreBaseline
                };

                _totalStatistics = new BotLifetimeStatistics { LastDeathScore = deathScoreBaseline };
                MarkStatisticsLeaderboardDirtyNoLock();
            }
        }

        QueueOnlinePlayerSnapshotRefresh();
        QueueTrackedPlayerGamemodeRefreshForStatistics();
        QueueTrackedPlayerDeathScoreRefreshForStatistics();
    }

    internal void PauseCurrentSurvivalForStatistics()
    {
        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            PauseCurrentLifeNoLock(now);
        }
    }

    internal void SetCurrentStatisticCommandName(string? commandName)
    {
        string normalizedCommand = StatisticNameHelper.NormalizeCommandName(commandName);
        _currentStatisticCommandName.Value = normalizedCommand.Length == 0 ? null : normalizedCommand;
    }

    internal void RecordCurrentGameAffectingCommandForStatistics(string sender, int tokensSpent = 0)
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        string command = _currentStatisticCommandName.Value ?? string.Empty;
        ChatCommandStatisticFlags statisticFlags = _commandRegistry.GetStatisticFlags(command);
        if ((statisticFlags & ChatCommandStatisticFlags.GameAffecting) == 0)
        {
            return;
        }

        EnsureStatisticsLoaded();

        string viewer = CommandUserHelper.NormalizeUsername(sender);
        bool isEffectCommand = string.Equals(command, "effect", StringComparison.OrdinalIgnoreCase);
        int dangerousScore = (statisticFlags & ChatCommandStatisticFlags.Dangerous) != 0 && !isEffectCommand ? 1 : 0;
        int niceScore = (statisticFlags & ChatCommandStatisticFlags.Nice) != 0 && !isEffectCommand ? 1 : 0;
        bool viewerCountsForRanking = viewer.Length > 0 && !IsStreamerViewerNormalized(viewer);
        long normalizedTokensSpent = Math.Max(0L, tokensSpent);

        bool databaseUpdated = BotStatisticsStore.ApplyGameCommandDeltaNormalized(
            command,
            normalizedTokensSpent,
            viewerCountsForRanking ? viewer : string.Empty,
            dangerousScore,
            niceScore);

        if (!databaseUpdated)
        {
            return;
        }

        lock (_statisticsGate)
        {
            _sessionStatistics.GameCommandsRun++;
            _totalStatistics.GameCommandsRun++;
            _sessionStatistics.TokensSpent += normalizedTokensSpent;
            _totalStatistics.TokensSpent += normalizedTokensSpent;
            AddViewerScore(_sessionStatistics.CommandUseCounts, command, 1);
            AddViewerScore(_totalStatistics.CommandUseCounts, command, 1);

            if (viewerCountsForRanking)
            {
                AddViewerScore(_sessionStatistics.DangerousViewerScores, viewer, dangerousScore);
                AddViewerScore(_sessionStatistics.NiceViewerScores, viewer, niceScore);
            }

            MarkStatisticsLeaderboardDirtyNoLock();
        }
    }

    internal void RecordEffectsGivenForStatistics(int count, bool streamerReceivedEffect)
    {
        if (!StatisticsEnabled || !streamerReceivedEffect)
        {
            return;
        }

        long effectsReceivedByStreamer = Math.Max(0L, count);
        if (effectsReceivedByStreamer <= 0)
        {
            return;
        }

        EnsureStatisticsLoaded();

        if (!BotStatisticsStore.ApplyEffectsGivenDelta(effectsReceivedByStreamer))
        {
            return;
        }

        lock (_statisticsGate)
        {
            _sessionStatistics.EffectsGiven += effectsReceivedByStreamer;
            _totalStatistics.EffectsGiven += effectsReceivedByStreamer;
        }
    }

    internal void RecordSessionStartedForStatistics()
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        EnsureStatisticsLoaded();

        if (!BotStatisticsStore.ApplySessionStartedDelta())
        {
            return;
        }

        lock (_statisticsGate)
        {
            _totalStatistics.SessionsStarted++;
        }
    }

    internal void RecordPlayerJoinForStatistics(string playerName)
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        if (!ShouldTrackSurvivalPlayer(playerName))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            _sessionStatistics.CurrentLifeHasStarted = true;
            if (!_sessionStatistics.CurrentPlayerIsSpectator &&
                !_sessionStatistics.CurrentLifeWaitingForRespawn &&
                _sessionStatistics.CurrentLifeStartedUtc == null)
            {
                _sessionStatistics.CurrentLifeStartedUtc = now;
            }
        }
    }

    internal void RecordPlayerLeaveForStatistics(string playerName)
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        if (!ShouldTrackSurvivalPlayer(playerName))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            PauseCurrentLifeNoLock(now);
            _sessionStatistics.CurrentPlayerIsSpectator = false;
        }
    }

    internal void RecordTrackedPlayerGamemodeForStatistics(string playerName, int gameType)
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        if (!ShouldTrackSurvivalPlayer(playerName))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        bool shouldRefreshRespawnPosition;
        lock (_statisticsGate)
        {
            _sessionStatistics.CurrentLifeHasStarted = true;

            if (gameType == 3)
            {
                PauseCurrentLifeNoLock(now);
                _sessionStatistics.CurrentPlayerIsSpectator = true;
                return;
            }

            _sessionStatistics.CurrentPlayerIsSpectator = false;
            shouldRefreshRespawnPosition = _sessionStatistics.CurrentLifeWaitingForRespawn;
            if (!shouldRefreshRespawnPosition && _sessionStatistics.CurrentLifeStartedUtc == null)
            {
                _sessionStatistics.CurrentLifeStartedUtc = now;
            }
        }

        QueueTrackedPlayerDeathScoreRefreshForStatistics(playerName);
        if (shouldRefreshRespawnPosition)
            QueueTrackedPlayerRespawnPositionRefreshForStatistics(playerName);
    }

    internal void RecordTrackedPlayerRespawnPositionForStatistics(string playerName)
    {
        if (!StatisticsEnabled || !ShouldTrackSurvivalPlayer(playerName))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            if (_sessionStatistics.CurrentPlayerIsSpectator || !_sessionStatistics.CurrentLifeWaitingForRespawn)
            {
                return;
            }

            _sessionStatistics.CurrentLifeHasStarted = true;
            _sessionStatistics.CurrentLifeWaitingForRespawn = false;
            _sessionStatistics.CurrentLifeStartedUtc ??= now;
        }
    }

    private bool ShouldRefreshTrackedPlayerRespawnPositionForStatistics(string playerName)
    {
        if (!StatisticsEnabled || !ShouldTrackSurvivalPlayer(playerName))
        {
            return false;
        }

        lock (_statisticsGate)
        {
            return _sessionStatistics.CurrentLifeWaitingForRespawn && !_sessionStatistics.CurrentPlayerIsSpectator;
        }
    }

    internal bool IsTrackingSurvivalPlayer(string playerName)
    {
        return ShouldTrackSurvivalPlayer(playerName);
    }

    private bool IsStreamerViewerNormalized(string normalizedViewer)
    {
        return normalizedViewer.Length > 0
            && _currentStreamerName.Length > 0
            && string.Equals(normalizedViewer, _currentStreamerName, StringComparison.OrdinalIgnoreCase);
    }

    internal void RecordServerLineForStatistics(string line)
    {
        bool hasDeathScoreObjective = !string.IsNullOrEmpty(line) &&
            line.Contains(DeathScoreObjective, StringComparison.OrdinalIgnoreCase);
        RecordServerLineForStatistics(line, hasDeathScoreObjective);
    }

    private void RecordServerLineForStatistics(string line, bool hasDeathScoreObjective)
    {
        if (!StatisticsEnabled || string.IsNullOrEmpty(line))
        {
            return;
        }

        string trackedPlayer = _currentStreamerMinecraftName;
        if (!hasDeathScoreObjective &&
            (trackedPlayer.Length == 0 || !line.Contains(trackedPlayer, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string message = ExtractMinecraftServerMessage(line);
        if (message.Length == 0)
        {
            return;
        }

        if (hasDeathScoreObjective && TryExtractDeathScoreFromMessage(message, out string scorePlayerName, out int deathScore))
        {
            RecordDeathScoreForStatistics(scorePlayerName, deathScore);
            return;
        }

        if (TryExtractDeathPlayerFromMessage(message, out string deathMessagePlayerName) &&
            ShouldTrackSurvivalPlayer(deathMessagePlayerName))
        {
            QueueTrackedPlayerDeathScoreRefreshForStatistics(deathMessagePlayerName);
        }
    }

    internal void RecordDeathScoreForStatistics(string playerName, int deathScore)
    {
        if (!StatisticsEnabled)
        {
            return;
        }

        if (deathScore < 0 || !ShouldTrackSurvivalPlayer(playerName))
        {
            return;
        }

        EnsureStatisticsLoaded();

        DateTime now = DateTime.UtcNow;
        long survivedSeconds = 0;
        bool saveBaselineOnly = false;
        bool processDeathScore = false;

        lock (_deathStatisticsGate)
        {
            lock (_statisticsGate)
            {
                if (!_sessionStatistics.DeathScoreBaselineSet || !_sessionStatistics.LastDeathScore.HasValue)
                {
                    saveBaselineOnly = true;
                }
                else
                {
                    int lastDeathScore = _sessionStatistics.LastDeathScore.Value;
                    if (deathScore <= lastDeathScore)
                    {
                        saveBaselineOnly = deathScore < lastDeathScore;
                    }
                    else
                    {
                        survivedSeconds = GetCurrentLifeSurvivalSeconds(_sessionStatistics, now);
                        processDeathScore = true;
                    }
                }
            }

            if (saveBaselineOnly)
            {
                if (!BotStatisticsStore.SaveDeathScoreBaseline(deathScore))
                {
                    return;
                }

                lock (_statisticsGate)
                {
                    _sessionStatistics.DeathScoreBaselineSet = true;
                    _sessionStatistics.LastDeathScore = deathScore;
                    _totalStatistics.LastDeathScore = deathScore;
                }
                return;
            }

            if (!processDeathScore)
            {
                return;
            }

            if (!BotStatisticsStore.ApplyDeathScore(deathScore, survivedSeconds, out long deathCount))
            {
                return;
            }

            lock (_statisticsGate)
            {
                _sessionStatistics.LastDeathScore = deathScore;
                _totalStatistics.LastDeathScore = deathScore;
                if (deathCount > 0)
                {
                    _ = RecordDeathNoLock(now, deathCount, survivedSeconds);
                }
            }
        }
    }

    private long RecordDeathNoLock(DateTime now, long deathCount, long survivedSeconds)
    {
        long normalizedDeathCount = Math.Max(1L, deathCount);
        _sessionStatistics.Deaths += normalizedDeathCount;
        _totalStatistics.Deaths += normalizedDeathCount;

        survivedSeconds = Math.Max(0L, survivedSeconds);
        if (survivedSeconds > 0)
        {
            if (survivedSeconds > _totalStatistics.LongestSurvivalSeconds)
            {
                _totalStatistics.LongestSurvivalSeconds = survivedSeconds;
            }

            if (_totalStatistics.ShortestSurvivalSeconds == 0 || survivedSeconds < _totalStatistics.ShortestSurvivalSeconds)
            {
                _totalStatistics.ShortestSurvivalSeconds = survivedSeconds;
            }
        }

        _sessionStatistics.CurrentLifeAccumulatedSeconds = 0;
        _sessionStatistics.CurrentLifeStartedUtc = null;
        _sessionStatistics.CurrentLifeHasStarted = true;
        _sessionStatistics.CurrentLifeWaitingForRespawn = true;
        return survivedSeconds;
    }

    public BotStatisticsSnapshot GetStatisticsSnapshot(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStatisticsLoaded();

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
                    return BuildStatisticsSnapshotNoLock(now);
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
                return BuildStatisticsSnapshotNoLock(now);
            }
        }
    }

    internal static void FlushStatisticsForShutdown()
    {
        BotStatisticsStore.TryExportReadableJson();
    }

    private BotStatisticsSnapshot BuildStatisticsSnapshotNoLock(DateTime now)
    {
        return new BotStatisticsSnapshot
        {
            StatisticsEnabled = StatisticsEnabled,
            SessionGameCommandsRun = _sessionStatistics.GameCommandsRun,
            SessionMostUsedCommand = _cachedSessionMostUsedCommand,
            SessionTokensSpent = _sessionStatistics.TokensSpent,
            SessionEffectsGiven = _sessionStatistics.EffectsGiven,
            SessionMostDangerousViewer = _cachedSessionMostDangerousViewer,
            SessionNicestViewer = _cachedSessionNicestViewer,
            SessionTimeSurvived = GetSessionTimeSurvived(_sessionStatistics, now),
            SessionDeaths = _sessionStatistics.Deaths,

            TotalGameCommandsRun = _totalStatistics.GameCommandsRun,
            TotalMostUsedCommand = _cachedTotalMostUsedCommand,
            TotalTokensSpent = _totalStatistics.TokensSpent,
            TotalEffectsGiven = _totalStatistics.EffectsGiven,
            TotalMostDangerousViewer = _cachedTotalMostDangerousViewer,
            TotalNicestViewer = _cachedTotalNicestViewer,
            TotalDeaths = _totalStatistics.Deaths,
            LongestTimeSurvived = SecondsToDuration(_totalStatistics.LongestSurvivalSeconds),
            ShortestTimeSurvived = SecondsToDuration(_totalStatistics.ShortestSurvivalSeconds),
            SessionsStarted = _totalStatistics.SessionsStarted
        };
    }

    private void MarkStatisticsLeaderboardDirtyNoLock()
    {
        _statisticsLeaderboardVersion++;
    }

    private static TimeSpan? SecondsToDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return null;
        }

        double clampedSeconds = Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    private void PauseCurrentLifeNoLock(DateTime now)
    {
        if (_sessionStatistics.CurrentLifeStartedUtc is not DateTime startedUtc)
        {
            return;
        }

        _sessionStatistics.CurrentLifeAccumulatedSeconds += CalculateElapsedSurvivalSeconds(startedUtc, now);
        _sessionStatistics.CurrentLifeStartedUtc = null;
    }

    private static TimeSpan? GetSessionTimeSurvived(BotSessionStatistics session, DateTime now)
    {
        long seconds = GetCurrentLifeSurvivalSeconds(session, now);
        bool hasStartedLife = session.CurrentLifeHasStarted || session.CurrentLifeStartedUtc != null || session.CurrentLifeAccumulatedSeconds > 0;
        return hasStartedLife ? SecondsToDuration(Math.Max(0, seconds)) ?? TimeSpan.Zero : null;
    }

    private static long GetCurrentLifeSurvivalSeconds(BotSessionStatistics session, DateTime now)
    {
        long seconds = Math.Max(0, session.CurrentLifeAccumulatedSeconds);
        if (session.CurrentLifeStartedUtc is DateTime startedUtc)
        {
            seconds += CalculateElapsedSurvivalSeconds(startedUtc, now);
        }

        return seconds;
    }

    private static long CalculateElapsedSurvivalSeconds(DateTime startedUtc, DateTime nowUtc)
    {
        if (nowUtc <= startedUtc)
        {
            return 0;
        }

        return Math.Max(0L, (long)Math.Floor((nowUtc - startedUtc).TotalSeconds));
    }

    private static int ClampDeathScore(long deathScore)
        => (int)Math.Min(int.MaxValue, Math.Max(0L, deathScore));

    private bool ShouldTrackSurvivalPlayer(string playerName)
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

            string command = StatisticNameHelper.NormalizeCommandName(pair.Key);
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

    private static void AddViewerScore(Dictionary<string, long> scores, string viewer, long amount)
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

        string excluded = CommandUserHelper.NormalizeUsername(excludedViewer);
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

    private static bool TryExtractDeathScoreFromMessage(string message, out string playerName, out int deathScore)
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

    private static bool TryExtractDeathPlayerFromMessage(string message, out string playerName)
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

    private static string ExtractMinecraftServerMessage(string line)
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
