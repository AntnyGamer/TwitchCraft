using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TwitchCraftBot_V1.Frames;

public partial class Main : UserControl
{
    private const int DefaultMaxLogLines = 250;
    private const string TwitchConnectedText = "Twitch: Connected";
    private const string TwitchDisconnectedText = "Twitch: Disconnected";
    private const string MinecraftConnectedText = "Minecraft: Connected";
    private const string MinecraftDisconnectedText = "Minecraft: Disconnected";
    private static readonly StringComparer ViewerNameComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Queue<string> _minecraftLogLines = [];
    private readonly Queue<string> _twitchLogLines = [];
    private readonly Queue<string> _pendingMinecraftLogLines = [];
    private readonly Queue<string> _pendingTwitchLogLines = [];
    private readonly Lock _logGate = new();
    private readonly DispatcherTimer _connectionHealthTimer;
    private bool _minecraftFlushQueued;
    private bool _twitchFlushQueued;
    private int _serverActionRunning;
    private Window? _parentWindow;
    private BotMainHandler? _runtime;
    private int _windowMinimized;
    private int _mainVisible;
    private List<string>? _deferredViewerList;

    public Main()
    {
        InitializeComponent();
        _connectionHealthTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        _connectionHealthTimer.Tick += ConnectionHealthTimer_Tick;
        IsVisibleChanged += Main_IsVisibleChanged;
        Loaded += (_, _) =>
        {
            _parentWindow = Window.GetWindow(this);
            _runtime = AppHelpers.GetParentBot(this)?.Runtime;
            Volatile.Write(ref _mainVisible, IsVisible ? 1 : 0);
            if (_parentWindow != null)
            {
                _parentWindow.StateChanged += ParentWindow_StateChanged;
                Volatile.Write(ref _windowMinimized, _parentWindow.WindowState == WindowState.Minimized ? 1 : 0);
            }
            if (IsVisible)
                FlushDeferredUIUpdates();
            RefreshConnectionHealthTimer();
        };
        Unloaded += (_, _) =>
        {
            _connectionHealthTimer.Stop();
            _parentWindow?.StateChanged -= ParentWindow_StateChanged;
            _parentWindow = null;
            Volatile.Write(ref _windowMinimized, 0);
            Volatile.Write(ref _mainVisible, 0);
        };
    }

    private void ParentWindow_StateChanged(object? sender, EventArgs e)
    {
        Volatile.Write(ref _windowMinimized, _parentWindow?.WindowState == WindowState.Minimized ? 1 : 0);
        if (ShouldPauseUIUpdates())
            return;

        FlushDeferredUIUpdates();
    }

    private void Main_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Volatile.Write(ref _mainVisible, IsVisible ? 1 : 0);
        if (IsVisible)
            FlushDeferredUIUpdates();
        RefreshConnectionHealthTimer();
    }

    private void ConnectionHealthTimer_Tick(object? sender, EventArgs e) => UpdateConnectionHealth();

    private void RefreshConnectionHealthTimer()
    {
        UpdateConnectionHealth();
        if (IsVisible && _runtime?.ShowConnectionHealth == true)
            _connectionHealthTimer.Start();
        else
            _connectionHealthTimer.Stop();
    }

    private void UpdateConnectionHealth()
    {
        BotMainHandler? runtime = _runtime;
        TimeSpan desiredInterval = runtime?.LowResourceModeEnabled == true ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1);
        if (_connectionHealthTimer.Interval != desiredInterval)
            _connectionHealthTimer.Interval = desiredInterval;
        bool visible = runtime?.ShowConnectionHealth ?? false;
        Visibility desiredVisibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (ConnectionHealthPanel.Visibility != desiredVisibility)
            ConnectionHealthPanel.Visibility = desiredVisibility;

        if (!visible || runtime == null)
            return;

        SetConnectionHealth(TwitchConnectionHealth, runtime.TwitchChatConnected, TwitchConnectedText, TwitchDisconnectedText);
        SetConnectionHealth(MinecraftConnectionHealth, runtime.MinecraftServerReady, MinecraftConnectedText, MinecraftDisconnectedText);
    }

    private static void SetConnectionHealth(TextBlock label, bool connected, string connectedText, string disconnectedText)
    {
        string text = connected ? connectedText : disconnectedText;
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
            label.Text = text;

        Brush foreground = connected ? Brushes.LightGreen : Brushes.IndianRed;
        if (!ReferenceEquals(label.Foreground, foreground))
            label.Foreground = foreground;
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

        if (ShouldPauseUIUpdates())
        {
            lock (_logGate)
                _deferredViewerList = [.. viewers];
            return;
        }

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
            int maxLines = GetMaxLogLines(isMinecraftLog);
            while (pendingLines.Count > maxLines)
            {
                pendingLines.Dequeue();
            }

            shouldSchedule = !ShouldPauseUIUpdates() && TryQueueFlush(isMinecraftLog);
        }

        if (shouldSchedule)
        {
            SafeInvoke(() => FlushLogQueue(box, lines, pendingLines, isMinecraftLog));
        }
    }

    private void FlushLogQueue(TextBox box, Queue<string> lines, Queue<string> pendingLines, bool isMinecraftLog)
    {
        if (ShouldPauseUIUpdates())
        {
            lock (_logGate)
                ClearQueuedFlush(isMinecraftLog);
            return;
        }

        int batchCount;
        string? singleLine = null;
        List<string>? batch = null;

        lock (_logGate)
        {
            batchCount = pendingLines.Count;
            if (batchCount == 1)
            {
                singleLine = pendingLines.Dequeue();
            }
            else if (batchCount > 1)
            {
                batch = new(batchCount);
                while (pendingLines.Count > 0)
                    batch.Add(pendingLines.Dequeue());
            }

            ClearQueuedFlush(isMinecraftLog);
        }

        if (batchCount == 0)
            return;

        int maxLines = GetMaxLogLines(isMinecraftLog);
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
                box.Text = string.Join(newLine, lines) + newLine;
            else
                box.AppendText(singleLine + newLine);
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
                box.Text = string.Join(newLine, lines) + newLine;
            else if (appended is { Length: > 0 })
                box.AppendText(appended.ToString());
        }

        box.ScrollToEnd();

        bool shouldSchedule = false;
        lock (_logGate)
        {
            if (pendingLines.Count > 0)
                shouldSchedule = TryQueueFlush(isMinecraftLog);
        }

        if (shouldSchedule)
            SafeInvoke(() => FlushLogQueue(box, lines, pendingLines, isMinecraftLog));
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

    private void FlushDeferredUIUpdates()
    {
        List<string>? viewers;
        bool flushMinecraft;
        bool flushTwitch;
        lock (_logGate)
        {
            viewers = _deferredViewerList;
            _deferredViewerList = null;
            flushMinecraft = _pendingMinecraftLogLines.Count > 0 && TryQueueFlush(isMinecraftLog: true);
            flushTwitch = _pendingTwitchLogLines.Count > 0 && TryQueueFlush(isMinecraftLog: false);
        }

        if (viewers != null)
            DisplayNormalizedViewerList(viewers);
        if (flushMinecraft)
            SafeInvoke(() => FlushLogQueue(MinecraftLogs, _minecraftLogLines, _pendingMinecraftLogLines, isMinecraftLog: true));
        if (flushTwitch)
            SafeInvoke(() => FlushLogQueue(TwitchLogs, _twitchLogLines, _pendingTwitchLogLines, isMinecraftLog: false));
        UpdateConnectionHealth();
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
