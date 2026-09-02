using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    private static async Task RunGuessNumberAsync(BotMainHandler runtime, CancellationToken cancellationToken)
    {
        if (!TryStartMinigame(runtime, "GuessNumber", out int runID))
            return;

        try
        {
            if (!TryStartGuessGame(runtime, out int roundID, out int minValue, out int maxValue))
                return;

            await PlaySoundAsync(runtime, "minecraft:block.note_block.pling", cancellationToken).ConfigureAwait(false);
            await ShowSubtitleAsync(runtime, "GUESS THE NUMBER", cancellationToken).ConfigureAwait(false);
            await SafeReplyAsync(
                runtime,
                string.Create(CultureInfo.InvariantCulture, $"🔴 Guess The Number has started! Use !guess <number> to guess a number between {minValue} and {maxValue}. If you win you will get free tokens! You have 60 seconds!"),
                cancellationToken).ConfigureAwait(false);

            DateTime endAtUtc = DateTime.UtcNow.AddSeconds(60.0);
            while (DateTime.UtcNow < endAtUtc)
            {
                await Task.Delay(OneSecondMinigameDelay, cancellationToken).ConfigureAwait(false);

                if (!IsGuessRoundActive(runtime, roundID))
                    break;
            }

            int answer = 0;
            bool shouldAnnounce = false;

            lock (MinigameGate)
            {
                GuessNumberState state = GetGuessStateNoLock(runtime);
                if (IsGuessRoundActiveNoLock(runtime, state, roundID))
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
                    string.Create(CultureInfo.InvariantCulture, $"Guess The Number is over! Nobody got it. The correct number was {answer}."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            EndMinigame(runtime, "GuessNumber", runID);
        }
    }

    private static bool TryStartGuessGame(BotMainHandler runtime, out int roundID, out int minValue, out int maxValue)
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

            GuessNumberState state = GetGuessStateNoLock(runtime);
            if (state.Active)
                return false;

            state.Active = true;
            state.LastGuessAtUtc.Clear();
            state.TargetNumber = BotMainHandler.SecureRandomInt(minValue, maxValue + 1);
            state.RoundID++;
            roundID = state.RoundID;
            return true;
        }
    }

    private static void EvaluateGuess(BotMainHandler runtime, int guess, out bool active, out bool correct, out int targetNumber)
    {
        active = false;
        correct = false;
        targetNumber = 0;

        if (runtime == null)
            return;

        lock (MinigameGate)
        {
            GuessNumberState state = GetGuessStateNoLock(runtime);
            active = IsGuessRoundActiveNoLock(runtime, state, state.RoundID);
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
