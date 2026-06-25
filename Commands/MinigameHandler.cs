using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    // ===== Minigame runners =====

    private static async Task RunChickenRunAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryBeginMinigame(runtime, "ChickenRun", out int runID))
            return;

        try
        {
            ChickenRunState state = GetChickenRunState(runtime);
            int minSeconds;
            int maxSeconds;

            lock (MinigameGate)
            {
                int span = BotMainHandler.Randomizer.Next(100, 121);
                int min = BotMainHandler.Randomizer.Next(5, 601 - span);

                state.BettingOpen = true;
                state.Running = false;
                state.MinSeconds = min;
                state.MaxSeconds = min + span;
                state.KillAtSeconds = 0;
                state.Bets.Clear();

                minSeconds = state.MinSeconds;
                maxSeconds = state.MaxSeconds;
            }

            await PlayMinigameSoundAsync(runtime, "minecraft:entity.chicken.ambient", cancellationToken).ConfigureAwait(false);
            await ShowMinigameSubtitleAsync(runtime, "CHICKEN RUN", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(
                runtime,
                "🔴 A Chicken Run is starting in 1 minute! You can bet between " +
                minSeconds.ToString(CultureInfo.InvariantCulture) + "-" +
                maxSeconds.ToString(CultureInfo.InvariantCulture) +
                " seconds for how long the chicken survives! Max " +
                MaxMinigameBetPerPlayer.ToString(CultureInfo.InvariantCulture) +
                " tokens per person. Max-second bets pay 3x if the chicken survives the full range. (!chickenbet [<amount>] [<seconds>])",
                cancellationToken).ConfigureAwait(false);

            await Task.Delay(ChickenRunBettingDelay, cancellationToken).ConfigureAwait(false);

            if (!IsActiveMinigame(runtime, "ChickenRun", runID))
                return;

            bool hasBets;
            lock (MinigameGate)
            {
                state = GetChickenRunStateNoLock(runtime);
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
                state = GetChickenRunStateNoLock(runtime);
                state.BettingOpen = false;
                state.Running = true;
                state.KillAtSeconds = BotMainHandler.Randomizer.Next(state.MinSeconds, state.MaxSeconds + 1);
                killAtSeconds = state.KillAtSeconds;
            }

            await PlayMinigameSoundAsync(runtime, "minecraft:entity.chicken.ambient", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(runtime, "Chicken Run has started! The chicken is now running...", cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(killAtSeconds), cancellationToken).ConfigureAwait(false);

            if (!IsActiveMinigame(runtime, "ChickenRun", runID))
                return;

            List<ChickenRunBet> bets;
            lock (MinigameGate)
            {
                state = GetChickenRunStateNoLock(runtime);
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
                runtime.AdjustTokens(payouts);

            await SafeReplyAsync(
                runtime,
                "The chicken has been killed at " +
                killAtSeconds.ToString(CultureInfo.InvariantCulture) +
                "! Viewers who bet by or before this time win! Bets can pay up to 3x if the chicken survives (later bets pay more).",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsActiveMinigame(runtime, "ChickenRun", runID))
                RefundAllChickenRunBets(runtime);

            throw;
        }
        finally
        {
            EndMinigame(runtime, "ChickenRun", runID);
        }
    }

    private static async Task RunGuessNumberAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryBeginMinigame(runtime, "GuessNumber", out int runID))
            return;

        try
        {
            if (!TryStartGuessNumberGame(runtime, out int roundID, out int minValue, out int maxValue))
                return;

            await PlayMinigameSoundAsync(runtime, "minecraft:block.note_block.pling", cancellationToken).ConfigureAwait(false);
            await ShowMinigameSubtitleAsync(runtime, "GUESS THE NUMBER", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(
                runtime,
                "🔴 Guess The Number has started! Use !guess [<number>] to guess a number between " +
                minValue.ToString(CultureInfo.InvariantCulture) + " and " +
                maxValue.ToString(CultureInfo.InvariantCulture) + ". If you win you will get free tokens! You have 60 seconds!",
                cancellationToken).ConfigureAwait(false);

            DateTime endAtUtc = DateTime.UtcNow.AddSeconds(60.0);
            while (DateTime.UtcNow < endAtUtc)
            {
                await Task.Delay(OneSecondMinigameDelay, cancellationToken).ConfigureAwait(false);

                if (!IsGuessNumberRoundActive(runtime, roundID))
                    break;
            }

            int answer = 0;
            bool shouldAnnounce = false;

            lock (MinigameGate)
            {
                GuessNumberState state = GetGuessNumberStateNoLock(runtime);
                if (IsGuessNumberRoundActiveNoLock(runtime, state, roundID))
                {
                    state.Active = false;
                    answer = state.TargetNumber;
                    shouldAnnounce = true;
                }
            }

            if (shouldAnnounce)
            {
                await SafeReplyAsync(
                    runtime,
                    "Guess The Number is over! Nobody got it. The correct number was " +
                    answer.ToString(CultureInfo.InvariantCulture) + ".",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            EndMinigame(runtime, "GuessNumber", runID);
        }
    }

    private static async Task RunWitherBattleAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryBeginMinigame(runtime, "WitherBattle", out int runID))
            return;

        List<WitherBattleBet>? settlementBets = null;
        bool payoutApplied = false;

        try
        {
            WitherBattleState state = GetWitherBattleState(runtime);
            int witherHealth = BotMainHandler.Randomizer.Next(300, 501);
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
                "🔴 A Wither Battle has started! Use !damagewither [<amount>] for the next 5 minutes. Your token bet is your damage dealt. The Wither has " +
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

            List<WitherBattleBet> bets = settlementBets ?? [];
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
            bool witherFled = witherDefeated && BotMainHandler.Randomizer.Next(0, 10) == 0;

            double payoutMultiplier = witherDefeated ? (witherFled ? 0.75 : 1.2) : 0.5;
            List<KeyValuePair<string, int>> payouts = new(bets.Count);
            for (int i = 0; i < bets.Count; i++)
            {
                WitherBattleBet bet = bets[i];
                int payout = (int)Math.Round(bet.TokenAmount * payoutMultiplier, MidpointRounding.AwayFromZero);
                if (payout > 0)
                    payouts.Add(new(bet.Viewer, payout));
            }

            runtime.AdjustTokens(payouts);
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

    private static bool TryStartGuessNumberGame(BotMainHandler runtime, out int roundID, out int minValue, out int maxValue)
    {
        roundID = 0;
        minValue = 1;
        maxValue = 100;

        if (runtime == null)
            return false;

        lock (MinigameGate)
        {
            if (!ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? active)
                || !string.Equals(active?.Kind, "GuessNumber", StringComparison.Ordinal))
            {
                return false;
            }

            GuessNumberState state = GetGuessNumberStateNoLock(runtime);
            if (state.Active)
                return false;

            state.Active = true;
            state.LastGuessAtUtc.Clear();
            state.TargetNumber = BotMainHandler.Randomizer.Next(minValue, maxValue + 1);
            state.RoundID++;
            roundID = state.RoundID;
            return true;
        }
    }

    private static void TryResolveGuess(BotMainHandler runtime, int guess, out bool active, out bool correct, out int targetNumber)
    {
        active = false;
        correct = false;
        targetNumber = 0;

        if (runtime == null)
            return;

        lock (MinigameGate)
        {
            GuessNumberState state = GetGuessNumberStateNoLock(runtime);
            active = IsGuessNumberRoundActiveNoLock(runtime, state, state.RoundID);
            targetNumber = state.TargetNumber;
            if (!active)
                return;

            if (guess == state.TargetNumber)
            {
                correct = true;
                state.Active = false;
            }
        }
    }

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

    private enum MinigameBetUpdateResult
    {
        Updated,
        NotEnoughTokens,
        Closed,
        OverMax
    }

    private static MinigameBetUpdateResult TryAddPaidBet(
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

    private static async Task<bool> TrySayBetFailureAsync(
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

    // ===== Chat command registration =====

    public static void AddMinigameHandlers(
        BotMainHandler runtime,
        Dictionary<string, ChatCommandHandler> handlers,
        Func<string, CancellationToken, Task> sayToChannel)
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
                ChickenRunState state = GetChickenRunStateNoLock(runtime);
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
                    ChickenRunState state = GetChickenRunStateNoLock(runtime);
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

            if (await TrySayBetFailureAsync(updateResult, sender, "Chicken Run", "Chicken Run betting is not open right now.", sayToChannel, ct).ConfigureAwait(false))
                return;

            if (addedToExistingBet)
            {
                await sayToChannel(
                    sender + ", added " + FormatTokens(tokenAmount) + " to your Chicken Run bet. Your total is " +
                    FormatTokens(finalTokenAmount) + " for " + finalBetSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.",
                    ct).ConfigureAwait(false);
                return;
            }

            await sayToChannel(
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
                GuessNumberState state = GetGuessNumberStateNoLock(runtime);
                gameActive = IsGuessNumberRoundActiveNoLock(runtime, state, state.RoundID);

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

            TryResolveGuess(runtime, guess, out bool active, out bool correct, out int targetNumber);

            if (!active)
            {
                await sayToChannel(sender + ", there is no Guess The Number round active right now.", ct).ConfigureAwait(false);
                return;
            }

            if (!correct)
            {
                await sayToChannel(sender + ", wrong guess.", ct).ConfigureAwait(false);
                return;
            }

            runtime.AdjustTokens(sender, 10); //Guess Number Win
            await sayToChannel(sender + ", you guessed the number " + targetNumber.ToString(CultureInfo.InvariantCulture) + " and won 10 tokens!", ct).ConfigureAwait(false);
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
                WitherBattleState state = GetWitherBattleStateNoLock(runtime);
                bettingOpen = IsWitherBattleBettingOpenNoLock(runtime, state);
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
                    WitherBattleState state = GetWitherBattleStateNoLock(runtime);
                    if (!IsWitherBattleBettingOpenNoLock(runtime, state))
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

            if (await TrySayBetFailureAsync(updateResult, sender, "Wither Battle", "a Wither Battle is not active right now.", sayToChannel, ct).ConfigureAwait(false))
                return;

            if (addedToExistingBet)
            {
                await sayToChannel(
                    sender + ", added " + FormatTokens(tokenAmount) + " to your Wither Battle damage. Your total is " +
                    FormatTokens(finalTokenAmount) + ". Wither HP left: " + remainingHealth.ToString(CultureInfo.InvariantCulture) + ".",
                    ct).ConfigureAwait(false);
                return;
            }

            await sayToChannel(
                sender + ", your Wither Battle damage is set to " + FormatTokens(finalTokenAmount) +
                ". Wither HP left: " + remainingHealth.ToString(CultureInfo.InvariantCulture) + ".",
                ct).ConfigureAwait(false);
        };
    }
}
