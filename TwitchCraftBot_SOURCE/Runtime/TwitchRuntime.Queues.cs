using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task NotifyIRCCommandQueueOverloadAsync(CancellationToken cancellationToken)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long previousTicks = Volatile.Read(ref _lastIRCCommandOverflowNoticeTicks);
        if (previousTicks != 0 && nowTicks - previousTicks < IRCCommandOverflowNoticeIntervalTicks)
            return;

        if (Interlocked.CompareExchange(ref _lastIRCCommandOverflowNoticeTicks, nowTicks, previousTicks) != previousTicks)
            return;

        _shellWindow?.AddChatLogLine("[IRC] Command queue overloaded; skipped commands temporarily.");
        await SendToChannelAsync("The bot is backed up, so commands are being skipped for a moment. Try again in a few seconds.", cancellationToken).ConfigureAwait(false);
    }

    private bool QueueIRCWorkCore(
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
            if (depth > state.MaxDepth)
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
            TrackSessionBackgroundTask(Task.Run(() => ProcessIRCWorkQueueAsync(state, queueToRun, quick), CancellationToken.None));

        return true;
    }

    private async Task ProcessIRCWorkQueueAsync(IRCWorkQueueState state, Queue<IRCQueuedWork> queue, bool quick)
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

                await ExecuteQueuedIRCWorkAsync(state, item, quick).ConfigureAwait(false);
            }
        }
        finally
        {
            bool restart = false;
            lock (state.Gate)
            {
                if (ReferenceEquals(queue, state.Queue))
                {
                    state.Active = 0;
                    if (queue.Count > 0)
                    {
                        state.Active = 1;
                        restart = true;
                    }
                }
            }

            if (restart)
                TrackSessionBackgroundTask(Task.Run(() => ProcessIRCWorkQueueAsync(state, queue, quick), CancellationToken.None));
        }
    }

    private async Task ExecuteQueuedIRCWorkAsync(IRCWorkQueueState state, IRCQueuedWork item, bool quick)
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
            string context = quick ? item.Context : BuildCommandQueueContext(item.Context);
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage(prefix + context + " failed", ex));
        }
        finally
        {
            if (item.Generation == Volatile.Read(ref _IRCQueueGeneration))
                Interlocked.Decrement(ref state.Depth);
        }
    }

    private void ResetIRCQueues()
    {
        lock (_IRCCommandQueue.Gate)
            lock (_IRCQuickQueue.Gate)
            {
                Interlocked.Increment(ref _IRCQueueGeneration);
                ResetIRCQueueStateNoLock(_IRCCommandQueue);
                ResetIRCQueueStateNoLock(_IRCQuickQueue);
            }
    }

    private static void ResetIRCQueueStateNoLock(IRCWorkQueueState state)
    {
        state.Queue = new Queue<IRCQueuedWork>();
        Volatile.Write(ref state.Depth, 0);
        state.Active = 0;
    }

    private static bool IsIgnoredIRCUser(string sender, string botName, bool separateBotAccount)
    {
        if (separateBotAccount &&
            string.Equals(sender, botName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int i = 0; i < IgnoredIRCUsers.Length; i++)
        {
            if (string.Equals(sender, IgnoredIRCUsers[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string StripIRCTagsForLog(string line)
    {
        if (line.Length == 0 || line[0] != '@')
            return line;

        int firstSpace = line.IndexOf(' ');
        return firstSpace > 0 && firstSpace + 1 < line.Length ? line[(firstSpace + 1)..] : line;
    }

    private static string BuildCommandQueueContext(string payload)
    {
        const string Prefix = "command ";
        int commandEnd = payload.IndexOf(' ');
        return commandEnd > 0
            ? string.Concat(Prefix.AsSpan(), payload.AsSpan(0, commandEnd))
            : Prefix + payload;
    }

    private async Task DispatchCommandAsync(string payload, string sender, bool isModerator, CancellationToken cancellationToken)
    {
        ParsedCommand parsed = ParsedCommand.Parse(payload);
        if (parsed.Name.Length == 0)
            return;

        if (!_commandRegistry.TryResolve(parsed.Name, out ChatCommandHandler handler))
            return;

        SetCurrentCommandSenderModeratorState(isModerator);
        SetCurrentStatisticCommandName(parsed.Name);
        try
        {
            await handler(parsed.ArgumentArray, sender, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Command error in !" + parsed.Name, ex));
        }
        finally
        {
            SetCurrentStatisticCommandName(null);
            SetCurrentCommandSenderModeratorState(false);
        }
    }

}
