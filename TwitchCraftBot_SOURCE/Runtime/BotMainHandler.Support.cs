using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    // ===== Active config helpers =====

    private static BotConfig CloneConfig(BotConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new BotConfig
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
            Identity = new BotIdentityConfig
            {
                StreamerMinecraftName = source.Identity.StreamerMinecraftName
            },
            Settings = new StartingProfile
            {
                MultiplayerEnabled = source.Settings.MultiplayerEnabled,
                MultiplayerPVPEnabled = source.Settings.MultiplayerPVPEnabled,
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
                PassiveRewardsRequireRecentChat = source.Settings.PassiveRewardsRequireRecentChat,
                ChannelCommandLimitPerMinute = source.Settings.ChannelCommandLimitPerMinute,
                AllowAllPlayerTarget = source.Settings.AllowAllPlayerTarget,
                AllowRandomPlayerTarget = source.Settings.AllowRandomPlayerTarget,
                IncludeRelayTimestamps = source.Settings.IncludeRelayTimestamps,
                MinecraftRelayTextColor = source.Settings.MinecraftRelayTextColor,
                ShowConnectionHealth = source.Settings.ShowConnectionHealth,
                ViewerCommandLimitPerMinute = source.Settings.ViewerCommandLimitPerMinute,
                PassiveRecentChatWindowMinutes = source.Settings.PassiveRecentChatWindowMinutes,
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
                CommandCustomizations = CloneCommandCustomizations(source.Settings.CommandCustomizations)
            }
        };
    }

    private static Dictionary<string, CommandCustomization> CloneCommandCustomizations(
        Dictionary<string, CommandCustomization>? source)
    {
        Dictionary<string, CommandCustomization> result = new(source?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        if (source == null)
            return result;

        foreach ((string name, CommandCustomization customization) in source)
            result[name] = new CommandCustomization
            {
                Enabled = customization.Enabled,
                CooldownSeconds = customization.CooldownSeconds
            };
        return result;
    }

    public async Task ApplySavedConfigAsync(BotConfig config, bool refreshMinigameLoops = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            BotConfig activeConfig = CloneConfig(config);
            ConfigurationStore.NormalizeForRuntime(activeConfig);
            bool minigamesEnabledChanged = false;
            bool passiveScheduleChanged = false;

            if (_activeConfig != null)
            {
                minigamesEnabledChanged = _activeConfig.Settings.MinigamesEnabled != activeConfig.Settings.MinigamesEnabled;
                passiveScheduleChanged =
                    _activeConfig.Settings.PassiveTokenPayoutMinimumSeconds != activeConfig.Settings.PassiveTokenPayoutMinimumSeconds ||
                    _activeConfig.Settings.PassiveTokenPayoutMaximumSeconds != activeConfig.Settings.PassiveTokenPayoutMaximumSeconds ||
                    _activeConfig.Settings.PassiveRewardsRequireRecentChat != activeConfig.Settings.PassiveRewardsRequireRecentChat ||
                    _activeConfig.Settings.PassiveRecentChatWindowMinutes != activeConfig.Settings.PassiveRecentChatWindowMinutes;
                activeConfig.Settings.MultiplayerEnabled = _activeConfig.Settings.MultiplayerEnabled;
                activeConfig.Settings.RemoteControlEnabled = _activeConfig.Settings.RemoteControlEnabled;
                activeConfig.Settings.RequireOnlineMode = _activeConfig.Settings.RequireOnlineMode;
            }

            SetActiveConfig(activeConfig);

            lock (_cooldownGate)
                _customCommandLastUsedTicks.Clear();

            if (!activeConfig.Settings.GlobalGameCommandCooldownEnabled)
                ClearGlobalGameCommandCooldown();

            if (passiveScheduleChanged)
            {
                lock (_viewerGate)
                    _viewerRewardSchedule.Clear();
            }

            if (refreshMinigameLoops || minigamesEnabledChanged)
            {
                RefreshMinigameLoopsForSetting(activeConfig.Settings.MinigamesEnabled);
            }

        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Failed to apply saved settings", ex));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void RefreshMinigameLoopsForSetting(bool minigamesEnabled)
    {
        if (_runtimeState != RuntimeState.Running)
        {
            return;
        }

        if (!minigamesEnabled)
        {
            MinigameManager.StopMinigameLoops(this);
            return;
        }

        CancellationToken token = _sessionCts?.Token ?? CancellationToken.None;
        if (token != CancellationToken.None)
        {
            MinigameManager.StartMinigameLoops(this, token);
        }
    }

    // ===== Token balance helpers =====

    public int GetTokens(string user) => _tokenStore.GetBalance(user);

    internal IReadOnlyList<KeyValuePair<string, int>> GetTopTokenBalances(int limit)
        => _tokenStore.GetTopBalances(limit);

    internal TokenRankResult? GetTokenRank(string user)
        => _tokenStore.GetRank(user);

    internal void CloseTokenStoreConnection() => _tokenStore.CloseConnection();

    public bool TrySpendTokens(string user, int amount)
        => amount > 0 && _tokenStore.TrySpendNow(user, amount);

    public int AdjustTokens(string user, int delta)
        => delta == 0 ? 0 : _tokenStore.AdjustBalance(user, delta);

    public int AdjustTokens(IEnumerable<string> users, int delta)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (delta == 0 || IsEmptyCollection(users))
        {
            return 0;
        }

        return _tokenStore.AdjustBalances(users, delta);
    }

    public void AdjustTokens(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (IsEmptyCollection(changes))
        {
            return;
        }

        _tokenStore.AdjustBalances(changes);
    }

    public int AwardTokens(string user, int amount)
        => amount <= 0 ? 0 : _tokenStore.AdjustBalance(user, amount, MaximumTokenBalance);

    public int AwardTokens(IEnumerable<string> users, int amount)
    {
        ArgumentNullException.ThrowIfNull(users);
        return amount <= 0 || IsEmptyCollection(users)
            ? 0
            : _tokenStore.AdjustBalances(users, amount, MaximumTokenBalance);
    }

    public void AwardTokens(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!IsEmptyCollection(changes))
            _tokenStore.AdjustBalances(changes, MaximumTokenBalance);
    }

    private static bool IsEmptyCollection<T>(IEnumerable<T> values)
        => values is ICollection<T> { Count: 0 } || values is IReadOnlyCollection<T> { Count: 0 };

    private void RefreshCatalogLists()
    {
        string version = CurrentMinecraftVersion;
        _mobList = TwitchCraftCatalogs.BuildMobList(version);
        _lootList = TwitchCraftCatalogs.BuildLootList(version);
    }

    private enum RuntimeState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }
}
