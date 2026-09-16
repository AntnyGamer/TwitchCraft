using System;
using System.Threading.Tasks;
using System.Windows;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Settings
{
    private async void ResetCategory_Click(object sender, RoutedEventArgs e)
    {
        SettingsCategory category = _currentCategory;
        if (!ErrorHandling.ConfirmResetCategory(this, GetCategoryName(category)))
            return;

        StartingProfile defaults = new();
        ServerConfig defaultServer = new();
        CancelRAMSave();

        Action<TwitchCraftConfig>? beforeSave = category is SettingsCategory.Gameplay or SettingsCategory.Server
            ? ApplyLocalProfile
            : null;
        await SaveConfigAsync(
            config => ResetCategory(category, defaults, defaultServer, config),
            beforeSave: beforeSave,
            refreshMinigameLoops: category == SettingsCategory.Gameplay);

        ReloadAfterReset();
    }

    private void ReloadAfterReset()
    {
        try
        {
            TwitchCraftConfig saved = ConfigurationStore.Load();
            _initializing = true;
            LoadSettings(saved.Settings, saved.Server);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsError(this, ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private static string GetCategoryName(SettingsCategory category) => category switch
    {
        SettingsCategory.CustomCommands => "Custom Commands",
        SettingsCategory.ChatDisplay => "Chat and Display",
        SettingsCategory.Performance => "Performance and Data",
        SettingsCategory.Server => "Minecraft Server",
        _ => category.ToString()
    };

    private static void ResetCategory(
        SettingsCategory category,
        StartingProfile defaults,
        ServerConfig defaultServer,
        TwitchCraftConfig config)
    {
        StartingProfile settings = config.Settings;
        switch (category)
        {
            case SettingsCategory.Commands:
                settings.CommandPrefix = defaults.CommandPrefix;
                settings.SecondaryCommandPrefix = defaults.SecondaryCommandPrefix;
                settings.ViewerCommandsPaused = defaults.ViewerCommandsPaused;
                settings.ModeratorsCanUseStreamerCommands = defaults.ModeratorsCanUseStreamerCommands;
                settings.ViewerCommandLimitPerMinute = defaults.ViewerCommandLimitPerMinute;
                settings.ChannelCommandLimitPerMinute = defaults.ChannelCommandLimitPerMinute;
                settings.GlobalGameCommandCooldownEnabled = defaults.GlobalGameCommandCooldownEnabled;
                settings.GlobalGameCommandCooldownSeconds = defaults.GlobalGameCommandCooldownSeconds;
                settings.ShowExactCooldownRemaining = defaults.ShowExactCooldownRemaining;
                settings.BotResponseVerbosity = defaults.BotResponseVerbosity;
                settings.RespondToUnknownCommands = defaults.RespondToUnknownCommands;
                settings.MentionViewersInBotReplies = defaults.MentionViewersInBotReplies;
                break;

            case SettingsCategory.CustomCommands:
                settings.CommandCustomizations.Clear();
                break;

            case SettingsCategory.Economy:
                settings.PassiveTokenEarningEnabled = defaults.PassiveTokenEarningEnabled;
                settings.PassiveTokensPerPayout = defaults.PassiveTokensPerPayout;
                settings.PassiveTokenPayoutMinimumSeconds = defaults.PassiveTokenPayoutMinimumSeconds;
                settings.PassiveTokenPayoutMaximumSeconds = defaults.PassiveTokenPayoutMaximumSeconds;
                settings.PassiveRewardsRequireActivity = defaults.PassiveRewardsRequireActivity;
                settings.PassiveActivityWindowMinutes = defaults.PassiveActivityWindowMinutes;
                settings.MaximumTokenBalance = defaults.MaximumTokenBalance;
                settings.AutomaticFollowRewardsEnabled = defaults.AutomaticFollowRewardsEnabled;
                settings.FollowRewardAmount = defaults.FollowRewardAmount;
                settings.AutomaticBitRewardsEnabled = defaults.AutomaticBitRewardsEnabled;
                settings.CommandCostMultiplier = defaults.CommandCostMultiplier;
                break;

            case SettingsCategory.Gameplay:
                settings.MinigamesEnabled = defaults.MinigamesEnabled;
                settings.MinigameCooldown = defaults.MinigameCooldown;
                settings.HardcoreEnabled = defaults.HardcoreEnabled;
                settings.Difficulty = defaults.Difficulty;
                settings.MultiplayerPVPEnabled = defaults.MultiplayerPVPEnabled;
                settings.AllowAllPlayerTarget = defaults.AllowAllPlayerTarget;
                settings.AllowRandomPlayerTarget = defaults.AllowRandomPlayerTarget;
                break;

            case SettingsCategory.ChatDisplay:
                settings.NonCommandChatRelayEnabled = defaults.NonCommandChatRelayEnabled;
                settings.IncludeRelayTimestamps = defaults.IncludeRelayTimestamps;
                settings.MinecraftRelayTextColor = defaults.MinecraftRelayTextColor;
                settings.MinecraftRelayMessagesPerSecond = defaults.MinecraftRelayMessagesPerSecond;
                settings.ShowConnectionHealth = defaults.ShowConnectionHealth;
                break;

            case SettingsCategory.Performance:
                settings.LowResourceModeEnabled = defaults.LowResourceModeEnabled;
                settings.PauseUIUpdatesWhenMinimized = defaults.PauseUIUpdatesWhenMinimized;
                settings.ViewerRosterRefreshIntervalSeconds = defaults.ViewerRosterRefreshIntervalSeconds;
                settings.MaxVisibleTwitchLogLines = defaults.MaxVisibleTwitchLogLines;
                settings.MaxVisibleMinecraftLogLines = defaults.MaxVisibleMinecraftLogLines;
                settings.MaxGameplayCommandQueue = defaults.MaxGameplayCommandQueue;
                settings.StatisticsEnabled = defaults.StatisticsEnabled;
                settings.SQLiteOptimizeIntervalHours = defaults.SQLiteOptimizeIntervalHours;
                settings.AutomaticBackupsEnabled = defaults.AutomaticBackupsEnabled;
                settings.AutomaticBackupIntervalHours = defaults.AutomaticBackupIntervalHours;
                settings.AutomaticBackupRetentionCount = defaults.AutomaticBackupRetentionCount;
                break;

            case SettingsCategory.Server:
                settings.ViewDistance = defaults.ViewDistance;
                settings.SimulationDistance = defaults.SimulationDistance;
                settings.EntityBroadcastRangePercentage = defaults.EntityBroadcastRangePercentage;
                settings.NetworkCompressionThreshold = defaults.NetworkCompressionThreshold;
                settings.WhitelistEnabled = defaults.WhitelistEnabled;
                settings.RCONTimeoutSeconds = defaults.RCONTimeoutSeconds;
                settings.GracefulShutdownTimeoutSeconds = defaults.GracefulShutdownTimeoutSeconds;
                settings.EmptyServerShutdownDelayMinutes = defaults.EmptyServerShutdownDelayMinutes;
                break;

            case SettingsCategory.Dangerous:
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
                break;
        }
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppHelpers.GetTwitchCraftWindow(this) is null || !ErrorHandling.ConfirmResetDefaults(this))
                return;

            CancelRAMSave();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsError(this, ex);
            return;
        }

        ServerConfig defaultServer = new();
        await SaveConfigAsync(
            config =>
            {
                config.Settings = new StartingProfile();
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
            },
            beforeSave: ApplyLocalProfile,
            refreshMinigameLoops: true);

        ReloadAfterReset();
    }

    private Task UpdateBoolAsync(bool enabled, Action<TwitchCraftConfig, bool> update, bool refreshMinigameLoops = false)
        => _initializing
            ? Task.CompletedTask
            : SaveConfigAsync(config => update(config, enabled), refreshMinigameLoops: refreshMinigameLoops);

    private async Task SaveConfigAsync(Action<TwitchCraftConfig> update, Action<TwitchCraftConfig>? beforeSave = null, bool refreshMinigameLoops = false)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
            if (parent is null)
            {
                return;
            }

            bool activeMultiplayerEnabled = parent.Runtime.MultiplayerEnabled;
            bool activeRemoteControlEnabled = parent.Runtime.RemoteControlEnabled;
            bool activeRequireOnlineMode = parent.Runtime.RequireOnlineMode;

            TwitchCraftConfig savedConfig = await Task.Run(() => ConfigurationStore.Update(config =>
            {
                update(config);
                config.Settings.MultiplayerEnabled = activeMultiplayerEnabled;
                config.Settings.RemoteControlEnabled = activeRemoteControlEnabled;
                config.Settings.RequireOnlineMode = activeRequireOnlineMode;
                beforeSave?.Invoke(config);
            }));

            await parent.Runtime.ApplySettingsAsync(savedConfig, refreshMinigameLoops, preserveTwitchAuth: true);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSaveSettingsError(this, ex);
            ReloadSettings();
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }
}
