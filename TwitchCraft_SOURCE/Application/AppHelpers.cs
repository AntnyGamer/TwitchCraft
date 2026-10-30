using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace TwitchCraft_V1;

internal static class AppHelpers
{
    public static string DisplayVersion { get; } = GetDisplayVersion();

    public static TwitchCraft? GetTwitchCraftWindow(DependencyObject source)
        => Window.GetWindow(source) as TwitchCraft;

    public static string? GetExecutablePath()
    {
        string? executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
            return executablePath;

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static void NavigateBack(DependencyObject source)
    {
        if (GetTwitchCraftWindow(source) is TwitchCraft parent)
            parent.Shell.Navigate(parent.Shell.PreviousPage);
    }

    public static void NavigateBackOnEscape(DependencyObject source, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        NavigateBack(source);
    }

    public static void OpenTarget(string target, string? workingDirectory = null)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = target,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            processStartInfo.WorkingDirectory = workingDirectory;

        Process.Start(processStartInfo)?.Dispose();
    }

    private static string GetDisplayVersion()
    {
        string version = typeof(AppHelpers).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
        int metadataSeparator = version.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0 ? version : version[..metadataSeparator];
    }

    internal static string? FindWorldFolder()
    {
        string initialPath = GetWorldPath();

        OpenFolderDialog dialog = new()
        {
            Multiselect = false,
            Title = "Select a Minecraft world folder.",
            InitialDirectory = initialPath,
            DefaultDirectory = initialPath,
            FolderName = initialPath
        };

        if (dialog.ShowDialog() != true)
            return null;

        string selectedPath = dialog.FolderName ?? string.Empty;
        return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
    }

    private static string GetWorldPath()
    {
        string minecraftSavesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft",
            "saves");

        if (Directory.Exists(minecraftSavesPath))
            return minecraftSavesPath;

        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return Directory.Exists(downloadsPath)
            ? downloadsPath
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
