using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TwitchCraft_V1.Setup;

public sealed partial class ConfigurationStore
{
    private const string AppFolderName = "TwitchCraft";
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

    private static readonly Lock IOGate = new();
    private static bool _writesDisabled;
    private static readonly UTF8Encoding UTF8NoBOM = new(false);
    private static readonly string WorkingDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);
    private static readonly string ConfigPathValue = Path.Combine(WorkingDirectoryPath, ConfigFileName);
    private static readonly string ConfigTempPathValue = ConfigPathValue + ".tmp";
    private static readonly string ViewerTokensPathValue = Path.Combine(WorkingDirectoryPath, ViewerTokensFileName);
    private static readonly string BackupsDirectoryPath = Path.Combine(WorkingDirectoryPath, BackupsFolderName);
    private static readonly string LogsDirectoryPath = Path.Combine(WorkingDirectoryPath, "logs");

    public static string WorkingDirectory => WorkingDirectoryPath;

    public static string ConfigPath => ConfigPathValue;

    public static string ViewerTokensPath => ViewerTokensPathValue;

    public static string BackupsDirectory => BackupsDirectoryPath;

    public static string LogsDirectory => LogsDirectoryPath;

    public static void EnsureWorkDir() => Directory.CreateDirectory(WorkingDirectory);

    public static bool HasConfig()
    {
        if (File.Exists(ConfigPath))
            return true;

        lock (IOGate)
        {
            return TryLoadConfig(ConfigTempPathValue, out _);
        }
    }

    public static void DeleteConfigFiles()
    {
        lock (IOGate)
        {
            File.Delete(ConfigPath);
            File.Delete(ConfigTempPathValue);
            _writesDisabled = true;
        }
    }

    public static TwitchCraftConfig Load()
    {
        EnsureWorkDir();

        lock (IOGate)
        {
            string tempPath = ConfigTempPathValue;
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);

            if (!hasConfig && !hasTemp)
                return new TwitchCraftConfig();

            if (TryLoadConfig(ConfigPath, out TwitchCraftConfig loaded))
            {
                Normalize(loaded);
                ResetStartMode(loaded);
                return loaded;
            }

            if (TryLoadConfig(tempPath, out loaded))
            {
                Normalize(loaded);
                ResetStartMode(loaded);
                SaveNoLock(loaded);
                return loaded;
            }
        }

        throw new InvalidDataException("config.json could not be read. Restore an automatic backup or run setup again.");
    }

    public static void NormalizeRuntime(TwitchCraftConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Normalize(config);
    }

    internal static TwitchCraftConfig Clone(TwitchCraftConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new TwitchCraftConfig
        {
            Server = new ServerConfig
            {
                Java = new JavaConfig
                {
                    ExecutablePath = source.Server.Java.ExecutablePath,
                    HomeDirectory = source.Server.Java.HomeDirectory
                },
                RCON = new RCONConfig
                {
                    Port = source.Server.RCON.Port,
                    Password = source.Server.RCON.Password
                },
                MinecraftVersion = source.Server.MinecraftVersion,
                ServerDirectory = source.Server.ServerDirectory,
                JarPath = source.Server.JarPath,
                BindIP = source.Server.BindIP,
                PreviousBindIP = source.Server.PreviousBindIP,
                RemoteHost = source.Server.RemoteHost,
                Port = source.Server.Port,
                MaxPlayers = source.Server.MaxPlayers,
                MemoryMinGB = source.Server.MemoryMinGB,
                MemoryMaxGB = source.Server.MemoryMaxGB
            },
            Twitch = new TwitchConfig
            {
                ClientID = source.Twitch.ClientID,
                BotToken = source.Twitch.BotToken,
                RefreshToken = source.Twitch.RefreshToken,
                StreamerName = source.Twitch.StreamerName,
                BotName = source.Twitch.BotName
            },
            Identity = new IdentityConfig
            {
                StreamerMinecraftName = source.Identity.StreamerMinecraftName
            },
            Settings = new StartingProfile
            {
                MultiplayerEnabled = source.Settings.MultiplayerEnabled,
                MultiplayerPVPEnabled = source.Settings.MultiplayerPVPEnabled,
                WhitelistEnabled = source.Settings.WhitelistEnabled,
                RemoteControlEnabled = source.Settings.RemoteControlEnabled,
                HardcoreEnabled = source.Settings.HardcoreEnabled,
                Difficulty = source.Settings.Difficulty,
                RequireOnlineMode = source.Settings.RequireOnlineMode,
                MinigamesEnabled = source.Settings.MinigamesEnabled,
                MinigameCooldown = source.Settings.MinigameCooldown,
                PassiveTokenEarningEnabled = source.Settings.PassiveTokenEarningEnabled,
                AutomaticFollowRewardsEnabled = source.Settings.AutomaticFollowRewardsEnabled,
                FollowRewardAmount = source.Settings.FollowRewardAmount,
                AutomaticBitRewardsEnabled = source.Settings.AutomaticBitRewardsEnabled,
                CommandCostMultiplier = source.Settings.CommandCostMultiplier,
                BotResponseVerbosity = source.Settings.BotResponseVerbosity,
                NonCommandChatRelayEnabled = source.Settings.NonCommandChatRelayEnabled,
                ModeratorsCanUseStreamerCommands = source.Settings.ModeratorsCanUseStreamerCommands,
                GlobalGameCommandCooldownEnabled = source.Settings.GlobalGameCommandCooldownEnabled,
                GlobalGameCommandCooldownSeconds = source.Settings.GlobalGameCommandCooldownSeconds,
                StatisticsEnabled = source.Settings.StatisticsEnabled,
                CommandPrefix = source.Settings.CommandPrefix,
                SecondaryCommandPrefix = source.Settings.SecondaryCommandPrefix,
                MentionViewersInBotReplies = source.Settings.MentionViewersInBotReplies,
                ShowExactCooldownRemaining = source.Settings.ShowExactCooldownRemaining,
                RespondToUnknownCommands = source.Settings.RespondToUnknownCommands,
                ViewerCommandsPaused = source.Settings.ViewerCommandsPaused,
                PassiveTokensPerPayout = source.Settings.PassiveTokensPerPayout,
                PassiveTokenPayoutMinimumSeconds = source.Settings.PassiveTokenPayoutMinimumSeconds,
                PassiveTokenPayoutMaximumSeconds = source.Settings.PassiveTokenPayoutMaximumSeconds,
                MaximumTokenBalance = source.Settings.MaximumTokenBalance,
                PassiveRewardsRequireActivity = source.Settings.PassiveRewardsRequireActivity,
                ChannelCommandLimitPerMinute = source.Settings.ChannelCommandLimitPerMinute,
                AllowAllPlayerTarget = source.Settings.AllowAllPlayerTarget,
                AllowRandomPlayerTarget = source.Settings.AllowRandomPlayerTarget,
                IncludeRelayTimestamps = source.Settings.IncludeRelayTimestamps,
                MinecraftRelayTextColor = source.Settings.MinecraftRelayTextColor,
                ShowConnectionHealth = source.Settings.ShowConnectionHealth,
                ViewerCommandLimitPerMinute = source.Settings.ViewerCommandLimitPerMinute,
                PassiveActivityWindowMinutes = source.Settings.PassiveActivityWindowMinutes,
                AutomaticBackupsEnabled = source.Settings.AutomaticBackupsEnabled,
                AutomaticBackupIntervalHours = source.Settings.AutomaticBackupIntervalHours,
                AutomaticBackupRetentionCount = source.Settings.AutomaticBackupRetentionCount,
                LowResourceModeEnabled = source.Settings.LowResourceModeEnabled,
                PauseUIUpdatesWhenMinimized = source.Settings.PauseUIUpdatesWhenMinimized,
                MaxVisibleTwitchLogLines = source.Settings.MaxVisibleTwitchLogLines,
                MaxVisibleMinecraftLogLines = source.Settings.MaxVisibleMinecraftLogLines,
                ViewerRosterRefreshIntervalSeconds = source.Settings.ViewerRosterRefreshIntervalSeconds,
                MinecraftRelayMessagesPerSecond = source.Settings.MinecraftRelayMessagesPerSecond,
                MaxGameplayCommandQueue = source.Settings.MaxGameplayCommandQueue,
                RCONTimeoutSeconds = source.Settings.RCONTimeoutSeconds,
                GracefulShutdownTimeoutSeconds = source.Settings.GracefulShutdownTimeoutSeconds,
                SQLiteOptimizeIntervalHours = source.Settings.SQLiteOptimizeIntervalHours,
                ViewDistance = source.Settings.ViewDistance,
                SimulationDistance = source.Settings.SimulationDistance,
                EntityBroadcastRangePercentage = source.Settings.EntityBroadcastRangePercentage,
                NetworkCompressionThreshold = source.Settings.NetworkCompressionThreshold,
                EmptyServerShutdownDelayMinutes = source.Settings.EmptyServerShutdownDelayMinutes,
                CommandCustomizations = CloneCommands(source.Settings.CommandCustomizations)
            }
        };
    }

    private static Dictionary<string, CommandCustomization> CloneCommands(
        Dictionary<string, CommandCustomization>? source)
    {
        Dictionary<string, CommandCustomization> result = new(source?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach ((string name, CommandCustomization customization) in source)
            result[name] = new CommandCustomization
            {
                Enabled = customization.Enabled,
                CooldownSeconds = customization.CooldownSeconds,
                GlobalCooldownSeconds = customization.GlobalCooldownSeconds
            };
        return result;
    }

    public static void Save(TwitchCraftConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Normalize(config);
        EnsureWorkDir();

        lock (IOGate)
        {
            if (_writesDisabled) throw new OperationCanceledException("Configuration reset is in progress.");
            SaveNoLock(config);
        }
    }

    public static TwitchCraftConfig Update(Action<TwitchCraftConfig> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        EnsureWorkDir();

        lock (IOGate)
        {
            if (_writesDisabled) throw new OperationCanceledException("Configuration reset is in progress.");
            string tempPath = ConfigTempPathValue;
            bool hasConfig = File.Exists(ConfigPath);
            bool hasTemp = File.Exists(tempPath);
            if (!TryLoadConfig(ConfigPath, out TwitchCraftConfig config) && !TryLoadConfig(tempPath, out config))
            {
                if (hasConfig || hasTemp)
                    throw new InvalidDataException("config.json could not be read. Restore an automatic backup or run setup again.");

                config = new TwitchCraftConfig();
            }

            Normalize(config);
            ResetStartMode(config);
            update(config);
            Normalize(config);
            SaveNoLock(config);
            return config;
        }
    }

    private static void SaveNoLock(TwitchCraftConfig config)
    {
        string json = SerializeConfig(config);
        string tempPath = ConfigTempPathValue;

        if (ConfigMatches(json))
        {
            TwitchCraft_V1.FileSystemHelper.DeleteFileSafe(tempPath);
            return;
        }

        File.WriteAllText(tempPath, json, UTF8NoBOM);
        TwitchCraft_V1.FileSystemHelper.ReplaceFile(tempPath, ConfigPath, null, "Atomic config save failed; falling back to copy");
    }

    private static string SerializeConfig(TwitchCraftConfig config)
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

    private static bool TryLoadConfig(string path, out TwitchCraftConfig config)
    {
        config = null!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            TwitchCraftConfig? loaded = JsonConvert.DeserializeObject<TwitchCraftConfig>(text, JsonSettings);
            if (loaded == null)
                return false;
            config = loaded;
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraft_V1.ErrorHandling.LogNonFatal("Failed to read config file", ex);
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
            TwitchCraft_V1.ErrorHandling.LogNonFatal("Failed to compare existing config file", ex);
            return false;
        }
    }

    internal static bool TryCopyConfig(string destinationPath)
    {
        try
        {
            lock (IOGate)
            {
                if (!File.Exists(ConfigPath))
                    return false;
                TwitchCraft_V1.FileSystemHelper.EnsureParentDir(destinationPath);
                File.Copy(ConfigPath, destinationPath, overwrite: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            TwitchCraft_V1.ErrorHandling.LogNonFatal("Failed to back up config", ex);
            return false;
        }
    }
}
