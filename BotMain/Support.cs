using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    // ===== Active configuration helpers =====

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
                NonCommandChatTellrawsEnabled = source.Settings.NonCommandChatTellrawsEnabled,
                ModeratorsCanUseStreamerCommands = source.Settings.ModeratorsCanUseStreamerCommands,
                GlobalGameCommandCooldownEnabled = source.Settings.GlobalGameCommandCooldownEnabled,
                GlobalGameCommandCooldownSeconds = source.Settings.GlobalGameCommandCooldownSeconds,
                StatisticsEnabled = source.Settings.StatisticsEnabled
            }
        };
    }

    public async Task ApplySavedConfigAsync(BotConfig config, bool refreshMinigameLoops = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            BotConfig activeConfig = CloneConfig(config);
            bool minigamesEnabledChanged = false;

            if (_activeConfig != null)
            {
                minigamesEnabledChanged = _activeConfig.Settings.MinigamesEnabled != activeConfig.Settings.MinigamesEnabled;
                activeConfig.Settings.MultiplayerEnabled = _activeConfig.Settings.MultiplayerEnabled;
                activeConfig.Settings.RemoteControlEnabled = _activeConfig.Settings.RemoteControlEnabled;
                activeConfig.Settings.RequireOnlineMode = _activeConfig.Settings.RequireOnlineMode;
            }

            SetActiveConfig(activeConfig);

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

    public int GetTokens(string user)
    {
        string normalized = NormalizeUser(user);
        return normalized.Length == 0 ? 0 : _tokenStore.GetBalance(normalized);
    }

    public bool TrySpendTokens(string user, int amount)
    {
        string normalized = NormalizeUser(user);
        return normalized.Length != 0 && amount > 0 && _tokenStore.TrySpendNow(normalized, amount);
    }

    public void AdjustTokens(string user, int delta)
    {
        string normalized = NormalizeUser(user);
        if (normalized.Length == 0 || delta == 0)
        {
            return;
        }

        _tokenStore.AdjustBalance(normalized, delta);
    }

    public void AdjustTokens(IEnumerable<string> users, int delta)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (delta == 0 || IsEmptyCollection(users))
        {
            return;
        }

        _tokenStore.AdjustBalances(users, delta);
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

    private static bool IsEmptyCollection<T>(IEnumerable<T> values)
        => values is ICollection<T> { Count: 0 } || values is IReadOnlyCollection<T> { Count: 0 };

    // ===== Shared catalogs =====

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
