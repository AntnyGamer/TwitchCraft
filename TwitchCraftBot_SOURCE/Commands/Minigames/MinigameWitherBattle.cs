using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    private static async Task RunWitherBattleAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryBeginMinigame(runtime, "WitherBattle", out int runID))
            return;

        List<WitherBattleBet>? settlementBets = null;
        bool payoutApplied = false;

        try
        {
            WitherBattleState state = GetWitherBattleState(runtime);
            int witherHealth = BotMainHandler.SecureRandomInt(300, 501);
            TaskCompletionSource<bool> defeatedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (MinigameGate)
            {
                state.BettingOpen = true;
                state.Running = true;
                state.CurrentHealth = witherHealth;
                state.DefeatedSignal = defeatedSignal;
                state.Bets.Clear();
            }

            await PlayMinigameSoundAsync(runtime, "minecraft:entity.wither.shoot", cancellationToken).ConfigureAwait(false);
            await ShowMinigameSubtitleAsync(runtime, "WITHER BATTLE", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(
                runtime,
                "🔴 A Wither Battle has started! Use !damagewither <amount> for the next 5 minutes. Your token bet is your damage dealt. The Wither has " +
                witherHealth.ToString(CultureInfo.InvariantCulture) +
                " HP. Max " +
                MaxMinigameBetPerPlayer.ToString(CultureInfo.InvariantCulture) +
                " tokens per person. If the Wither dies in time, each player gets 1.2x their bet back. If it is not killed in 5 minutes, everyone loses half.",
                cancellationToken).ConfigureAwait(false);

            Task timerTask = Task.Delay(WitherBattleDuration, cancellationToken);
            await Task.WhenAny(timerTask, defeatedSignal.Task).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            lock (MinigameGate)
            {
                if (!ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? activeState) ||
                    !string.Equals(activeState.Kind, "WitherBattle", StringComparison.Ordinal) ||
                    activeState.RunID != runID)
                {
                    return;
                }

                state = GetWitherBattleStateNoLock(runtime);
                state.BettingOpen = false;
                state.Running = false;
                state.CurrentHealth = 0;
                state.DefeatedSignal = null;

                settlementBets = CloneBets(state.Bets, static bet => new WitherBattleBet
                {
                    Viewer = bet.Viewer,
                    TokenAmount = bet.TokenAmount
                });

                // From this point on, this method owns the settlement. Clearing here prevents
                // StopMinigameLoopsCore/RefundAllWitherBattleBets from refunding the same bets
                // while the cloned settlement list is also being paid out.
                state.Bets.Clear();
            }

            List<WitherBattleBet> bets = settlementBets!;
            if (bets.Count == 0)
            {
                await SafeReplyAsync(runtime, "The Wither Battle ended because nobody joined.", cancellationToken).ConfigureAwait(false);
                return;
            }

            int totalDamage = 0;
            for (int i = 0; i < bets.Count; i++)
            {
                WitherBattleBet bet = bets[i];
                totalDamage += bet.TokenAmount;
            }

            bool witherDefeated = totalDamage >= witherHealth;
            bool witherFled = witherDefeated && BotMainHandler.SecureRandomInt(10) == 0;

            double payoutMultiplier = witherDefeated ? (witherFled ? 0.75 : 1.2) : 0.5;
            List<KeyValuePair<string, int>> payouts = new(bets.Count);
            for (int i = 0; i < bets.Count; i++)
            {
                WitherBattleBet bet = bets[i];
                int payout = (int)Math.Round(bet.TokenAmount * payoutMultiplier, MidpointRounding.AwayFromZero);
                if (payout > 0)
                    payouts.Add(new(bet.Viewer, payout));
            }

            runtime.AwardTokens(payouts);
            payoutApplied = true;

            if (witherDefeated)
            {
                if (witherFled)
                {
                    await SafeReplyAsync(
                        runtime,
                        "The Wither should have been defeated, but it fled at the last second! Total damage: " +
                        totalDamage.ToString(CultureInfo.InvariantCulture) +
                        ". Everyone gets 75% of their stake back and only loses 25%.",
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SafeReplyAsync(
                        runtime,
                        "The Wither was defeated in time! Total damage: " +
                        totalDamage.ToString(CultureInfo.InvariantCulture) +
                        ". Everyone who joined gets 1.2x their tokens back!",
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await SafeReplyAsync(
                    runtime,
                    "Time is up! The Wither survived with " +
                    Math.Max(0, witherHealth - totalDamage).ToString(CultureInfo.InvariantCulture) +
                    " HP left. Everyone who joined gets 50% of their stake back and loses 50%.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (settlementBets is { Count: > 0 } && !payoutApplied)
                runtime.AdjustTokens(BuildRefunds(settlementBets));
            else if (IsActiveMinigame(runtime, "WitherBattle", runID))
                RefundAllWitherBattleBets(runtime);

            throw;
        }
        finally
        {
            EndMinigame(runtime, "WitherBattle", runID);
        }
    }

    // ===== Betting and round resolution helpers =====

}
