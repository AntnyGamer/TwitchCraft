using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public static partial class MinigameManager
{
    private const int MaxBetPerPlayer = 200;
    private static readonly TimeSpan OneSecondDelay = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan ChickenRunBettingDelay = TimeSpan.FromMinutes(1.0);
    private static readonly TimeSpan WitherBattleDuration = TimeSpan.FromMinutes(5.0);

    private static readonly Lock MinigameGate = new();
    private static readonly Dictionary<MainHandler, ChickenRunState> ChickenRunStates = [];
    private static readonly Dictionary<MainHandler, GuessNumberState> GuessNumberStates = [];
    private static readonly Dictionary<MainHandler, WitherBattleState> WitherBattleStates = [];
    private static readonly Dictionary<MainHandler, LoopState> MinigameLoops = [];
    private static Dictionary<MainHandler, DateTime>? PreservedNextAtUtc;
    private static readonly Dictionary<MainHandler, ActiveState> ActiveMinigames = [];
    private static long _nextGeneration;

    // ===== State model types =====

    private sealed class LoopState(MainHandler runtime, CancellationTokenSource cts, DateTime nextAtUtc)
    {
        public MainHandler Runtime { get; } = runtime;
        public CancellationTokenSource Cts { get; } = cts;
        public DateTime NextAtUtc { get; set; } = nextAtUtc;
        public Task? Task { get; set; }
    }

    private interface IBet
    {
        string Viewer { get; }
        int TokenAmount { get; }
    }

    private abstract class BettingState<TBet> where TBet : IBet
    {
        public bool BettingOpen { get; set; }
        public List<TBet> Bets { get; } = [];
        // Preserve calculated winnings across transient settlement failures.
        public List<KeyValuePair<string, int>>? PendingSettlement { get; set; }
        public Lock SettlementGate { get; } = new();
    }

    private sealed class ChickenRunBet : IBet
    {
        public string Viewer { get; set; } = string.Empty;
        public int TokenAmount { get; set; }
        public int BetSeconds { get; set; }
    }

    private sealed class ChickenRunState : BettingState<ChickenRunBet>
    {
        public int MinSeconds { get; set; }
        public int MaxSeconds { get; set; }
    }

    private sealed class GuessNumberState
    {
        public bool Active { get; set; }
        public int TargetNumber { get; set; }
        public long RoundID { get; set; }
        public Dictionary<string, DateTime> LastGuessAtUtc { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WitherBattleBet : IBet
    {
        public string Viewer { get; set; } = string.Empty;
        public int TokenAmount { get; set; }
    }

    private sealed class WitherBattleState : BettingState<WitherBattleBet>
    {
        public int CurrentHealth { get; set; }
        public TaskCompletionSource? DefeatedSignal { get; set; }
    }

    private sealed class ActiveState
    {
        public string Kind { get; set; } = string.Empty;
        public long RunID { get; set; }
    }

    // ===== State access helpers =====

    private static ChickenRunState GetChickenState(MainHandler runtime)
    {
        lock (MinigameGate)
            return GetChickenStateNoLock(runtime);
    }

    private static TState GetStateNoLock<TState>(Dictionary<MainHandler, TState> states, MainHandler runtime)
        where TState : class, new()
    {
        if (!states.TryGetValue(runtime, out TState? state))
        {
            state = new();
            states[runtime] = state;
        }

        return state;
    }

    private static ChickenRunState GetChickenStateNoLock(MainHandler runtime)
        => GetStateNoLock(ChickenRunStates, runtime);

    private static GuessNumberState GetGuessStateNoLock(MainHandler runtime)
        => GetStateNoLock(GuessNumberStates, runtime);

    private static WitherBattleState GetWitherState(MainHandler runtime)
    {
        lock (MinigameGate)
            return GetWitherStateNoLock(runtime);
    }

    private static WitherBattleState GetWitherStateNoLock(MainHandler runtime)
        => GetStateNoLock(WitherBattleStates, runtime);

    private static ActiveState GetActiveStateNoLock(MainHandler runtime)
        => GetStateNoLock(ActiveMinigames, runtime);

    private static TBet? FindBet<TBet>(List<TBet> bets, string viewer) where TBet : class, IBet
    {
        for (int i = 0; i < bets.Count; i++)
        {
            if (string.Equals(bets[i].Viewer, viewer, StringComparison.OrdinalIgnoreCase))
                return bets[i];
        }

        return null;
    }

    private static List<TBet> CloneBets<TBet>(List<TBet> bets, Func<TBet, TBet> cloneBet) where TBet : class, IBet
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

}
