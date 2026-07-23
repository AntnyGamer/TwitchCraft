using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public static partial class MinigameManager
{
    private static void ResetAllMinigameStatesNoLock(BotMainHandler runtime)
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

    private static bool TryBeginMinigame(BotMainHandler runtime, string kind, out int runID)
    {
        runID = 0;

        if (runtime == null || string.IsNullOrWhiteSpace(kind))
            return false;

        lock (MinigameGate)
        {
            ActiveMinigameState state = GetActiveMinigameStateNoLock(runtime);
            if (!string.IsNullOrWhiteSpace(state.Kind))
                return false;

            ResetAllMinigameStatesNoLock(runtime);
            state.Kind = kind.Trim();
            state.RunID++;
            runID = state.RunID;
            return true;
        }
    }

    private static bool IsActiveMinigame(BotMainHandler runtime, string kind, int runID)
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

    private static bool IsGuessNumberRoundActive(BotMainHandler runtime, int roundID)
    {
        if (runtime == null || roundID <= 0)
            return false;

        lock (MinigameGate)
        {
            return IsGuessNumberRoundActiveNoLock(runtime, GetGuessNumberStateNoLock(runtime), roundID);
        }
    }

    private static bool IsGuessNumberRoundActiveNoLock(BotMainHandler runtime, GuessNumberState state, int roundID)
    {
        return roundID > 0
               && ActiveMinigames.TryGetValue(runtime, out ActiveMinigameState? activeState)
               && string.Equals(activeState.Kind, "GuessNumber", StringComparison.Ordinal)
               && activeState.RunID > 0
               && state.Active
               && state.RoundID == roundID;
    }

    private static bool IsWitherBattleBettingOpenNoLock(BotMainHandler runtime, WitherBattleState state)
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

            ResetAllMinigameStatesNoLock(runtime);
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
            await runtime.SendToChannelAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            runtime.AddChatLogLine(ErrorHandling.FormatLogMessage("Minigame chat reply failed", ex));
        }
    }

    private static async Task PlayMinigameSoundAsync(BotMainHandler runtime, string soundID, CancellationToken cancellationToken)
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
            runtime.AddServerLogLine(ErrorHandling.FormatLogMessage("Minigame sound playback failed", ex));
        }
    }

    private static async Task ShowMinigameSubtitleAsync(BotMainHandler runtime, string subtitleText, CancellationToken cancellationToken)
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
            runtime.AddServerLogLine(ErrorHandling.FormatLogMessage("Minigame subtitle failed", ex));
        }
    }
}
