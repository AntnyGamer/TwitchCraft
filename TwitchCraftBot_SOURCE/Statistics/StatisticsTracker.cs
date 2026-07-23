using System;
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

}
