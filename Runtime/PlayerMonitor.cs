using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private static readonly TimeSpan RemoteRCONPlayerListTimeout = TimeSpan.FromSeconds(2);
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

            bool hasTcMarker = line.Contains("tc_", StringComparison.OrdinalIgnoreCase);
            bool hasEntityData = line.Contains(EntityDataMarker, StringComparison.OrdinalIgnoreCase);
            bool hasProbeMarkerStorage = line.Contains(ProbeMarkerStorage, StringComparison.OrdinalIgnoreCase);
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
            HasTcPlayerList = hasTcMarker && line.Contains("tc_playerlist", StringComparison.OrdinalIgnoreCase);
            HasTcHealth = hasTcMarker && line.Contains("tc_health", StringComparison.OrdinalIgnoreCase);
            HasTcDeaths = hasTcMarker && line.Contains(DeathScoreObjective, StringComparison.OrdinalIgnoreCase);
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

    private bool TryGetQueuedSessionToken(bool requireMultiplayer, out CancellationToken token)
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

    private void RunQueuedSessionWork(
        Func<CancellationToken, Task> work,
        Action clearQueued,
        string? errorContext = null,
        Action<Exception>? onError = null,
        CancellationToken token = default)
    {
        TrackSessionBackgroundTask(Task.Run(async () =>
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
                if (onError != null)
                    onError(ex);
                else if (!string.IsNullOrWhiteSpace(errorContext))
                    ErrorHandling.LogNonFatal(errorContext, ex);
            }
            finally
            {
                clearQueued();
            }
        }, CancellationToken.None));
    }

    private void RunCoalescedQueuedSessionWork(
        Func<CancellationToken, Task> work,
        Func<bool> clearIfNoRerunRequested,
        Action markRunning,
        Action clearQueued,
        string? errorContext = null,
        Action<Exception>? onError = null,
        CancellationToken token = default)
    {
        TrackSessionBackgroundTask(Task.Run(async () =>
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

    private static int FindSortedPlayerIndex(IReadOnlyList<string> players, string playerName)
        => SortedListHelper.FindIndex(players, playerName, PlayerNameComparer);

    private static bool ContainsPlayer(IReadOnlyList<string> players, string playerName)
        => SortedListHelper.Contains(players, playerName, PlayerNameComparer);

    private async Task<List<string>> RefreshOnlinePlayersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTime snapshotBefore;
        lock (_playerGate)
            snapshotBefore = _lastOnlinePlayersSnapshotUtc;

        if (_minecraftServerReady && DateTime.UtcNow - snapshotBefore >= OnlinePlayersRefreshInterval)
            await RefreshOnlinePlayerSnapshotNowAsync(cancellationToken).ConfigureAwait(false);

        return GetKnownPlayersList();
    }

    private void QueueOnlinePlayerSnapshotRefresh()
    {
        if (!TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token))
            return;

        int previous = Interlocked.CompareExchange(ref _onlinePlayerSnapshotQueued, 1, 0);
        if (previous != 0)
        {
            Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 2);
            return;
        }

        RunCoalescedQueuedSessionWork(
            RefreshOnlinePlayerSnapshotNowAsync,
            () => Interlocked.CompareExchange(ref _onlinePlayerSnapshotQueued, 0, 1) == 1,
            () => Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 1),
            () => Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 0),
            "Online player snapshot refresh failed",
            token: token);
    }

    private static async Task<TResult> AwaitProbeResultAsync<TResult>(
        TaskCompletionSource<TResult> waiter,
        bool createdWaiter,
        Func<CancellationToken, Task<bool>> sendProbe,
        TResult defaultResult,
        CancellationToken cancellationToken)
    {
        if (createdWaiter && !await sendProbe(cancellationToken).ConfigureAwait(false))
        {
            waiter.TrySetResult(defaultResult);
            return defaultResult;
        }

        return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RefreshOnlinePlayerSnapshotNowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeConfig?.Settings.RemoteControlEnabled == true)
        {
            if (await TryRefreshOnlinePlayerSnapshotFromRemoteRCONAsync(cancellationToken).ConfigureAwait(false))
                return true;

            return await TryRefreshOnlinePlayerSnapshotFromQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (await TryRefreshOnlinePlayerSnapshotFromQueryAsync(cancellationToken).ConfigureAwait(false))
            return true;

        return await RefreshOnlinePlayerSnapshotFromListCommandAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryRefreshOnlinePlayerSnapshotFromRemoteRCONAsync(CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Server == null)
            return false;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RemoteRCONPlayerListTimeout);
            string? response = await MinecraftRCONClient.ExecuteQueryAsync(
                GetRemoteControllerHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                "list",
                timeout.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
                return false;

            if (!TryParsePlayerListResponse(response, true, out List<string> players))
                return false;

            ApplyOnlinePlayerSnapshot(players);
            CompleteOnlinePlayerSnapshotRequest(true);
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

    private async Task<bool> RefreshOnlinePlayerSnapshotFromListCommandAsync(CancellationToken cancellationToken)
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

        void ReleaseSuppressedLineOnce()
        {
            if (Interlocked.Exchange(ref suppressedLineReleasePending, 0) != 0)
                ReleaseSuppressedOnlinePlayersLogLine();
        }

        try
        {
            if (createdWaiter)
            {
                Interlocked.Increment(ref _suppressedOnlinePlayersLogLines);
                suppressedLineReleasePending = 1;
                if (!await SendInternalProbeCommandAsync(
                        "list",
                        () =>
                        {
                            CompleteOnlinePlayerSnapshotRequest(false, waiter);
                            ReleaseSuppressedLineOnce();
                        },
                        cancellationToken).ConfigureAwait(false))
                {
                    CompleteOnlinePlayerSnapshotRequest(false, waiter);
                    ReleaseSuppressedLineOnce();
                    return false;
                }
            }

            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (createdWaiter)
            {
                CompleteOnlinePlayerSnapshotRequest(false, waiter);
                ReleaseSuppressedLineOnce();
            }

            throw;
        }
        catch
        {
            if (createdWaiter)
            {
                CompleteOnlinePlayerSnapshotRequest(false, waiter);
                ReleaseSuppressedLineOnce();
            }

            return false;
        }
    }

    private async Task<bool> TryRefreshOnlinePlayerSnapshotFromQueryAsync(CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Server == null)
            return false;

        long nowTicks = DateTime.UtcNow.Ticks;
        if (Volatile.Read(ref _minecraftQueryUnavailableUntilTicks) > nowTicks)
            return false;

        string host = GetMinecraftQueryHost(config);
        int port = config.Server.Port;

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MinecraftQueryTimeout);
            List<string> players = await MinecraftQueryClient.GetOnlinePlayerNamesAsync(host, port, timeout.Token).ConfigureAwait(false);
            Volatile.Write(ref _minecraftQueryUnavailableUntilTicks, 0);
            ApplyOnlinePlayerSnapshot(players);
            CompleteOnlinePlayerSnapshotRequest(true);
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

    private static string GetMinecraftQueryHost(BotConfig config)
    {
        string host = config.Settings.RemoteControlEnabled
            ? GetRemoteControllerHost(config)
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

    private void ApplyOnlinePlayerSnapshot(List<string> currentPlayers)
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
            RecordPlayerRosterChanges(previousPlayers, currentPlayers);
            if (MultiplayerEnabled)
                QueuePlayerSidebarRefresh();
        }

        QueueTrackedPlayerGamemodeRefreshForStatistics();
        QueueTrackedPlayerDeathScoreRefreshForStatistics();
    }

    private void CompleteOnlinePlayerSnapshotRequest(bool result, TaskCompletionSource<bool>? expectedWaiter = null)
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

    private static bool IsSidebarObjectiveIssueLine(
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

    private void HandleServerReadyState(string line)
    {
        if (_minecraftServerReady || string.IsNullOrEmpty(line))
            return;

        if (!line.Contains("Done (", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("For help, type \"help\"", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _minecraftServerReady = true;

        try
        {
            if (_activeConfig is { } activeConfig && activeConfig.Settings?.RemoteControlEnabled != true && ServerPropertiesChangedSinceLastApply(activeConfig))
                ApplyStartProfileAndRemember(activeConfig);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to reformat server.properties after Minecraft startup", ex);
        }

        QueueDeathScoreObjectiveInitialization();
        QueueInitialPlayerSnapshot();
        QueuePlayerSidebarRefresh();
        QueueTrackedPlayerGamemodeRefreshForStatistics();
        QueueTrackedPlayerDeathScoreRefreshForStatistics();
    }

    private void RecoverSidebarInitializationFromServerLine(bool isSidebarObjectiveIssue)
    {
        if (!isSidebarObjectiveIssue)
            return;

        bool hasOnlinePlayers;
        lock (_playerGate)
        {
            hasOnlinePlayers = _knownPlayers.Count > 0;
            _playerSidebarInitialized = false;
        }

        if (hasOnlinePlayers)
            QueuePlayerSidebarRefresh();
    }

    private bool ShouldSuppressOnlinePlayersLogLine(string line)
    {
        if (Volatile.Read(ref _suppressedOnlinePlayersLogLines) <= 0 || string.IsNullOrEmpty(line))
            return false;

        return (line.Contains("players online:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("player online:", StringComparison.OrdinalIgnoreCase))
            && TryReleaseSuppressedOnlinePlayersLogLine();
    }

    private void ReleaseSuppressedOnlinePlayersLogLine()
    {
        _ = TryReleaseSuppressedOnlinePlayersLogLine();
    }

    private bool TryReleaseSuppressedOnlinePlayersLogLine()
    {
        while (true)
        {
            int pending = Volatile.Read(ref _suppressedOnlinePlayersLogLines);
            if (pending <= 0)
                return false;

            if (Interlocked.CompareExchange(ref _suppressedOnlinePlayersLogLines, pending - 1, pending) == pending)
                return true;
        }
    }

    private async Task<bool> SendInternalProbeCommandAsync(string command, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            onProbeCompleted();
            return false;
        }

        if (RemoteControlEnabled)
        {
            try
            {
                string? response = await ExecuteRemoteServerQueryAsync(command, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(response))
                    HandleRemoteQueryResponse(response);

                return response != null;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        string marker = RegisterServerProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            (BotMainHandler handler, string marker, Action onCompleted) = ((BotMainHandler Handler, string Marker, Action OnCompleted))state!;
            if (handler.TryCancelServerProbeMarker(marker))
                onCompleted();
        }, (this, marker, onProbeCompleted));

        try
        {
            string escapedMarker = MinecraftCommandBuilder.EscapeJson(marker);
            string[] probeCommands =
            [
                command,
                "data modify storage " + ProbeMarkerStorage + " " + ProbeMarkerPath + " set value \"" + escapedMarker + "\"",
                "data get storage " + ProbeMarkerStorage + " " + ProbeMarkerPath
            ];

            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueServerProbeMarkerFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelServerProbeMarker(marker))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (TryCancelServerProbeMarker(marker))
                onProbeCompleted();

            throw;
        }
    }

    private async Task<bool> SendInternalProbeCommandsAsync(string[] commands, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        if (commands.Length == 0)
            return false;

        bool remoteControlEnabled = RemoteControlEnabled;
        List<string> probeCommands = new(remoteControlEnabled ? commands.Length : commands.Length + 2);
        for (int i = 0; i < commands.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(commands[i]))
                probeCommands.Add(commands[i]);
        }

        if (probeCommands.Count == 0)
        {
            onProbeCompleted();
            return false;
        }

        if (remoteControlEnabled)
        {
            try
            {
                List<string?>? responses = await ExecuteRemoteServerQueriesAsync(probeCommands, cancellationToken).ConfigureAwait(false);
                if (responses == null)
                    return false;

                bool delivered = false;
                for (int i = 0, count = responses.Count; i < count; i++)
                {
                    string? response = responses[i];
                    if (response == null)
                        continue;

                    delivered = true;
                    if (!string.IsNullOrWhiteSpace(response))
                        HandleRemoteQueryResponse(response);
                }

                return delivered;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        string marker = RegisterServerProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            (BotMainHandler handler, string marker, Action onCompleted) = ((BotMainHandler Handler, string Marker, Action OnCompleted))state!;
            if (handler.TryCancelServerProbeMarker(marker))
                onCompleted();
        }, (this, marker, onProbeCompleted));

        try
        {
            string escapedMarker = MinecraftCommandBuilder.EscapeJson(marker);
            probeCommands.Add("data modify storage " + ProbeMarkerStorage + " " + ProbeMarkerPath + " set value \"" + escapedMarker + "\"");
            probeCommands.Add("data get storage " + ProbeMarkerStorage + " " + ProbeMarkerPath);
            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueServerProbeMarkerFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelServerProbeMarker(marker))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (TryCancelServerProbeMarker(marker))
                onProbeCompleted();

            throw;
        }
    }

    private string RegisterServerProbeMarker(Action onProbeCompleted)
    {
        string marker = _serverProbeMarkerSessionPrefix + Interlocked.Increment(ref _serverProbeMarkerCounter).ToString(CultureInfo.InvariantCulture);
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers[marker] = onProbeCompleted;
            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
        }

        return marker;
    }

    private void QueueServerProbeMarkerFallback(string marker, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        _ = CompleteAfterDelayAsync();

        async Task CompleteAfterDelayAsync()
        {
            try
            {
                await Task.Delay(ServerProbeMarkerFallbackTimeout, cancellationToken).ConfigureAwait(false);
                if (TryCancelServerProbeMarker(marker))
                    onProbeCompleted();
            }
            catch (OperationCanceledException)
            {
                if (TryCancelServerProbeMarker(marker))
                    onProbeCompleted();
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Server probe marker fallback failed", ex);
            }
        }
    }

    private bool TryCancelServerProbeMarker(string marker)
    {
        lock (_serverProbeMarkerGate)
        {
            bool removed = _pendingServerProbeMarkers.Remove(marker);
            if (removed)
                Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);

            return removed;
        }
    }

    private bool TryHandleServerProbeMarkerLine(string line)
    {
        if (Volatile.Read(ref _pendingServerProbeMarkerCount) <= 0)
            return false;

        string marker = ExtractServerProbeMarker(line);
        if (marker.Length == 0)
            return false;

        Action? onCompleted;
        lock (_serverProbeMarkerGate)
        {
            if (!_pendingServerProbeMarkers.Remove(marker, out onCompleted))
                return false;

            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
        }

        onCompleted();
        return true;
    }

    private static string ExtractServerProbeMarker(string line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        int markerIndex = line.IndexOf(ProbeMarkerPrefix, StringComparison.Ordinal);
        if (markerIndex < 0)
            return string.Empty;

        int end = markerIndex + ProbeMarkerPrefix.Length;
        while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '_'))
            end++;

        return line[markerIndex..end];
    }

    private static bool IsUnexpectedCommandErrorLine(string line)
        => !string.IsNullOrEmpty(line) &&
           (line.Contains("An unexpected error occurred trying to execute that command", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("An unexpected error occurred while trying to execute that command", StringComparison.OrdinalIgnoreCase));

    private static bool IsMinecraftCommandErrorContextLine(string line)
        => !string.IsNullOrEmpty(line) &&
           (line.Contains("Command exception:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Failed to execute", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Unable to execute command", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Error trying to execute", StringComparison.OrdinalIgnoreCase));

    private void RememberSuppressedServerLogContextLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_suppressedServerLogContextGate)
        {
            if (_suppressedServerLogContextLines.Count >= 8)
                _suppressedServerLogContextLines.Dequeue();

            _suppressedServerLogContextLines.Enqueue(line);
        }
    }

    private void ShowSuppressedServerLogContextLines()
    {
        string[] lines;
        lock (_suppressedServerLogContextGate)
        {
            if (_suppressedServerLogContextLines.Count == 0)
                return;

            lines = [.. _suppressedServerLogContextLines];
            _suppressedServerLogContextLines.Clear();
        }

        foreach (string contextLine in lines)
        {
            _shellWindow?.AddServerLogLine(contextLine);
        }
    }

    private static bool ShouldSuppressServerLogLine(
        string line,
        in ServerLogLineFlags flags,
        bool isCommandParserError,
        bool isUnexpectedCommandError,
        bool isMinecraftCommandErrorContext,
        bool isSidebarObjectiveIssue)
    {
        if (flags.HasEntityData)
            return true;

        if (string.IsNullOrEmpty(line))
            return false;

        if (isUnexpectedCommandError || isCommandParserError || isMinecraftCommandErrorContext)
            return false;

        if (!flags.HasObjective &&
            !flags.HasPlayerList &&
            !flags.HasTcMarker &&
            !flags.HasHealth &&
            !flags.HasDisplaySlot)
        {
            return false;
        }

        return
            line.Contains("An objective already exists by that name", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set [Player List:] for ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_playerlist]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Created new objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot sidebar to show objective Player List:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot list to show objective Health", StringComparison.OrdinalIgnoreCase) ||
            isSidebarObjectiveIssue ||
            (flags.HasTcPlayerList && flags.hasAlreadyExists) ||
            (flags.HasTcHealth && flags.hasAlreadyExists) ||
            flags.HasTcDeaths ||
            line.Contains("Created new objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Changed render type of [Health]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommandParserErrorLine(string line)
        => !string.IsNullOrEmpty(line) &&
           line.Contains("Unknown or incomplete command", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("See below for error", StringComparison.OrdinalIgnoreCase);

    private bool TryConsumeServerCommandErrorContextLine()
    {
        while (true)
        {
            int pending = Volatile.Read(ref _serverCommandErrorContextLines);
            if (pending <= 0)
                return false;

            if (Interlocked.CompareExchange(ref _serverCommandErrorContextLines, pending - 1, pending) == pending)
                return true;
        }
    }

    private void HandleRemoteQueryResponse(string response)
    {
        if (!response.Contains('\n') && !response.Contains('\r'))
        {
            HandleRemoteQueryResponseLine(response);
            return;
        }

        using StringReader reader = new(response);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            HandleRemoteQueryResponseLine(line);
        }
    }

    private void HandleRemoteQueryResponseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        ServerLogLineFlags flags = new(line);
        if (flags.HasEntityData)
            HandleEntityDataLine(line);
        else if (flags.HasGameMode && TryHandlePlayerGamemodeLine(line, out string playerName, out int gameType))
            HandlePlayerGamemodeResult(playerName, gameType);

        RecordServerLineForStatistics(line, flags.HasTcDeaths);
    }

    private async Task ReadServerOutputAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                if (TryHandleServerProbeMarkerLine(line))
                    continue;

                ServerLogLineFlags flags = new(line);
                if (flags.HasProbeMarkerStorage)
                    continue;

                if (flags.HasEntityData)
                {
                    HandleEntityDataLine(line);
                }
                else if (flags.HasGameMode)
                {
                    if (TryHandlePlayerGamemodeLine(line, out string playerName, out int gameType))
                        HandlePlayerGamemodeResult(playerName, gameType);
                }

                bool mightContainCommandError = line.Contains("command", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("execute", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase);
                bool isCommandParserError = mightContainCommandError && IsCommandParserErrorLine(line);
                bool isUnexpectedCommandError = mightContainCommandError && IsUnexpectedCommandErrorLine(line);
                bool isMinecraftCommandErrorContext = mightContainCommandError && IsMinecraftCommandErrorContextLine(line);
                bool isSidebarObjectiveIssue = IsSidebarObjectiveIssueLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext);

                HandleServerReadyState(line);
                RecoverSidebarInitializationFromServerLine(isSidebarObjectiveIssue);
                RecordServerLineForStatistics(line, flags.HasTcDeaths);

                bool showCommandErrorContext = TryConsumeServerCommandErrorContextLine();
                if (isCommandParserError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 1);
                }
                else if (isUnexpectedCommandError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 8);
                }

                bool suppressServerLogLine = ShouldSuppressServerLogLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext,
                    isSidebarObjectiveIssue);
                bool suppressOnlinePlayersLogLine = !suppressServerLogLine && ShouldSuppressOnlinePlayersLogLine(line);
                bool shouldShowLogLine = showCommandErrorContext ||
                    (!suppressServerLogLine && !suppressOnlinePlayersLogLine);

                if (isUnexpectedCommandError && shouldShowLogLine)
                {
                    ShowSuppressedServerLogContextLines();
                }

                if (shouldShowLogLine)
                {
                    _shellWindow?.AddServerLogLine(line);
                }
                else if (suppressServerLogLine && !flags.HasEntityData)
                {
                    RememberSuppressedServerLogContextLine(line);
                }

                CaptureOnlinePlayers(line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Server output reader failed", ex));
        }
    }

    private async Task ReadServerErrorAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                _shellWindow?.AddServerLogLine("[stderr] " + line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Server error reader failed", ex));
        }
    }

    private async Task RunPlayerRosterLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (MultiTargetingEnabled)
                    QueueOnlinePlayerSnapshotRefresh();

                if (MultiplayerEnabled)
                    await RefreshPlayerSidebarAsync(cancellationToken).ConfigureAwait(false);

                QueueTrackedPlayerGamemodeRefreshForStatistics();
                QueueTrackedPlayerDeathScoreRefreshForStatistics();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                RecordPlayerSidebarRefreshFailure(ex);
            }

            try
            {
                await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool AddKnownPlayer(string playerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayer))
            return false;

        lock (_playerGate)
        {
            int index = FindSortedPlayerIndex(_knownPlayers, normalizedPlayer);
            if (index >= 0)
                return false;

            List<string> players = [.. _knownPlayers];
            players.Insert(~index, normalizedPlayer);
            _knownPlayers = players;
        }

        if (MultiplayerEnabled)
            QueuePlayerSidebarRefresh();

        return true;
    }

    private bool RemoveKnownPlayer(string playerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayer))
            return false;

        lock (_playerGate)
        {
            int index = FindSortedPlayerIndex(_knownPlayers, normalizedPlayer);
            if (index < 0)
                return false;

            if (_knownPlayers.Count == 1)
            {
                _knownPlayers = [];
            }
            else
            {
                List<string> players = [.. _knownPlayers];
                players.RemoveAt(index);
                _knownPlayers = players;
            }
        }

        if (MultiplayerEnabled)
            QueuePlayerSidebarRefresh();

        return true;
    }

    private void CaptureOnlinePlayers(string line)
    {
        if (string.IsNullOrEmpty(line) ||
            (!line.Contains("game", StringComparison.OrdinalIgnoreCase) &&
             !line.Contains("online", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        int joinedMarker = line.IndexOf(" joined the game", StringComparison.OrdinalIgnoreCase);
        if (joinedMarker >= 0)
        {
            string joinedPlayer = ExtractPlayerEventName(line, joinedMarker);
            if (joinedPlayer.Length > 0)
            {
                if (!AddKnownPlayer(joinedPlayer) && MultiplayerEnabled)
                    QueuePlayerSidebarRefresh();

                RemoveSpectatorPlayer(joinedPlayer);

                RecordPlayerJoinForStatistics(joinedPlayer);
                QueueTrackedPlayerGamemodeRefreshForStatistics(joinedPlayer);
                QueueOnlinePlayerSnapshotRefresh();
            }

            return;
        }

        int leftMarker = line.IndexOf(" left the game", StringComparison.OrdinalIgnoreCase);
        if (leftMarker >= 0)
        {
            string leftPlayer = ExtractPlayerEventName(line, leftMarker);
            if (leftPlayer.Length > 0)
            {
                if (!RemoveKnownPlayer(leftPlayer) && MultiplayerEnabled)
                    QueuePlayerSidebarRefresh();

                RemoveSpectatorPlayer(leftPlayer);

                RecordPlayerLeaveForStatistics(leftPlayer);
                QueueOnlinePlayerSnapshotRefresh();
            }

            return;
        }

        if (!TryParsePlayerListResponse(line, false, out List<string> players))
            return;

        ApplyOnlinePlayerSnapshot(players);
        CompleteOnlinePlayerSnapshotRequest(true);
    }

    private void QueueTrackedPlayerGamemodeRefreshForStatistics()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        lock (_playerGate)
        {
            if (!ContainsPlayer(_knownPlayers, playerName))
                return;
        }

        QueueTrackedPlayerGamemodeRefreshForStatistics(playerName);
    }

    private void QueueTrackedPlayerGamemodeRefreshForStatistics(string playerName)
    {
        if (!StatisticsEnabled ||
            !IsTrackingSurvivalPlayer(playerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                _ = await QueryPlayerProbeAsync<int?>(
                    playerName,
                    _spectatorProbeGate,
                    _pendingGameTypeRequests,
                    (waiter, ct) => SendInternalProbeCommandAsync($"data get entity {playerName} playerGameType", () => waiter.TrySetResult(default), ct),
                    defaultResult: null,
                    t).ConfigureAwait(false);
            },
            () => Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 0),
            "Tracked player gamemode refresh failed",
            token: token);
    }

    private void QueueTrackedPlayerRespawnPositionRefreshForStatistics(string playerName)
    {
        if (!ShouldRefreshTrackedPlayerRespawnPositionForStatistics(playerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            async t =>
            {
                await Task.Delay(250, t).ConfigureAwait(false);
                if (await QueryPlayerRespawnPositionAsync(playerName, t).ConfigureAwait(false))
                    RecordTrackedPlayerRespawnPositionForStatistics(playerName);
            },
            () => Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 0),
            "Tracked player respawn position refresh failed",
            token: token);
    }

    private void QueueDeathScoreObjectiveInitialization()
    {
        if (!StatisticsEnabled ||
            Volatile.Read(ref _deathScoreObjectiveReady) != 0 ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token) ||
            Interlocked.Exchange(ref _deathScoreObjectiveQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            EnsureDeathScoreObjectiveReadyAsync,
            () => Interlocked.Exchange(ref _deathScoreObjectiveQueued, 0),
            "Death score objective initialization failed",
            token: token);
    }

    private async Task<bool> EnsureDeathScoreObjectiveReadyAsync(CancellationToken cancellationToken)
    {
        if (!StatisticsEnabled)
            return false;

        if (Volatile.Read(ref _deathScoreObjectiveReady) != 0)
            return true;

        await _deathScoreObjectiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _deathScoreObjectiveReady) != 0)
                return true;

            TaskCompletionSource<bool> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool sent = await SendInternalProbeCommandsAsync(
                [
                    "scoreboard objectives add " + DeathScoreObjective + " deathCount"
                ],
                () => waiter.TrySetResult(true),
                cancellationToken).ConfigureAwait(false);

            if (!sent)
                return false;

            await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            ResetDeathScoreBaselineForStatistics();
            Volatile.Write(ref _deathScoreObjectiveReady, 1);
            return true;
        }
        finally
        {
            _deathScoreObjectiveGate.Release();
        }
    }

    private void QueueTrackedPlayerDeathScoreRefreshForStatistics()
    {
        if (!StatisticsEnabled)
            return;

        string playerName = _currentStreamerMinecraftName;
        if (playerName.Length == 0)
            return;

        QueueTrackedPlayerDeathScoreRefreshForStatistics(playerName);
    }

    private void QueueTrackedPlayerDeathScoreRefreshForStatistics(string playerName)
    {
        if (!StatisticsEnabled ||
            !MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayerName) ||
            !IsTrackingSurvivalPlayer(normalizedPlayerName) ||
            !TryGetQueuedSessionToken(requireMultiplayer: false, out CancellationToken token))
        {
            return;
        }

        if (Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 1) != 0)
            return;

        RunQueuedSessionWork(
            async t =>
            {
                if (await EnsureDeathScoreObjectiveReadyAsync(t).ConfigureAwait(false))
                {
                    string command = "scoreboard players get " + normalizedPlayerName + " " + DeathScoreObjective;
                    if (RemoteControlEnabled)
                    {
                        string? response = await ExecuteRemoteServerQueryAsync(command, t).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(response))
                            HandleRemoteQueryResponse(response);
                    }
                    else
                    {
                        await SendServerCommandAsync(command, t).ConfigureAwait(false);
                    }
                }
            },
            () => Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 0),
            "Tracked player death score refresh failed",
            token: token);
    }

    private void RecordPlayerSidebarRefreshFailure(Exception ex)
    {
        DateTime now = DateTime.UtcNow;
        lock (_playerGate)
        {
            if (now - _lastPlayerSidebarRefreshErrorUtc < PlayerSidebarRefreshErrorLogInterval)
                return;

            _lastPlayerSidebarRefreshErrorUtc = now;
        }

        _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Player sidebar refresh failed", ex));
    }

    private void RecordPlayerRosterChanges(List<string> previousPlayers, List<string> currentPlayers)
    {
        int previousIndex = 0;
        int currentIndex = 0;

        while (previousIndex < previousPlayers.Count && currentIndex < currentPlayers.Count)
        {
            string previous = previousPlayers[previousIndex];
            string current = currentPlayers[currentIndex];
            int comparison = PlayerNameComparer.Compare(previous, current);
            if (comparison == 0)
            {
                previousIndex++;
                currentIndex++;
                continue;
            }

            if (comparison < 0)
            {
                RemoveSpectatorPlayer(previous);
                RecordPlayerLeaveForStatistics(previous);
                previousIndex++;
                continue;
            }

            RecordPlayerJoinForStatistics(current);
            currentIndex++;
        }

        for (; previousIndex < previousPlayers.Count; previousIndex++)
        {
            string previous = previousPlayers[previousIndex];
            RemoveSpectatorPlayer(previous);
            RecordPlayerLeaveForStatistics(previous);
        }

        for (; currentIndex < currentPlayers.Count; currentIndex++)
            RecordPlayerJoinForStatistics(currentPlayers[currentIndex]);
    }

    private static List<string> ParseOnlinePlayers(ReadOnlySpan<char> remainder)
    {
        List<string> players = [];
        if (remainder.IsEmpty)
            return players;

        int start = 0;
        for (int i = 0; i <= remainder.Length; i++)
        {
            if (i < remainder.Length && remainder[i] != ',')
                continue;

            if (MinecraftNameHelper.TryNormalizePlayerName(remainder[start..i], out string normalizedPlayer))
                players.Add(normalizedPlayer);

            start = i + 1;
        }

        SortedListHelper.SortAndDeduplicate(players, PlayerNameComparer);
        return players;
    }

    private static bool TryParsePlayerListResponse(string response, bool allowFallbackColon, out List<string> players)
    {
        players = [];
        ReadOnlySpan<char> text = (response ?? string.Empty).AsSpan().Trim();
        if (text.IsEmpty)
            return false;

        int marker = text.IndexOf("players online".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("player online".AsSpan(), StringComparison.OrdinalIgnoreCase);

        if (marker >= 0)
        {
            int colon = text[marker..].IndexOf(':');
            if (colon >= 0)
            {
                players = ParseOnlinePlayers(text[(marker + colon + 1)..]);
                return true;
            }

            return TryParseFirstInt(text[..marker], out int count) && count == 0;
        }

        if (!allowFallbackColon)
            return false;

        int fallbackColon = text.LastIndexOf(':');
        if (fallbackColon < 0)
            return false;

        players = ParseOnlinePlayers(text[(fallbackColon + 1)..]);
        return true;
    }

    private static bool TryParseFirstInt(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                continue;

            int start = i;
            while (i < text.Length && char.IsDigit(text[i]))
                i++;

            return int.TryParse(text[start..i], out value);
        }

        return false;
    }

    private static string ExtractPlayerEventName(string line, int markerIndex)
    {
        if (string.IsNullOrEmpty(line) || markerIndex <= 0)
            return string.Empty;

        return TrimPrefixAfterLastColon(line, 0, markerIndex);
    }

    private static string TrimPrefixAfterLastColon(string value, int start, int length)
    {
        string segment = TextSegmentHelper.TrimSegment(value, start, length);
        if (segment.Length == 0)
            return string.Empty;

        int colon = segment.LastIndexOf(':');
        return colon < 0 || colon == segment.Length - 1
            ? segment
            : TextSegmentHelper.TrimSegment(segment, colon + 1, segment.Length - colon - 1);
    }

}
