using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public partial class TwitchCraftBot : Window
{
    private const double DesignContentWidth = 800.0;
    private const double DesignContentHeight = 450.0;

    private bool _initialClientSizeApplied;
    private bool _shutdownAlreadyHandled;
    private bool _restartAfterClose;

    public TwitchCraftBot()
    {
        InitializeComponent();

        Shell = new AppShellViewModel();
        Runtime = new BotMainHandler(Shell);

        DataContext = Shell;
        Runtime.InitializeWindow(this);

        SourceInitialized += TwitchCraftBot_SourceInitialized;
        Loaded += TwitchCraftBot_Loaded;
        Closing += TwitchCraftBot_Closing;
        Shell.PropertyChanged += Shell_PropertyChanged;
    }

    public AppShellViewModel Shell { get; }

    public BotMainHandler Runtime { get; }

    public void NavigateToStart() => Shell.Navigate(ShellPage.Start);

    public void NavigateToHelp() => Shell.Navigate(ShellPage.Help);

    public void NavigateToSettings() => Shell.Navigate(ShellPage.Settings);

    public void NavigateToStatistics() => Shell.Navigate(ShellPage.Statistics);

    public BotStatisticsSnapshot GetStatisticsSnapshot(CancellationToken cancellationToken = default) => Runtime.GetStatisticsSnapshot(cancellationToken);

    public Task ResetStatisticsAsync() => Runtime.ResetAllStatisticsAsync();

    public Task BeginLaunchAsync() => Runtime.StartMainHandlerAsync();

    public Task Reset() => Runtime.Reset();

    public Task Restart() => Runtime.Restart();

    public void RestartAfterConfigDelete()
    {
        _restartAfterClose = true;
        Close();
    }

    public Task PauseAsync() => Runtime.PauseAsync();

    public Task<bool> ExecuteMinecraftCommandAsync(string command) => Runtime.ExecuteMinecraftCommandAsync(command);

    public void AddServerLogLine(string line) => Main.AddServerLogLine(line);

    public void ClearServerLogView() => Main.ClearServerLogView();

    public void AddChatLogLine(string line) => Main.AddChatLogLine(line);

    public void ClearChatLogView() => Main.ClearChatLogView();

    public void DisplayNormalizedViewerList(List<string> viewers) => Main.DisplayNormalizedViewerList(viewers);

    private void TwitchCraftBot_SourceInitialized(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(ApplyInitialClientSize, DispatcherPriority.Loaded);
    }

    private void ApplyInitialClientSize()
    {
        if (_initialClientSizeApplied)
        {
            return;
        }

        _initialClientSizeApplied = true;
        WindowContentHost.UpdateLayout();

        if (WindowContentHost.ActualWidth <= 0 || WindowContentHost.ActualHeight <= 0)
        {
            return;
        }

        double widthDelta = DesignContentWidth - WindowContentHost.ActualWidth;
        double heightDelta = DesignContentHeight - WindowContentHost.ActualHeight;

        if (Math.Abs(widthDelta) > 0.5)
        {
            Width = Math.Ceiling(Width + widthDelta);
        }

        if (Math.Abs(heightDelta) > 0.5)
        {
            Height = Math.Ceiling(Height + heightDelta);
        }

        MinWidth = Width;
        MinHeight = Height;
    }

    private void TwitchCraftBot_Loaded(object? sender, RoutedEventArgs e)
    {
        ConfigurationStore.CheckRootFolder();

        try
        {
            if (ConfigurationStore.HasConfig())
            {
                _ = ConfigurationStore.Load();
                Shell.Navigate(ShellPage.Start);
            }
            else
            {
                Shell.Navigate(ShellPage.Setup);
            }
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowConfigError(this, ex);
            Shell.Navigate(ShellPage.Setup);
        }

        ApplyCurrentPageToFrameState();
    }

    private async void TwitchCraftBot_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownAlreadyHandled)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _shutdownAlreadyHandled = true;

        bool stopped = await Runtime.BeginShutdownAsync();
        if (!stopped)
        {
            _shutdownAlreadyHandled = false;
            return;
        }

        string? executablePath = _restartAfterClose ? AppHelpers.GetExecutablePath() : null;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            try
            {
                AppHelpers.OpenShellTarget(executablePath, AppContext.BaseDirectory);
            }
            catch (Exception ex)
            {
                ErrorHandling.ShowConfigError(this, ex);
            }
        }

        SourceInitialized -= TwitchCraftBot_SourceInitialized;
        Loaded -= TwitchCraftBot_Loaded;
        Closing -= TwitchCraftBot_Closing;
        Shell.PropertyChanged -= Shell_PropertyChanged;
        UIThread.BeginInvoke(Close);
    }

    private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_shutdownAlreadyHandled)
            ApplyCurrentPageToFrameState();
    }

    private void ApplyCurrentPageToFrameState()
    {
        if (_shutdownAlreadyHandled)
        {
            return;
        }

        UIThread.Invoke(() =>
        {
            if (_shutdownAlreadyHandled)
            {
                return;
            }

            SetFrameVisibility(Setup, Shell.IsSetupVisible);
            SetFrameVisibility(Start, Shell.IsLaunchVisible);
            SetFrameVisibility(Main, Shell.IsConsoleVisible);
            SetFrameVisibility(Help, Shell.IsHelpVisible);
            SetFrameVisibility(Settings, Shell.IsSettingsVisible);
            SetFrameVisibility(Statistics, Shell.IsStatisticsVisible);

            if (Shell.IsLaunchVisible)
            {
                Start.RefreshFromConfig();
            }
        });
    }

    private static void SetFrameVisibility(Control control, bool isVisible)
        => control.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
}
