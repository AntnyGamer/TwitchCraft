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

    private static ChickenRunState GetChickenState(BotMainHandler runtime)
    {
        lock (MinigameGate)
        {
            return GetChickenStateNoLock(runtime);
        }
    }

    private static ChickenRunState GetChickenStateNoLock(BotMainHandler runtime)
    {
        if (!ChickenRunStates.TryGetValue(runtime, out ChickenRunState? state))
        {
            state = new();
            ChickenRunStates[runtime] = state;
        }

        return state;
    }

    private static GuessNumberState GetGuessStateNoLock(BotMainHandler runtime)
    {
        if (!GuessNumberStates.TryGetValue(runtime, out GuessNumberState? state))
        {
            state = new();
            GuessNumberStates[runtime] = state;
        }

        return state;
    }

    private static WitherBattleState GetWitherState(BotMainHandler runtime)
    {
        lock (MinigameGate)
        {
            return GetWitherStateNoLock(runtime);
        }
    }

    private static WitherBattleState GetWitherStateNoLock(BotMainHandler runtime)
    {
        if (!WitherBattleStates.TryGetValue(runtime, out WitherBattleState? state))
        {
            state = new();
            WitherBattleStates[runtime] = state;
        }

        return state;
    }

    private static ActiveMinigameState GetActiveStateNoLock(BotMainHandler runtime)
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
}
