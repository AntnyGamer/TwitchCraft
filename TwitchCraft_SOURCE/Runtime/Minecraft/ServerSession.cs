using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static string GetRCONHost(TwitchCraftConfig config)
    {
        string host = (config.Server.RemoteHost ?? string.Empty).Trim();
        return host.Length == 0 ? "127.0.0.1" : host;
    }

    private void ResetSession()
    {
        ResetQueues();
        _timedPlayerScaleController.MarkForRecovery();

        lock (_viewerGate)
        {
            _knownViewers = [];
            _viewerRewardSchedule = new(PlayerNameComparer);
            _viewerLastChatActivity.Clear();
        }

        Commands.ResetCommandState();
        _twitchSession.ResetRelayRateLimit();

        lock (_playerGate)
        {
            _knownPlayers = [];
            _lastSidebarPlayers = [];
            _playerSidebarInitialized = false;
            Volatile.Write(ref _lastOnlinePlayersSnapshotTicks, 0);
        }

        lock (_spectatorProbeGate)
        {
            CompletePendingRequests(_pendingGamemodeRequests, (int?)null);

            _pendingGamemodeRequests.Clear();
            _spectatorPlayers.Clear();
            _lastSpectatorRefreshUtc = DateTime.MinValue;
            _spectatorSnapshotInitialized = false;
        }

        lock (_selectedItemProbeGate)
        {
            CompletePendingRequests(_pendingSelectedItemRequests, (string?)null);

            _pendingSelectedItemRequests.Clear();
        }

        lock (_maxHealthProbeGate)
        {
            CompletePendingRequests(_pendingMaxHealthRequests, (double?)null);

            CompletePendingRequests(_pendingHeartAttributeRequests, (string?)null);

            _pendingMaxHealthRequests.Clear();
            _pendingHeartAttributeRequests.Clear();
        }
        lock (_respawnProbeGate)
        {
            CompletePendingRequests(_pendingRespawnRequests, false);

            _pendingRespawnRequests.Clear();
        }

        CompleteSnapshot(false);
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers.Clear();
        }

        Interlocked.Exchange(ref _playerSidebarRefreshQueued, 0);
        Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 0);
        Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 0);
        Interlocked.Exchange(ref _suppressedOnlinePlayersLogLines, 0);
        Interlocked.Exchange(ref _gamemodeRefreshQueued, 0);
        Interlocked.Exchange(ref _respawnRefreshQueued, 0);
        Interlocked.Exchange(ref _deathScoreObjectiveQueued, 0);
        Interlocked.Exchange(ref _deathScoreObjectiveReady, 0);
        Interlocked.Exchange(ref _deathScoreRefreshQueued, 0);
        Volatile.Write(ref _deathScoreInitializedPlayerName, null);
        Volatile.Write(ref _minecraftQueryUnavailableUntilTicks, 0);
        _minecraftSession.RCONHealthy = false;
        _minecraftSession.ServerReady = false;

        _shellWindow?.ClearServerLog();
        _shellWindow?.ClearChatLog();
        _shellWindow?.UpdateViewers([]);
    }

    private static void CompletePendingRequests<T>(Dictionary<string, TaskCompletionSource<T>> requests, T result)
    {
        foreach (TaskCompletionSource<T> waiter in requests.Values)
        {
            waiter.TrySetResult(result);
        }
    }

    internal Task StopProcessSafeAsync(bool waitBriefly)
        => _minecraftSession.StopProcessSafeAsync(waitBriefly, GracefulShutdownTimeout);

    private void SafeCleanup()
    {
        Statistics.PauseSurvival();

        try
        {
            MinigameManager.StopLoops(this);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to stop minigames during cleanup", ex);
        }

        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
        }

        _twitchSession.CloseSocket();
        _minecraftSession.StopProcessSafe();

        Tokens.TryExportJson();
        StatisticsService.FlushForShutdown();
        CloseStores();
    }
}
