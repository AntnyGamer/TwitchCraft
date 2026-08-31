using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

internal sealed class TimedPlayerScaleController
{
    private static readonly TimeSpan RestoreWarningLeadTime = TimeSpan.FromSeconds(3);

    private readonly record struct ScaleState(
        long Generation,
        bool UsesModernAttributeIds,
        bool UsesInlineTextComponents);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SemaphoreSlim> _playerGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScaleState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task<bool>> _sendCommand;
    private readonly Action<Task> _trackTask;
    private readonly Action<string> _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private long _nextGeneration;

    internal TimedPlayerScaleController(
        Func<string, CancellationToken, Task<bool>> sendCommand,
        Action<Task> trackTask,
        Action<string> log,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _sendCommand = sendCommand ?? throw new ArgumentNullException(nameof(sendCommand));
        _trackTask = trackTask ?? throw new ArgumentNullException(nameof(trackTask));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _delay = delay ?? Task.Delay;
    }

    internal async Task<bool> ApplyAsync(
        IReadOnlyList<string> playerNames,
        double scale,
        bool usesModernAttributeIds,
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

        List<SemaphoreSlim> acquiredGates = await LockPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        Dictionary<string, ScaleState?> previousStates = new(players.Count, StringComparer.OrdinalIgnoreCase);
        List<(string Player, ScaleState State)> appliedStates = new(players.Count);
        try
        {
            List<string> commands = new(players.Count);
            lock (_gate)
            {
                foreach (string player in players)
                {
                    previousStates[player] = _states.TryGetValue(player, out ScaleState previous) ? previous : null;
                    ScaleState applied = new(
                        Interlocked.Increment(ref _nextGeneration),
                        usesModernAttributeIds,
                        usesInlineTextComponents);
                    _states[player] = applied;
                    appliedStates.Add((player, applied));
                    commands.Add(MinecraftCommandBuilder.SetScale(
                        MinecraftCommandBuilder.SinglePlayerSelector(player),
                        scale,
                        usesModernAttributeIds));
                }
            }

            bool dispatched;
            try
            {
                dispatched = await dispatchInitialCommands(commands, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                RestoreStates(appliedStates, previousStates);
                throw;
            }

            if (!dispatched)
            {
                RestoreStates(appliedStates, previousStates);
                return false;
            }

            foreach ((string player, ScaleState state) in appliedStates)
            {
                _trackTask(ResetLaterAsync(player, state, duration, cancellationToken));
            }

            return true;
        }
        finally
        {
            UnlockPlayers(acquiredGates);
        }
    }

    internal async Task ResetAllAsync(CancellationToken cancellationToken)
    {
        List<string> players;
        lock (_gate)
        {
            players = [.. _states.Keys];
        }

        players.Sort(StringComparer.OrdinalIgnoreCase);
        if (players.Count == 0)
            return;

        List<SemaphoreSlim> acquiredGates = await LockPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (string player in players)
            {
                ScaleState state;
                lock (_gate)
                {
                    if (!_states.TryGetValue(player, out state))
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

    internal void ClearTracking()
    {
        lock (_gate)
        {
            _states.Clear();
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

            foreach (string command in commands)
            {
                if (!await _sendCommand(command, cancellationToken).ConfigureAwait(false))
                    break;
            }

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
            state.UsesModernAttributeIds);

        do
        {
            if (await _sendCommand(command, cancellationToken).ConfigureAwait(false))
                return true;
            if (!retryUntilCancelled)
                break;

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        while (!cancellationToken.IsCancellationRequested);

        _log("Could not restore " + player + " to normal size before the Minecraft connection closed.");
        return false;
    }

    private async Task<List<SemaphoreSlim>> LockPlayersAsync(
        List<string> players,
        CancellationToken cancellationToken)
    {
        List<SemaphoreSlim> acquired = new(players.Count);
        try
        {
            foreach (string player in players)
            {
                SemaphoreSlim playerGate = GetGate(player);
                await playerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired.Add(playerGate);
            }

            return acquired;
        }
        catch
        {
            UnlockPlayers(acquired);
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
        IReadOnlyList<(string Player, ScaleState State)> appliedStates,
        IReadOnlyDictionary<string, ScaleState?> previousStates)
    {
        lock (_gate)
        {
            foreach ((string player, ScaleState applied) in appliedStates)
            {
                if (!_states.TryGetValue(player, out ScaleState current) || current != applied)
                    continue;

                if (previousStates.TryGetValue(player, out ScaleState? previous) && previous.HasValue)
                    _states[player] = previous.Value;
                else
                    _states.Remove(player);
            }
        }
    }

    private static List<string> NormalizePlayers(IReadOnlyList<string> playerNames)
    {
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (string playerName in playerNames)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalized))
                unique.Add(normalized);
        }

        List<string> players = [.. unique];
        players.Sort(StringComparer.OrdinalIgnoreCase);
        return players;
    }

    private static void UnlockPlayers(List<SemaphoreSlim> acquiredGates)
    {
        for (int i = acquiredGates.Count - 1; i >= 0; i--)
            acquiredGates[i].Release();
    }
}
