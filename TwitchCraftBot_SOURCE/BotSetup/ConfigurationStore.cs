using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace TwitchCraftBot_V1.BotSetup;

public sealed partial class ConfigurationStore
{
    private const string AppFolderName = "TwitchCraftBot";
    private const string ConfigFileName = "config.json";
    private const string ViewerTokensFileName = "viewer_tokens.db";
    private const string DefaultBindIP = "127.0.0.1";
    private const int DefaultServerPort = 25565;
    private const int DefaultRCONPort = 25575;
    private const int MinPort = 1;
    private const int MaxPort = 65535;
    private const int DefaultMaxPlayers = 1;
    private const int DefaultMemoryGB = 8;
    private const int DefaultMinigameCooldown = 15;
    private const double DefaultGlobalGameCommandCooldownSeconds = 10.0;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented
    };

    private static readonly Lock IoGate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string WorkingDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);
    private static readonly string ConfigPathValue = Path.Combine(WorkingDirectoryPath, ConfigFileName);
    private static readonly string ViewerTokensPathValue = Path.Combine(WorkingDirectoryPath, ViewerTokensFileName);

    public static string WorkingDirectory => WorkingDirectoryPath;

    public static string ConfigPath => ConfigPathValue;

    public static string ViewerTokensPath => ViewerTokensPathValue;

    public static void CheckRootFolder() => Directory.CreateDirectory(WorkingDirectory);

    public static bool HasConfig()
    {
        if (File.Exists(ConfigPath))
            return true;

        lock (IoGate)
        {
            return TryLoadConfig(GetTempPath(ConfigPath), out _);
        }
    }

    public static void DeleteConfigFiles()
    {
        lock (IoGate)
        {
            TwitchCraftBot_V1.FileSystemHelper.TryDeleteFile(ConfigPath);
            TwitchCraftBot_V1.FileSystemHelper.TryDeleteFile(GetTempPath(ConfigPath));
        }
    }

    public static BotConfig Load()
    {
        CheckRootFolder();

        lock (IoGate)
        {
            string tempPath = GetTempPath(ConfigPath);
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);

            if (!hasConfig && !hasTemp)
                return new BotConfig();

            if (TryLoadConfig(ConfigPath, out BotConfig loaded) ||
                TryLoadConfig(tempPath, out loaded))
            {
                Normalize(loaded);
                ResetTransientStartMode(loaded);
                return loaded;
            }
        }

        throw new InvalidDataException("config.json could not be read. Restore a backup or run setup again.");
    }

    public static void NormalizeForRuntime(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Normalize(config);
    }

    public static void Save(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Normalize(config);
        CheckRootFolder();

        lock (IoGate)
            SaveNoLock(config);
    }

    public static BotConfig Update(Action<BotConfig> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        CheckRootFolder();

        lock (IoGate)
        {
            string tempPath = GetTempPath(ConfigPath);
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);
            if (!TryLoadConfig(ConfigPath, out BotConfig config) && !TryLoadConfig(tempPath, out config))
            {
                if (hasConfig || hasTemp)
                    throw new InvalidDataException("config.json could not be read. Restore a backup or run setup again.");

                config = new BotConfig();
            }

            Normalize(config);
            ResetTransientStartMode(config);
            update(config);
            Normalize(config);
            SaveNoLock(config);
            return config;
        }
    }

    private static void SaveNoLock(BotConfig config)
    {
        string json = SerializeForStorage(config);
        string tempPath = GetTempPath(ConfigPath);
        string backupPath = ConfigPath + ".bak";

        if (ConfigFileAlreadyMatches(json))
        {
            TwitchCraftBot_V1.FileSystemHelper.TryDeleteFile(tempPath);
            return;
        }

        File.WriteAllText(tempPath, json, Utf8NoBom);
        TwitchCraftBot_V1.FileSystemHelper.ReplaceOrMoveWithFallback(tempPath, ConfigPath, backupPath, "Atomic config save failed; falling back to copy");
    }

    private static string SerializeForStorage(BotConfig config)
    {
        bool originalMultiplayerEnabled = config.Settings.MultiplayerEnabled;
        bool originalRemoteControlEnabled = config.Settings.RemoteControlEnabled;
        bool originalRequireOnlineMode = config.Settings.RequireOnlineMode;

        try
        {
            ResetTransientStartMode(config);
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
            config = JsonConvert.DeserializeObject<BotConfig>(text) ?? new BotConfig();
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to read config file", ex);
            return false;
        }
    }

    private static bool ConfigFileAlreadyMatches(string json)
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

    private static string GetTempPath(string path) => path + ".tmp";
}
