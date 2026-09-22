using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public static partial class MinigameManager
{
    public static void StartLoops(MainHandler runtime, CancellationToken sessionToken)
    {
        if (runtime == null || !runtime.MinigamesEnabled)
            return;

        LoopState loop;
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out LoopState? existing)
                && !existing.Cts.IsCancellationRequested
                && existing.Task is not { IsCompleted: true })
                return;

            loop = new(runtime, CancellationTokenSource.CreateLinkedTokenSource(sessionToken), TakeNextTimeNoLock(runtime));
            MinigameLoops[runtime] = loop;
        }

        try { RefundChickenBets(runtime); RefundWitherBets(runtime); }
        catch { CleanupLoop(loop); throw; }
        Task loopTask = Task.Run(() => RunLoopAsync(loop.Runtime, loop, loop.Cts.Token), CancellationToken.None);
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out LoopState? current) && ReferenceEquals(current, loop))
                loop.Task = loopTask;
        }

        _ = loopTask.ContinueWith(
            static (_, state) => CleanupLoop((LoopState)state!),
            loop,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void StopLoops(MainHandler runtime, bool preserveSchedule = false)
    {
        if (runtime == null)
            return;

        _ = StopLoopsCore(runtime, preserveSchedule);
    }

    public static async Task StopLoopsAsync(MainHandler runtime, bool preserveSchedule = false)
    {
        if (runtime == null)
            return;

        Task? loopTask = StopLoopsCore(runtime, preserveSchedule);
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
            runtime.AddChatLogLine(ErrorHandling.FormatLog("Minigame loop stop failed", ex));
        }
    }

    private static Task? StopLoopsCore(MainHandler runtime, bool preserveSchedule)
    {
        LoopState? loop;

        lock (MinigameGate)
        {
            MinigameLoops.Remove(runtime, out loop);
            if (loop != null && preserveSchedule)
                (PreservedNextAtUtc ??= [])[runtime] = loop.NextAtUtc;
            else if (!preserveSchedule)
            {
                PreservedNextAtUtc?.Remove(runtime);
                ClearScheduleNoLock();
            }

            if (ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chickenState))
                chickenState.BettingOpen = false;
            if (WitherBattleStates.TryGetValue(runtime, out WitherBattleState? witherState))
            {
                witherState.BettingOpen = false;
                witherState.CurrentHealth = 0;
                witherState.DefeatedSignal?.TrySetResult();
                witherState.DefeatedSignal = null;
            }
        }

        if (loop != null)
        {
            try { loop.Cts.Cancel(); } catch { }
        }

        RefundChickenBets(runtime);
        RefundWitherBets(runtime);
        RemoveState(runtime);
        return loop?.Task;
    }

    private static void CleanupLoop(LoopState loop)
    {
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(loop.Runtime, out LoopState? current) && ReferenceEquals(current, loop))
                MinigameLoops.Remove(loop.Runtime);
        }

        try { loop.Cts.Dispose(); } catch { }
    }

    private static void RemoveState(MainHandler runtime)
    {
        lock (MinigameGate)
        {
            if (ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chicken) && chicken.Bets.Count == 0) ChickenRunStates.Remove(runtime);
            GuessNumberStates.Remove(runtime);
            if (WitherBattleStates.TryGetValue(runtime, out WitherBattleState? wither) && wither.Bets.Count == 0) WitherBattleStates.Remove(runtime);
            ActiveMinigames.Remove(runtime);
        }
    }

    private static bool IsCurrentLoop(MainHandler runtime, LoopState loop)
    {
        lock (MinigameGate)
        {
            return MinigameLoops.TryGetValue(runtime, out LoopState? current) && ReferenceEquals(current, loop);
        }
    }

    private static async Task RunLoopAsync(MainHandler runtime, LoopState loop, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime nextAtUtc;
                lock (MinigameGate)
                {
                    if (!MinigameLoops.TryGetValue(runtime, out LoopState? current) || !ReferenceEquals(current, loop))
                        break;
                    nextAtUtc = loop.NextAtUtc;
                }

                TimeSpan remaining = nextAtUtc - nowUtc;
                if (remaining > TimeSpan.Zero)
                {
                    await WaitForDelayAsync(runtime, loop, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                RefundChickenBets(runtime);
                RefundWitherBets(runtime);
                switch (CommandRandom.Next(3))
                {
                    case 0:
                        await RunChickenAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                    case 1:
                        await RunGuessNumberAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        await RunWitherAsync(runtime, cancellationToken).ConfigureAwait(false);
                        break;
                }

                if (cancellationToken.IsCancellationRequested || !IsCurrentLoop(runtime, loop))
                    break;

                SetNextTime(runtime, runtime.MinigameCooldown);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || !IsCurrentLoop(runtime, loop))
                    break;

                runtime.AddChatLogLine(ErrorHandling.FormatLog("Minigame loop error", ex));
                RefundChickenBets(runtime);
                RefundWitherBets(runtime);

                RemoveState(runtime);
                SetNextTime(runtime, 10.0);

                try
                {
                    await Task.Delay(OneSecondDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static DateTime TakeNextTimeNoLock(MainHandler runtime)
    {
        if (PreservedNextAtUtc?.Remove(runtime, out DateTime nextAtUtc) == true)
        {
            ClearScheduleNoLock();
            return nextAtUtc;
        }

        return DateTime.UtcNow.AddMinutes(runtime.MinigameCooldown);
    }

    private static void ClearScheduleNoLock()
    {
        if (PreservedNextAtUtc?.Count == 0)
            PreservedNextAtUtc = null;
    }

    internal static void SetNextTime(MainHandler runtime, double minutesFromNow)
    {
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out LoopState? loop))
                loop.NextAtUtc = DateTime.UtcNow.AddMinutes(minutesFromNow);
        }
    }

    private static async Task WaitForDelayAsync(MainHandler runtime, LoopState expectedLoop, CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (MinigameGate)
        {
            if (!MinigameLoops.TryGetValue(runtime, out LoopState? loop) || !ReferenceEquals(loop, expectedLoop))
                return;

            delay = loop.NextAtUtc - DateTime.UtcNow;
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }
}
