using System;

namespace TwitchCraftBot_V1;

public sealed partial class StatisticsService
{
    internal void PauseSurvival()
    {
        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            PauseLifeNoLock(now);
        }
    }

    internal void SetStatsCommand(string? commandName)
    {
        string normalizedCommand = StatisticNameHelper.CleanCommandName(commandName);
        _currentStatisticCommandName.Value = normalizedCommand.Length == 0 ? null : normalizedCommand;
    }

    internal string CurrentCommandName => _currentStatisticCommandName.Value ?? string.Empty;

    internal void RecordCommand(string sender, int tokensSpent = 0)
    {
        if (!Enabled)
        {
            return;
        }

        string command = _currentStatisticCommandName.Value ?? string.Empty;
        ChatCommandStatisticFlags statisticFlags = _dependencies.GetCommandFlags(command);
        if ((statisticFlags & ChatCommandStatisticFlags.GameAffecting) == 0)
        {
            return;
        }

        Load();

        string viewer = CommandUserHelper.NormalizeUser(sender);
        bool isEffectCommand = string.Equals(command, "effect", StringComparison.OrdinalIgnoreCase);
        int dangerousScore = (statisticFlags & ChatCommandStatisticFlags.Dangerous) != 0 && !isEffectCommand ? 1 : 0;
        int niceScore = (statisticFlags & ChatCommandStatisticFlags.Nice) != 0 && !isEffectCommand ? 1 : 0;
        bool viewerCountsForRanking = viewer.Length > 0 && !IsStreamer(viewer);
        long normalizedTokensSpent = Math.Max(0L, tokensSpent);

        bool databaseUpdated = BotStatisticsStore.ApplyCommandDelta(
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
            AddScore(_sessionStatistics.CommandUseCounts, command, 1);
            AddScore(_totalStatistics.CommandUseCounts, command, 1);

            if (viewerCountsForRanking)
            {
                AddScore(_sessionStatistics.DangerousViewerScores, viewer, dangerousScore);
                AddScore(_sessionStatistics.NiceViewerScores, viewer, niceScore);
            }

            MarkLeaderboardDirty();
        }
    }

    internal void RecordEffects(int count, bool streamerReceivedEffect)
    {
        if (!Enabled || !streamerReceivedEffect)
        {
            return;
        }

        long effectsReceivedByStreamer = Math.Max(0L, count);
        if (effectsReceivedByStreamer <= 0)
        {
            return;
        }

        Load();

        if (!BotStatisticsStore.ApplyEffectsDelta(effectsReceivedByStreamer))
        {
            return;
        }

        lock (_statisticsGate)
        {
            _sessionStatistics.EffectsGiven += effectsReceivedByStreamer;
            _totalStatistics.EffectsGiven += effectsReceivedByStreamer;
        }
    }

    internal void RecordSession()
    {
        if (!Enabled)
        {
            return;
        }

        Load();

        if (!BotStatisticsStore.ApplySessionDelta())
        {
            return;
        }

        lock (_statisticsGate)
        {
            _totalStatistics.SessionsStarted++;
        }
    }

    internal void RecordPlayerJoin(string playerName)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
            return;

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

    internal void RecordPlayerLeave(string playerName)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
            return;

        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            PauseLifeNoLock(now);
            _sessionStatistics.CurrentPlayerIsSpectator = false;
        }
    }

    internal void RecordGamemode(string playerName, int gameType)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
            return;

        DateTime now = DateTime.UtcNow;
        bool shouldRefreshRespawnPosition;
        lock (_statisticsGate)
        {
            _sessionStatistics.CurrentLifeHasStarted = true;

            if (gameType == 3)
            {
                PauseLifeNoLock(now);
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

        _dependencies.QueueDeathScore(playerName);
        if (shouldRefreshRespawnPosition)
            _dependencies.QueueRespawn(playerName);
    }

    internal void RecordRespawn(string playerName)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
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

    internal bool NeedsRespawnRefresh(string playerName)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
        {
            return false;
        }

        lock (_statisticsGate)
        {
            return _sessionStatistics.CurrentLifeWaitingForRespawn && !_sessionStatistics.CurrentPlayerIsSpectator;
        }
    }

    private bool IsStreamer(string normalizedViewer)
    {
        string streamerName = _streamerName;
        return normalizedViewer.Length > 0
            && streamerName.Length > 0
            && string.Equals(normalizedViewer, streamerName, StringComparison.OrdinalIgnoreCase);
    }

}
