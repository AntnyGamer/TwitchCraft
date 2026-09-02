using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    // ===== Chat command registration =====

    public static void AddMinigameHandlers(
        BotMainHandler runtime,
        Dictionary<string, ChatCommandHandler> handlers,
        Func<string, CancellationToken, Task> sayToChannel,
        Func<string, CancellationToken, Task> saySuccessfulToChannel)
    {
        handlers["chickenbet"] = async delegate (string[] args, string sender, CancellationToken ct)
        {
            if (args == null || args.Length < 2)
            {
                await sayToChannel(sender + ", usage: !chickenbet <tokenamt> <seconds>", ct).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int firstValue) || firstValue <= 0)
            {
                await sayToChannel(sender + ", please enter a valid token amount.", ct).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int secondValue) || secondValue <= 0)
            {
                await sayToChannel(sender + ", please enter a valid second value.", ct).ConfigureAwait(false);
                return;
            }

            bool bettingOpen;
            int minSeconds;
            int maxSeconds;
            int existingAmount;
            int existingBetSeconds;

            lock (MinigameGate)
            {
                ChickenRunState state = GetChickenStateNoLock(runtime);
                bettingOpen = state.BettingOpen;
                minSeconds = state.MinSeconds;
                maxSeconds = state.MaxSeconds;
                ChickenRunBet? existing = FindBet(state.Bets, sender);
                existingAmount = existing?.TokenAmount ?? 0;
                existingBetSeconds = existing?.BetSeconds ?? 0;
            }

            if (!bettingOpen)
            {
                await sayToChannel(sender + ", Chicken Run betting is not open right now.", ct).ConfigureAwait(false);
                return;
            }

            int tokenAmount = firstValue;
            int betSeconds = secondValue;

            if ((tokenAmount > MaxMinigameBetPerPlayer || betSeconds < minSeconds || betSeconds > maxSeconds) &&
                firstValue >= minSeconds && firstValue <= maxSeconds &&
                secondValue <= MaxMinigameBetPerPlayer)
            {
                betSeconds = firstValue;
                tokenAmount = secondValue;
            }

            if (tokenAmount > MaxMinigameBetPerPlayer)
            {
                await sayToChannel(sender + ", " + MaxBetMessage("Chicken Run"), ct).ConfigureAwait(false);
                return;
            }

            if (betSeconds < minSeconds || betSeconds > maxSeconds)
            {
                await sayToChannel(sender + ", your bet must be between " + minSeconds.ToString(CultureInfo.InvariantCulture) + " and " + maxSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.", ct).ConfigureAwait(false);
                return;
            }

            if (existingAmount + tokenAmount > MaxMinigameBetPerPlayer)
            {
                await sayToChannel(sender + ", " + MaxBetMessage("Chicken Run"), ct).ConfigureAwait(false);
                return;
            }

            int finalTokenAmount = existingAmount + tokenAmount;
            int finalBetSeconds = existingAmount > 0 ? existingBetSeconds : betSeconds;
            bool addedToExistingBet = existingAmount > 0;

            MinigameBetUpdateResult updateResult = TryAddPaidBet(
                runtime,
                sender,
                tokenAmount,
                () =>
                {
                    ChickenRunState state = GetChickenStateNoLock(runtime);
                    if (!state.BettingOpen)
                        return MinigameBetUpdateResult.Closed;

                    ChickenRunBet? existing = FindBet(state.Bets, sender);
                    if (existing == null)
                    {
                        state.Bets.Add(new ChickenRunBet
                        {
                            Viewer = sender,
                            TokenAmount = tokenAmount,
                            BetSeconds = betSeconds
                        });
                        finalTokenAmount = tokenAmount;
                        finalBetSeconds = betSeconds;
                        addedToExistingBet = false;
                    }
                    else
                    {
                        int updatedTokenAmount = existing.TokenAmount + tokenAmount;
                        if (updatedTokenAmount > MaxMinigameBetPerPlayer)
                            return MinigameBetUpdateResult.OverMax;

                        existing.TokenAmount = updatedTokenAmount;
                        finalTokenAmount = updatedTokenAmount;
                        finalBetSeconds = existing.BetSeconds;
                        addedToExistingBet = true;
                    }

                    return MinigameBetUpdateResult.Updated;
                });

            if (await ReplyBetErrorAsync(updateResult, sender, "Chicken Run", "Chicken Run betting is not open right now.", sayToChannel, ct).ConfigureAwait(false))
                return;

            if (addedToExistingBet)
            {
                await saySuccessfulToChannel(
                    sender + ", added " + FormatTokens(tokenAmount) + " to your Chicken Run bet. Your total is " +
                    FormatTokens(finalTokenAmount) + " for " + finalBetSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.",
                    ct).ConfigureAwait(false);
                return;
            }

            await saySuccessfulToChannel(
                sender + ", your Chicken Run bet is set for " + finalBetSeconds.ToString(CultureInfo.InvariantCulture) +
                " seconds with " + FormatTokens(finalTokenAmount) + ".",
                ct).ConfigureAwait(false);
        };

        handlers["guess"] = async delegate (string[] args, string sender, CancellationToken ct)
        {
            if (args == null || args.Length < 1)
            {
                await sayToChannel(sender + ", usage: !guess <number>", ct).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int guess) || guess < 1 || guess > 100)
            {
                await sayToChannel(sender + ", please enter a valid number between 1 and 100.", ct).ConfigureAwait(false);
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            bool gameActive;
            bool onCooldown = false;
            int waitSeconds = 0;

            lock (MinigameGate)
            {
                GuessNumberState state = GetGuessStateNoLock(runtime);
                gameActive = IsGuessRoundActiveNoLock(runtime, state, state.RoundID);

                if (gameActive)
                {
                    if (state.LastGuessAtUtc.TryGetValue(sender, out DateTime lastUtc))
                    {
                        double elapsed = (nowUtc - lastUtc).TotalSeconds;
                        if (elapsed < 5.0)
                        {
                            onCooldown = true;
                            waitSeconds = (int)Math.Ceiling(5.0 - elapsed);
                        }
                    }

                    if (!onCooldown)
                        state.LastGuessAtUtc[sender] = nowUtc;
                }
            }

            if (!gameActive)
            {
                await sayToChannel(sender + ", there is no Guess The Number round active right now.", ct).ConfigureAwait(false);
                return;
            }

            if (onCooldown)
            {
                await sayToChannel(sender + ", please wait " + waitSeconds.ToString(CultureInfo.InvariantCulture) + " second" + (waitSeconds == 1 ? "" : "s") + " before guessing again.", ct).ConfigureAwait(false);
                return;
            }

            EvaluateGuess(runtime, guess, out bool active, out bool correct, out int targetNumber);

            if (!active)
            {
                await sayToChannel(sender + ", there is no Guess The Number round active right now.", ct).ConfigureAwait(false);
                return;
            }

            if (!correct)
            {
                await saySuccessfulToChannel(sender + ", wrong guess.", ct).ConfigureAwait(false);
                return;
            }

            runtime.Tokens.Award(sender, 10); //Guess Number Win
            await saySuccessfulToChannel(sender + ", you guessed the number " + targetNumber.ToString(CultureInfo.InvariantCulture) + " and won 10 tokens!", ct).ConfigureAwait(false);
        };

        handlers["damagewither"] = async delegate (string[] args, string sender, CancellationToken ct)
        {
            if (args == null || args.Length < 1)
            {
                await sayToChannel(sender + ", usage: !damagewither <tokenamt> (your token bet is your damage)", ct).ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tokenAmount) || tokenAmount <= 0)
            {
                await sayToChannel(sender + ", please enter a valid token amount.", ct).ConfigureAwait(false);
                return;
            }

            if (tokenAmount > MaxMinigameBetPerPlayer)
            {
                await sayToChannel(sender + ", " + MaxBetMessage("Wither Battle"), ct).ConfigureAwait(false);
                return;
            }

            bool bettingOpen;
            int existingAmount;

            lock (MinigameGate)
            {
                WitherBattleState state = GetWitherStateNoLock(runtime);
                bettingOpen = IsWitherBettingOpenNoLock(runtime, state);
                WitherBattleBet? existing = FindBet(state.Bets, sender);
                existingAmount = existing?.TokenAmount ?? 0;
            }

            if (!bettingOpen)
            {
                await sayToChannel(sender + ", a Wither Battle is not active right now.", ct).ConfigureAwait(false);
                return;
            }

            if (existingAmount + tokenAmount > MaxMinigameBetPerPlayer)
            {
                await sayToChannel(sender + ", " + MaxBetMessage("Wither Battle"), ct).ConfigureAwait(false);
                return;
            }

            int finalTokenAmount = existingAmount + tokenAmount;
            int remainingHealth = 0;
            bool addedToExistingBet = existingAmount > 0;

            MinigameBetUpdateResult updateResult = TryAddPaidBet(
                runtime,
                sender,
                tokenAmount,
                () =>
                {
                    WitherBattleState state = GetWitherStateNoLock(runtime);
                    if (!IsWitherBettingOpenNoLock(runtime, state))
                        return MinigameBetUpdateResult.Closed;

                    WitherBattleBet? existing = FindBet(state.Bets, sender);
                    if (existing == null)
                    {
                        state.Bets.Add(new WitherBattleBet
                        {
                            Viewer = sender,
                            TokenAmount = tokenAmount
                        });
                        finalTokenAmount = tokenAmount;
                        addedToExistingBet = false;
                    }
                    else
                    {
                        int updatedTokenAmount = existing.TokenAmount + tokenAmount;
                        if (updatedTokenAmount > MaxMinigameBetPerPlayer)
                            return MinigameBetUpdateResult.OverMax;

                        existing.TokenAmount = updatedTokenAmount;
                        finalTokenAmount = updatedTokenAmount;
                        addedToExistingBet = true;
                    }

                    state.CurrentHealth = Math.Max(0, state.CurrentHealth - tokenAmount);
                    remainingHealth = state.CurrentHealth;
                    if (remainingHealth == 0)
                    {
                        state.BettingOpen = false;
                        state.DefeatedSignal?.TrySetResult(true);
                    }

                    return MinigameBetUpdateResult.Updated;
                });

            if (await ReplyBetErrorAsync(updateResult, sender, "Wither Battle", "a Wither Battle is not active right now.", sayToChannel, ct).ConfigureAwait(false))
                return;

            if (addedToExistingBet)
            {
                await saySuccessfulToChannel(
                    sender + ", added " + FormatTokens(tokenAmount) + " to your Wither Battle damage. Your total is " +
                    FormatTokens(finalTokenAmount) + ". Wither HP left: " + remainingHealth.ToString(CultureInfo.InvariantCulture) + ".",
                    ct).ConfigureAwait(false);
                return;
            }

            await saySuccessfulToChannel(
                sender + ", your Wither Battle damage is set to " + FormatTokens(finalTokenAmount) +
                ". Wither HP left: " + remainingHealth.ToString(CultureInfo.InvariantCulture) + ".",
                ct).ConfigureAwait(false);
        };
    }
}
