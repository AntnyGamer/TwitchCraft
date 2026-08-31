using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1.BotSetup;

public sealed class BotConfig
{
    public ServerConfig Server { get; set; } = new();
    public TwitchConfig Twitch { get; set; } = new();
    public BotIdentityConfig Identity { get; set; } = new();
    public StartingProfile Settings { get; set; } = new();
}

public sealed class ServerConfig
{
    public JavaConfig Java { get; set; } = new();
    public RCONConfig RCON { get; set; } = new();
    public string MinecraftVersion { get; set; } = string.Empty;
    public string ServerDirectory { get; set; } = string.Empty;
    public string JarPath { get; set; } = string.Empty;
    public string BindIP { get; set; } = "127.0.0.1";
    public string PreviousBindIP { get; set; } = string.Empty;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 1;
    public int MemoryMinGB { get; set; } = 8;
    public int MemoryMaxGB { get; set; } = 8;
}

public sealed class JavaConfig
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string HomeDirectory { get; set; } = string.Empty;
}

public sealed class RCONConfig
{
    public int Port { get; set; } = 25575;
    public string Password { get; set; } = string.Empty;
}

public sealed class TwitchConfig
{
    public string ClientID { get; set; } = string.Empty;
    public string BotToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string StreamerName { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
}

public sealed class BotIdentityConfig
{
    public string StreamerMinecraftName { get; set; } = string.Empty;
}

public sealed class StartingProfile
{
    internal const int DefaultAutomaticBackupRetentionCount = 3;

    public bool MultiplayerEnabled { get; set; }
    public bool MultiplayerPVPEnabled { get; set; }
    public bool RemoteControlEnabled { get; set; }
    public bool RequireOnlineMode { get; set; } = true;
    public bool HardcoreEnabled { get; set; } = true;
    public string Difficulty { get; set; } = "Medium";
    public bool MinigamesEnabled { get; set; } = true;
    public int MinigameCooldown { get; set; } = 15;
    public bool GlobalGameCommandCooldownEnabled { get; set; }
    public double GlobalGameCommandCooldownSeconds { get; set; } = 10.0;
    public bool PassiveTokenEarningEnabled { get; set; } = true;
    public bool AutomaticFollowRewardsEnabled { get; set; } = true;
    public int FollowRewardAmount { get; set; } = 100;
    public bool AutomaticBitRewardsEnabled { get; set; } = true;
    public double CommandCostMultiplier { get; set; } = 1.0;
    public string BotResponseVerbosity { get; set; } = "Normal";
    public bool NonCommandChatRelayEnabled { get; set; } = true;
    public bool ModeratorsCanUseStreamerCommands { get; set; }
    public bool StatisticsEnabled { get; set; } = true;
    public string CommandPrefix { get; set; } = "!";
    public string SecondaryCommandPrefix { get; set; } = string.Empty;
    public bool MentionViewersInBotReplies { get; set; } = true;
    public bool ShowExactCooldownRemaining { get; set; } = true;
    public bool RespondToUnknownCommands { get; set; }
    public bool ViewerCommandsPaused { get; set; }
    public int PassiveTokensPerPayout { get; set; } = 1;
    public int PassiveTokenPayoutMinimumSeconds { get; set; } = 30;
    public int PassiveTokenPayoutMaximumSeconds { get; set; } = 60;
    public int MaximumTokenBalance { get; set; }
    public bool PassiveRewardsRequireRecentChat { get; set; }
    public int ChannelCommandLimitPerMinute { get; set; }
    public bool AllowAllPlayerTarget { get; set; } = true;
    public bool AllowRandomPlayerTarget { get; set; } = true;
    public bool IncludeRelayTimestamps { get; set; }
    public string MinecraftRelayTextColor { get; set; } = "white";
    public bool ShowConnectionHealth { get; set; }
    public int ViewerCommandLimitPerMinute { get; set; }
    public int PassiveRecentChatWindowMinutes { get; set; } = 10;
    public bool AutomaticBackupsEnabled { get; set; } = true;
    public int AutomaticBackupIntervalHours { get; set; } = 24;
    public int AutomaticBackupRetentionCount { get; set; } = DefaultAutomaticBackupRetentionCount;
    public bool LowResourceModeEnabled { get; set; }
    public bool PauseUIUpdatesWhenMinimized { get; set; }
    public int MaxVisibleTwitchLogLines { get; set; } = 250;
    public int MaxVisibleMinecraftLogLines { get; set; } = 250;
    public int ViewerRosterRefreshIntervalSeconds { get; set; } = 30;
    public int MinecraftRelayMessagesPerSecond { get; set; }
    public int MaxGameplayCommandQueue { get; set; } = 75;
    public int RCONTimeoutSeconds { get; set; } = 5;
    public int GracefulShutdownTimeoutSeconds { get; set; } = 5;
    public int SQLiteOptimizeIntervalHours { get; set; }
    public int ViewDistance { get; set; } = 12;
    public int SimulationDistance { get; set; } = 10;
    public int EntityBroadcastRangePercentage { get; set; } = 100;
    public int NetworkCompressionThreshold { get; set; } = 256;
    public int EmptyServerShutdownDelayMinutes { get; set; }
    public Dictionary<string, CommandCustomization> CommandCustomizations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CommandCustomization
{
    public bool Enabled { get; set; } = true;
    public int? CooldownSeconds { get; set; }
    public double? GlobalCooldownSeconds { get; set; }
}
