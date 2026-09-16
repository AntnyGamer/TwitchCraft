using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Help : UserControl
{
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public Help()
    {
        InitializeComponent();
        AboutVersion.Text = "Version: " + AppHelpers.DisplayVersion;
        _diagnosticsTimer.Tick += (_, _) => ClearDiagnosticsStatus();
        Unloaded += (_, _) => ClearDiagnosticsStatus();
    }

    private void OpenReadme_Click(object sender, RoutedEventArgs e) => OpenBundledFile("README.txt", "README");
    private void OpenLicense_Click(object sender, RoutedEventArgs e) => OpenBundledFile(Path.Combine("licenses", "LICENSE"), "License");

    private void OpenBundledFile(string relativePath, string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relativePath);
        try
        {
            if (File.Exists(path))
                AppHelpers.OpenTarget(path);
            else
                ErrorHandling.ShowFileMissing(this, name, path);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowFileError(this, name, path, ex);
        }
    }

    private void Help_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ClearDiagnosticsStatus();
        if (IsVisible)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible) BackButton.Focus();
            }, DispatcherPriority.Input);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => AppHelpers.NavigateBack(this);

    private void Back_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            AppHelpers.NavigateBack(this);
        }
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ClearDiagnosticsStatus();
        try
        {
            MainHandler? runtime = AppHelpers.GetTwitchCraftWindow(this)?.Runtime;
            string minecraftVersion = runtime?.CurrentMinecraftVersion ?? string.Empty;
            bool hasActiveProfile = runtime?.ProfileApplied == true;
            bool? lowResourceModeEnabled = hasActiveProfile ? runtime!.LowResourceModeEnabled : null;

            if (!hasActiveProfile)
            {
                try
                {
                    TwitchCraftConfig config = ConfigurationStore.Load();
                    minecraftVersion = config.Server.MinecraftVersion;
                    lowResourceModeEnabled = config.Settings.LowResourceModeEnabled;
                }
                catch
                {
                    minecraftVersion = "Unavailable";
                }
            }

            bool remoteControlEnabled = hasActiveProfile && runtime!.RemoteControlEnabled;
            string diagnostics = string.Join(Environment.NewLine,
                "TwitchCraft: " + AppHelpers.DisplayVersion,
                ".NET: " + RuntimeInformation.FrameworkDescription,
                "OS: " + RuntimeInformation.OSDescription,
                "OS architecture: " + RuntimeInformation.OSArchitecture,
                "CPU threads: " + Environment.ProcessorCount,
                "Minecraft: " + (minecraftVersion.Length == 0 ? "Not configured" : minecraftVersion),
                "Control: " + (hasActiveProfile ? remoteControlEnabled ? "Remote Control" : "Local" : "Unavailable"),
                "Multiplayer: " + (hasActiveProfile ? FormatEnabled(runtime!.MultiplayerEnabled) : "Unavailable"),
                "Online mode: " + (hasActiveProfile ? FormatEnabled(runtime!.RequireOnlineMode) : "Unavailable"),
                "Low resource mode: " + FormatEnabled(lowResourceModeEnabled),
                "Twitch connection: " + (runtime?.TwitchChatConnected == true ? "Connected" : "Disconnected"),
                "Minecraft process: " + (hasActiveProfile ? remoteControlEnabled ? "Not applicable" : runtime!.MinecraftProcessRunning ? "Running" : "Not running" : "Unavailable"),
                "Minecraft server: " + (runtime?.MinecraftServerReady == true ? "Ready" : "Not ready"),
                "RCON: " + (hasActiveProfile ? remoteControlEnabled ? runtime!.RCONConnected ? "Connected" : "Disconnected" : "Not applicable" : "Unavailable"));

            Clipboard.SetText(diagnostics);
            ShowDiagnosticsCopied();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowCopyDiagnosticsError(this, ex);
        }
    }

    private static string FormatEnabled(bool? enabled)
        => enabled.HasValue ? enabled.Value ? "Enabled" : "Disabled" : "Unavailable";

    internal void ShowDiagnosticsCopied()
    {
        _diagnosticsTimer.Stop();
        DiagnosticsStatus.Text = "Copied to clipboard.";
        _diagnosticsTimer.Start();
    }

    private void ClearDiagnosticsStatus()
    {
        _diagnosticsTimer.Stop();
        DiagnosticsStatus.Text = string.Empty;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ConfigurationStore.LogsDirectory);
            AppHelpers.OpenTarget(ConfigurationStore.LogsDirectory);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowLogsError(this, ex);
        }
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            AppHelpers.OpenTarget(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowLinkError(this, ex);
        }
    }
}
