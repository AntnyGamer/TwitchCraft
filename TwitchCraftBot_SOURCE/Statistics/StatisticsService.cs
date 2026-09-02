using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

/// <summary>
/// Owns session and lifetime statistics, persistence coordination, and snapshots.
/// </summary>
public sealed partial class StatisticsService
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
    private readonly BotStatisticsDependencies _dependencies;
    private bool _enabled = true;
    private string _streamerName = string.Empty;
    private string _streamerMinecraftName = string.Empty;

    internal StatisticsService(BotStatisticsDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _dependencies = dependencies;
    }

    public bool Enabled => _enabled;

    internal void SetContext(bool enabled, string streamerName, string streamerMinecraftName)
    {
        _enabled = enabled;
        _streamerName = streamerName;
        _streamerMinecraftName = streamerMinecraftName;
    }

    internal void Load()
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

            BotLifetimeStatistics loadedStatistics = BotStatisticsStore.LoadGlobal();
            lock (_statisticsGate)
            {
                _totalStatistics = loadedStatistics;
                _statisticsLoaded = true;
                MarkLeaderboardDirty();
            }

        }

    }

    internal void ResetForSession()
    {
        lock (_statisticsGate)
        {
            _sessionStatistics = new BotSessionStatistics
            {
                DeathScoreBaselineSet = true,
                LastDeathScore = ClampDeathScore(_totalStatistics.LastDeathScore)
            };
            MarkLeaderboardDirty();
        }

    }

    internal void ResetSurvival()
    {
        if (Enabled)
        {
            Load();
            _ = BotStatisticsStore.SaveDeathBaseline(0);
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

    internal void ResetDeathBaseline()
    {
        Load();
        lock (_statisticsGate)
        {
            _sessionStatistics.DeathScoreBaselineSet = true;
            _sessionStatistics.LastDeathScore = ClampDeathScore(_totalStatistics.LastDeathScore);
        }

    }

    public Task ResetAllAsync()
        => Task.Run(ResetAll);

    private void ResetAll()
    {
        Load();
        DateTime now = DateTime.UtcNow;
        bool trackedPlayerIsOnline = false;
        bool trackedPlayerIsSpectator = false;
        string trackedPlayer = _streamerMinecraftName;

        if (trackedPlayer.Length > 0)
        {
            foreach (string knownPlayer in _dependencies.GetKnownPlayers())
            {
                if (string.Equals(knownPlayer, trackedPlayer, StringComparison.OrdinalIgnoreCase))
                {
                    trackedPlayerIsOnline = true;
                    break;
                }

            }

            trackedPlayerIsSpectator = _dependencies.IsSpectator(trackedPlayer);
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
                    preservedLifeSeconds += GetSurvivalSeconds(startedUtc, now);
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
            if (!BotStatisticsStore.SaveDeathBaseline(deathScoreBaseline))
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
                MarkLeaderboardDirty();
            }

        }

        _dependencies.QueueSnapshot();
        _dependencies.QueueGamemode();
        _dependencies.QueueAllDeathScores();
    }

}

internal sealed record BotStatisticsDependencies(
    Func<string, ChatCommandStatisticFlags> GetCommandFlags,
    Func<List<string>> GetKnownPlayers,
    Func<string, bool> IsSpectator,
    Action QueueSnapshot,
    Action QueueGamemode,
    Action QueueAllDeathScores,
    Action<string> QueueDeathScore,
    Action<string> QueueRespawn);

public sealed partial class BotMainHandler
{
    private bool IsSpectatorPlayer(string playerName)
    {
        lock (_spectatorProbeGate)
            return _spectatorPlayers.Contains(playerName);
    }
}
