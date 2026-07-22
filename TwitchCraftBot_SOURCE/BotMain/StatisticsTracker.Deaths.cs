using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
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
