using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TwitchCraftBot_V1;

public enum ShellPage
{
    Setup,
    Start,
    Main,
    Help,
    Settings,
    Statistics
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, params string[] affectedProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged();

        for (int i = 0; i < affectedProperties.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(affectedProperties[i]))
            {
                OnPropertyChanged(affectedProperties[i]);
            }
        }

        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

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

internal static class TextSegmentHelper
{
    public static string TrimSegment(string? value, int start, int length)
    {
        if (string.IsNullOrEmpty(value) || length <= 0)
            return string.Empty;

        int segmentStart = Math.Clamp(start, 0, value.Length);
        int segmentEnd = Math.Min(value.Length, segmentStart + length) - 1;
        while (segmentStart <= segmentEnd && char.IsWhiteSpace(value[segmentStart]))
            segmentStart++;
        while (segmentEnd >= segmentStart && char.IsWhiteSpace(value[segmentEnd]))
            segmentEnd--;

        if (segmentStart > segmentEnd)
            return string.Empty;

        return segmentStart == 0 && segmentEnd == value.Length - 1
            ? value
            : value.Substring(segmentStart, segmentEnd - segmentStart + 1);
    }
}

internal static class FileSystemHelper
{
    public static string GetUniqueTempPath(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        string fileName = Path.GetFileName(path);
        return Path.Combine(directory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    public static void EnsureDirectoryForFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public static bool ReplaceOrMoveWithFallback(string tempPath, string targetPath, string? backupPath, string logMessage)
    {
        try
        {
            if (File.Exists(targetPath))
                File.Replace(tempPath, targetPath, backupPath, true);
            else
                File.Move(tempPath, targetPath);

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal(logMessage, ex);
            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(targetPath))
            {
                try
                {
                    File.Copy(targetPath, backupPath, true);
                }
                catch (Exception backupEx)
                {
                    ErrorHandling.LogNonFatal("Failed to copy file", backupEx);
                }
            }

            try
            {
                File.Move(tempPath, targetPath, true);
            }
            catch (Exception moveEx)
            {
                ErrorHandling.LogNonFatal("Failed to move file", moveEx);
                File.Copy(tempPath, targetPath, true);
                TryDeleteFile(tempPath);
            }

            return false;
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to delete file", ex);
        }
    }

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool skipReparsePoints)
    {
        DirectoryInfo source = new(sourceDirectory);
        if (skipReparsePoints && (source.Attributes & FileAttributes.ReparsePoint) != 0)
            return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (FileInfo file in source.EnumerateFiles())
        {
            if (!skipReparsePoints || (file.Attributes & FileAttributes.ReparsePoint) == 0)
                file.CopyTo(Path.Combine(destinationDirectory, file.Name), true);
        }

        foreach (DirectoryInfo directory in source.EnumerateDirectories())
        {
            if (!skipReparsePoints || (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                CopyDirectory(directory.FullName, Path.Combine(destinationDirectory, directory.Name), skipReparsePoints);
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to delete directory", ex);
        }
    }
}

internal static class SortedListHelper
{
    public static int FindIndex(IReadOnlyList<string> values, string value, StringComparer comparer)
    {
        int low = 0;
        int high = values.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = comparer.Compare(values[middle], value);
            if (comparison == 0)
                return middle;

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return ~low;
    }

    public static bool Contains(IReadOnlyList<string> values, string value, StringComparer comparer)
        => FindIndex(values, value, comparer) >= 0;

    public static bool EqualInOrder(IReadOnlyList<string> left, IReadOnlyList<string> right, StringComparer comparer)
    {
        if (ReferenceEquals(left, right))
            return true;

        int count = left.Count;
        if (count != right.Count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public static void SortAndDeduplicate(List<string> values, StringComparer comparer)
    {
        int count = values.Count;
        if (count <= 1)
            return;

        values.Sort(comparer);
        int writeIndex = 1;
        for (int readIndex = 1; readIndex < count; readIndex++)
        {
            if (!comparer.Equals(values[readIndex], values[writeIndex - 1]))
                values[writeIndex++] = values[readIndex];
        }

        if (writeIndex < count)
            values.RemoveRange(writeIndex, count - writeIndex);
    }

    public static List<string> NormalizeMinecraftPlayerNames(List<string>? players, StringComparer comparer)
    {
        if (players is null)
            return [];

        if (players.Count == 0 || IsNormalizedMinecraftPlayerList(players, comparer))
            return players;

        return NormalizeMinecraftPlayerNames((IEnumerable<string>)players, comparer);
    }

    public static List<string> NormalizeMinecraftPlayerNames(IEnumerable<string> players, StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(players);

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(players, out int count) ? count : 0;

        List<string> normalized = new(capacity);
        foreach (string player in players)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(player, out string normalizedPlayer))
                normalized.Add(normalizedPlayer);
        }

        SortAndDeduplicate(normalized, comparer);
        return normalized;
    }

    private static bool IsNormalizedMinecraftPlayerList(List<string> players, StringComparer comparer)
    {
        int count = players.Count;
        if (!MinecraftNameHelper.TryNormalizePlayerName(players[0], out string previous) ||
            !string.Equals(previous, players[0], StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = 1; i < count; i++)
        {
            if (!MinecraftNameHelper.TryNormalizePlayerName(players[i], out string current) ||
                !string.Equals(current, players[i], StringComparison.Ordinal) ||
                comparer.Compare(previous, current) >= 0)
            {
                return false;
            }

            previous = current;
        }

        return true;
    }
}

public sealed class AppShellViewModel : ObservableObject
{
    private static readonly string[] CurrentPageAffectedProperties =
    [
        nameof(IsSetupVisible),
        nameof(IsLaunchVisible),
        nameof(IsConsoleVisible),
        nameof(IsHelpVisible),
        nameof(IsSettingsVisible),
        nameof(IsStatisticsVisible)
    ];

    private ShellPage _currentPage;
    private ShellPage _previousPage;
    private ShellPage _pageBeforeSettings;
    private ShellPage _helpBackTarget;

    public ShellPage PreviousPage
    {
        get => _previousPage;
        private set => SetProperty(ref _previousPage, value);
    }

    public ShellPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value, CurrentPageAffectedProperties);
        }
    }

    public bool IsSetupVisible => CurrentPage == ShellPage.Setup;
    public bool IsLaunchVisible => CurrentPage == ShellPage.Start;
    public bool IsConsoleVisible => CurrentPage == ShellPage.Main;
    public bool IsHelpVisible => CurrentPage == ShellPage.Help;
    public bool IsSettingsVisible => CurrentPage == ShellPage.Settings;
    public bool IsStatisticsVisible => CurrentPage == ShellPage.Statistics;

    public void Navigate(ShellPage page)
    {
        if (CurrentPage == page)
        {
            return;
        }

        if (page == ShellPage.Settings)
        {
            _pageBeforeSettings = CurrentPage;

            if (CurrentPage == ShellPage.Help)
            {
                _helpBackTarget = PreviousPage;
            }
        }

        if (CurrentPage == ShellPage.Settings &&
            page == ShellPage.Help &&
            _pageBeforeSettings == ShellPage.Help)
        {
            PreviousPage = _helpBackTarget;
        }
        else
        {
            PreviousPage = CurrentPage;
        }

        CurrentPage = page;
    }
}
