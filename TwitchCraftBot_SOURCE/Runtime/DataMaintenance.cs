using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

internal sealed class DataMaintenance(
    Func<BotConfig?> getConfig,
    StartingProfile defaultSettings,
    TokenService tokens,
    Func<string, CancellationToken, Task<string>> validateBot,
    Action<string, string> saveBot,
    Func<string, CancellationToken, Task<bool>> tryRefreshAuth)
{
    private readonly Func<BotConfig?> _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
    private readonly StartingProfile _defaultSettings = defaultSettings ?? throw new ArgumentNullException(nameof(defaultSettings));
    private readonly TokenService _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    private readonly Func<string, CancellationToken, Task<string>> _validateBot = validateBot ?? throw new ArgumentNullException(nameof(validateBot));
    private readonly Action<string, string> _saveBot = saveBot ?? throw new ArgumentNullException(nameof(saveBot));
    private readonly Func<string, CancellationToken, Task<bool>> _tryRefreshAuth = tryRefreshAuth ?? throw new ArgumentNullException(nameof(tryRefreshAuth));
    private readonly Lock _automaticBackupGate = new();
    private DateTime _lastDatabaseOptimizeUtc = DateTime.MinValue;
    private DateTime _lastTwitchValidationUtc = DateTime.MinValue;
    private DateTime _lastAutomaticBackupUtc = DateTime.MinValue;
    private bool _automaticBackupTimestampLoaded;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                StartingProfile settings = _getConfig()?.Settings ?? _defaultSettings;
                if (DateTime.UtcNow - _lastTwitchValidationUtc >= TimeSpan.FromHours(1))
                {
                    TwitchConfig? twitch = _getConfig()?.Twitch;
                    string token = TwitchTokenHelper.NormalizeAccessToken(twitch?.BotToken);
                    if (token.Length > 0)
                    {
                        try
                        {
                            string login = await _validateBot(token, cancellationToken).ConfigureAwait(false);
                            if (login.Length > 0 && !string.Equals(login, twitch?.BotName, StringComparison.OrdinalIgnoreCase))
                                _saveBot(token, login);
                            _lastTwitchValidationUtc = DateTime.UtcNow;
                        }

                        catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        { if (await _tryRefreshAuth(token, cancellationToken).ConfigureAwait(false)) _lastTwitchValidationUtc = DateTime.UtcNow; }
                    }

                }

                if (settings.AutomaticBackupsEnabled && IsBackupDue(settings.AutomaticBackupIntervalHours))
                    CreateBackup(settings.AutomaticBackupRetentionCount);
                int optimizeHours = settings.SQLiteOptimizeIntervalHours;
                if (optimizeHours > 0 && DateTime.UtcNow - _lastDatabaseOptimizeUtc >= TimeSpan.FromHours(optimizeHours))
                {
                    if (_tokens.TryOptimize())
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

    internal void MarkTwitchValidated()
        => _lastTwitchValidationUtc = DateTime.UtcNow;

    private bool IsBackupDue(int intervalHours)
    {
        if (!_automaticBackupTimestampLoaded)
        {
            _lastAutomaticBackupUtc = GetLatestBackupTime();
            _automaticBackupTimestampLoaded = true;
        }

        return _lastAutomaticBackupUtc == DateTime.MinValue ||
            DateTime.UtcNow - _lastAutomaticBackupUtc >= TimeSpan.FromHours(Math.Clamp(intervalHours, 1, 168));
    }

    private static DateTime GetLatestBackupTime()
    {
        string root = ConfigurationStore.BackupsDirectory;
        if (!Directory.Exists(root))
            return DateTime.MinValue;
        DateTime newest = DateTime.MinValue;
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (TryGetBackupTime(directory, requireCompleteBackup: true, out DateTime timestamp) && timestamp > newest)
                newest = timestamp;
        }

        return newest;
    }

    private void CreateBackup(int retentionCount)
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

                bool configSaved = ConfigurationStore.TryCopyConfig(Path.Combine(backupDirectory, "config.json"));
                bool tokensSaved = _tokens.TryBackup(Path.Combine(backupDirectory, "viewer_tokens.db"));
                if (!configSaved || !tokensSaved)
                {
                    try { Directory.Delete(backupDirectory, recursive: true); } catch { }
                    return;
                }

                _lastAutomaticBackupUtc = DateTime.UtcNow;
                _automaticBackupTimestampLoaded = true;
                PruneBackups(root, retentionCount);
            }

            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to create automatic backup", ex);
            }

        }

    }

    internal void BackupOnShutdown()
    {
        try
        {
            StartingProfile? settings = _getConfig()?.Settings;
            if (settings == null)
            {
                if (!ConfigurationStore.HasConfig())
                    return;
                settings = ConfigurationStore.Load().Settings;
            }

            if (settings.AutomaticBackupsEnabled)
                CreateBackup(settings.AutomaticBackupRetentionCount);
        }

        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to create shutdown backup", ex);
        }

    }

    internal static void PruneBackups(string root, int retentionCount)
    {
        List<(DirectoryInfo Directory, DateTime Timestamp)> backups = [];
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (!TryGetBackupTime(directory, requireCompleteBackup: false, out DateTime timestamp))
                continue;

            if (!IsBackupComplete(directory.FullName))
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

    private static bool TryGetBackupTime(
        DirectoryInfo directory,
        bool requireCompleteBackup,
        out DateTime timestampUtc)
    {
        timestampUtc = DateTime.MinValue;
        if (directory.Name.Length < 15 ||
            !DateTime.TryParseExact(
                directory.Name.AsSpan(0, 15),
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestampUtc))
        {
            return false;
        }

        return !requireCompleteBackup || IsBackupComplete(directory.FullName);
    }

    private static bool IsBackupComplete(string directory)
        => File.Exists(Path.Combine(directory, "config.json")) &&
           File.Exists(Path.Combine(directory, "viewer_tokens.db"));
}
