using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TwitchCraftBot_V1.BotSetup;

public sealed partial class ConfigurationStore
{
    private const string AppFolderName = "TwitchCraftBot";
    private const string ConfigFileName = "config.json";
    private const string ViewerTokensFileName = "viewer_tokens.db";
    private const string BackupsFolderName = "backups";
    private const string DefaultBindIP = "127.0.0.1";
    private const int DefaultServerPort = 25565;
    private const int DefaultRCONPort = 25575;
    private const int MinPort = 1;
    private const int MaxPort = 65535;
    private const int DefaultMaxPlayers = 1;
    private const int DefaultMemoryGB = 8;
    private const int DefaultMinigameCooldown = 15;
    private const double DefaultGlobalGameCommandCooldownSeconds = 10.0;
    private const int DefaultFollowRewardAmount = 100;
    private const double DefaultCommandCostMultiplier = 1.0;
    private const int DefaultPassiveTokensPerPayout = 1;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StartingProfileJsonConverter() }
    };

    private static readonly Lock IoGate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string WorkingDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);
    private static readonly string ConfigPathValue = Path.Combine(WorkingDirectoryPath, ConfigFileName);
    private static readonly string ConfigTempPathValue = ConfigPathValue + ".tmp";
    private static readonly string ViewerTokensPathValue = Path.Combine(WorkingDirectoryPath, ViewerTokensFileName);
    private static readonly string BackupsDirectoryPath = Path.Combine(WorkingDirectoryPath, BackupsFolderName);

    public static string WorkingDirectory => WorkingDirectoryPath;

    public static string ConfigPath => ConfigPathValue;

    public static string ViewerTokensPath => ViewerTokensPathValue;

    public static string BackupsDirectory => BackupsDirectoryPath;

    public static void EnsureWorkDir() => Directory.CreateDirectory(WorkingDirectory);

    public static bool HasConfig()
    {
        if (File.Exists(ConfigPath))
            return true;

        lock (IoGate)
        {
            return TryLoadConfig(ConfigTempPathValue, out _);
        }
    }

    public static void DeleteConfigFiles()
    {
        lock (IoGate)
        {
            TwitchCraftBot_V1.FileSystemHelper.DeleteFileSafe(ConfigPath);
            TwitchCraftBot_V1.FileSystemHelper.DeleteFileSafe(ConfigTempPathValue);
        }
    }

    public static BotConfig Load()
    {
        EnsureWorkDir();

        lock (IoGate)
        {
            string tempPath = ConfigTempPathValue;
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);

            if (!hasConfig && !hasTemp)
                return new BotConfig();

            if (TryLoadConfig(ConfigPath, out BotConfig loaded) ||
                TryLoadConfig(tempPath, out loaded))
            {
                Normalize(loaded);
                ResetStartMode(loaded);
                return loaded;
            }
        }

        throw new InvalidDataException("config.json could not be read. Restore an automatic backup or run setup again.");
    }

    public static void NormalizeRuntime(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Normalize(config);
    }

    public static void Save(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Normalize(config);
        EnsureWorkDir();

        lock (IoGate)
            SaveNoLock(config);
    }

    public static BotConfig Update(Action<BotConfig> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        EnsureWorkDir();

        lock (IoGate)
        {
            string tempPath = ConfigTempPathValue;
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);
            if (!TryLoadConfig(ConfigPath, out BotConfig config) && !TryLoadConfig(tempPath, out config))
            {
                if (hasConfig || hasTemp)
                    throw new InvalidDataException("config.json could not be read. Restore an automatic backup or run setup again.");

                config = new BotConfig();
            }

            Normalize(config);
            ResetStartMode(config);
            update(config);
            Normalize(config);
            SaveNoLock(config);
            return config;
        }
    }

    private static void SaveNoLock(BotConfig config)
    {
        string json = SerializeConfig(config);
        string tempPath = ConfigTempPathValue;

        if (ConfigMatches(json))
        {
            TwitchCraftBot_V1.FileSystemHelper.DeleteFileSafe(tempPath);
            return;
        }

        File.WriteAllText(tempPath, json, Utf8NoBom);
        TwitchCraftBot_V1.FileSystemHelper.ReplaceFile(tempPath, ConfigPath, null, "Atomic config save failed; falling back to copy");
    }

    private static string SerializeConfig(BotConfig config)
    {
        bool originalMultiplayerEnabled = config.Settings.MultiplayerEnabled;
        bool originalRemoteControlEnabled = config.Settings.RemoteControlEnabled;
        bool originalRequireOnlineMode = config.Settings.RequireOnlineMode;

        try
        {
            ResetStartMode(config);
            return JsonConvert.SerializeObject(config, JsonSettings);
        }
        finally
        {
            config.Settings.MultiplayerEnabled = originalMultiplayerEnabled;
            config.Settings.RemoteControlEnabled = originalRemoteControlEnabled;
            config.Settings.RequireOnlineMode = originalRequireOnlineMode;
        }
    }

    private static bool TryLoadConfig(string path, out BotConfig config)
    {
        config = null!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            config = JsonConvert.DeserializeObject<BotConfig>(text, JsonSettings) ?? new BotConfig();
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to read config file", ex);
            return false;
        }
    }

    private static bool ConfigMatches(string json)
    {
        try
        {
            return File.Exists(ConfigPath) && string.Equals(File.ReadAllText(ConfigPath, Encoding.UTF8), json, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to compare existing config file", ex);
            return false;
        }
    }

    internal static bool TryCopyConfig(string destinationPath)
    {
        try
        {
            lock (IoGate)
            {
                if (!File.Exists(ConfigPath))
                    return false;
                TwitchCraftBot_V1.FileSystemHelper.EnsureParentDir(destinationPath);
                File.Copy(ConfigPath, destinationPath, overwrite: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to back up config", ex);
            return false;
        }
    }
}
