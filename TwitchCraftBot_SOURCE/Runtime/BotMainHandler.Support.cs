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

    public async Task ApplySettingsAsync(BotConfig config, bool refreshMinigameLoops = false, bool preserveTwitchAuth = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            BotConfig activeConfig = CloneConfig(config);
            ConfigurationStore.NormalizeRuntime(activeConfig);
            bool minigamesEnabledChanged = false, passiveScheduleChanged = false, followRewardsChanged = false, twitchAuthChanged = false, maximumBalanceNeedsClamp = false;

            lock (_configPersistenceGate)
            {
                if (_activeConfig != null)
                {
                    minigamesEnabledChanged = _activeConfig.Settings.MinigamesEnabled != activeConfig.Settings.MinigamesEnabled;
                    followRewardsChanged = _activeConfig.Settings.AutomaticFollowRewardsEnabled != activeConfig.Settings.AutomaticFollowRewardsEnabled;
                    twitchAuthChanged = !preserveTwitchAuth && !string.Equals(NormalizeToken(_activeConfig.Twitch.BotToken), NormalizeToken(activeConfig.Twitch.BotToken), StringComparison.Ordinal);
                    maximumBalanceNeedsClamp = activeConfig.Settings.MaximumTokenBalance > 0 && (_activeConfig.Settings.MaximumTokenBalance == 0 || activeConfig.Settings.MaximumTokenBalance < _activeConfig.Settings.MaximumTokenBalance);
                    passiveScheduleChanged =
                        _activeConfig.Settings.PassiveTokenPayoutMinimumSeconds != activeConfig.Settings.PassiveTokenPayoutMinimumSeconds ||
                        _activeConfig.Settings.PassiveTokenPayoutMaximumSeconds != activeConfig.Settings.PassiveTokenPayoutMaximumSeconds ||
                        _activeConfig.Settings.PassiveRewardsRequireRecentChat != activeConfig.Settings.PassiveRewardsRequireRecentChat ||
                        _activeConfig.Settings.PassiveRecentChatWindowMinutes != activeConfig.Settings.PassiveRecentChatWindowMinutes;
                    activeConfig.Settings.MultiplayerEnabled = _activeConfig.Settings.MultiplayerEnabled;
                    activeConfig.Settings.RemoteControlEnabled = _activeConfig.Settings.RemoteControlEnabled;
                    activeConfig.Settings.RequireOnlineMode = _activeConfig.Settings.RequireOnlineMode;
                    if (preserveTwitchAuth)
                    {
                        activeConfig.Twitch.BotToken = _activeConfig.Twitch.BotToken;
                        activeConfig.Twitch.RefreshToken = _activeConfig.Twitch.RefreshToken;
                        activeConfig.Twitch.BotName = _activeConfig.Twitch.BotName;
                    }
                }

                SetConfig(activeConfig);
            }

            if (!activeConfig.Settings.GlobalGameCommandCooldownEnabled)
                Commands.ClearGlobalCooldown();
            if (maximumBalanceNeedsClamp && _runtimeState == RuntimeState.Running) Tokens.ApplyMaximumBalance(activeConfig.Settings.MaximumTokenBalance);
            if (twitchAuthChanged) CloseIrcSocket();
            if (twitchAuthChanged || followRewardsChanged) await RefreshFollowRewardsAsync().ConfigureAwait(false);

            if (passiveScheduleChanged)
            {
                lock (_viewerGate)
                {
                    _viewerRewardSchedule.Clear();
                    if (!activeConfig.Settings.PassiveRewardsRequireRecentChat)
                        _viewerLastChatActivity.Clear();
                }
            }

            if (refreshMinigameLoops || minigamesEnabledChanged)
            {
                RefreshMinigames(activeConfig.Settings.MinigamesEnabled);
            }

        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void RefreshMinigames(bool minigamesEnabled)
    {
        if (_runtimeState != RuntimeState.Running)
        {
            return;
        }

        if (!minigamesEnabled)
        {
            MinigameManager.StopLoops(this);
            return;
        }

        CancellationToken token = _sessionCts?.Token ?? CancellationToken.None;
        if (token != CancellationToken.None)
        {
            MinigameManager.StartLoops(this, token);
        }
    }

    private async Task RefreshFollowRewardsAsync()
    {
        CancellationTokenSource? oldCts = _followRewardsCts; Task? oldTask = _followRewardsTask;
        _followRewardsCts = null; _followRewardsTask = null; oldCts?.Cancel();
        if (oldTask != null) await oldTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        oldCts?.Dispose();
        if (!AutomaticFollowRewardsEnabled || _runtimeState != RuntimeState.Running || _sessionCts == null) return;
        _followRewardsCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
        _followRewardsTask = RunFollowRewardsAsync(_followRewardsCts.Token); TrackTask(_followRewardsTask);
    }

    private void RefreshCatalogs()
    {
        string version = CurrentMinecraftVersion;
        _mobList = Catalogs.BuildMobs(version);
        _lootList = Catalogs.BuildLoot(version);
    }

    private enum RuntimeState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }
}
