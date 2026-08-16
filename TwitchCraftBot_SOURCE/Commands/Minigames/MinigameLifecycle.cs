using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    public static void StartMinigameLoops(BotMainHandler runtime, CancellationToken sessionToken)
    {
        if (runtime == null || !runtime.MinigamesEnabled)
            return;

        MinigameLoopState loop;
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out MinigameLoopState? existing)
                && !existing.Cts.IsCancellationRequested
                && existing.Task is not { IsCompleted: true })
                return;

            loop = new(
                CancellationTokenSource.CreateLinkedTokenSource(sessionToken),
                TakeNextMinigameTimeNoLock(runtime));
            MinigameLoops[runtime] = loop;
        }

        Task loopTask = Task.Run(() => RunMinigameLoopAsync(runtime, loop, loop.Cts.Token), CancellationToken.None);
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out MinigameLoopState? current) && ReferenceEquals(current, loop))
                loop.Task = loopTask;
        }

        _ = loopTask.ContinueWith(
            _ => CleanupMinigameLoop(runtime, loop),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void StopMinigameLoops(BotMainHandler runtime, bool preserveSchedule = false)
    {
        _ = StopMinigameLoopsCore(runtime, preserveSchedule);
    }

    public static async Task StopMinigameLoopsAsync(BotMainHandler runtime, bool preserveSchedule = false)
    {
        Task? loopTask = StopMinigameLoopsCore(runtime, preserveSchedule);
        if (loopTask == null)
            return;

        try
        {
            await loopTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (Exception ex)
        {
            runtime.AddChatLogLine(ErrorHandling.FormatLogMessage("Minigame loop stop failed", ex));
        }
    }

    private static Task? StopMinigameLoopsCore(BotMainHandler runtime, bool preserveSchedule)
    {
        if (runtime == null)
            return null;

        MinigameLoopState? loop;
        List<KeyValuePair<string, int>>? chickenRefunds = null;
        List<KeyValuePair<string, int>>? witherRefunds = null;

        lock (MinigameGate)
        {
            MinigameLoops.Remove(runtime, out loop);
            if (loop != null && preserveSchedule)
                (PreservedNextMinigameAtUtc ??= [])[runtime] = loop.NextAtUtc;
            else if (!preserveSchedule)
            {
                PreservedNextMinigameAtUtc?.Remove(runtime);
                ClearEmptyPreservedScheduleNoLock();
            }

            if (ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chickenState))
            {
                chickenState.BettingOpen = false;
                chickenState.Running = false;
                if (chickenState.Bets.Count > 0)
                    chickenRefunds = BuildRefunds(chickenState.Bets);
                chickenState.Bets.Clear();
            }

            if (WitherBattleStates.TryGetValue(runtime, out WitherBattleState? witherState))
            {
                witherState.BettingOpen = false;
                witherState.Running = false;
                witherState.CurrentHealth = 0;
                witherState.DefeatedSignal?.TrySetResult(false);
                witherState.DefeatedSignal = null;
                if (witherState.Bets.Count > 0)
                    witherRefunds = BuildRefunds(witherState.Bets);
                witherState.Bets.Clear();
            }

            ChickenRunStates.Remove(runtime);
            GuessNumberStates.Remove(runtime);
            WitherBattleStates.Remove(runtime);
            ActiveMinigames.Remove(runtime);
        }

        if (loop != null)
        {
            try { loop.Cts.Cancel(); } catch { }
        }

        if (chickenRefunds is { Count: > 0 })
            runtime.AdjustTokens(chickenRefunds);
        if (witherRefunds is { Count: > 0 })
            runtime.AdjustTokens(witherRefunds);

        return loop?.Task;
    }

    private static void CleanupMinigameLoop(BotMainHandler runtime, MinigameLoopState loop)
    {
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out MinigameLoopState? current) && ReferenceEquals(current, loop))
                MinigameLoops.Remove(runtime);
        }

        try { loop.Cts.Dispose(); } catch { }
    }

    private static void RemoveMinigameState(BotMainHandler runtime)
    {
        lock (MinigameGate)
        {
            ChickenRunStates.Remove(runtime);
            GuessNumberStates.Remove(runtime);
            WitherBattleStates.Remove(runtime);
            ActiveMinigames.Remove(runtime);
        }
    }

    private static bool IsCurrentMinigameLoop(BotMainHandler runtime, MinigameLoopState loop)
    {
        lock (MinigameGate)
        {
            return MinigameLoops.TryGetValue(runtime, out MinigameLoopState? current) && ReferenceEquals(current, loop);
        }
    }

    private static async Task RunMinigameLoopAsync(BotMainHandler runtime, MinigameLoopState loop, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime nextAtUtc;
                lock (MinigameGate)
                {
                    if (!MinigameLoops.TryGetValue(runtime, out MinigameLoopState? current) || !ReferenceEquals(current, loop))
                        break;
                    nextAtUtc = loop.NextAtUtc;
                }

                TimeSpan remaining = nextAtUtc - nowUtc;
                if (remaining > TimeSpan.Zero)
                {
                    await WaitForMinigameDelayAsync(runtime, loop, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (BotMainHandler.SecureRandomInt(3))
                {
                    case 0:
                        await RunChickenRunAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                    case 1:
                        await RunGuessNumberAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await RunWitherBattleAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                }

                if (cancellationToken.IsCancellationRequested || !IsCurrentMinigameLoop(runtime, loop))
                    break;

                SetNextMinigameTime(runtime, runtime.MinigameCooldown);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || !IsCurrentMinigameLoop(runtime, loop))
                    break;

                runtime.AddChatLogLine(ErrorHandling.FormatLogMessage("Minigame loop error", ex));
                RefundAllChickenRunBets(runtime);
                RefundAllWitherBattleBets(runtime);

                RemoveMinigameState(runtime);
                SetNextMinigameTime(runtime, 10.0);

                try
                {
                    await Task.Delay(OneSecondMinigameDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // ===== Minigame session control =====

}
