using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly TimeSpan SpectatorRefreshInterval = TimeSpan.FromSeconds(5);
    private const string SpectatorGamemodeProbeCommand = "execute as @a run data get entity @s playerGameType";

    private readonly Lock _spectatorProbeGate = new();
    private readonly Lock _selectedItemProbeGate = new();
    private readonly Lock _maxHealthProbeGate = new();
    private readonly Lock _respawnProbeGate = new();
    private readonly SemaphoreSlim _spectatorRefreshGate = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<int?>> _pendingGamemodeRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingSelectedItemRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<double?>> _pendingMaxHealthRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingHeartAttributeRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingRespawnRequests = new(PlayerNameComparer);
    private HashSet<string> _spectatorPlayers = new(PlayerNameComparer);
    private DateTime _lastSpectatorRefreshUtc = DateTime.MinValue;
    private bool _spectatorSnapshotInitialized;
    private int _spectatorStateRefreshQueued;

    private async Task<TResult> QueryPlayerAsync<TResult>(
        string playerName,
        Lock gate,
        Dictionary<string, TaskCompletionSource<TResult>> pendingRequests,
        Func<Action, CancellationToken, Task<bool>> sendProbe,
        CancellationToken cancellationToken)
    {
        if (!MinecraftNameHelper.IsValidPlayerName(playerName))
            return default!;

        TaskCompletionSource<TResult> waiter;
        bool createdWaiter = false;
        lock (gate)
            if (!pendingRequests.TryGetValue(playerName, out waiter!))
            {
                pendingRequests[playerName] = waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                createdWaiter = true;
            }

        try
        {
            if (createdWaiter)
            {
                void CompleteProbe() => CompleteRequest(playerName, gate, pendingRequests, waiter, default!);
                _ = SendPlayerQueryAsync(sendProbe, CompleteProbe, _sessionCts?.Token ?? CancellationToken.None);
            }
            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default!;
        }
    }

    internal static void CompleteRequest<TResult>(
        string playerName,
        Lock gate,
        Dictionary<string, TaskCompletionSource<TResult>> pendingRequests,
        TaskCompletionSource<TResult> waiter,
        TResult result)
    {
        lock (gate)
            if (pendingRequests.TryGetValue(playerName, out TaskCompletionSource<TResult>? current) && ReferenceEquals(current, waiter))
                pendingRequests.Remove(playerName);
        waiter.TrySetResult(result);
    }

    private static async Task<bool> SendPlayerQueryAsync(
        Func<Action, CancellationToken, Task<bool>> sendProbe,
        Action completeProbe,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await sendProbe(completeProbe, cancellationToken).ConfigureAwait(false))
                return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Player probe send failed", ex);
        }

        completeProbe();
        return false;
    }

    public Task<double?> QueryMaxHealthAsync(string playerName, CancellationToken cancellationToken)
        => QueryPlayerAsync<double?>(playerName, _maxHealthProbeGate, _pendingMaxHealthRequests,
            (complete, ct) => SendProbeAsync("attribute " + MinecraftCommandBuilder.SinglePlayerSelector(playerName) + " " + (UsesModernAttributeIDs ? "minecraft:max_health" : "minecraft:generic.max_health") + " get", complete, ct), cancellationToken);

    private Task<string?> QueryEntityDataAsync(
        string playerName, string path, Lock gate,
        Dictionary<string, TaskCompletionSource<string?>> pendingRequests,
        CancellationToken cancellationToken)
        => QueryPlayerAsync<string?>(playerName, gate, pendingRequests,
            (complete, ct) => SendProbeAsync("data get entity " + MinecraftCommandBuilder.SinglePlayerSelector(playerName) + " " + path, complete, ct), cancellationToken);

    public Task<string?> QueryHeartModifiersAsync(string playerName, CancellationToken cancellationToken)
        => QueryEntityDataAsync(playerName, UsesModernEntityAttributeNbt ? "attributes" : "Attributes",
            _maxHealthProbeGate, _pendingHeartAttributeRequests, cancellationToken);

    public Task<string?> QueryItemAsync(string playerName, CancellationToken cancellationToken)
        => QueryEntityDataAsync(playerName, "SelectedItem", _selectedItemProbeGate, _pendingSelectedItemRequests, cancellationToken);

    public async Task<Dictionary<string, string?>> QueryItemsAsync(List<string> players, CancellationToken cancellationToken)
    {
        players = SortedListHelper.NormalizePlayerNames(players, PlayerNameComparer);
        Dictionary<string, string?> results = new(players.Count, PlayerNameComparer);
        if (players.Count == 0)
            return results;

        (string Player, TaskCompletionSource<string?> Waiter)[] waiters =
            new (string, TaskCompletionSource<string?>)[players.Count];
        List<(string Player, TaskCompletionSource<string?> Waiter)> createdWaiters = new(players.Count);
        lock (_selectedItemProbeGate)
        {
            for (int i = 0; i < players.Count; i++)
            {
                string player = players[i];
                if (!_pendingSelectedItemRequests.TryGetValue(player, out TaskCompletionSource<string?>? waiter))
                {
                    _pendingSelectedItemRequests[player] = waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    createdWaiters.Add((player, waiter));
                }
                waiters[i] = (player, waiter!);
            }
        }

        try
        {
            Task<string?>[] tasks = new Task<string?>[waiters.Length];
            for (int i = 0; i < waiters.Length; i++)
                tasks[i] = waiters[i].Waiter.Task;

            if (createdWaiters.Count > 0)
            {
                string[] commands = new string[createdWaiters.Count];
                for (int i = 0; i < createdWaiters.Count; i++)
                    commands[i] = "data get entity " + MinecraftCommandBuilder.SinglePlayerSelector(createdWaiters[i].Player) + " SelectedItem";

                await SendPlayerQueryAsync(
                    (complete, ct) => SendProbesAsync(commands, complete, ct),
                    () =>
                    {
                        for (int i = 0; i < createdWaiters.Count; i++)
                            CompleteRequest(createdWaiters[i].Player, _selectedItemProbeGate, _pendingSelectedItemRequests, createdWaiters[i].Waiter, null);
                    },
                    _sessionCts?.Token ?? CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(ServerProbeMarkerFallbackTimeout + TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            for (int i = 0; i < waiters.Length; i++)
            {
                (string player, TaskCompletionSource<string?> waiter) = waiters[i];
                Task<string?> task = waiter.Task;
                results[player] = task.IsCompletedSuccessfully ? task.Result : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            for (int i = 0; i < waiters.Length; i++)
                results[waiters[i].Player] = null;
        }
        return results;
    }

    private Task<bool> QueryRespawnAsync(string playerName, CancellationToken cancellationToken)
    {
        string selector = "@a[name=\"" + MinecraftCommandBuilder.EscapeSelector(playerName) + "\",limit=1,gamemode=!spectator,nbt={DeathTime:0s}]";
        return QueryPlayerAsync(
            playerName,
            _respawnProbeGate,
            _pendingRespawnRequests,
            (complete, ct) => SendProbeAsync("data get entity " + selector + " Pos", complete, ct),
            cancellationToken);
    }

    private TaskCompletionSource<int?>[] CreateGamemodeWaiters(List<string> players)
    {
        TaskCompletionSource<int?>[] waiters = new TaskCompletionSource<int?>[players.Count];
        lock (_spectatorProbeGate)
        {
            for (int i = 0; i < players.Count; i++)
            {
                string player = players[i];
                if (!_pendingGamemodeRequests.TryGetValue(player, out TaskCompletionSource<int?>? waiter))
                {
                    waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingGamemodeRequests[player] = waiter;
                }
                waiters[i] = waiter;
            }
        }

        return waiters;
    }

    private static Task WaitForGamemodesAsync(TaskCompletionSource<int?>[] waiters, CancellationToken cancellationToken)
    {
        if (waiters.Length == 0)
            return Task.CompletedTask;
        if (waiters.Length == 1)
            return waiters[0].Task.WaitAsync(cancellationToken);

        Task<int?>[] tasks = new Task<int?>[waiters.Length];
        for (int i = 0; i < waiters.Length; i++)
            tasks[i] = waiters[i].Task;
        return Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private async Task RefreshSpectatorsAsync(List<string> players, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        players = SortedListHelper.NormalizePlayerNames(players, PlayerNameComparer);

        await _spectatorRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (players.Count == 0)
            {
                lock (_spectatorProbeGate)
                {
                    _spectatorPlayers.Clear();
                    _lastSpectatorRefreshUtc = DateTime.UtcNow;
                    _spectatorSnapshotInitialized = true;
                }
                return;
            }

            lock (_spectatorProbeGate)
            {
                if (_spectatorSnapshotInitialized && (DateTime.UtcNow - _lastSpectatorRefreshUtc) < SpectatorRefreshInterval)
                    return;
            }

            TaskCompletionSource<int?>[] waiters = CreateGamemodeWaiters(players);
            bool refreshCompleted = false;
            if (await SendPlayerQueryAsync(
                (complete, ct) => SendProbeAsync(SpectatorGamemodeProbeCommand, complete, ct),
                () =>
                {
                    for (int i = 0; i < waiters.Length; i++)
                        CompleteRequest(players[i], _spectatorProbeGate, _pendingGamemodeRequests, waiters[i], default);
                },
                _sessionCts?.Token ?? CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                await WaitForGamemodesAsync(waiters, cancellationToken).ConfigureAwait(false);
                refreshCompleted = true;
            }

            bool canReplaceSpectatorSnapshot = refreshCompleted;
            for (int i = 0; i < waiters.Length; i++)
            {
                Task<int?> task = waiters[i].Task;
                if (!task.IsCompletedSuccessfully || !task.Result.HasValue)
                {
                    canReplaceSpectatorSnapshot = false;
                    break;
                }
            }

            if (canReplaceSpectatorSnapshot)
            {
                lock (_spectatorProbeGate)
                {
                    _spectatorPlayers.Clear();
                    for (int i = 0; i < waiters.Length; i++)
                        if (waiters[i].Task.Result == 3)
                            _spectatorPlayers.Add(players[i]);
                    _lastSpectatorRefreshUtc = DateTime.UtcNow;
                    _spectatorSnapshotInitialized = true;
                }
                return;
            }

            HashSet<string> nextSpectators;
            lock (_spectatorProbeGate)
                nextSpectators = new HashSet<string>(_spectatorPlayers, PlayerNameComparer);

            for (int i = 0; i < waiters.Length; i++)
            {
                Task<int?> task = waiters[i].Task;
                if (!task.IsCompletedSuccessfully || !task.Result.HasValue)
                    continue;
                if (task.Result.Value == 3)
                    nextSpectators.Add(players[i]);
                else
                    nextSpectators.Remove(players[i]);
            }

            lock (_spectatorProbeGate)
            {
                _spectatorPlayers = nextSpectators;
                if (refreshCompleted)
                {
                    _lastSpectatorRefreshUtc = DateTime.UtcNow;
                    _spectatorSnapshotInitialized = true;
                }
            }
        }
        finally
        {
            _spectatorRefreshGate.Release();
        }
    }

    public async Task<List<string>> GetPlayersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> online = await RefreshPlayersAsync(cancellationToken).ConfigureAwait(false);

        bool hasSpectatorSnapshot;
        bool spectatorSnapshotIsFresh;
        lock (_spectatorProbeGate)
        {
            hasSpectatorSnapshot = _spectatorSnapshotInitialized;
            spectatorSnapshotIsFresh = DateTime.UtcNow - _lastSpectatorRefreshUtc < SpectatorRefreshInterval;
        }

        if (!hasSpectatorSnapshot)
        {
            await RefreshSpectatorsAsync(online, cancellationToken).ConfigureAwait(false);
        }
        else if (!spectatorSnapshotIsFresh)
        {
            QueueSpectators(online, cancellationToken);
        }

        return GetTargets(online);
    }

    private List<string> GetTargets(List<string> online)
    {
        lock (_spectatorProbeGate)
        {
            if (_spectatorPlayers.Count == 0)
                return online;

            List<string> targetable = new(online.Count);
            foreach (string player in online)
            {
                if (!_spectatorPlayers.Contains(player))
                    targetable.Add(player);
            }

            return targetable;
        }
    }

    private void QueueSpectators(List<string> players, CancellationToken cancellationToken)
    {
        CancellationToken refreshToken = _sessionCts?.Token ?? cancellationToken;
        if (refreshToken.IsCancellationRequested || Interlocked.CompareExchange(ref _spectatorStateRefreshQueued, 1, 0) != 0)
            return;

        List<string> snapshot = [.. players];
        TrackTask(Task.Run(async () =>
        {
            try
            {
                await RefreshSpectatorsAsync(snapshot, refreshToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Background spectator refresh failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _spectatorStateRefreshQueued, 0);
            }
        }, CancellationToken.None));
    }

    private void RemoveSpectator(string playerName)
    {
        lock (_spectatorProbeGate)
        {
            _spectatorPlayers.Remove(playerName);
        }
    }
}
