using System;

namespace TwitchCraftBot_V1;

public sealed partial class StatisticsService
{
    internal void RecordLine(string line, bool hasDeathScoreObjective)
    {
        if (!Enabled || string.IsNullOrEmpty(line))
        {
            return;
        }

        string trackedPlayer = _streamerMinecraftName;
        if (!hasDeathScoreObjective &&
            (trackedPlayer.Length == 0 || !line.Contains(trackedPlayer, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string message = ExtractMessage(line);
        if (message.Length == 0)
        {
            return;
        }

        if (hasDeathScoreObjective && TryExtractDeathScore(message, out string scorePlayerName, out int deathScore))
        {
            RecordDeathScore(scorePlayerName, deathScore);
            return;
        }

        if (TryExtractPlayer(message, out string deathMessagePlayerName) &&
            ShouldTrackPlayer(deathMessagePlayerName))
        {
            _dependencies.QueueDeathScore(deathMessagePlayerName);
        }
    }

    internal void RecordDeathScore(string playerName, int deathScore)
    {
        if (!Enabled || deathScore < 0 || !ShouldTrackPlayer(playerName))
            return;

        Load();

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
                        survivedSeconds = GetLifeSeconds(_sessionStatistics, now);
                        processDeathScore = true;
                    }
                }
            }

            if (saveBaselineOnly)
            {
                if (!BotStatisticsStore.SaveDeathBaseline(deathScore))
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
                    RecordDeathNoLock(deathCount, survivedSeconds);
                }
            }
        }
    }

    private void RecordDeathNoLock(long deathCount, long survivedSeconds)
    {
        _sessionStatistics.Deaths += deathCount;
        _totalStatistics.Deaths += deathCount;

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
    }
}
