using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
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
                "🔴 Guess The Number has started! Use !guess <number> to guess a number between " +
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

}
