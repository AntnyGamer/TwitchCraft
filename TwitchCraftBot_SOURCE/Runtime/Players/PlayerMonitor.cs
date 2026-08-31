using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private const string EntityDataMarker = " has the following entity data: ";
    private const string DeathScoreObjective = "tc_deaths";
    private const string ProbeMarkerStorage = "twitchcraft:probe";
    private const string ProbeMarkerPath = "marker";
    private const string ProbeMarkerPrefix = "tc_probe_";
    private static readonly StringComparer PlayerNameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string MinecraftQueryLoopbackHost = IPAddress.Loopback.ToString();
    private static readonly TimeSpan OnlinePlayersRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ServerProbeMarkerFallbackTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MinecraftQueryTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MinecraftQueryFailureBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PlayerSidebarRefreshErrorLogInterval = TimeSpan.FromMinutes(1);

    private readonly struct ServerLogLineFlags
    {
        internal readonly bool HasEntityData;
        internal readonly bool HasGameMode;
        internal readonly bool HasObjective;
        internal readonly bool HasPlayerList;
        internal readonly bool HasTcMarker;
        internal readonly bool HasHealth;
        internal readonly bool HasDisplaySlot;
        internal readonly bool HasTcPlayerList;
        internal readonly bool HasTcHealth;
        internal readonly bool HasTcDeaths;
        internal readonly bool HasProbeMarkerStorage;
        internal readonly bool hasAlreadyExists;
        internal readonly bool hasDoesNotExist;

        internal ServerLogLineFlags(string line)
        {
            this = default;

            bool hasTcMarker = line.Contains("tc_", StringComparison.Ordinal);
            bool hasEntityData = line.Contains(EntityDataMarker, StringComparison.OrdinalIgnoreCase);
            bool hasProbeMarkerStorage = line.Contains(ProbeMarkerStorage, StringComparison.Ordinal);
            bool hasGameMode = line.Contains("game mode", StringComparison.OrdinalIgnoreCase);
            bool hasObjective = line.Contains("objective", StringComparison.OrdinalIgnoreCase);
            bool hasPlayerList = line.Contains("Player List", StringComparison.OrdinalIgnoreCase);
            bool hasHealth = line.Contains("Health", StringComparison.OrdinalIgnoreCase);
            bool hasDisplaySlot = line.Contains("display slot", StringComparison.OrdinalIgnoreCase);

            if (!hasTcMarker && !hasEntityData && !hasProbeMarkerStorage &&
                !hasGameMode && !hasObjective && !hasPlayerList && !hasHealth && !hasDisplaySlot)
            {
                return;
            }

            HasTcMarker = hasTcMarker;
            HasEntityData = hasEntityData;
            HasProbeMarkerStorage = hasProbeMarkerStorage;
            HasGameMode = hasGameMode;
            HasObjective = hasObjective;
            HasPlayerList = hasPlayerList;
            HasHealth = hasHealth;
            HasDisplaySlot = hasDisplaySlot;
            HasTcPlayerList = hasTcMarker && line.Contains("tc_playerlist", StringComparison.Ordinal);
            HasTcHealth = hasTcMarker && line.Contains("tc_health", StringComparison.Ordinal);
            HasTcDeaths = hasTcMarker && line.Contains(DeathScoreObjective, StringComparison.Ordinal);
            hasAlreadyExists = hasObjective && line.Contains("already exists", StringComparison.OrdinalIgnoreCase);
            hasDoesNotExist = hasObjective && line.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);
        }
    }

    private int _onlinePlayerSnapshotQueued;
    private readonly Lock _onlinePlayerSnapshotRequestGate = new();
    private readonly Lock _serverProbeMarkerGate = new();
    private readonly SemaphoreSlim _deathScoreObjectiveGate = new(1, 1);
    private readonly Dictionary<string, Action> _pendingServerProbeMarkers = new(StringComparer.Ordinal);
    private int _pendingServerProbeMarkerCount;
    private readonly string _serverProbeMarkerSessionPrefix = ProbeMarkerPrefix + Guid.NewGuid().ToString("N") + "_";
    private long _serverProbeMarkerCounter;
    private long _minecraftQueryUnavailableUntilTicks;
    private TaskCompletionSource<bool>? _onlinePlayerSnapshotRequest;
    private DateTime _lastPlayerSidebarRefreshErrorUtc = DateTime.MinValue;
    private int _playerSidebarRefreshQueued;
    private int _trackedPlayerGamemodeRefreshQueued;
    private int _trackedPlayerRespawnPositionRefreshQueued;
    private int _deathScoreObjectiveQueued;
    private int _deathScoreObjectiveReady;
    private int _trackedPlayerDeathScoreRefreshQueued;

    private bool TryGetSessionToken(bool requireMultiplayer, out CancellationToken token)
    {
        token = default;
        CancellationTokenSource? cts = _sessionCts;
        RuntimeState state = _runtimeState;
        if (cts == null ||
            cts.IsCancellationRequested ||
            !_minecraftServerReady ||
            state == RuntimeState.Stopping ||
            state == RuntimeState.Stopped ||
            (requireMultiplayer && !MultiplayerEnabled))
        {
            return false;
        }

        token = cts.Token;
        return true;
    }

    private void RunSessionWork(
        Func<CancellationToken, Task> work,
        Action clearQueued,
        string? errorContext = null,
        CancellationToken token = default)
    {
        TrackTask(Task.Run(async () =>
        {
            try
            {
                await work(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(errorContext))
                    ErrorHandling.LogNonFatal(errorContext, ex);
            }
            finally
            {
                clearQueued();
            }
        }, CancellationToken.None));
    }

    private void RunCoalescedWork(
        Func<CancellationToken, Task> work,
        Func<bool> clearIfNoRerunRequested,
        Action markRunning,
        Action clearQueued,
        string? errorContext = null,
        Action<Exception>? onError = null,
        CancellationToken token = default)
    {
        TrackTask(Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await work(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    clearQueued();
                    break;
                }
                catch (Exception ex)
                {
                    if (onError != null)
                        onError(ex);
                    else if (!string.IsNullOrWhiteSpace(errorContext))
                        ErrorHandling.LogNonFatal(errorContext, ex);
                }

                if (clearIfNoRerunRequested())
                    break;

                markRunning();
            }
        }, CancellationToken.None));
    }

    private static int FindPlayerIndex(IReadOnlyList<string> players, string playerName)
        => SortedListHelper.FindIndex(players, playerName, PlayerNameComparer);

    private static bool HasPlayer(IReadOnlyList<string> players, string playerName)
        => SortedListHelper.Contains(players, playerName, PlayerNameComparer);

    private async Task<List<string>> RefreshPlayersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTime snapshotBefore;
        lock (_playerGate)
            snapshotBefore = _lastOnlinePlayersSnapshotUtc;

        if (_minecraftServerReady && DateTime.UtcNow - snapshotBefore >= OnlinePlayersRefreshInterval)
            await RefreshSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return GetKnownPlayers();
    }

    private void QueueSnapshot()
    {
        if (!TryGetSessionToken(requireMultiplayer: false, out CancellationToken token))
            return;

        int previous = Interlocked.CompareExchange(ref _onlinePlayerSnapshotQueued, 1, 0);
        if (previous != 0)
        {
            Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 2);
            return;
        }

        RunCoalescedWork(
            RefreshSnapshotAsync,
            () => Interlocked.CompareExchange(ref _onlinePlayerSnapshotQueued, 0, 1) == 1,
            () => Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 1),
            () => Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 0),
            "Online player snapshot refresh failed",
            token: token);
    }

    private async Task<bool> RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeConfig?.Settings.RemoteControlEnabled == true)
        {
            if (await TryRefreshRconAsync(cancellationToken).ConfigureAwait(false))
                return true;

            return await TryRefreshQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (await TryRefreshQueryAsync(cancellationToken).ConfigureAwait(false))
            return true;

        return await RefreshListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryRefreshRconAsync(CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Server == null)
            return false;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RCONTimeout);
            string? response = await MinecraftRCONClient.ExecuteQueryAsync(
                GetRconHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                "list",
                timeout.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (!TryParseList(response, true, out List<string> players))
                return false;

            ApplySnapshot(players);
            CompleteSnapshot(true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RefreshListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool> waiter;
        bool createdWaiter = false;
        lock (_onlinePlayerSnapshotRequestGate)
        {
            if (_onlinePlayerSnapshotRequest != null && !_onlinePlayerSnapshotRequest.Task.IsCompleted)
            {
                waiter = _onlinePlayerSnapshotRequest;
            }
            else
            {
                waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _onlinePlayerSnapshotRequest = waiter;
                createdWaiter = true;
            }
        }

        int suppressedLineReleasePending = 0;

        void ReleaseLine()
        {
            if (Interlocked.Exchange(ref suppressedLineReleasePending, 0) != 0)
                _ = TryReleasePlayerList();
        }

        try
        {
            if (createdWaiter)
            {
                Interlocked.Increment(ref _suppressedOnlinePlayersLogLines);
                suppressedLineReleasePending = 1;
                if (!await SendProbeAsync(
                        "list",
                        () =>
                        {
                            CompleteSnapshot(false, waiter);
                            ReleaseLine();
                        },
                        cancellationToken).ConfigureAwait(false))
                {
                    CompleteSnapshot(false, waiter);
                    ReleaseLine();
                    return false;
                }
            }

            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (createdWaiter)
            {
                CompleteSnapshot(false, waiter);
                ReleaseLine();
            }

            throw;
        }
        catch
        {
            if (createdWaiter)
            {
                CompleteSnapshot(false, waiter);
                ReleaseLine();
            }

            return false;
        }
    }

    private async Task<bool> TryRefreshQueryAsync(CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Server == null)
            return false;

        long nowTicks = DateTime.UtcNow.Ticks;
        if (Volatile.Read(ref _minecraftQueryUnavailableUntilTicks) > nowTicks)
            return false;

        string host = GetQueryHost(config);
        int port = config.Server.Port;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MinecraftQueryTimeout);
            List<string> players = await MinecraftQueryClient.GetPlayersAsync(host, port, timeout.Token).ConfigureAwait(false);
            Volatile.Write(ref _minecraftQueryUnavailableUntilTicks, 0);
            ApplySnapshot(players);
            CompleteSnapshot(true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Volatile.Write(ref _minecraftQueryUnavailableUntilTicks, DateTime.UtcNow.Add(MinecraftQueryFailureBackoff).Ticks);
            return false;
        }
    }

    private static string GetQueryHost(BotConfig config)
    {
        string host = config.Settings.RemoteControlEnabled
            ? GetRconHost(config)
            : (config.Server.BindIP ?? string.Empty).Trim();
        if (host.Length == 0)
            return MinecraftQueryLoopbackHost;

        if (IPAddress.TryParse(host, out IPAddress? address))
        {
            if (IPAddress.IPv6Any.Equals(address))
                return IPAddress.IPv6Loopback.ToString();
            if (IPAddress.Any.Equals(address))
                return MinecraftQueryLoopbackHost;
        }

        return host;
    }

    private void ApplySnapshot(List<string> currentPlayers)
    {
        List<string>? previousPlayers = null;
        bool playersChanged;
        lock (_playerGate)
        {
            playersChanged = !SortedListHelper.EqualInOrder(_knownPlayers, currentPlayers, PlayerNameComparer);
            if (playersChanged)
            {
                previousPlayers = _knownPlayers;
                _knownPlayers = currentPlayers;
            }

            _lastOnlinePlayersSnapshotUtc = DateTime.UtcNow;
        }

        if (playersChanged && previousPlayers != null)
        {
            RecordRoster(previousPlayers, currentPlayers);
            if (MultiplayerEnabled)
                QueueSidebarRefresh();
        }

        QueueGamemode();
        QueueDeathScore();
    }

    private void CompleteSnapshot(bool result, TaskCompletionSource<bool>? expectedWaiter = null)
    {
        TaskCompletionSource<bool>? waiter;
        lock (_onlinePlayerSnapshotRequestGate)
        {
            waiter = _onlinePlayerSnapshotRequest;
            if (waiter == null || (expectedWaiter != null && !ReferenceEquals(waiter, expectedWaiter)))
                return;

            _onlinePlayerSnapshotRequest = null;
        }

        waiter.TrySetResult(result);
    }

    private static bool IsSidebarErrorLine(
        string line,
        in ServerLogLineFlags flags,
        bool isCommandParserError,
        bool isUnexpectedCommandError,
        bool isMinecraftCommandErrorContext)
    {
        if (string.IsNullOrEmpty(line) || isUnexpectedCommandError || isCommandParserError || isMinecraftCommandErrorContext)
            return false;

        if (!flags.HasObjective && !flags.HasTcPlayerList && !flags.HasTcHealth && !flags.HasHealth)
            return false;

        return
            line.Contains("Unknown scoreboard objective 'tc_playerlist'", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Unknown scoreboard objective 'tc_health'", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No objective was found by the name 'tc_playerlist'", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No objective was found by the name 'tc_health'", StringComparison.OrdinalIgnoreCase) ||
            (flags.HasTcPlayerList && flags.hasDoesNotExist) ||
            (flags.HasTcHealth && flags.hasDoesNotExist);
    }

}
