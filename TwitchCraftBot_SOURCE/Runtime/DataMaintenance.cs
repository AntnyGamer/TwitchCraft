using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private readonly Lock _automaticBackupGate = new();
    private DateTime _lastDatabaseOptimizeUtc = DateTime.MinValue;
    private DateTime _lastAutomaticBackupUtc = DateTime.MinValue;
    private bool _automaticBackupTimestampLoaded;

    private async Task RunDataMaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                StartingProfile settings = EffectiveSettings;
                if (settings.AutomaticBackupsEnabled && IsAutomaticBackupDue(settings.AutomaticBackupIntervalHours))
                    CreateAutomaticBackup(settings.AutomaticBackupRetentionCount);

                int optimizeHours = settings.SQLiteOptimizeIntervalHours;
                if (optimizeHours > 0 && DateTime.UtcNow - _lastDatabaseOptimizeUtc >= TimeSpan.FromHours(optimizeHours))
                {
                    if (_tokenStore.TryOptimizeDatabase())
                        _lastDatabaseOptimizeUtc = DateTime.UtcNow;
                }

                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Automatic data maintenance failed", ex);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool IsAutomaticBackupDue(int intervalHours)
    {
        if (!_automaticBackupTimestampLoaded)
        {
            _lastAutomaticBackupUtc = GetNewestAutomaticBackupUtc();
            _automaticBackupTimestampLoaded = true;
        }

        return _lastAutomaticBackupUtc == DateTime.MinValue ||
            DateTime.UtcNow - _lastAutomaticBackupUtc >= TimeSpan.FromHours(Math.Clamp(intervalHours, 1, 168));
    }

    private static DateTime GetNewestAutomaticBackupUtc()
    {
        string root = ConfigurationStore.BackupsDirectory;
        if (!Directory.Exists(root))
            return DateTime.MinValue;

        DateTime newest = DateTime.MinValue;
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (TryGetAutomaticBackupTimestampUtc(directory, requireCompleteBackup: true, out DateTime timestamp) && timestamp > newest)
                newest = timestamp;
        }

        return newest;
    }

    private void CreateAutomaticBackup(int retentionCount)
    {
        lock (_automaticBackupGate)
        {
            try
            {
                string root = ConfigurationStore.BackupsDirectory;
                Directory.CreateDirectory(root);
                string backupDirectory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                if (Directory.Exists(backupDirectory))
                    backupDirectory += "-" + Guid.NewGuid().ToString("N")[..6];
                Directory.CreateDirectory(backupDirectory);

                bool configSaved = ConfigurationStore.TryCopyConfigTo(Path.Combine(backupDirectory, "config.json"));
                bool tokensSaved = _tokenStore.TryBackupDatabase(Path.Combine(backupDirectory, "viewer_tokens.db"));
                if (!configSaved || !tokensSaved)
                {
                    try { Directory.Delete(backupDirectory, recursive: true); } catch { }
                    return;
                }

                _lastAutomaticBackupUtc = DateTime.UtcNow;
                _automaticBackupTimestampLoaded = true;
                PruneAutomaticBackups(root, retentionCount);
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to create automatic backup", ex);
            }
        }
    }

    private void CreateShutdownBackupIfEnabled()
    {
        try
        {
            StartingProfile? settings = _activeConfig?.Settings;
            if (settings == null)
            {
                if (!ConfigurationStore.HasConfig())
                    return;

                settings = ConfigurationStore.Load().Settings;
            }

            if (settings.AutomaticBackupsEnabled)
                CreateAutomaticBackup(settings.AutomaticBackupRetentionCount);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to create shutdown backup", ex);
        }
    }

    private static void PruneAutomaticBackups(string root, int retentionCount)
    {
        List<(DirectoryInfo Directory, DateTime Timestamp)> backups = [];
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (!TryGetAutomaticBackupTimestampUtc(directory, requireCompleteBackup: false, out DateTime timestamp))
                continue;

            if (!IsCompleteAutomaticBackup(directory.FullName))
            {
                try { directory.Delete(recursive: true); }
                catch (Exception ex) { ErrorHandling.LogNonFatal("Failed to remove an incomplete automatic backup", ex); }
                continue;
            }

            backups.Add((directory, timestamp));
        }

        backups.Sort(static (left, right) => right.Timestamp.CompareTo(left.Timestamp));
        int keepCount = Math.Clamp(retentionCount, 1, 20);
        for (int i = keepCount; i < backups.Count; i++)
        {
            try { backups[i].Directory.Delete(recursive: true); }
            catch (Exception ex) { ErrorHandling.LogNonFatal("Failed to prune an old automatic backup", ex); }
        }
    }

    private static bool TryGetAutomaticBackupTimestampUtc(
        DirectoryInfo directory,
        bool requireCompleteBackup,
        out DateTime timestampUtc)
    {
        timestampUtc = DateTime.MinValue;
        string timestamp = directory.Name.Length >= 15 ? directory.Name[..15] : string.Empty;
        if (!DateTime.TryParseExact(
                timestamp,
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestampUtc))
        {
            return false;
        }

        return !requireCompleteBackup || IsCompleteAutomaticBackup(directory.FullName);
    }

    private static bool IsCompleteAutomaticBackup(string directory)
        => File.Exists(Path.Combine(directory, "config.json")) &&
           File.Exists(Path.Combine(directory, "viewer_tokens.db"));
}
