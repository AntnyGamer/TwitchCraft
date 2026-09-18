using System;

namespace TwitchCraft_V1;

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

    internal void RecordCommand(string commandName, string sender, int tokensSpent = 0)
    {
        if (!Enabled)
        {
            return;
        }

        string command = StatisticNameHelper.CleanCommandName(commandName);
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

        lock (_deathStatisticsGate)
        {
            bool databaseUpdated = StatisticsStore.ApplyCommandDelta(
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
    }

    internal void RecordEffects(int count, bool streamerReceivedEffect)
    {
        if (!Enabled || !streamerReceivedEffect || count <= 0)
        {
            return;
        }

        Load();

        lock (_deathStatisticsGate)
        {
            if (!StatisticsStore.ApplyEffectsDelta(count))
            {
                return;
            }

            lock (_statisticsGate)
            {
                _sessionStatistics.EffectsGiven += count;
                _totalStatistics.EffectsGiven += count;
            }
        }
    }

    internal void RecordSession()
    {
        if (!Enabled)
        {
            return;
        }

        Load();

        lock (_deathStatisticsGate)
        {
            if (!StatisticsStore.ApplySessionDelta())
            {
                return;
            }

            lock (_statisticsGate)
            {
                _totalStatistics.SessionsStarted++;
            }
        }
    }

    internal void RecordPlayerJoin(string playerName)
    {
        if (!Enabled || !ShouldTrackPlayer(playerName))
            return;

        DateTime now = DateTime.UtcNow;
        lock (_statisticsGate)
        {
            _sessionStatistics.CurrentLifeHasStarted |= !_sessionStatistics.CurrentLifeWaitingForRespawn;
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
            _sessionStatistics.CurrentLifeHasStarted |= !_sessionStatistics.CurrentLifeWaitingForRespawn;

            if (gameType == 3)
            {
                PauseLifeNoLock(now);
                _sessionStatistics.CurrentPlayerIsSpectator = true;
                return;
            }

            _sessionStatistics.CurrentPlayerIsSpectator = false;
            shouldRefreshRespawnPosition = _sessionStatistics.CurrentLifeWaitingForRespawn && _sessionStatistics.CurrentLifeHasStarted;
            if (!_sessionStatistics.CurrentLifeWaitingForRespawn && _sessionStatistics.CurrentLifeStartedUtc == null)
            {
                _sessionStatistics.CurrentLifeStartedUtc = now;
            }
        }

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
            if (_sessionStatistics.CurrentPlayerIsSpectator || !_sessionStatistics.CurrentLifeWaitingForRespawn || !_sessionStatistics.CurrentLifeHasStarted)
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
            return _sessionStatistics.CurrentLifeWaitingForRespawn && _sessionStatistics.CurrentLifeHasStarted && !_sessionStatistics.CurrentPlayerIsSpectator;
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
