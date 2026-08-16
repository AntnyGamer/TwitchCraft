using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    // ===== Minigame runners =====

    private static void RefundAllChickenRunBets(BotMainHandler runtime)
    {
        List<KeyValuePair<string, int>>? refunds = null;

        lock (MinigameGate)
        {
            ChickenRunState state = GetChickenRunStateNoLock(runtime);
            state.BettingOpen = false;
            state.Running = false;
            if (state.Bets.Count > 0)
                refunds = BuildRefunds(state.Bets);
            state.Bets.Clear();
        }

        if (refunds is { Count: > 0 })
            runtime.AdjustTokens(refunds); //Chicken Run Refund
    }

    private static void RefundAllWitherBattleBets(BotMainHandler runtime)
    {
        List<KeyValuePair<string, int>>? refunds = null;

        lock (MinigameGate)
        {
            WitherBattleState state = GetWitherBattleStateNoLock(runtime);
            state.BettingOpen = false;
            state.Running = false;
            state.CurrentHealth = 0;
            state.DefeatedSignal?.TrySetResult(false);
            state.DefeatedSignal = null;
            if (state.Bets.Count > 0)
                refunds = BuildRefunds(state.Bets);
            state.Bets.Clear();
        }

        if (refunds is { Count: > 0 })
            runtime.AdjustTokens(refunds); //Wither Battle Refund
    }

    internal enum MinigameBetUpdateResult
    {
        Updated,
        NotEnoughTokens,
        Closed,
        OverMax
    }

    internal static MinigameBetUpdateResult TryAddPaidBet(
        BotMainHandler runtime,
        string sender,
        int additionalTokenAmount,
        Func<MinigameBetUpdateResult> updateBetNoLock)
    {
        if (!runtime.TrySpendTokens(sender, additionalTokenAmount))
            return MinigameBetUpdateResult.NotEnoughTokens;

        MinigameBetUpdateResult result;
        lock (MinigameGate)
            result = updateBetNoLock();

        if (result != MinigameBetUpdateResult.Updated)
            runtime.AdjustTokens(sender, additionalTokenAmount);

        return result;
    }

    private static string FormatTokens(int amount)
        => amount.ToString(CultureInfo.InvariantCulture) + " token" + (amount == 1 ? "" : "s");

    private static string MaxBetMessage(string game)
        => "the max " + game + " bet is " + FormatTokens(MaxMinigameBetPerPlayer) + ".";

    internal static async Task<bool> TrySayBetFailureAsync(
        MinigameBetUpdateResult result,
        string sender,
        string game,
        string closedMessage,
        Func<string, CancellationToken, Task> sayToChannel,
        CancellationToken ct)
    {
        string? message = result switch
        {
            MinigameBetUpdateResult.NotEnoughTokens => "you do not have enough tokens for that bet.",
            MinigameBetUpdateResult.OverMax => MaxBetMessage(game),
            MinigameBetUpdateResult.Closed => closedMessage,
            _ => null
        };

        if (message == null)
            return false;

        await sayToChannel(sender + ", " + message, ct).ConfigureAwait(false);
        return true;
    }

}
