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
            _playerSidebarCleared = false;
            Volatile.Write(ref _lastOnlinePlayersSnapshotTicks, 0);
        }

        lock (_spectatorProbeGate)
        {
            _pendingGamemodeRequests.Clear();
            _spectatorPlayers.Clear();
            _lastSpectatorRefreshUtc = DateTime.MinValue;
            _spectatorSnapshotInitialized = false;
        }

        lock (_selectedItemProbeGate)
        {
            _pendingSelectedItemRequests.Clear();
        }

        lock (_maxHealthProbeGate)
        {
            foreach (TaskCompletionSource<double?> waiter in _pendingMaxHealthRequests.Values) waiter.TrySetResult(null);
            foreach (TaskCompletionSource<string?> waiter in _pendingHeartAttributeRequests.Values) waiter.TrySetResult(null);
            _pendingMaxHealthRequests.Clear();
            _pendingHeartAttributeRequests.Clear();
        }
        lock (_respawnProbeGate)
        {
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

    internal Task StopProcessSafeAsync(bool waitBriefly)
        => _minecraftSession.StopProcessSafeAsync(waitBriefly, GracefulShutdownTimeout);

    private void SafeCleanup()
    {
        Statistics.PauseSurvival();

        try
        {
            MinigameManager.StopLoops(this);
        }
        catch
        {
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

    private static string NormalizeUser(string? user) => CommandUserHelper.NormalizeUser(user);

    private List<string> GetKnownPlayers()
    {
        lock (_playerGate)
        {
            return [.. _knownPlayers];
        }
    }
}
