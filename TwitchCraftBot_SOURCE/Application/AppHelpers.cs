using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TwitchCraftBot_V1;

internal static class AppHelpers
{
    public static TwitchCraftBot? GetParentBot(DependencyObject source)
        => Window.GetWindow(source) as TwitchCraftBot;

    public static string? GetExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static string GetExecutableDirectory()
    {
        string? exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return AppContext.BaseDirectory;
        }

        return Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
    }

    public static void NavigateBack(DependencyObject source)
    {
        if (GetParentBot(source) is TwitchCraftBot parent)
        {
            parent.Shell.Navigate(parent.Shell.PreviousPage);
        }
    }

    public static void OpenShellTarget(string target, string? workingDirectory = null)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = target,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        Process.Start(processStartInfo);
    }
}
