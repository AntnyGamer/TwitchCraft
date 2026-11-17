using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Controls;

namespace TwitchCraft_V1.Frames;

public partial class Main
{
    private const int DefaultMaxLogLines = 250;

    private readonly Queue<string> _minecraftLogLines = [];
    private readonly Queue<string> _twitchLogLines = [];
    private readonly Queue<string> _pendingMinecraftLogLines = [];
    private readonly Queue<string> _pendingTwitchLogLines = [];
    private readonly Lock _logGate = new();

    private readonly Action _flushMinecraftLogs;
    private readonly Action _flushTwitchLogs;
    private bool _minecraftFlushQueued;
    private bool _twitchFlushQueued;

    public void AddServerLogLine(string line)
    {
        QueueLog(_pendingMinecraftLogLines, isMinecraftLog: true, line);
    }

    public void ClearServerLog()
    {
        ClearLog(MinecraftLogs, _minecraftLogLines, _pendingMinecraftLogLines, isMinecraftLog: true);
    }

    public void AddChatLogLine(string line)
    {
        QueueLog(_pendingTwitchLogLines, isMinecraftLog: false, line);
    }

    public void ClearChatLog()
    {
        ClearLog(TwitchLogs, _twitchLogLines, _pendingTwitchLogLines, isMinecraftLog: false);
    }

    private void QueueLog(Queue<string> pendingLines, bool isMinecraftLog, string? line)
    {
        bool shouldSchedule;

        lock (_logGate)
        {
            pendingLines.Enqueue(line ?? string.Empty);
            int maxLines = GetMaxLogLines(isMinecraftLog);
            while (pendingLines.Count > maxLines)
            {
                pendingLines.Dequeue();
            }

            shouldSchedule = !ShouldPauseUIUpdates() && TryQueueFlush(isMinecraftLog);
        }

        if (shouldSchedule)
        {
            SafeInvoke(isMinecraftLog ? _flushMinecraftLogs : _flushTwitchLogs);
        }
    }

    private void FlushLogs(TextBox box, Queue<string> lines, Queue<string> pendingLines, bool isMinecraftLog)
    {
        if (ShouldPauseUIUpdates())
        {
            lock (_logGate)
            {
                CancelQueuedFlush(isMinecraftLog);
            }

            return;
        }

        int batchCount;
        string? singleLine = null;
        string[]? batch = null;

        lock (_logGate)
        {
            batchCount = pendingLines.Count;
            if (batchCount == 1)
            {
                singleLine = pendingLines.Dequeue();
            }
            else if (batchCount > 1)
            {
                batch = [.. pendingLines];
                pendingLines.Clear();
            }

            CancelQueuedFlush(isMinecraftLog);
        }

        if (batchCount == 0)
        {
            return;
        }

        int maxLines = GetMaxLogLines(isMinecraftLog);
        double scrollOffset = box.VerticalOffset;
        bool followTail = scrollOffset >= Math.Max(0, box.ExtentHeight - box.ViewportHeight) - 1;
        bool rebuild = lines.Count + batchCount > maxLines;
        string newLine = Environment.NewLine;

        if (batchCount == 1)
        {
            while (lines.Count >= maxLines)
            {
                lines.Dequeue();
                rebuild = true;
            }

            lines.Enqueue(singleLine!);
            if (rebuild)
            {
                box.Text = string.Join(newLine, lines) + newLine;
            }
            else
            {
                box.AppendText(singleLine + newLine);
            }
        }
        else
        {
            StringBuilder? appended = rebuild ? null : new StringBuilder(Math.Min(batchCount * 64, 8192));
            foreach (string entry in batch!)
            {
                while (lines.Count >= maxLines)
                {
                    lines.Dequeue();
                    rebuild = true;
                }

                lines.Enqueue(entry);
                appended?.Append(entry).Append(newLine);
            }

            if (rebuild)
            {
                box.Text = string.Join(newLine, lines) + newLine;
            }
            else if (appended is { Length: > 0 })
            {
                box.AppendText(appended.ToString());
            }
        }

        if (followTail)
        {
            box.ScrollToEnd();
        }
        else
        {
            box.ScrollToVerticalOffset(scrollOffset);
        }

        bool shouldSchedule = false;
        lock (_logGate)
        {
            if (pendingLines.Count > 0)
            {
                shouldSchedule = TryQueueFlush(isMinecraftLog);
            }
        }

        if (shouldSchedule)
        {
            SafeInvoke(isMinecraftLog ? _flushMinecraftLogs : _flushTwitchLogs);
        }
    }

    private void ClearLog(TextBox box, Queue<string> lines, Queue<string> pendingLines, bool isMinecraftLog)
    {
        SafeInvoke(() =>
        {
            lock (_logGate)
            {
                lines.Clear();
                pendingLines.Clear();

                CancelQueuedFlush(isMinecraftLog);
            }

            box.Clear();
        });
    }

    private bool TryQueueFlush(bool isMinecraftLog)
    {
        ref bool flushQueued = ref (isMinecraftLog ? ref _minecraftFlushQueued : ref _twitchFlushQueued);
        if (flushQueued)
        {
            return false;
        }

        flushQueued = true;
        return true;
    }

    private void CancelQueuedFlush(bool isMinecraftLog)
    {
        if (isMinecraftLog)
        {
            _minecraftFlushQueued = false;
        }
        else
        {
            _twitchFlushQueued = false;
        }
    }

    private int GetMaxLogLines(bool isMinecraftLog)
    {
        int configured = isMinecraftLog
            ? _runtime?.MaxVisibleMinecraftLogLines ?? DefaultMaxLogLines
            : _runtime?.MaxVisibleTwitchLogLines ?? DefaultMaxLogLines;
        return Math.Clamp(configured, 50, 5000);
    }
}
