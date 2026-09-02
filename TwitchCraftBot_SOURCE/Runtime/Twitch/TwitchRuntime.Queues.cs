using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task WarnQueueOverloadAsync(CancellationToken cancellationToken)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long previousTicks = Volatile.Read(ref _lastIRCCommandOverflowNoticeTicks);
        if (previousTicks != 0 && nowTicks - previousTicks < IRCCommandOverflowNoticeIntervalTicks)
            return;

        if (Interlocked.CompareExchange(ref _lastIRCCommandOverflowNoticeTicks, nowTicks, previousTicks) != previousTicks)
            return;

        _shellWindow?.AddChatLogLine("[IRC] Command queue overloaded; skipped commands temporarily.");
        await SendChatAsync("The bot is backed up, so commands are being skipped for a moment. Try again in a few seconds.", cancellationToken).ConfigureAwait(false);
    }

    private bool QueueIrcWork(
        IRCWorkQueueState state,
        Func<CancellationToken, Task> work,
        string context,
        bool quick,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        Queue<IRCQueuedWork> queueToRun;
        bool startProcessor;
        int generation;

        lock (state.Gate)
        {
            generation = Volatile.Read(ref _IRCQueueGeneration);
            if (cancellationToken.IsCancellationRequested)
                return false;

            int depth = Interlocked.Increment(ref state.Depth);
            int maxDepth = ReferenceEquals(state, _IRCCommandQueue) ? MaxGameplayCommandQueue : state.MaxDepth;
            if (depth > maxDepth)
            {
                Interlocked.Decrement(ref state.Depth);
                return false;
            }

            state.Queue.Enqueue(new IRCQueuedWork(work, context, generation, cancellationToken));
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
        => QueueIrcWork(
            _IRCCommandQueue,
            work,
            context,
            quick: false,
            cancellationToken);

    private async Task RunQueueAsync(IRCWorkQueueState state, Queue<IRCQueuedWork> queue, bool quick)
    {
        try
        {
            while (true)
            {
                IRCQueuedWork item;
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
            Queue<IRCQueuedWork>? queueToRestart = null;
            lock (state.Gate)
            {
                state.Active = 0;
                Queue<IRCQueuedWork> currentQueue = state.Queue;
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

    private async Task RunQueuedWorkAsync(IRCWorkQueueState state, IRCQueuedWork item, bool quick)
    {
        CancellationToken cancellationToken = item.CancellationToken;
        try
        {
            if (item.Generation == Volatile.Read(ref _IRCQueueGeneration) && !cancellationToken.IsCancellationRequested)
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
            if (item.Generation == Volatile.Read(ref _IRCQueueGeneration))
                Interlocked.Decrement(ref state.Depth);
        }
    }

    internal void ResetQueues()
    {
        lock (_IRCCommandQueue.Gate)
            lock (_IRCQuickQueue.Gate)
            {
                Interlocked.Increment(ref _IRCQueueGeneration);
                ResetQueueNoLock(_IRCCommandQueue);
                ResetQueueNoLock(_IRCQuickQueue);
            }
    }

    private static void ResetQueueNoLock(IRCWorkQueueState state)
    {
        state.Queue = new Queue<IRCQueuedWork>();
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

    internal static string StripIrcTags(string line)
    {
        if (line.Length == 0 || line[0] != '@')
            return line;

        int firstSpace = line.IndexOf(' ');
        return firstSpace > 0 && firstSpace + 1 < line.Length ? line[(firstSpace + 1)..] : line;
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

        CustomCommandCooldownReservation customCooldownReservation = default;
        CustomCommandCooldownReservation globalCooldownReservation = default;
        _currentCommandSender.Value = sender;
        try
        {
            if (!_commandRegistry.TryResolve(parsed.Name, out ChatCommandHandler handler))
            {
                if (_activeConfig?.Settings.RespondToUnknownCommands == true)
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

            CommandCustomization? customization = TryGetCommandSettings(parsed.Name, out CommandCustomization resolvedCustomization)
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
            if (!TryReserveCustomCooldown(
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

            if (!TryReserveCustomCooldown(
                    parsed.Name,
                    GlobalCooldownKey,
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

            if (!TryUseCommandSlots(sender, out bool viewerLimited))
            {
                if (viewerLimited && ShouldWarnViewerLimit(sender))
                    await SendReplyAsync(sender + ", you have reached your command limit. Try again shortly.", BotResponseKind.Essential, cancellationToken).ConfigureAwait(false);
                else if (!viewerLimited && ShouldWarnChannelLimit())
                    await SendReplyAsync(
                        sender + ", the channel command limit has been reached. Try again shortly.",
                        BotResponseKind.Essential,
                        cancellationToken).ConfigureAwait(false);
                return;
            }

            BeginCommand();
            Commands.SetModerator(isModerator);
            Statistics.SetStatsCommand(parsed.Name);
            await handler(parsed.ArgumentArray, sender, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Command error in " + prefix + parsed.Name, ex));
        }
        finally
        {
            FinishCustomCooldown(customCooldownReservation, CommandSucceeded);
            FinishCustomCooldown(globalCooldownReservation, CommandSucceeded);
            EndCommand();
            Statistics.SetStatsCommand(null);
            Commands.SetModerator(false);
            _currentCommandSender.Value = null;
        }
    }

}
