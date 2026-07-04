using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{

    private const int MaxMinigameBetPerPlayer = 200;
    private static readonly TimeSpan OneSecondMinigameDelay = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan ChickenRunBettingDelay = TimeSpan.FromMinutes(1.0);
    private static readonly TimeSpan WitherBattleDuration = TimeSpan.FromMinutes(5.0);

    private static readonly Lock MinigameGate = new();
    private static readonly Dictionary<BotMainHandler, ChickenRunState> ChickenRunStates = [];
    private static readonly Dictionary<BotMainHandler, GuessNumberState> GuessNumberStates = [];
    private static readonly Dictionary<BotMainHandler, WitherBattleState> WitherBattleStates = [];
    private static readonly Dictionary<BotMainHandler, MinigameLoopState> MinigameLoops = [];
    private static Dictionary<BotMainHandler, DateTime>? PreservedNextMinigameAtUtc;
    private static readonly Dictionary<BotMainHandler, ActiveMinigameState> ActiveMinigames = [];

    // ===== State model types =====

    private sealed class MinigameLoopState(CancellationTokenSource cts, DateTime nextAtUtc)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public DateTime NextAtUtc { get; set; } = nextAtUtc;
        public Task? Task { get; set; }
    }

    private interface IMinigameBet
    {
        string Viewer { get; }
        int TokenAmount { get; }
    }

    private sealed class ChickenRunBet : IMinigameBet
    {
        public string Viewer { get; set; } = string.Empty;
        public int TokenAmount { get; set; }
        public int BetSeconds { get; set; }
    }

    private sealed class ChickenRunState
    {
        public bool BettingOpen { get; set; }
        public bool Running { get; set; }
        public int MinSeconds { get; set; }
        public int MaxSeconds { get; set; }
        public int KillAtSeconds { get; set; }
        public List<ChickenRunBet> Bets { get; } = [];
    }

    private sealed class GuessNumberState
    {
        public bool Active { get; set; }
        public int TargetNumber { get; set; }
        public int RoundID { get; set; }
        public Dictionary<string, DateTime> LastGuessAtUtc { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WitherBattleBet : IMinigameBet
    {
        public string Viewer { get; set; } = string.Empty;
        public int TokenAmount { get; set; }
    }

    private sealed class WitherBattleState
    {
        public bool BettingOpen { get; set; }
        public bool Running { get; set; }
        public int CurrentHealth { get; set; }
        public TaskCompletionSource<bool>? DefeatedSignal { get; set; }
        public List<WitherBattleBet> Bets { get; } = [];
    }

    private sealed class ActiveMinigameState
    {
        public string Kind { get; set; } = string.Empty;
        public int RunID { get; set; }
    }

    // ===== State access helpers =====

    private static ChickenRunState GetChickenRunState(BotMainHandler runtime)
    {
        lock (MinigameGate)
        {
            return GetChickenRunStateNoLock(runtime);
        }
    }

    private static ChickenRunState GetChickenRunStateNoLock(BotMainHandler runtime)
    {
        if (!ChickenRunStates.TryGetValue(runtime, out ChickenRunState? state))
        {
            state = new();
            ChickenRunStates[runtime] = state;
        }

        return state;
    }

    private static GuessNumberState GetGuessNumberStateNoLock(BotMainHandler runtime)
    {
        if (!GuessNumberStates.TryGetValue(runtime, out GuessNumberState? state))
        {
            state = new();
            GuessNumberStates[runtime] = state;
        }

        return state;
    }

    private static WitherBattleState GetWitherBattleState(BotMainHandler runtime)
    {
        lock (MinigameGate)
        {
            return GetWitherBattleStateNoLock(runtime);
        }
    }

    private static WitherBattleState GetWitherBattleStateNoLock(BotMainHandler runtime)
    {
        if (!WitherBattleStates.TryGetValue(runtime, out WitherBattleState? state))
        {
            state = new();
            WitherBattleStates[runtime] = state;
        }

        return state;
    }

    private static ActiveMinigameState GetActiveMinigameStateNoLock(BotMainHandler runtime)
    {
        if (!ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? state))
        {
            state = new();
            ActiveMinigames[runtime] = state;
        }

        return state;
    }

    private static TBet? FindBet<TBet>(List<TBet> bets, string viewer) where TBet : class, IMinigameBet
    {
        for (int i = 0; i < bets.Count; i++)
        {
            if (string.Equals(bets[i].Viewer, viewer, StringComparison.OrdinalIgnoreCase))
                return bets[i];
        }

        return null;
    }

    private static List<TBet> CloneBets<TBet>(List<TBet> bets, Func<TBet, TBet> cloneBet) where TBet : class, IMinigameBet
    {
        List<TBet> cloned = new(bets.Count);
        for (int i = 0; i < bets.Count; i++)
        {
            TBet bet = bets[i];
            if (string.IsNullOrWhiteSpace(bet.Viewer) || bet.TokenAmount <= 0)
                continue;

            cloned.Add(cloneBet(bet));
        }

        return cloned;
    }

    private static List<KeyValuePair<string, int>> BuildRefunds<TBet>(List<TBet> bets) where TBet : IMinigameBet
    {
        List<KeyValuePair<string, int>> refunds = new(bets.Count);
        for (int i = 0; i < bets.Count; i++)
        {
            TBet bet = bets[i];
            if (!string.IsNullOrWhiteSpace(bet.Viewer) && bet.TokenAmount > 0)
                refunds.Add(new(bet.Viewer, bet.TokenAmount));
        }

        return refunds;
    }

    // ===== Loop lifecycle and scheduling =====

    private static DateTime TakeNextMinigameTimeNoLock(BotMainHandler runtime)
    {
        if (PreservedNextMinigameAtUtc?.Remove(runtime, out DateTime nextAtUtc) == true)
        {
            ClearEmptyPreservedScheduleNoLock();
            return nextAtUtc;
        }

        return DateTime.UtcNow.AddMinutes(runtime.MinigameCooldown);
    }

    private static void ClearEmptyPreservedScheduleNoLock()
    {
        if (PreservedNextMinigameAtUtc?.Count == 0)
            PreservedNextMinigameAtUtc = null;
    }

    private static void SetNextMinigameTime(BotMainHandler runtime, double minutesFromNow)
    {
        lock (MinigameGate)
        {
            if (MinigameLoops.TryGetValue(runtime, out MinigameLoopState? loop))
                loop.NextAtUtc = DateTime.UtcNow.AddMinutes(minutesFromNow);
        }
    }

    private static async Task WaitForMinigameDelayAsync(BotMainHandler runtime, MinigameLoopState expectedLoop, CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (MinigameGate)
        {
            if (!MinigameLoops.TryGetValue(runtime, out MinigameLoopState? loop) || !ReferenceEquals(loop, expectedLoop))
                return;

            delay = loop.NextAtUtc - DateTime.UtcNow;
        }

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

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

    private static void RemoveMinigameState(BotMainHandler runtime, bool preserveSchedule)
    {
        lock (MinigameGate)
        {
            ChickenRunStates.Remove(runtime);
            GuessNumberStates.Remove(runtime);
            WitherBattleStates.Remove(runtime);
            ActiveMinigames.Remove(runtime);
            if (!preserveSchedule)
            {
                PreservedNextMinigameAtUtc?.Remove(runtime);
                ClearEmptyPreservedScheduleNoLock();
            }
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

                switch (BotMainHandler.Randomizer.Next(0, 3))
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

                RemoveMinigameState(runtime, preserveSchedule: true);
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

    private static void ResetAllMinigameStatesNoLock(BotMainHandler runtime)
    {
        if (ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chickenState))
        {
            chickenState.BettingOpen = false;
            chickenState.Running = false;
            chickenState.MinSeconds = 0;
            chickenState.MaxSeconds = 0;
            chickenState.KillAtSeconds = 0;
            chickenState.Bets.Clear();
        }

        if (GuessNumberStates.TryGetValue(runtime, out GuessNumberState? guessState))
        {
            guessState.Active = false;
            guessState.TargetNumber = 0;
            guessState.LastGuessAtUtc.Clear();
        }

        if (WitherBattleStates.TryGetValue(runtime, out WitherBattleState? witherState))
        {
            witherState.BettingOpen = false;
            witherState.Running = false;
            witherState.CurrentHealth = 0;
            witherState.DefeatedSignal?.TrySetResult(false);
            witherState.DefeatedSignal = null;
            witherState.Bets.Clear();
        }
    }

    private static bool TryBeginMinigame(BotMainHandler runtime, string kind, out int runID)
    {
        runID = 0;

        if (runtime == null || string.IsNullOrWhiteSpace(kind))
            return false;

        lock (MinigameGate)
        {
            ActiveMinigameState state = GetActiveMinigameStateNoLock(runtime);
            if (!string.IsNullOrWhiteSpace(state.Kind))
                return false;

            ResetAllMinigameStatesNoLock(runtime);
            state.Kind = kind.Trim();
            state.RunID++;
            runID = state.RunID;
            return true;
        }
    }

    private static bool IsActiveMinigame(BotMainHandler runtime, string kind, int runID)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(kind) || runID <= 0)
            return false;

        lock (MinigameGate)
        {
            return ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? state)
                   && string.Equals(state.Kind, kind, StringComparison.Ordinal)
                   && state.RunID == runID;
        }
    }

    private static bool IsGuessNumberRoundActive(BotMainHandler runtime, int roundID)
    {
        if (runtime == null || roundID <= 0)
            return false;

        lock (MinigameGate)
        {
            return IsGuessNumberRoundActiveNoLock(runtime, GetGuessNumberStateNoLock(runtime), roundID);
        }
    }

    private static bool IsGuessNumberRoundActiveNoLock(BotMainHandler runtime, GuessNumberState state, int roundID)
    {
        return roundID > 0
               && ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? activeState)
               && string.Equals(activeState.Kind, "GuessNumber", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.Active
               && state.RoundID == roundID;
    }

    private static bool IsWitherBattleBettingOpenNoLock(BotMainHandler runtime, WitherBattleState state)
    {
        return ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? activeState)
               && string.Equals(activeState.Kind, "WitherBattle", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.BettingOpen;
    }

    private static void EndMinigame(BotMainHandler runtime, string kind, int runID)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(kind) || runID <= 0)
            return;

        lock (MinigameGate)
        {
            if (!ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? state)
                || !string.Equals(state.Kind, kind, StringComparison.Ordinal)
                || state.RunID != runID)
            {
                return;
            }

            ResetAllMinigameStatesNoLock(runtime);
            state.Kind = string.Empty;
        }
    }

    // ===== Shared minigame runtime helpers =====

    private static async Task SafeReplyAsync(BotMainHandler runtime, string message, CancellationToken cancellationToken)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            await runtime.SendToChannelAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            runtime.AddChatLogLine(ErrorHandling.FormatLogMessage("Minigame chat reply failed", ex));
        }
    }

    private static async Task PlayMinigameSoundAsync(BotMainHandler runtime, string soundID, CancellationToken cancellationToken)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(soundID))
            return;

        string sound = soundID.Trim();

        try
        {
            await runtime.SendServerCommandAsync(
                "execute as @a at @s run playsound " + sound + " master @s ~ ~ ~ 2 1",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            runtime.AddServerLogLine(ErrorHandling.FormatLogMessage("Minigame sound playback failed", ex));
        }
    }

    private static async Task ShowMinigameSubtitleAsync(BotMainHandler runtime, string subtitleText, CancellationToken cancellationToken)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(subtitleText))
            return;

        try
        {
            await runtime.SendServerCommandsAsync(
                [
                    MinecraftCommandBuilder.TitleTimes("@a", 0, 100, 10),
                    MinecraftCommandBuilder.Title("@a", " ", "white", runtime.UsesInlineTextComponentSyntax),
                    MinecraftCommandBuilder.Subtitle("@a", subtitleText, "yellow", runtime.UsesInlineTextComponentSyntax)
                ],
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            runtime.AddServerLogLine(ErrorHandling.FormatLogMessage("Minigame subtitle failed", ex));
        }
    }
}
