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
    public string StreamerName { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
}

public sealed class BotIdentityConfig
{
    public string StreamerMinecraftName { get; set; } = string.Empty;
}

public sealed class StartingProfile
{
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
    public bool NonCommandChatTellrawsEnabled { get; set; } = true;
    public bool ModeratorsCanUseStreamerCommands { get; set; }
    public bool StatisticsEnabled { get; set; } = true;
}
