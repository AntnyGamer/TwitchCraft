using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly TimeSpan SpectatorRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly string[] SpectatorGameTypeProbeCommands = ["execute as @a run data get entity @s playerGameType"];

    private readonly Lock _spectatorProbeGate = new();
    private readonly Lock _selectedItemProbeGate = new();
    private readonly Lock _maxHealthProbeGate = new();
    private readonly Lock _healthModifierProbeGate = new();
    private readonly Lock _respawnPositionProbeGate = new();
    private readonly SemaphoreSlim _spectatorRefreshGate = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<int?>> _pendingGameTypeRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingSelectedItemRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<double?>> _pendingMaxHealthRequests = new(PlayerNameComparer);
    private readonly Dictionary<(string Player, string ID), TaskCompletionSource<bool?>> _pendingHealthModifierRequests = [];
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingRespawnPositionRequests = new(PlayerNameComparer);
    private HashSet<string> _spectatorPlayers = new(PlayerNameComparer);
    private DateTime _lastSpectatorRefreshUtc = DateTime.MinValue;
    private bool _spectatorSnapshotInitialized;
    private int _spectatorStateRefreshQueued;

    private Task<TResult> QueryPlayerAsync<TResult>(
        string playerName,
        Lock gate,
        Dictionary<string, TaskCompletionSource<TResult>> pendingRequests,
        Func<Action, CancellationToken, Task<bool>> sendProbe,
        CancellationToken cancellationToken)
        => MinecraftNameHelper.IsValidPlayerName(playerName)
            ? QueryAsync(playerName, gate, pendingRequests, sendProbe, cancellationToken)
            : Task.FromResult(default(TResult)!);

    private async Task<TResult> QueryAsync<TKey, TResult>(
        TKey key,
        Lock gate,
        Dictionary<TKey, TaskCompletionSource<TResult>> pendingRequests,
        Func<Action, CancellationToken, Task<bool>> sendProbe,
        CancellationToken cancellationToken,
        bool allowAfterSessionCancellation = false) where TKey : notnull
    {
        TaskCompletionSource<TResult> waiter;
        bool createdWaiter = false;
        lock (gate)
            if (!pendingRequests.TryGetValue(key, out waiter!)) { pendingRequests[key] = waiter = new(TaskCreationOptions.RunContinuationsAsynchronously); createdWaiter = true; }

        try
        {
            if (createdWaiter)
            {
                void CompleteProbe() => CompleteRequest(key, gate, pendingRequests, waiter, default!);
                CancellationToken probeToken = allowAfterSessionCancellation && _sessionCts?.IsCancellationRequested == true ? CancellationToken.None : _sessionCts?.Token ?? CancellationToken.None;
                _ = SendPlayerQueryAsync(sendProbe, CompleteProbe, probeToken);
            }
            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return default!; }
    }

    internal static void CompleteRequest<TKey, TResult>(
        TKey key,
        Lock gate,
        Dictionary<TKey, TaskCompletionSource<TResult>> pendingRequests,
        TaskCompletionSource<TResult> waiter,
        TResult result) where TKey : notnull
    {
        lock (gate)
            if (pendingRequests.TryGetValue(key, out TaskCompletionSource<TResult>? current) && ReferenceEquals(current, waiter)) pendingRequests.Remove(key);
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

    public Task<bool?> QueryHealthModifierAsync(string playerName, string id, CancellationToken cancellationToken)
    {
        if (!MinecraftNameHelper.IsValidPlayerName(playerName) || string.IsNullOrWhiteSpace(id)) return Task.FromResult<bool?>(null);
        string attribute = UsesModernAttributeIDs ? "minecraft:max_health" : "minecraft:generic.max_health";
        return QueryAsync((playerName, id), _healthModifierProbeGate, _pendingHealthModifierRequests,
            (complete, ct) => SendProbeAsync("attribute " + MinecraftCommandBuilder.SinglePlayerSelector(playerName) + " " + attribute + " modifier value get " + id, complete, ct), cancellationToken, allowAfterSessionCancellation: true);
    }

    public Task<string?> QueryItemAsync(string playerName, CancellationToken cancellationToken)
    {
        string selector = MinecraftCommandBuilder.SinglePlayerSelector(playerName);
        return QueryPlayerAsync<string?>(
            playerName,
            _selectedItemProbeGate,
            _pendingSelectedItemRequests,
            (complete, ct) => SendProbeAsync("data get entity " + selector + " SelectedItem", complete, ct),
            cancellationToken);
    }

    public async Task<Dictionary<string, string?>> QueryItemsAsync(List<string> players, CancellationToken cancellationToken)
    {
        players = SortedListHelper.NormalizePlayerNames(players, PlayerNameComparer);
        Dictionary<string, string?> results = new(players.Count, PlayerNameComparer);
        if (players.Count == 0)
            return results;

        Dictionary<string, TaskCompletionSource<string?>> waiters = new(players.Count, PlayerNameComparer);
        List<string> createdWaiterPlayers = new(players.Count);
        lock (_selectedItemProbeGate)
        {
            foreach (string player in players)
            {
                if (!_pendingSelectedItemRequests.TryGetValue(player, out TaskCompletionSource<string?>? waiter))
                {
                    waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingSelectedItemRequests[player] = waiter;
                    createdWaiterPlayers.Add(player);
                }

                waiters[player] = waiter;
            }
        }

        try
        {
            Task<string?>[] tasks = new Task<string?>[waiters.Count];
            int taskIndex = 0;
            foreach (TaskCompletionSource<string?> waiter in waiters.Values)
                tasks[taskIndex++] = waiter.Task;

            if (createdWaiterPlayers.Count > 0)
            {
                string[] commands = new string[createdWaiterPlayers.Count];
                for (int i = 0; i < createdWaiterPlayers.Count; i++)
                {
                    commands[i] = "data get entity " + MinecraftCommandBuilder.SinglePlayerSelector(createdWaiterPlayers[i]) + " SelectedItem";
                }

                await SendPlayerQueryAsync(
                    (complete, ct) => SendProbesAsync(commands, complete, ct),
                    () =>
                    {
                        foreach (string player in createdWaiterPlayers)
                        {
                            if (waiters.TryGetValue(player, out TaskCompletionSource<string?>? waiter))
                                CompleteRequest(player, _selectedItemProbeGate, _pendingSelectedItemRequests, waiter, null);
                        }
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

            foreach (KeyValuePair<string, TaskCompletionSource<string?>> entry in waiters)
            {
                Task<string?> task = entry.Value.Task;
                results[entry.Key] = task.IsCompletedSuccessfully ? task.Result : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (string player in players)
                results[player] = null;
        }
        return results;
    }

    private Task<bool> QueryRespawnAsync(string playerName, CancellationToken cancellationToken)
    {
        string selector = "@a[name=\"" + MinecraftCommandBuilder.EscapeSelector(playerName) + "\",limit=1,gamemode=!spectator,nbt={DeathTime:0s}]";
        return QueryPlayerAsync(
            playerName,
            _respawnPositionProbeGate,
            _pendingRespawnPositionRequests,
            (complete, ct) => SendProbeAsync("data get entity " + selector + " Pos", complete, ct),
            cancellationToken);
    }

    private Dictionary<string, TaskCompletionSource<int?>> CreateGamemodeWaiters(List<string> players)
    {
        Dictionary<string, TaskCompletionSource<int?>> waiters = new(players.Count, PlayerNameComparer);
        lock (_spectatorProbeGate)
        {
            foreach (string player in players)
            {
                if (_pendingGameTypeRequests.TryGetValue(player, out TaskCompletionSource<int?>? waiter))
                {
                    waiters[player] = waiter;
                    continue;
                }

                waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingGameTypeRequests[player] = waiter;
                waiters[player] = waiter;
            }
        }

        return waiters;
    }

    private static Task WaitForGamemodesAsync(Dictionary<string, TaskCompletionSource<int?>> waiters, CancellationToken cancellationToken)
    {
        if (waiters.Count == 0)
            return Task.CompletedTask;

        Task<int?>[] tasks = new Task<int?>[waiters.Count];
        int index = 0;
        foreach (TaskCompletionSource<int?> waiter in waiters.Values)
            tasks[index++] = waiter.Task;

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

            Dictionary<string, TaskCompletionSource<int?>> waiters = CreateGamemodeWaiters(players);
            bool refreshCompleted = false;
            HashSet<string> nextSpectators;
            if (await SendPlayerQueryAsync(
                (complete, ct) => SendProbesAsync(SpectatorGameTypeProbeCommands, complete, ct),
                () =>
                {
                    foreach (KeyValuePair<string, TaskCompletionSource<int?>> entry in waiters)
                        CompleteRequest(entry.Key, _spectatorProbeGate, _pendingGameTypeRequests, entry.Value, default);
                },
                _sessionCts?.Token ?? CancellationToken.None).WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                await WaitForGamemodesAsync(waiters, cancellationToken).ConfigureAwait(false);
                refreshCompleted = true;
            }

            bool canReplaceSpectatorSnapshot = refreshCompleted;
            foreach (KeyValuePair<string, TaskCompletionSource<int?>> entry in waiters)
            {
                Task<int?> task = entry.Value.Task;
                if (!task.IsCompletedSuccessfully || !task.Result.HasValue)
                {
                    canReplaceSpectatorSnapshot = false;
                    break;
                }
            }

            if (canReplaceSpectatorSnapshot)
            {
                nextSpectators = new HashSet<string>(players.Count, PlayerNameComparer);
            }
            else
            {
                lock (_spectatorProbeGate)
                {
                    nextSpectators = new HashSet<string>(_spectatorPlayers, PlayerNameComparer);
                }
            }

            foreach (KeyValuePair<string, TaskCompletionSource<int?>> entry in waiters)
            {
                Task<int?> task = entry.Value.Task;
                if (!task.IsCompletedSuccessfully)
                    continue;

                int? gameType = task.Result;
                if (!gameType.HasValue)
                    continue;

                if (gameType.Value == 3)
                    nextSpectators.Add(entry.Key);
                else if (!canReplaceSpectatorSnapshot)
                    nextSpectators.Remove(entry.Key);
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
        RunSessionWork(
            t => RefreshSpectatorsAsync(snapshot, t),
            () => Interlocked.Exchange(ref _spectatorStateRefreshQueued, 0),
            "Background spectator refresh failed",
            token: refreshToken);
    }

    private void RemoveSpectator(string playerName)
    {
        lock (_spectatorProbeGate)
        {
            _spectatorPlayers.Remove(playerName);
        }
    }
}
