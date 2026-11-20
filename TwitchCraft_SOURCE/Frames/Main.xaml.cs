using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TwitchCraft_V1.Frames;

public partial class Main : UserControl
{
    private const int DefaultMaxLogLines = 250;
    private const string TwitchConnectedText = "Twitch: Connected";
    private const string TwitchDisconnectedText = "Twitch: Disconnected";
    private const string MinecraftConnectedText = "Minecraft: Connected";
    private const string MinecraftDisconnectedText = "Minecraft: Disconnected";
    private static readonly StringComparer ViewerNameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Queue<int> _minecraftLogLineLengths = [];
    private readonly Queue<int> _twitchLogLineLengths = [];
    private readonly Queue<string> _pendingMinecraftLogLines = [];
    private readonly Queue<string> _pendingTwitchLogLines = [];
    private readonly Lock _logGate = new();
    private readonly DispatcherTimer _connectionHealthTimer;
    private readonly DispatcherTimer _logFlushTimer;
    private bool _logFlushScheduled;
    private int _serverActionRunning;
    private Window? _parentWindow;
    private MainHandler? _runtime;
    private int _windowMinimized;
    private int _mainVisible;
    private List<string>? _deferredViewerList;

    public Main()
    {
        InitializeComponent();
        _logFlushTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _logFlushTimer.Tick += LogFlushTimer_Tick;
        _connectionHealthTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        _connectionHealthTimer.Tick += HealthTimer_Tick;
        IsVisibleChanged += Visibility_Changed;
        Loaded += (_, _) =>
        {
            _parentWindow = Window.GetWindow(this);
            _runtime = AppHelpers.GetTwitchCraftWindow(this)?.Runtime;
            Volatile.Write(ref _mainVisible, IsVisible ? 1 : 0);
            if (_parentWindow != null)
            {
                _parentWindow.StateChanged += Parent_StateChanged;
                Volatile.Write(ref _windowMinimized, _parentWindow.WindowState == WindowState.Minimized ? 1 : 0);
            }
            if (IsVisible)
            {
                FlushDeferredUpdates();
            }

            RefreshHealth();
        };
        Unloaded += (_, _) =>
        {
            _connectionHealthTimer.Stop();
            _logFlushTimer.Stop();
            lock (_logGate)
            {
                _logFlushScheduled = false;
            }
            _parentWindow?.StateChanged -= Parent_StateChanged;
            _parentWindow = null;
            Volatile.Write(ref _windowMinimized, 0);
            Volatile.Write(ref _mainVisible, 0);
        };
    }

    private void Parent_StateChanged(object? sender, EventArgs e)
    {
        Volatile.Write(ref _windowMinimized, _parentWindow?.WindowState == WindowState.Minimized ? 1 : 0);
        if (ShouldPauseUIUpdates())
        {
            return;
        }

        FlushDeferredUpdates();
    }

    private void Visibility_Changed(object sender, DependencyPropertyChangedEventArgs e)
    {
        Volatile.Write(ref _mainVisible, IsVisible ? 1 : 0);
        if (IsVisible)
        {
            FlushDeferredUpdates();
        }

        RefreshHealth();
    }

    private void HealthTimer_Tick(object? sender, EventArgs e) => UpdateHealth();

    private void RefreshHealth()
    {
        UpdateHealth();
        if (IsVisible && _runtime?.ShowConnectionHealth == true)
        {
            _connectionHealthTimer.Start();
        }
        else
        {
            _connectionHealthTimer.Stop();
        }
    }

    private void UpdateHealth()
    {
        MainHandler? runtime = _runtime;
        TimeSpan desiredInterval = runtime?.LowResourceModeEnabled == true ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1);
        if (_connectionHealthTimer.Interval != desiredInterval)
        {
            _connectionHealthTimer.Interval = desiredInterval;
        }

        bool visible = runtime?.ShowConnectionHealth ?? false;
        Visibility desiredVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (ConnectionHealthPanel.Visibility != desiredVisibility)
        {
            ConnectionHealthPanel.Visibility = desiredVisibility;
        }

        if (!visible || runtime == null)
        {
            return;
        }

        SetHealth(TwitchConnectionHealth, runtime.TwitchChatConnected, TwitchConnectedText, TwitchDisconnectedText);
        SetHealth(MinecraftConnectionHealth, runtime.MinecraftServerReady, MinecraftConnectedText, MinecraftDisconnectedText);
    }

    private static void SetHealth(TextBlock label, bool connected, string connectedText, string disconnectedText)
    {
        string text = connected ? connectedText : disconnectedText;
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.Text = text;
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                UIElementAutomationPeer.CreatePeerForElement(label)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        Brush foreground = connected ? Brushes.LightGreen : Brushes.IndianRed;
        if (!ReferenceEquals(label.Foreground, foreground))
        {
            label.Foreground = foreground;
        }
    }

    public void AddServerLogLine(string line)
    {
        QueueLog(_pendingMinecraftLogLines, isMinecraftLog: true, line);
    }

    public void ClearServerLog()
    {
        ClearLog(MinecraftLogs, _minecraftLogLineLengths, _pendingMinecraftLogLines);
    }

    public void AddChatLogLine(string line)
    {
        QueueLog(_pendingTwitchLogLines, isMinecraftLog: false, line);
    }

    public void ClearChatLog()
    {
        ClearLog(TwitchLogs, _twitchLogLineLengths, _pendingTwitchLogLines);
    }

    public void UpdateViewers(List<string> viewers)
    {
        viewers ??= [];

        if (ShouldPauseUIUpdates())
        {
            lock (_logGate)
            {
                _deferredViewerList = [.. viewers];
            }

            return;
        }

        SafeInvoke(() =>
        {
            string countText = $"You have {viewers.Count} viewer{(viewers.Count == 1 ? string.Empty : "s")}.";

            if (BotViewerList.ItemsSource is not List<string> currentViewers || !SortedListHelper.EqualInOrder(currentViewers, viewers, ViewerNameComparer))
            {
                BotViewerList.ItemsSource = viewers;
            }

            if (!string.Equals(BotViewerCount.Text, countText, StringComparison.Ordinal))
            {
                BotViewerCount.Text = countText;
            }
        });
    }

    private void QueueLog(Queue<string> pendingLines, bool isMinecraftLog, string? line)
    {
        bool startTimer;
        lock (_logGate)
        {
            pendingLines.Enqueue(line ?? string.Empty);
            int maxLines = GetMaxLogLines(isMinecraftLog);
            while (pendingLines.Count > maxLines)
            {
                pendingLines.Dequeue();
            }

            startTimer = !ShouldPauseUIUpdates() && !_logFlushScheduled;
            if (startTimer)
            {
                _logFlushScheduled = true;
            }
        }

        if (startTimer)
        {
            SafeInvoke(StartLogFlushTimer);
        }
    }

    private void StartLogFlushTimer()
    {
        if (ShouldPauseUIUpdates())
        {
            lock (_logGate)
            {
                _logFlushScheduled = false;
            }

            return;
        }

        _logFlushTimer.Start();
    }

    private void LogFlushTimer_Tick(object? sender, EventArgs e)
    {
        if (ShouldPauseUIUpdates())
        {
            _logFlushTimer.Stop();
            lock (_logGate)
            {
                _logFlushScheduled = false;
            }

            return;
        }

        FlushLogs(MinecraftLogs, _minecraftLogLineLengths, _pendingMinecraftLogLines, isMinecraftLog: true);
        FlushLogs(TwitchLogs, _twitchLogLineLengths, _pendingTwitchLogLines, isMinecraftLog: false);

        lock (_logGate)
        {
            if (_pendingMinecraftLogLines.Count > 0 || _pendingTwitchLogLines.Count > 0)
            {
                return;
            }

            _logFlushTimer.Stop();
            _logFlushScheduled = false;
        }
    }

    private void FlushLogs(TextBox box, Queue<int> lineLengths, Queue<string> pendingLines, bool isMinecraftLog)
    {
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
        }

        if (batchCount == 0)
        {
            return;
        }

        int maxLines = GetMaxLogLines(isMinecraftLog);
        double scrollOffset = box.VerticalOffset;
        bool followTail = scrollOffset >= Math.Max(0, box.ExtentHeight - box.ViewportHeight) - 1;
        double horizontalOffset = followTail ? 0 : box.HorizontalOffset;
        bool preserveScroll = !followTail && lineLengths.Count + batchCount > maxLines && box.LineCount > 0;
        double lineHeight = preserveScroll ? box.ExtentHeight / box.LineCount : 0;
        int removedVisualLines = AppendLogBatch(box, lineLengths, maxLines, singleLine, batch, preserveScroll);

        if (followTail)
        {
            box.ScrollToEnd();
        }
        else
        {
            box.ScrollToVerticalOffset(Math.Max(0, scrollOffset - removedVisualLines * lineHeight));
            box.ScrollToHorizontalOffset(horizontalOffset);
        }
    }

    private static int AppendLogBatch(TextBox box, Queue<int> lineLengths, int maxLines, string? singleLine, string[]? batch, bool preserveScroll)
    {
        int batchCount = batch?.Length ?? 1;
        int retainedBatchCount = Math.Min(batchCount, maxLines);
        int trimLength = 0;
        while (lineLengths.Count > maxLines - retainedBatchCount)
        {
            trimLength += lineLengths.Dequeue();
        }

        int removedVisualLines = preserveScroll && trimLength > 0
            ? Math.Max(0, box.GetLineIndexFromCharacterIndex(trimLength))
            : 0;
        string newLine = Environment.NewLine;
        string appended;
        if (batch == null)
        {
            lineLengths.Enqueue(singleLine!.Length + newLine.Length);
            appended = singleLine + newLine;
        }
        else
        {
            StringBuilder builder = new(Math.Min(retainedBatchCount * 64, 8192));
            for (int index = batchCount - retainedBatchCount; index < batchCount; index++)
            {
                string entry = batch[index];
                lineLengths.Enqueue(entry.Length + newLine.Length);
                builder.Append(entry).Append(newLine);
            }

            appended = builder.ToString();
        }

        if (trimLength == 0)
        {
            box.AppendText(appended);
            return 0;
        }

        int selectionStart = box.SelectionStart;
        int selectionEnd = selectionStart + box.SelectionLength;
        using (box.DeclareChangeBlock())
        {
            box.Select(0, trimLength);
            box.SelectedText = string.Empty;
            box.AppendText(appended);
            int start = Math.Max(0, selectionStart - trimLength);
            box.Select(start, Math.Max(0, selectionEnd - trimLength) - start);
        }

        return removedVisualLines;
    }

    private void ClearLog(TextBox box, Queue<int> lineLengths, Queue<string> pendingLines)
    {
        SafeInvoke(() =>
        {
            lock (_logGate)
            {
                lineLengths.Clear();
                pendingLines.Clear();
            }

            box.Clear();
        });
    }

    private int GetMaxLogLines(bool isMinecraftLog)
    {
        int configured = isMinecraftLog
            ? _runtime?.MaxVisibleMinecraftLogLines ?? DefaultMaxLogLines
            : _runtime?.MaxVisibleTwitchLogLines ?? DefaultMaxLogLines;
        return Math.Clamp(configured, 50, 5000);
    }

    private bool ShouldPauseUIUpdates()
        => Volatile.Read(ref _mainVisible) == 0 ||
            (_runtime?.PauseUIUpdatesWhenMinimized == true && Volatile.Read(ref _windowMinimized) != 0);

    private void FlushDeferredUpdates()
    {
        List<string>? viewers;
        bool startTimer;
        lock (_logGate)
        {
            viewers = _deferredViewerList;
            _deferredViewerList = null;
            startTimer = !_logFlushScheduled && (_pendingMinecraftLogLines.Count > 0 || _pendingTwitchLogLines.Count > 0);
            if (startTimer)
            {
                _logFlushScheduled = true;
            }
        }

        if (viewers != null)
        {
            UpdateViewers(viewers);
        }

        if (startTimer)
        {
            StartLogFlushTimer();
        }

        UpdateHealth();
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

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SendCommandAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Manual command send failed", ex);
        }
    }

    private async void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            try
            {
                await SendCommandAsync();
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Manual command send failed", ex);
            }
        }
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        await RunServerActionAsync(parent => parent.PauseAsync(), () => ErrorHandling.ShowPauseWindowError(this), "Pause button failed");
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        await RunRestartResetAsync(parent => parent.ResetAsync(), () => ErrorHandling.ShowResetWindowError(this));
    }

    private void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        AppHelpers.GetTwitchCraftWindow(this)?.Close();
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        await RunRestartResetAsync(parent => parent.RestartAsync(), () => ErrorHandling.ShowRestartWindowError(this));
    }

    private Task RunRestartResetAsync(Func<TwitchCraft, Task> action, Action parentMissingAction)
        => RunServerActionAsync(action, parentMissingAction, "Restart/reset button failed");

    private async Task RunServerActionAsync(Func<TwitchCraft, Task> action, Action parentMissingAction, string errorContext)
    {
        if (Interlocked.Exchange(ref _serverActionRunning, 1) != 0)
        {
            return;
        }

        SetServerControls(false);
        try
        {
            await WithTwitchCraftAsync(action, parentMissingAction);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal(errorContext, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _serverActionRunning, 0);
            SetServerControls(true);
        }
    }

    private void SetServerControls(bool enabled)
    {
        Restart.IsEnabled = enabled;
        Reset.IsEnabled = enabled;
        Pause.IsEnabled = enabled;
        Shutdown.IsEnabled = enabled;
        CommandButton.IsEnabled = enabled;
        CommandTextBox.IsEnabled = enabled;
        object? toolTip = enabled ? null : "Another server action is in progress.";
        Restart.ToolTip = Reset.ToolTip = Pause.ToolTip = Shutdown.ToolTip = CommandButton.ToolTip = CommandTextBox.ToolTip = toolTip;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        WithTwitchCraft(parent =>
        {
            parent.ShowSettings();
            e.Handled = true;
        });
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        WithTwitchCraft(parent =>
        {
            parent.ShowStatistics();
            e.Handled = true;
        });
    }

    private async Task SendCommandAsync()
    {
        string command = CommandTextBox.Text;
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
        if (parent == null)
        {
            ErrorHandling.ShowCommandWindowError(this);
            return;
        }

        CommandTextBox.Clear();
        if (!await parent.RunMinecraftCommandAsync(command) && CommandTextBox.Text.Length == 0)
        {
            CommandTextBox.Text = command;
        }
    }

    private void WithTwitchCraft(Action<TwitchCraft> action)
    {
        TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
        if (parent != null)
        {
            action(parent);
        }
    }

    private async Task WithTwitchCraftAsync(Func<TwitchCraft, Task> action, Action? onFailure)
    {
        TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
        if (parent != null)
        {
            await action(parent);
            return;
        }

        onFailure?.Invoke();
    }
}
