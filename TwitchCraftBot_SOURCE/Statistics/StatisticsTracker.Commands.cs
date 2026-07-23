using System;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
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

}
