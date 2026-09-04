using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static readonly TimeSpan SpectatorRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly string[] SpectatorGameTypeProbeCommands = ["execute as @a run data get entity @s playerGameType"];

    private readonly Lock _spectatorProbeGate = new();
    private readonly Lock _selectedItemProbeGate = new();
    private readonly Lock _respawnPositionProbeGate = new();
    private readonly SemaphoreSlim _spectatorRefreshGate = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<int?>> _pendingGameTypeRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingSelectedItemRequests = new(PlayerNameComparer);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingRespawnPositionRequests = new(PlayerNameComparer);
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
        {
            if (!pendingRequests.TryGetValue(playerName, out waiter!))
            {
                waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pendingRequests[playerName] = waiter;
                createdWaiter = true;
            }
        }

        try
        {
            if (createdWaiter)
            {
                void CompleteProbe() => CompletePlayer(playerName, gate, pendingRequests, waiter, default!);
                _ = SendPlayerQueryAsync(sendProbe, CompleteProbe, _sessionCts?.Token ?? CancellationToken.None);
            }

            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default!;
        }
    }

    internal static void CompletePlayer<TResult>(
        string playerName,
        Lock gate,
        Dictionary<string, TaskCompletionSource<TResult>> pendingRequests,
        TaskCompletionSource<TResult> waiter,
        TResult result)
    {
        lock (gate)
        {
            if (pendingRequests.TryGetValue(playerName, out TaskCompletionSource<TResult>? current) && ReferenceEquals(current, waiter))
                pendingRequests.Remove(playerName);
        }

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
        catch (Exception)
        {
        }

        completeProbe();
        return false;
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
                                CompletePlayer(player, _selectedItemProbeGate, _pendingSelectedItemRequests, waiter, null);
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
                        CompletePlayer(entry.Key, _spectatorProbeGate, _pendingGameTypeRequests, entry.Value, default);
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
                if (!task.IsCompleted || task.IsCanceled || task.IsFaulted || !task.Result.HasValue)
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
                if (!task.IsCompleted || task.IsCanceled || task.IsFaulted)
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
