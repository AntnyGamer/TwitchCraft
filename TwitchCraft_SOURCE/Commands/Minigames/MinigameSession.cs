using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public static partial class MinigameManager
{
    private static void ResetStatesNoLock(MainHandler runtime)
    {
        if (ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chickenState))
        {
            chickenState.BettingOpen = false;
            chickenState.MinSeconds = 0;
            chickenState.MaxSeconds = 0;
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
            witherState.CurrentHealth = 0;
            witherState.DefeatedSignal?.TrySetResult();
            witherState.DefeatedSignal = null;
        }
    }

    private static bool TryStart(MainHandler runtime, string kind, out long runID)
    {
        runID = 0;

        if (runtime == null || string.IsNullOrWhiteSpace(kind))
            return false;

        lock (MinigameGate)
        {
            ActiveState state = GetActiveStateNoLock(runtime);
            if (!string.IsNullOrWhiteSpace(state.Kind) ||
                ChickenRunStates.TryGetValue(runtime, out ChickenRunState? chicken) && (chicken.Bets.Count > 0 || chicken.PendingSettlement != null) ||
                WitherBattleStates.TryGetValue(runtime, out WitherBattleState? wither) && (wither.Bets.Count > 0 || wither.PendingSettlement != null))
                return false;

            ResetStatesNoLock(runtime);
            state.Kind = kind.Trim();
            runID = state.RunID = ++_nextGeneration;
            return true;
        }
    }

    private static bool IsActive(MainHandler runtime, string kind, long runID)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(kind) || runID <= 0)
            return false;

        lock (MinigameGate)
        {
            return ActiveMinigames.TryGetValue(runtime, out ActiveState? state)
                   && string.Equals(state.Kind, kind, StringComparison.Ordinal)
                   && state.RunID == runID;
        }
    }

    private static bool IsGuessRoundActive(MainHandler runtime, long roundID)
    {
        if (runtime == null || roundID <= 0)
            return false;

        lock (MinigameGate)
        {
            return IsGuessRoundActiveNoLock(runtime, GetGuessStateNoLock(runtime), roundID);
        }
    }

    private static bool IsGuessRoundActiveNoLock(MainHandler runtime, GuessNumberState state, long roundID)
    {
        return roundID > 0
               && ActiveMinigames.TryGetValue(runtime, out ActiveState? activeState)
               && string.Equals(activeState.Kind, "GuessNumber", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.Active
               && state.RoundID == roundID;
    }

    private static bool IsWitherBettingOpenNoLock(MainHandler runtime, WitherBattleState state)
    {
        return ActiveMinigames.TryGetValue(runtime, out ActiveState? activeState)
               && string.Equals(activeState.Kind, "WitherBattle", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.BettingOpen;
    }

    private static void End(MainHandler runtime, string kind, long runID)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(kind) || runID <= 0)
            return;

        lock (MinigameGate)
        {
            if (!ActiveMinigames.TryGetValue(runtime, out ActiveState? state)
                || !string.Equals(state.Kind, kind, StringComparison.Ordinal)
                || state.RunID != runID)
            {
                return;
            }

            ResetStatesNoLock(runtime);
            state.Kind = string.Empty;
        }
    }

    // ===== Shared minigame runtime helpers =====

    private static async Task SafeReplyAsync(MainHandler runtime, string message, CancellationToken cancellationToken)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            await runtime.SendChatAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.AddChatLogLine(ErrorHandling.FormatLog("Minigame chat reply failed", ex));
        }
    }

    private static async Task PlaySoundAsync(MainHandler runtime, string soundID, CancellationToken cancellationToken)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.AddServerLogLine(ErrorHandling.FormatLog("Minigame sound playback failed", ex));
        }
    }

    private static async Task ShowSubtitleAsync(MainHandler runtime, string subtitleText, CancellationToken cancellationToken)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.AddServerLogLine(ErrorHandling.FormatLog("Minigame subtitle failed", ex));
        }
    }

    private static void RefundBets<TBet>(MainHandler runtime, BettingState<TBet> state) where TBet : IBet
    {
        lock (state.SettlementGate)
        {
            List<KeyValuePair<string, int>> adjustments;
            lock (MinigameGate)
            {
                if (state.PendingSettlement == null && state.Bets.Count == 0) return;
                adjustments = state.PendingSettlement ?? BuildRefunds(state.Bets);
            }
            if (runtime.Tokens.Adjust(adjustments))
                lock (MinigameGate) { state.Bets.Clear(); state.PendingSettlement = null; }
        }
    }

    private static void RefundChickenBets(MainHandler runtime)
    {
        ChickenRunState state;
        lock (MinigameGate)
        {
            if (!ChickenRunStates.TryGetValue(runtime, out state!)) return;
            state.BettingOpen = false;
        }
        RefundBets(runtime, state);
    }

    private static void RefundWitherBets(MainHandler runtime)
    {
        WitherBattleState state;
        lock (MinigameGate)
        {
            if (!WitherBattleStates.TryGetValue(runtime, out state!)) return;
            state.BettingOpen = false;
            state.CurrentHealth = 0;
            state.DefeatedSignal?.TrySetResult();
            state.DefeatedSignal = null;
        }
        RefundBets(runtime, state);
    }

    private static bool SettleBets<TBet>(MainHandler runtime, BettingState<TBet> state, string kind, long runID, List<KeyValuePair<string, int>> payouts) where TBet : IBet
    {
        lock (state.SettlementGate)
        {
            lock (MinigameGate)
            {
                if (state.Bets.Count == 0 ||
                    !ActiveMinigames.TryGetValue(runtime, out ActiveState? activeState) ||
                    !string.Equals(activeState.Kind, kind, StringComparison.Ordinal) || activeState.RunID != runID)
                    return false;
                state.PendingSettlement = payouts;
            }
            if (!runtime.Tokens.Adjust(payouts)) return false;
            lock (MinigameGate) { state.Bets.Clear(); state.PendingSettlement = null; }
            return true;
        }
    }

    internal enum BetUpdateResult
    {
        Updated,
        NotEnoughTokens,
        Closed,
        OverMax,
        OutOfRange
    }

    private static string FormatTokens(int amount)
        => amount.ToString(CultureInfo.InvariantCulture) + " token" + (amount == 1 ? "" : "s");

    private static string MaxBetMessage(string game)
        => "the max " + game + " bet is " + FormatTokens(MaxBetPerPlayer) + ".";

    internal static async Task<bool> ReplyBetErrorAsync(
        BetUpdateResult result,
        string sender,
        string game,
        string closedMessage,
        Func<string, CancellationToken, Task> sayToChannel,
        CancellationToken ct)
    {
        string? message = result switch
        {
            BetUpdateResult.NotEnoughTokens => "you do not have enough tokens for that bet.",
            BetUpdateResult.OverMax => MaxBetMessage(game),
            BetUpdateResult.Closed => closedMessage,
            _ => null
        };

        if (message == null)
            return false;

        await sayToChannel(sender + ", " + message, ct).ConfigureAwait(false);
        return true;
    }
}
