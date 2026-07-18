using System;
using System.Collections.Generic;
using System.Globalization;
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

    private static bool TryParseEntityDataLine(string line, out string playerName, out string data)
    {
        playerName = string.Empty;
        data = string.Empty;

        if (string.IsNullOrEmpty(line))
            return false;

        int markerIndex = line.IndexOf(EntityDataMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
            return false;

        string prefix = TrimPrefixAfterLastColon(line, markerIndex);
        if (!MinecraftNameHelper.IsValidPlayerName(prefix))
            return false;

        playerName = prefix;
        int dataStart = markerIndex + EntityDataMarker.Length;
        data = TextSegmentHelper.TrimSegment(line, dataStart, line.Length - dataStart);
        return true;
    }

    private static bool TryHandlePlayerGamemodeLine(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        if (string.IsNullOrEmpty(line))
            return false;

        if (TryParseEntityDataLine(line, out playerName, out string suffix)
            && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out gameType))
        {
            return true;
        }

        return TryParseGamemodeAnnouncementLine(line, out playerName, out gameType);
    }

    private static bool TryParseMinecraftPositionValue(string value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        return text.Length >= 5 &&
               text[0] == '[' &&
               text[^1] == ']' &&
               text.Contains(',');
    }

    private bool HasPendingRespawnPositionRequest(string playerName)
    {
        lock (_respawnPositionProbeGate)
            return _pendingRespawnPositionRequests.TryGetValue(playerName, out _);
    }

    private static bool TryParseGamemodeAnnouncementLine(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        string message = TrimPrefixAfterLastColon(line, line.Length);
        if (message.Length == 0 || !message.Contains("game mode", StringComparison.OrdinalIgnoreCase))
            return false;

        const string setPrefix = "Set ";
        const string possessiveMarker = "'s game mode to ";
        int possessiveIndex = message.IndexOf(possessiveMarker, StringComparison.OrdinalIgnoreCase);
        if (message.StartsWith(setPrefix, StringComparison.OrdinalIgnoreCase) && possessiveIndex > setPrefix.Length)
        {
            string candidate = message[setPrefix.Length..possessiveIndex].Trim();
            string modeText = message[(possessiveIndex + possessiveMarker.Length)..].Trim();
            if (MinecraftNameHelper.IsValidPlayerName(candidate) && TryParseGamemodeName(modeText, out gameType))
            {
                playerName = candidate;
                return true;
            }
        }

        const string ofPrefix = "Set the game mode of ";
        if (message.StartsWith(ofPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int toIndex = message.IndexOf(" to ", ofPrefix.Length, StringComparison.OrdinalIgnoreCase);
            if (toIndex > ofPrefix.Length)
            {
                string candidate = message[ofPrefix.Length..toIndex].Trim();
                string modeText = message[(toIndex + 4)..].Trim();
                if (MinecraftNameHelper.IsValidPlayerName(candidate) && TryParseGamemodeName(modeText, out gameType))
                {
                    playerName = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseGamemodeName(string value, out int gameType)
    {
        gameType = -1;
        string text = (value ?? string.Empty).Trim();

        if (text.Contains("survival", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 0;
            return true;
        }

        if (text.Contains("creative", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 1;
            return true;
        }

        if (text.Contains("adventure", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 2;
            return true;
        }

        if (text.Contains("spectator", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 3;
            return true;
        }

        return false;
    }

    private void HandlePlayerGamemodeResult(string playerName, int gameType)
    {
        TaskCompletionSource<int?>? waiter;

        lock (_spectatorProbeGate)
        {
            if (gameType == 3)
                _spectatorPlayers.Add(playerName);
            else
                _spectatorPlayers.Remove(playerName);

            _pendingGameTypeRequests.Remove(playerName, out waiter);
        }

        RecordTrackedPlayerGamemodeForStatistics(playerName, gameType);
        waiter?.TrySetResult(gameType);
    }

    private void HandlePlayerRespawnPositionResult(string playerName)
    {
        TaskCompletionSource<bool>? waiter;

        lock (_respawnPositionProbeGate)
        {
            _pendingRespawnPositionRequests.Remove(playerName, out waiter);
        }

        waiter?.TrySetResult(true);
    }

    private void HandleSelectedItemResult(string playerName, string itemData)
    {
        TaskCompletionSource<string?>? waiter;

        lock (_selectedItemProbeGate)
        {
            _pendingSelectedItemRequests.Remove(playerName, out waiter);
        }

        waiter?.TrySetResult(itemData);
    }

    private void HandleEntityDataLine(string line)
    {
        if (!TryParseEntityDataLine(line, out string playerName, out string suffix))
            return;

        if (HasPendingRespawnPositionRequest(playerName) && TryParseMinecraftPositionValue(suffix))
        {
            HandlePlayerRespawnPositionResult(playerName);
            return;
        }

        if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameType))
        {
            HandlePlayerGamemodeResult(playerName, gameType);
            return;
        }

        if (suffix.Length >= 2 &&
            suffix[0] == '{' &&
            suffix[^1] == '}' &&
            suffix.Contains("minecraft:", StringComparison.OrdinalIgnoreCase))
        {
            HandleSelectedItemResult(playerName, suffix);
        }
    }

    private static async Task<TResult> QueryPlayerProbeAsync<TResult>(
        string playerName,
        Lock gate,
        Dictionary<string, TaskCompletionSource<TResult>> pendingRequests,
        Func<TaskCompletionSource<TResult>, CancellationToken, Task<bool>> sendProbe,
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
            return await AwaitProbeResultAsync(
                waiter,
                createdWaiter,
                ct => sendProbe(waiter, ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return default!;
        }
        finally
        {
            if (createdWaiter)
            {
                lock (gate)
                {
                    if (pendingRequests.TryGetValue(playerName, out TaskCompletionSource<TResult>? existing) &&
                        ReferenceEquals(existing, waiter))
                    {
                        pendingRequests.Remove(playerName);
                    }
                }
            }
        }
    }

    public Task<string?> QuerySelectedItemDataAsync(string playerName, CancellationToken cancellationToken)
    {
        string selector = MinecraftCommandBuilder.PlayerSelectorLimitOne(playerName);
        return QueryPlayerProbeAsync<string?>(
            playerName,
            _selectedItemProbeGate,
            _pendingSelectedItemRequests,
            (waiter, ct) => SendInternalProbeCommandAsync("data get entity " + selector + " SelectedItem", () => waiter.TrySetResult(default), ct),
            cancellationToken);
    }

    public async Task<Dictionary<string, string?>> QuerySelectedItemDataBatchAsync(List<string> players, CancellationToken cancellationToken)
    {
        players = SortedListHelper.NormalizeMinecraftPlayerNames(players, PlayerNameComparer);
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
                    commands[i] = "data get entity " + MinecraftCommandBuilder.PlayerSelectorLimitOne(createdWaiterPlayers[i]) + " SelectedItem";
                }

                await SendInternalProbeCommandsAsync(commands, () =>
                {
                    foreach (string player in createdWaiterPlayers)
                    {
                        if (waiters.TryGetValue(player, out TaskCompletionSource<string?>? waiter))
                            waiter.TrySetResult(null);
                    }
                }, cancellationToken).ConfigureAwait(false);
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
        finally
        {
            if (createdWaiterPlayers.Count > 0)
            {
                lock (_selectedItemProbeGate)
                {
                    foreach (string player in createdWaiterPlayers)
                    {
                        if (_pendingSelectedItemRequests.TryGetValue(player, out TaskCompletionSource<string?>? existing)
                            && waiters.TryGetValue(player, out TaskCompletionSource<string?>? waiter)
                            && ReferenceEquals(existing, waiter))
                        {
                            _pendingSelectedItemRequests.Remove(player);
                        }
                    }
                }
            }
        }

        return results;
    }

    private Task<bool> QueryPlayerRespawnPositionAsync(string playerName, CancellationToken cancellationToken)
    {
        string selector = "@a[name=\"" + MinecraftCommandBuilder.EscapeSelectorValue(playerName) + "\",limit=1,gamemode=!spectator,nbt={DeathTime:0s}]";
        return QueryPlayerProbeAsync(
            playerName,
            _respawnPositionProbeGate,
            _pendingRespawnPositionRequests,
            (waiter, ct) => SendInternalProbeCommandAsync("data get entity " + selector + " Pos", () => waiter.TrySetResult(false), ct),
            cancellationToken);
    }

    private Dictionary<string, TaskCompletionSource<int?>> CreateGameTypeBatchWaiters(List<string> players, List<string> createdWaiterPlayers)
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
                createdWaiterPlayers.Add(player);
                waiters[player] = waiter;
            }
        }

        return waiters;
    }

    private static Task WaitForGameTypeBatchAsync(Dictionary<string, TaskCompletionSource<int?>> waiters, CancellationToken cancellationToken)
    {
        if (waiters.Count == 0)
            return Task.CompletedTask;

        Task<int?>[] tasks = new Task<int?>[waiters.Count];
        int index = 0;
        foreach (TaskCompletionSource<int?> waiter in waiters.Values)
            tasks[index++] = waiter.Task;

        return Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private void CleanupCreatedGameTypeBatchWaiters(Dictionary<string, TaskCompletionSource<int?>> waiters, List<string> createdWaiterPlayers)
    {
        if (createdWaiterPlayers.Count == 0)
            return;

        lock (_spectatorProbeGate)
        {
            foreach (string player in createdWaiterPlayers)
            {
                if (_pendingGameTypeRequests.TryGetValue(player, out TaskCompletionSource<int?>? existing)
                    && waiters.TryGetValue(player, out TaskCompletionSource<int?>? waiter)
                    && ReferenceEquals(existing, waiter))
                {
                    _pendingGameTypeRequests.Remove(player);
                }
            }
        }
    }

    private async Task RefreshSpectatorStatesAsync(List<string> players, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        players = SortedListHelper.NormalizeMinecraftPlayerNames(players, PlayerNameComparer);

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

            List<string> createdWaiterPlayers = new(players.Count);
            Dictionary<string, TaskCompletionSource<int?>> waiters = CreateGameTypeBatchWaiters(players, createdWaiterPlayers);
            bool refreshCompleted = false;
            HashSet<string>? nextSpectators = null;
            try
            {
                if (await SendInternalProbeCommandsAsync(SpectatorGameTypeProbeCommands, () =>
                {
                    foreach (TaskCompletionSource<int?> waiter in waiters.Values)
                        waiter.TrySetResult(default);
                }, cancellationToken).ConfigureAwait(false))
                {
                    await WaitForGameTypeBatchAsync(waiters, cancellationToken).ConfigureAwait(false);
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
            }
            finally
            {
                CleanupCreatedGameTypeBatchWaiters(waiters, createdWaiterPlayers);
            }

            lock (_spectatorProbeGate)
            {
                _spectatorPlayers = nextSpectators ?? new HashSet<string>(players.Count, PlayerNameComparer);
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

    public async Task<List<string>> GetOnlinePlayersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> online = await RefreshOnlinePlayersAsync(cancellationToken).ConfigureAwait(false);

        bool hasSpectatorSnapshot;
        bool spectatorSnapshotIsFresh;
        lock (_spectatorProbeGate)
        {
            hasSpectatorSnapshot = _spectatorSnapshotInitialized;
            spectatorSnapshotIsFresh = DateTime.UtcNow - _lastSpectatorRefreshUtc < SpectatorRefreshInterval;
        }

        if (!hasSpectatorSnapshot)
        {
            await RefreshSpectatorStatesAsync(online, cancellationToken).ConfigureAwait(false);
        }
        else if (!spectatorSnapshotIsFresh)
        {
            QueueSpectatorStateRefresh(online, cancellationToken);
        }

        return GetTargetablePlayersFromSpectatorSnapshot(online);
    }

    private List<string> GetTargetablePlayersFromSpectatorSnapshot(List<string> online)
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

    private void QueueSpectatorStateRefresh(List<string> players, CancellationToken cancellationToken)
    {
        CancellationToken refreshToken = _sessionCts?.Token ?? cancellationToken;
        if (refreshToken.IsCancellationRequested || Interlocked.CompareExchange(ref _spectatorStateRefreshQueued, 1, 0) != 0)
            return;

        List<string> snapshot = [.. players];
        TrackSessionBackgroundTask(Task.Run(async () =>
        {
            try
            {
                await RefreshSpectatorStatesAsync(snapshot, refreshToken).ConfigureAwait(false);
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

    private void RemoveSpectatorPlayer(string playerName)
    {
        lock (_spectatorProbeGate)
        {
            _spectatorPlayers.Remove(playerName);
        }
    }

}
