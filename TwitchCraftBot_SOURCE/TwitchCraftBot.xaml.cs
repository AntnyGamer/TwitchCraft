using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        Runtime.AttachWindow(this);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Shell.PropertyChanged += OnShellPropertyChanged;
    }

    public AppShellViewModel Shell { get; }

    public BotMainHandler Runtime { get; }

    public void ShowStart() => Shell.Navigate(ShellPage.Start);

    public void ShowHelp() => Shell.Navigate(ShellPage.Help);

    public void ShowSettings() => Shell.Navigate(ShellPage.Settings);

    public void ShowStatistics() => Shell.Navigate(ShellPage.Statistics);

    public Task StartAsync() => Runtime.StartAsync();

    public Task ResetAsync() => Runtime.ResetAsync();

    public Task RestartAsync() => Runtime.RestartAsync();

    public void RestartAfterReset()
    {
        _restartAfterClose = true;
        Close();
    }

    public Task PauseAsync() => Runtime.PauseAsync();

    public Task<bool> RunMinecraftCommandAsync(string command) => Runtime.RunMinecraftCommandAsync(command);

    public void AddServerLogLine(string line) => Main.AddServerLogLine(line);

    public void ClearServerLog() => Main.ClearServerLog();

    public void AddChatLogLine(string line) => Main.AddChatLogLine(line);

    public void ClearChatLog() => Main.ClearChatLog();

    public void UpdateViewers(List<string> viewers) => Main.UpdateViewers(viewers);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _ = Dispatcher.BeginInvoke(SetInitialSize, DispatcherPriority.Loaded);
    }

    private void SetInitialSize()
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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ConfigurationStore.EnsureWorkDir();

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

        ApplyPageState();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownAlreadyHandled)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _shutdownAlreadyHandled = true;

        bool stopped = await Runtime.ShutdownAsync();
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
                AppHelpers.OpenTarget(executablePath, AppContext.BaseDirectory);
            }
            catch (Exception ex)
            {
                ErrorHandling.ShowConfigError(this, ex);
            }
        }

        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Shell.PropertyChanged -= OnShellPropertyChanged;
        UIThread.BeginInvoke(Close);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_shutdownAlreadyHandled)
            ApplyPageState();
    }

    private void ApplyPageState()
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

            SetPageVisibility(Setup, Shell.IsSetupVisible);
            SetPageVisibility(Start, Shell.IsLaunchVisible);
            SetPageVisibility(Main, Shell.IsConsoleVisible);
            SetPageVisibility(Help, Shell.IsHelpVisible);
            SetPageVisibility(Settings, Shell.IsSettingsVisible);
            SetPageVisibility(Statistics, Shell.IsStatisticsVisible);

            if (Shell.IsLaunchVisible)
            {
                Start.RefreshConfig();
            }
        });
    }

    private static void SetPageVisibility(Control control, bool isVisible)
        => control.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
}
