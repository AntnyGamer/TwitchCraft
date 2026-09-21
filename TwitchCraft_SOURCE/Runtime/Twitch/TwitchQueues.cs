using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private async Task WarnQueueOverloadAsync(CancellationToken cancellationToken)
    {
        long nowMilliseconds = Environment.TickCount64;
        long previousMilliseconds = Volatile.Read(ref _twitchSession.LastCommandOverflowNoticeMilliseconds);
        if (previousMilliseconds != 0 && nowMilliseconds - previousMilliseconds < TwitchSession.CommandOverflowNoticeIntervalMilliseconds)
            return;

        if (Interlocked.CompareExchange(ref _twitchSession.LastCommandOverflowNoticeMilliseconds, nowMilliseconds, previousMilliseconds) != previousMilliseconds)
            return;

        _shellWindow?.AddChatLogLine("[IRC] Command queue overloaded; skipped commands temporarily.");
        await SendChatAsync("The bot is backed up, so commands are being skipped for a moment. Try again in a few seconds.", cancellationToken).ConfigureAwait(false);
    }

    private bool QueueIRCWork(
        TwitchSession.WorkQueueState state,
        Func<CancellationToken, Task> work,
        string context,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        Queue<TwitchSession.QueuedWork> queueToRun;
        bool startProcessor;
        bool quick = ReferenceEquals(state, _twitchSession.QuickQueue);
        int generation;

        lock (state.Gate)
        {
            generation = Volatile.Read(ref _twitchSession.QueueGeneration);
            if (cancellationToken.IsCancellationRequested)
                return false;

            int depth = Interlocked.Increment(ref state.Depth);
            int maxDepth = quick ? state.MaxDepth : MaxGameplayCommandQueue;
            if (depth > maxDepth)
            {
                Interlocked.Decrement(ref state.Depth);
                return false;
            }

            state.Queue.Enqueue(new TwitchSession.QueuedWork(work, context, generation, cancellationToken));
            queueToRun = state.Queue;
            startProcessor = state.Active == 0;
            if (startProcessor)
                state.Active = 1;
        }

        if (startProcessor)
            TrackTask(Task.Run(() => RunQueueAsync(state, queueToRun, quick), CancellationToken.None));

        return true;
    }

    internal bool QueueCommand(
        Func<CancellationToken, Task> work,
        string context,
        CancellationToken cancellationToken)
        => QueueIRCWork(
            _twitchSession.CommandQueue,
            work,
            context,
            cancellationToken);

    private async Task RunQueueAsync(TwitchSession.WorkQueueState state, Queue<TwitchSession.QueuedWork> queue, bool quick)
    {
        try
        {
            while (true)
            {
                TwitchSession.QueuedWork item;
                lock (state.Gate)
                {
                    if (!ReferenceEquals(queue, state.Queue) || queue.Count == 0)
                        return;

                    item = queue.Dequeue();
                }

                await RunQueuedWorkAsync(state, item, quick).ConfigureAwait(false);
            }
        }
        finally
        {
            Queue<TwitchSession.QueuedWork>? queueToRestart = null;
            lock (state.Gate)
            {
                state.Active = 0;
                Queue<TwitchSession.QueuedWork> currentQueue = state.Queue;
                if (currentQueue.Count > 0)
                {
                    state.Active = 1;
                    queueToRestart = currentQueue;
                }
            }

            if (queueToRestart != null)
            {
                TrackTask(Task.Run(
                    () => RunQueueAsync(state, queueToRestart, quick),
                    CancellationToken.None));
            }
        }
    }

    private async Task RunQueuedWorkAsync(TwitchSession.WorkQueueState state, TwitchSession.QueuedWork item, bool quick)
    {
        CancellationToken cancellationToken = item.CancellationToken;
        try
        {
            if (item.Generation == Volatile.Read(ref _twitchSession.QueueGeneration) && !cancellationToken.IsCancellationRequested)
                await item.Work(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            string prefix = quick ? "Quick IRC " : "Queued IRC ";
            string context = quick ? item.Context : BuildQueueContext(item.Context);
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog(prefix + context + " failed", ex));
        }
        finally
        {
            if (item.Generation == Volatile.Read(ref _twitchSession.QueueGeneration))
                Interlocked.Decrement(ref state.Depth);
        }
    }

    internal void ResetQueues()
    {
        lock (_twitchSession.CommandQueue.Gate)
            lock (_twitchSession.QuickQueue.Gate)
            {
                Interlocked.Increment(ref _twitchSession.QueueGeneration);
                ResetQueueNoLock(_twitchSession.CommandQueue);
                ResetQueueNoLock(_twitchSession.QuickQueue);
            }
    }

    private static void ResetQueueNoLock(TwitchSession.WorkQueueState state)
    {
        state.Queue = new Queue<TwitchSession.QueuedWork>();
        Volatile.Write(ref state.Depth, 0);
    }

    internal static bool IsIgnoredUser(string sender, string botName, bool separateBotAccount)
    {
        if (separateBotAccount &&
            string.Equals(sender, botName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(sender, "nightbot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sender, "streamlabs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sender, "streamelements", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildQueueContext(string payload)
    {
        const string Prefix = "command ";
        int commandEnd = payload.IndexOf(' ');
        return commandEnd > 0
            ? string.Concat(Prefix.AsSpan(), payload.AsSpan(0, commandEnd))
            : Prefix + payload;
    }

    internal async Task DispatchAsync(string payload, string prefix, string sender, bool isModerator, CancellationToken cancellationToken)
    {
        ParsedCommand parsed = ParsedCommand.Parse(payload, prefix);
        if (parsed.Name.Length == 0)
            return;

        CommandService.CooldownReservation customCooldownReservation = default;
        CommandService.CooldownReservation globalCooldownReservation = default;
        Commands.SetCurrentSender(sender);
        try
        {
            if (!_commandRegistry.TryResolve(parsed.Name, out ChatCommandHandler handler))
            {
                if (CurrentSettings.RespondToUnknownCommands)
                {
                    await SendReplyAsync(
                        sender + ", unknown command " + prefix + parsed.Name + ".",
                        BotResponseKind.Essential,
                        cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            if (AreViewerCommandsPaused(sender))
            {
                await SendReplyAsync(
                    sender + ", viewer commands are currently paused.",
                    BotResponseKind.Essential,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            CommandCustomization? customization = Commands.TryGetCommandSettings(parsed.Name, out CommandCustomization resolvedCustomization)
                ? resolvedCustomization
                : null;
            if (customization?.Enabled == false)
            {
                await SendReplyAsync(
                    sender + ", " + prefix + parsed.Name + " is disabled.",
                    BotResponseKind.Essential,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!Commands.TryReserveCustomCooldown(
                    parsed.Name,
                    sender,
                    customization?.CooldownSeconds,
                    out TimeSpan customCooldownRemaining,
                    out customCooldownReservation))
            {
                await SendReplyAsync(
                    sender + ", you are on cooldown for " + prefix + parsed.Name + ". Try again in " + FormatCooldown(customCooldownRemaining) + ".",
                    BotResponseKind.Essential,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!Commands.TryReserveCustomCooldown(
                    parsed.Name,
                    CommandService.GlobalCooldownKey,
                    customization?.GlobalCooldownSeconds,
                    out TimeSpan customGlobalCooldownRemaining,
                    out globalCooldownReservation))
            {
                await SendReplyAsync(
                    sender + ", " + prefix + parsed.Name + " is on global cooldown. Try again in " + FormatCooldown(customGlobalCooldownRemaining) + ".",
                    BotResponseKind.Essential,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!Commands.TryUseCommandSlots(sender, out bool viewerLimited))
            {
                if (viewerLimited && Commands.ShouldWarnViewerLimit(sender))
                    await SendReplyAsync(sender + ", you have reached your command limit. Try again shortly.", BotResponseKind.Essential, cancellationToken).ConfigureAwait(false);
                else if (!viewerLimited && Commands.ShouldWarnChannelLimit())
                    await SendReplyAsync(
                        sender + ", the channel command limit has been reached. Try again shortly.",
                        BotResponseKind.Essential,
                        cancellationToken).ConfigureAwait(false);
                return;
            }

            Commands.BeginCommand(parsed.Name, isModerator);
            await handler(parsed.ArgumentArray, sender, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Command error in " + prefix + parsed.Name, ex));
        }
        finally
        {
            Commands.FinishCustomCooldown(customCooldownReservation, Commands.CommandSucceeded);
            Commands.FinishCustomCooldown(globalCooldownReservation, Commands.CommandSucceeded);
            Commands.EndCommand();
            Commands.SetCurrentSender(null);
        }
    }
}
