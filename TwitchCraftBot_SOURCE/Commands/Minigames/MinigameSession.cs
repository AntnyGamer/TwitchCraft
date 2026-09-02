using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    private static void ResetStatesNoLock(BotMainHandler runtime)
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

    private static bool TryStartMinigame(BotMainHandler runtime, string kind, out int runID)
    {
        runID = 0;

        if (runtime == null || string.IsNullOrWhiteSpace(kind))
            return false;

        lock (MinigameGate)
        {
            ActiveMinigameState state = GetActiveStateNoLock(runtime);
            if (!string.IsNullOrWhiteSpace(state.Kind))
                return false;

            ResetStatesNoLock(runtime);
            state.Kind = kind.Trim();
            state.RunID++;
            runID = state.RunID;
            return true;
        }
    }

    private static bool IsMinigameActive(BotMainHandler runtime, string kind, int runID)
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

    private static bool IsGuessRoundActive(BotMainHandler runtime, int roundID)
    {
        if (runtime == null || roundID <= 0)
            return false;

        lock (MinigameGate)
        {
            return IsGuessRoundActiveNoLock(runtime, GetGuessStateNoLock(runtime), roundID);
        }
    }

    private static bool IsGuessRoundActiveNoLock(BotMainHandler runtime, GuessNumberState state, int roundID)
    {
        return roundID > 0
               && ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? activeState)
               && string.Equals(activeState.Kind, "GuessNumber", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.Active
               && state.RoundID == roundID;
    }

    private static bool IsWitherBettingOpenNoLock(BotMainHandler runtime, WitherBattleState state)
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

            ResetStatesNoLock(runtime);
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
            await runtime.SendChatAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.AddChatLogLine(ErrorHandling.FormatLog("Minigame chat reply failed", ex));
        }
    }

    private static async Task PlaySoundAsync(BotMainHandler runtime, string soundID, CancellationToken cancellationToken)
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

    private static async Task ShowSubtitleAsync(BotMainHandler runtime, string subtitleText, CancellationToken cancellationToken)
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

    private static void RefundChickenBets(BotMainHandler runtime)
    {
        List<KeyValuePair<string, int>>? refunds = null;

        lock (MinigameGate)
        {
            ChickenRunState state = GetChickenStateNoLock(runtime);
            state.BettingOpen = false;
            state.Running = false;
            if (state.Bets.Count > 0)
                refunds = BuildRefunds(state.Bets);
            state.Bets.Clear();
        }

        if (refunds is { Count: > 0 })
            runtime.Tokens.Adjust(refunds); //Chicken Run Refund
    }

    private static void RefundWitherBets(BotMainHandler runtime)
    {
        List<KeyValuePair<string, int>>? refunds = null;

        lock (MinigameGate)
        {
            WitherBattleState state = GetWitherStateNoLock(runtime);
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
            runtime.Tokens.Adjust(refunds); //Wither Battle Refund
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
        if (!runtime.Tokens.TrySpend(sender, additionalTokenAmount))
            return MinigameBetUpdateResult.NotEnoughTokens;

        MinigameBetUpdateResult result;
        lock (MinigameGate)
            result = updateBetNoLock();

        if (result != MinigameBetUpdateResult.Updated)
            runtime.Tokens.Adjust(sender, additionalTokenAmount);

        return result;
    }

    private static string FormatTokens(int amount)
        => amount.ToString(CultureInfo.InvariantCulture) + " token" + (amount == 1 ? "" : "s");

    private static string MaxBetMessage(string game)
        => "the max " + game + " bet is " + FormatTokens(MaxMinigameBetPerPlayer) + ".";

    internal static async Task<bool> ReplyBetErrorAsync(
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
