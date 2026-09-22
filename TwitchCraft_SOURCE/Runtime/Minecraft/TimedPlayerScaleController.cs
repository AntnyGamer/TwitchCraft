using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal sealed class TimedPlayerScaleController
{
    private static readonly TimeSpan RestoreWarningLeadTime = TimeSpan.FromSeconds(3);

    private readonly record struct ScaleState(
        long Generation,
        bool UsesModernAttributeIDs,
        bool UsesInlineTextComponents);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SemaphoreSlim> _playerGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScaleState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task<bool>> _sendCommand;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<bool>> _sendCommands;
    private readonly Func<string, bool> _isPlayerOnline;
    private readonly Action<Task> _trackTask;
    private readonly Action<string> _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private long _nextGeneration;
    private long _recoveryGeneration;

    internal TimedPlayerScaleController(
        Func<string, CancellationToken, Task<bool>> sendCommand,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>> sendCommands,
        Func<string, bool> isPlayerOnline,
        Action<Task> trackTask,
        Action<string> log,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
        _sendCommands = sendCommands ?? throw new ArgumentNullException(nameof(sendCommands));
        _isPlayerOnline = isPlayerOnline ?? throw new ArgumentNullException(nameof(isPlayerOnline));
        _trackTask = trackTask ?? throw new ArgumentNullException(nameof(trackTask));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _delay = delay ?? Task.Delay;
    }

    internal async Task<bool> ApplyAsync(
        IReadOnlyList<string> playerNames,
        double scale,
        bool usesModernAttributeIDs,
        bool usesInlineTextComponents,
        TimeSpan duration,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>> dispatchInitialCommands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerNames);
        ArgumentNullException.ThrowIfNull(dispatchInitialCommands);
        if (!double.IsFinite(scale) || scale <= 0 || duration <= TimeSpan.Zero)
            return false;

        List<string> players = NormalizePlayers(playerNames);
        if (players.Count == 0)
            return false;

        SemaphoreSlim[] acquiredGates = await LockPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        (string Player, ScaleState Applied, ScaleState? Previous)[] appliedStates = new (string, ScaleState, ScaleState?)[players.Count];
        int appliedCount = 0;
        try
        {
            string[] commands = new string[players.Count];
            lock (_gate)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    string player = players[i];
                    ScaleState? previous = _states.TryGetValue(player, out ScaleState existing) ? existing : null;
                    ScaleState applied = new(
                        Interlocked.Increment(ref _nextGeneration),
                        usesModernAttributeIDs,
                        usesInlineTextComponents);
                    _states[player] = applied;
                    appliedStates[i] = (player, applied, previous);
                    appliedCount++;
                    commands[i] = MinecraftCommandBuilder.SetScale(
                        MinecraftCommandBuilder.SinglePlayerSelector(player),
                        scale,
                        usesModernAttributeIDs);
                }
            }

            bool dispatched;
            try
            {
                dispatched = await dispatchInitialCommands(commands, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RestoreStates(appliedStates, appliedCount);
                throw;
            }

            if (!dispatched)
            {
                RestoreStates(appliedStates, appliedCount);
                return false;
            }

            for (int i = 0; i < appliedCount; i++)
            {
                (string player, ScaleState state, _) = appliedStates[i];
                _trackTask(ResetLaterAsync(player, state, duration, cancellationToken));
            }

            return true;
        }
        finally
        {
            UnlockPlayers(acquiredGates);
        }
    }

    internal Task ResetRecoveredAsync(string player, CancellationToken cancellationToken)
    {
        long maxGeneration = Volatile.Read(ref _recoveryGeneration);
        lock (_gate)
            if (!_states.TryGetValue(player, out ScaleState state) || state.Generation > maxGeneration)
                return Task.CompletedTask;
        return ResetAllAsync(cancellationToken, maxGeneration, player);
    }

    internal void MarkForRecovery() => Volatile.Write(ref _recoveryGeneration, Volatile.Read(ref _nextGeneration));

    internal async Task ResetAllAsync(CancellationToken cancellationToken, long maxGeneration = long.MaxValue, string? playerName = null)
    {
        List<string> players;
        lock (_gate)
            players = playerName == null ? [.. _states.Keys] : _states.ContainsKey(playerName) ? [playerName] : [];

        if (playerName == null) players.Sort(StringComparer.OrdinalIgnoreCase);
        if (players.Count == 0)
            return;

        SemaphoreSlim[] acquiredGates = await LockPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (string player in players)
            {
                ScaleState state;
                lock (_gate)
                {
                    if (!_states.TryGetValue(player, out state) || state.Generation > maxGeneration)
                        continue;
                }

                bool sent = await TryResetAsync(player, state, retryUntilCancelled: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (sent)
                    RemoveIfCurrent(player, state);
            }
        }
        finally
        {
            UnlockPlayers(acquiredGates);
        }
    }

    private async Task ResetLaterAsync(
        string player,
        ScaleState state,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan warningDelay = duration - RestoreWarningLeadTime;
            if (warningDelay > TimeSpan.Zero)
            {
                await _delay(warningDelay, cancellationToken).ConfigureAwait(false);
                if (!await ShowRestoreWarningAsync(player, state, cancellationToken).ConfigureAwait(false))
                    return;

                await _delay(RestoreWarningLeadTime, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _delay(duration, cancellationToken).ConfigureAwait(false);
            }

            SemaphoreSlim playerGate = GetGate(player);
            await playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsCurrent(player, state))
                    return;

                if (await TryResetAsync(player, state, retryUntilCancelled: true, cancellationToken: cancellationToken).ConfigureAwait(false))
                    RemoveIfCurrent(player, state);
            }
            finally
            {
                playerGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log(ErrorHandling.FormatLog("Timed player-size reset failed", ex));
        }
    }

    private async Task<bool> ShowRestoreWarningAsync(
        string player,
        ScaleState state,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim playerGate = GetGate(player);
        await playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(player, state))
                return false;

            string selector = MinecraftCommandBuilder.SinglePlayerSelector(player);
            string[] commands =
            [
                MinecraftCommandBuilder.TitleTimes(selector, 0, 60, 0),
                MinecraftCommandBuilder.Subtitle(
                    selector,
                    "RETURNING TO NORMAL SIZE IN 3 SECONDS!",
                    "red",
                    state.UsesInlineTextComponents),
                MinecraftCommandBuilder.Title(selector, " ", "white", state.UsesInlineTextComponents)
            ];

            _ = await _sendCommands(commands, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log(ErrorHandling.FormatLog("Timed player-size warning failed", ex));
            return true;
        }
        finally
        {
            playerGate.Release();
        }
    }

    private async Task<bool> TryResetAsync(
        string player,
        ScaleState state,
        bool retryUntilCancelled,
        CancellationToken cancellationToken)
    {
        string command = MinecraftCommandBuilder.SetScale(
            MinecraftCommandBuilder.SinglePlayerSelector(player),
            1.0,
            state.UsesModernAttributeIDs);

        do
        {
            if (!_isPlayerOnline(player))
            {
                if (!retryUntilCancelled) return false;
            }
            else if (await _sendCommand(command, cancellationToken).ConfigureAwait(false))
                return true;
            if (!retryUntilCancelled)
                break;

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        while (!cancellationToken.IsCancellationRequested);

        _log("Could not restore " + player + " to normal size before the Minecraft connection closed.");
        return false;
    }

    private async Task<SemaphoreSlim[]> LockPlayersAsync(
        List<string> players,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim[] acquired = new SemaphoreSlim[players.Count];
        int acquiredCount = 0;
        try
        {
            foreach (string player in players)
            {
                SemaphoreSlim playerGate = GetGate(player);
                await playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired[acquiredCount++] = playerGate;
            }

            return acquired;
        }
        catch
        {
            for (int i = acquiredCount - 1; i >= 0; i--)
                acquired[i].Release();
            throw;
        }
    }

    private SemaphoreSlim GetGate(string player)
    {
        lock (_gate)
        {
            if (!_playerGates.TryGetValue(player, out SemaphoreSlim? playerGate))
            {
                playerGate = new SemaphoreSlim(1, 1);
                _playerGates[player] = playerGate;
            }

            return playerGate;
        }
    }

    private bool IsCurrent(string player, ScaleState state)
    {
        lock (_gate)
        {
            return _states.TryGetValue(player, out ScaleState current) && current == state;
        }
    }

    private void RemoveIfCurrent(string player, ScaleState state)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(player, out ScaleState current) && current == state)
                _states.Remove(player);
        }
    }

    private void RestoreStates(
        IReadOnlyList<(string Player, ScaleState Applied, ScaleState? Previous)> appliedStates,
        int count)
    {
        lock (_gate)
        {
            for (int i = 0; i < count; i++)
            {
                (string player, ScaleState applied, ScaleState? previous) = appliedStates[i];
                if (!_states.TryGetValue(player, out ScaleState current) || current != applied)
                    continue;

                if (previous.HasValue)
                    _states[player] = previous.Value;
                else
                    _states.Remove(player);
            }
        }
    }

    private static List<string> NormalizePlayers(IReadOnlyList<string> playerNames)
        => SortedListHelper.NormalizePlayerNames(playerNames, StringComparer.OrdinalIgnoreCase);

    private static void UnlockPlayers(SemaphoreSlim[] acquiredGates)
    {
        for (int i = acquiredGates.Length - 1; i >= 0; i--)
            acquiredGates[i].Release();
    }
}
