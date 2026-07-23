using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TwitchCraftBot_V1.Frames;

public partial class Main : UserControl
{
    private const int MaxLogLines = 250;
    private static readonly StringComparer ViewerNameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Queue<string> _minecraftLogLines = [];
    private readonly Queue<string> _twitchLogLines = [];
    private readonly Queue<string> _pendingMinecraftLogLines = [];
    private readonly Queue<string> _pendingTwitchLogLines = [];
    private readonly Lock _logGate = new();
    private bool _minecraftFlushQueued;
    private bool _twitchFlushQueued;
    private int _serverActionRunning;

    public Main()
    {
        InitializeComponent();
    }

    public void AddServerLogLine(string line)
    {
        QueueLogLine(MinecraftLogs, _minecraftLogLines, _pendingMinecraftLogLines, isMinecraftLog: true, line);
    }

    public void ClearServerLogView()
    {
        ClearLog(MinecraftLogs, _minecraftLogLines, _pendingMinecraftLogLines, isMinecraftLog: true);
    }

    public void AddChatLogLine(string line)
    {
        QueueLogLine(TwitchLogs, _twitchLogLines, _pendingTwitchLogLines, isMinecraftLog: false, line);
    }

    public void ClearChatLogView()
    {
        ClearLog(TwitchLogs, _twitchLogLines, _pendingTwitchLogLines, isMinecraftLog: false);
    }

    public void DisplayNormalizedViewerList(List<string> viewers)
    {
        viewers ??= [];

        SafeInvoke(() =>
        {
            string countText = $"You have {viewers.Count} viewer{(viewers.Count == 1 ? string.Empty : "s")}.";

            if (BotViewerList.ItemsSource is not List<string> currentViewers || !SortedListHelper.EqualInOrder(currentViewers, viewers, ViewerNameComparer))
                BotViewerList.ItemsSource = viewers;

            if (!string.Equals(BotViewerCount.Text, countText, StringComparison.Ordinal))
                BotViewerCount.Text = countText;
        });
    }

    private void QueueLogLine(TextBox box, Queue<string> lines, Queue<string> pendingLines, bool isMinecraftLog, string? line)
    {
        bool shouldSchedule = false;

        lock (_logGate)
        {
            pendingLines.Enqueue(line ?? string.Empty);
            while (pendingLines.Count > MaxLogLines)
            {
                pendingLines.Dequeue();
            }

            shouldSchedule = TryQueueFlush(isMinecraftLog);
        }

        if (shouldSchedule)
        {
            SafeInvoke(() => FlushLogQueue(box, lines, pendingLines, isMinecraftLog));
        }
    }

    private void FlushLogQueue(TextBox box, Queue<string> lines, Queue<string> pendingLines, bool isMinecraftLog)
    {
        List<string> batch;

        lock (_logGate)
        {
            batch = new(pendingLines.Count);
            while (pendingLines.Count > 0)
            {
                batch.Add(pendingLines.Dequeue());
            }

            ClearQueuedFlush(isMinecraftLog);
        }

        if (batch.Count == 0)
        {
            return;
        }

        bool rebuild = lines.Count + batch.Count > MaxLogLines;
        string newLine = Environment.NewLine;
        StringBuilder? appended = rebuild || batch.Count == 1 ? null : new StringBuilder(Math.Min(batch.Count * 64, 8192));

        foreach (string entry in batch)
        {
            if (lines.Count >= MaxLogLines)
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
        else if (batch.Count == 1)
        {
            box.AppendText(batch[0] + newLine);
        }
        else if (appended is { Length: > 0 })
        {
            box.AppendText(appended.ToString());
        }

        box.ScrollToEnd();

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
            SafeInvoke(() => FlushLogQueue(box, lines, pendingLines, isMinecraftLog));
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

                ClearQueuedFlush(isMinecraftLog);
            }

            box.Clear();
        });
    }

    private bool TryQueueFlush(bool isMinecraftLog)
    {
        ref bool flushQueued = ref (isMinecraftLog ? ref _minecraftFlushQueued : ref _twitchFlushQueued);
        if (flushQueued)
            return false;

        flushQueued = true;
        return true;
    }

    private void ClearQueuedFlush(bool isMinecraftLog)
    {
        if (isMinecraftLog)
            _minecraftFlushQueued = false;
        else
            _twitchFlushQueued = false;
    }

    private void SafeInvoke(Action action)
    {
        if (action == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void CommandButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendManualCommandAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Manual command send failed", ex);
        }
    }

    private async void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            try
            {
                await SendManualCommandAsync();
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Manual command send failed", ex);
            }
        }
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        await RunServerActionAsync(parent => parent.PauseAsync(), () => ErrorHandling.ShowPauseParentNotFound(this), "Pause button failed");
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        await RunRestartOrResetAsync(parent => parent.Reset(), () => ErrorHandling.ShowResetParentNotFound(this));
    }

    private void ShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        AppHelpers.GetParentBot(this)?.Close();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        await RunRestartOrResetAsync(parent => parent.Restart(), () => ErrorHandling.ShowRestartParentNotFound(this));
    }

    private Task RunRestartOrResetAsync(Func<TwitchCraftBot, Task> action, Action parentMissingAction)
        => RunServerActionAsync(action, parentMissingAction, "Restart/reset button failed");

    private async Task RunServerActionAsync(Func<TwitchCraftBot, Task> action, Action parentMissingAction, string errorContext)
    {
        if (Interlocked.Exchange(ref _serverActionRunning, 1) != 0)
            return;

        SetServerActionControlsEnabled(false);
        try
        {
            await ExecuteWithParentAsync(action, parentMissingAction);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal(errorContext, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _serverActionRunning, 0);
            SetServerActionControlsEnabled(true);
        }
    }

    private void SetServerActionControlsEnabled(bool enabled)
    {
        Restart.IsEnabled = enabled;
        Reset.IsEnabled = enabled;
        Pause.IsEnabled = enabled;
        Shutdown.IsEnabled = enabled;
        CommandButton.IsEnabled = enabled;
        CommandTextBox.IsEnabled = enabled;
    }

    private void Help_Click(object sender, MouseButtonEventArgs e)
    {
        ExecuteWithParent(parent =>
        {
            parent.NavigateToHelp();
            e.Handled = true;
        });
    }

    private void Stats_Click(object sender, MouseButtonEventArgs e)
    {
        ExecuteWithParent(parent =>
        {
            parent.NavigateToStatistics();
            e.Handled = true;
        });
    }

    private async Task SendManualCommandAsync()
    {
        string command = CommandTextBox.Text;

        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
        if (parent == null)
        {
            ErrorHandling.ShowCommandParentNotFound(this);
            return;
        }

        if (await parent.ExecuteMinecraftCommandAsync(command))
        {
            CommandTextBox.Text = string.Empty;
        }
    }

    private void ExecuteWithParent(Action<TwitchCraftBot> action)
    {
        TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
        if (parent != null)
            action(parent);
    }

    private async Task ExecuteWithParentAsync(Func<TwitchCraftBot, Task> action, Action? onFailure = null)
    {
        TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
        if (parent != null)
        {
            await action(parent);
            return;
        }

        onFailure?.Invoke();
    }
}
