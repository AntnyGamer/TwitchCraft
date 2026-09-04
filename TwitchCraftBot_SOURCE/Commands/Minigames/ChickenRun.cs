using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    private static async Task RunChickenAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryStartMinigame(runtime, "ChickenRun", out int runID))
            return;

        try
        {
            ChickenRunState state = GetChickenState(runtime);
            int minSeconds;
            int maxSeconds;

            lock (MinigameGate)
            {
                int span = BotMainHandler.SecureRandomInt(100, 121);
                int min = BotMainHandler.SecureRandomInt(5, 601 - span);

                state.BettingOpen = true;
                state.Running = false;
                state.MinSeconds = min;
                state.MaxSeconds = min + span;
                state.KillAtSeconds = 0;
                state.Bets.Clear();

                minSeconds = state.MinSeconds;
                maxSeconds = state.MaxSeconds;
            }

            await PlaySoundAsync(runtime, "minecraft:entity.chicken.ambient", cancellationToken).ConfigureAwait(false);
            await ShowSubtitleAsync(runtime, "CHICKEN RUN", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(
                runtime,
                "🔴 A Chicken Run is starting in 1 minute! You can bet between " +
                minSeconds.ToString(CultureInfo.InvariantCulture) + "-" +
                maxSeconds.ToString(CultureInfo.InvariantCulture) +
                " seconds for how long the chicken survives! Max " +
                MaxMinigameBetPerPlayer.ToString(CultureInfo.InvariantCulture) +
                " tokens per person. Max-second bets pay 3x if the chicken survives the full range. (!chickenbet <amount> <seconds>)",
                cancellationToken).ConfigureAwait(false);

            await Task.Delay(ChickenRunBettingDelay, cancellationToken).ConfigureAwait(false);

            if (!IsMinigameActive(runtime, "ChickenRun", runID))
                return;

            bool hasBets;
            lock (MinigameGate)
            {
                state = GetChickenStateNoLock(runtime);
                hasBets = state.Bets.Count > 0;

                if (!hasBets)
                {
                    state.BettingOpen = false;
                    state.Running = false;
                    state.KillAtSeconds = 0;
                }
            }

            if (!hasBets)
            {
                await SafeReplyAsync(runtime, "Chicken Run was cancelled because nobody placed a bet.", cancellationToken).ConfigureAwait(false);
                return;
            }

            int killAtSeconds;
            lock (MinigameGate)
            {
                state = GetChickenStateNoLock(runtime);
                state.BettingOpen = false;
                state.Running = true;
                state.KillAtSeconds = BotMainHandler.SecureRandomInt(state.MinSeconds, state.MaxSeconds + 1);
                killAtSeconds = state.KillAtSeconds;
            }

            await PlaySoundAsync(runtime, "minecraft:entity.chicken.ambient", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(runtime, "Chicken Run has started! The chicken is now running...", cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(killAtSeconds), cancellationToken).ConfigureAwait(false);

            if (!IsMinigameActive(runtime, "ChickenRun", runID))
                return;

            List<ChickenRunBet> bets;
            lock (MinigameGate)
            {
                state = GetChickenStateNoLock(runtime);
                state.BettingOpen = false;
                state.Running = false;
                bets = CloneBets(state.Bets, static bet => new ChickenRunBet
                {
                    Viewer = bet.Viewer,
                    TokenAmount = bet.TokenAmount,
                    BetSeconds = bet.BetSeconds
                });
                state.Bets.Clear();
            }

            double multiplierPerSecond = 1.98 / (maxSeconds - minSeconds);
            List<KeyValuePair<string, int>> payouts = new(bets.Count);
            for (int i = 0; i < bets.Count; i++)
            {
                ChickenRunBet bet = bets[i];
                bool won = bet.BetSeconds <= killAtSeconds;
                if (!won)
                    continue;

                double multiplier = 1.02 + ((bet.BetSeconds - minSeconds) * multiplierPerSecond);
                int payout = (int)Math.Round(bet.TokenAmount * multiplier, MidpointRounding.AwayFromZero);
                if (payout > 0)
                    payouts.Add(new(bet.Viewer, payout)); //Chicken Run Win
            }

            if (payouts.Count > 0)
                runtime.Tokens.Adjust(payouts);

            await SafeReplyAsync(
                runtime,
                "The chicken has been killed at " +
                killAtSeconds.ToString(CultureInfo.InvariantCulture) +
                "! Viewers who bet by or before this time win! Bets can pay up to 3x if the chicken survives (later bets pay more).",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsMinigameActive(runtime, "ChickenRun", runID))
                RefundChickenBets(runtime);

            throw;
        }
        finally
        {
            EndMinigame(runtime, "ChickenRun", runID);
        }
    }
}
