using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

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

public sealed class ConfigurationStore
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

    public static string NormalizeBindIP(string? value)
    {
        string text = CleanText(value);
        return string.Equals(text, "localhost", StringComparison.OrdinalIgnoreCase) ? DefaultBindIP : text;
    }

    public static bool IsValidBindIP(string? value)
    {
        string text = NormalizeBindIP(value);
        return text.Length > 0 && IPAddress.TryParse(text, out _);
    }

    public static bool IsValidRemoteHost(string? value)
    {
        string host = CleanText(value);
        return TryNormalizeRemoteHost(host, out _);
    }

    private static bool TryNormalizeRemoteHost(string host, out string normalizedHost)
    {
        normalizedHost = string.Empty;
        if (host.Length == 0 || host.Length > 300)
            return false;

        for (int i = 0; i < host.Length; i++)
        {
            char c = host[i];
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '\\')
                return false;
        }

        if (!TryRemoveOptionalRemotePort(host, out string hostOnly))
            return false;

        if (hostOnly.EndsWith('.'))
            hostOnly = hostOnly[..^1];

        if (hostOnly.Length == 0 || hostOnly.Length > 253)
            return false;

        if (IPAddress.TryParse(hostOnly, out _))
        {
            normalizedHost = hostOnly;
            return true;
        }

        if (hostOnly.Contains(':'))
            return false;

        int labelLength = 0;
        for (int i = 0; i < hostOnly.Length; i++)
        {
            char c = hostOnly[i];
            if (c == '.')
            {
                if (labelLength == 0 || labelLength > 63)
                    return false;

                labelLength = 0;
                continue;
            }

            if (c != '-' && c != '_' && !char.IsAsciiLetterOrDigit(c))
                return false;

            labelLength++;
        }

        if (labelLength == 0 || labelLength > 63)
            return false;

        normalizedHost = hostOnly;
        return true;
    }

    private static bool TryRemoveOptionalRemotePort(string host, out string hostOnly)
    {
        hostOnly = host;

        if (host.StartsWith('['))
        {
            int bracketEnd = host.IndexOf(']');
            if (bracketEnd <= 1)
                return false;

            string address = host[1..bracketEnd];
            string remainder = host[(bracketEnd + 1)..];
            if (remainder.Length > 0)
            {
                if (!remainder.StartsWith(':') || !IsValidPortText(remainder.AsSpan(1)))
                    return false;
            }

            hostOnly = address;
            return IPAddress.TryParse(hostOnly, out _);
        }

        int colonIndex = host.LastIndexOf(':');
        if (colonIndex > 0 && host.AsSpan(0, colonIndex).IndexOf(':') < 0)
        {
            ReadOnlySpan<char> portText = host.AsSpan(colonIndex + 1);
            if (IsValidPortText(portText))
                hostOnly = host[..colonIndex];
        }

        return true;
    }

    private static bool IsValidPortText(ReadOnlySpan<char> portText)
        => int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && IsValidPort(port);

    public static bool ShouldShowAdvancedBindIPWarning(string? value)
    {
        if (!IPAddress.TryParse(CleanText(value), out IPAddress? IP))
            return true;

        byte[] bytes = IP.GetAddressBytes();
        return bytes.Length != 4 || bytes[0] == 25 || bytes[0] == 26 || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
    }

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
            if (!TryLoadConfig(ConfigPath, out BotConfig config) && !TryLoadConfig(tempPath, out config))
                config = new BotConfig();

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

    private static void ResetTransientStartMode(BotConfig config)
    {
        config.Settings.MultiplayerEnabled = false;
        config.Settings.RemoteControlEnabled = false;
        config.Settings.RequireOnlineMode = true;
    }

    private static void Normalize(BotConfig config)
    {
        config.Server ??= new ServerConfig();
        config.Server.Java ??= new JavaConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Twitch ??= new TwitchConfig();
        config.Identity ??= new BotIdentityConfig();
        config.Settings ??= new StartingProfile();

        config.Server.BindIP = NormalizeBindIP(config.Server.BindIP);
        if (!IsValidBindIP(config.Server.BindIP))
            config.Server.BindIP = DefaultBindIP;

        if (!IsValidPort(config.Server.Port))
            config.Server.Port = DefaultServerPort;

        if (config.Server.MaxPlayers <= 0)
            config.Server.MaxPlayers = DefaultMaxPlayers;

        if (config.Server.MemoryMinGB <= 0)
            config.Server.MemoryMinGB = DefaultMemoryGB;

        if (config.Server.MemoryMaxGB <= 0)
            config.Server.MemoryMaxGB = DefaultMemoryGB;

        if (config.Server.MemoryMinGB > config.Server.MemoryMaxGB)
            config.Server.MemoryMinGB = config.Server.MemoryMaxGB;

        if (!IsValidPort(config.Server.RCON.Port))
            config.Server.RCON.Port = DefaultRCONPort;

        NormalizeRemoteHostAndPort(config.Server);

        if (config.Settings.MinigameCooldown < 2 || config.Settings.MinigameCooldown > 30)
            config.Settings.MinigameCooldown = DefaultMinigameCooldown;

        if (double.IsNaN(config.Settings.GlobalGameCommandCooldownSeconds) ||
            config.Settings.GlobalGameCommandCooldownSeconds < 0.1 ||
            config.Settings.GlobalGameCommandCooldownSeconds > 120.0)
        {
            config.Settings.GlobalGameCommandCooldownSeconds = DefaultGlobalGameCommandCooldownSeconds;
        }

        config.Settings.Difficulty = NormalizeDifficulty(config.Settings.Difficulty);

        config.Server.Java.ExecutablePath = CleanText(config.Server.Java.ExecutablePath);
        config.Server.Java.HomeDirectory = CleanText(config.Server.Java.HomeDirectory);
        config.Server.RCON.Password = NormalizeRconPassword(config.Server.RCON.Password);
        config.Server.MinecraftVersion = CleanText(config.Server.MinecraftVersion);
        config.Server.ServerDirectory = CleanText(config.Server.ServerDirectory);
        config.Server.PreviousBindIP = NormalizeBindIP(config.Server.PreviousBindIP);
        if (config.Server.PreviousBindIP.Length > 0 && !IsValidBindIP(config.Server.PreviousBindIP))
            config.Server.PreviousBindIP = string.Empty;
        config.Server.JarPath = CleanText(config.Server.JarPath);
        config.Twitch.StreamerName = CleanText(config.Twitch.StreamerName);
        config.Twitch.BotName = CleanText(config.Twitch.BotName);
        config.Twitch.BotToken = TwitchCraftBot_V1.TwitchTokenHelper.NormalizeAccessToken(config.Twitch.BotToken);
        config.Twitch.ClientID = CleanText(config.Twitch.ClientID);
        config.Identity.StreamerMinecraftName = CleanText(config.Identity.StreamerMinecraftName);
    }

    private static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

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

    private static void NormalizeRemoteHostAndPort(ServerConfig server)
    {
        string host = CleanText(server.RemoteHost);
        int bracketPortIndex = host.IndexOf("]:", StringComparison.Ordinal);
        if (host.StartsWith('[') && bracketPortIndex > 0)
        {
            string portText = host[(bracketPortIndex + 2)..];
            if (int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int bracketPort) && IsValidPort(bracketPort))
            {
                server.RCON.Port = bracketPort;
                host = host[1..bracketPortIndex];
            }
        }
        else
        {
            int colonIndex = host.LastIndexOf(':');
            if (colonIndex > 0 && host.IndexOf(':') == colonIndex)
            {
                string portText = host[(colonIndex + 1)..];
                if (int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort) && IsValidPort(parsedPort))
                {
                    server.RCON.Port = parsedPort;
                    host = host[..colonIndex];
                }
            }
        }

        server.RemoteHost = TryNormalizeRemoteHost(host, out string normalizedHost) ? normalizedHost : DefaultBindIP;
    }

    internal static string NormalizeDifficulty(string? difficulty)
    {
        string value = (difficulty ?? string.Empty).Trim();
        return value.Equals("easy", StringComparison.OrdinalIgnoreCase) ? "Easy"
            : value.Equals("hard", StringComparison.OrdinalIgnoreCase) ? "Hard"
            : "Medium";
    }

    private static string CleanText(string? value) => (value ?? string.Empty).Trim();

    public static string NormalizeRconPassword(string? value) => CleanText(value);

    public static bool TryNormalizeRconPassword(string? value, out string password)
    {
        password = CleanText(value);
        return password.Length > 0 && password.AsSpan().IndexOfAny('\r', '\n') < 0;
    }

    private static string GetTempPath(string path) => path + ".tmp";
}

public sealed class ServerPropertyEditor
{
    private const string DefaultLevelName = "world";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly string[] ManagedServerPropertyOrder =
    [
        "rcon.password",
        "rcon.port",
        "difficulty",
        "enable-query",
        "enable-rcon",
        "hardcore",
        "level-name",
        "max-players",
        "motd",
        "online-mode",
        "pvp",
        "query.port",
        "server-ip",
        "server-port"
    ];

    private static readonly HashSet<string> ManagedServerProperties = new(ManagedServerPropertyOrder, StringComparer.OrdinalIgnoreCase);

    public static string GetPropertiesPath(BotConfig config)
    {
        if (config?.Server == null || string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
            return string.Empty;

        return Path.Combine(config.Server.ServerDirectory, "server.properties");
    }

    public static string GetLevelName(BotConfig config)
    {
        string propsPath = GetPropertiesPath(config);
        if (!string.IsNullOrWhiteSpace(propsPath) && File.Exists(propsPath))
        {
            Dictionary<string, string> props = LoadProperties(propsPath);
            if (props.TryGetValue("level-name", out string? levelName))
                return NormalizeLevelName(levelName);
        }

        return DefaultLevelName;
    }

    public static string GetWorldDirectory(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return GetWorldDirectory(config.Server?.ServerDirectory, GetLevelName(config));
    }

    public static string GetWorldDirectory(string? serverDirectory, string? levelName)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
            throw new InvalidOperationException("Server directory is missing.");

        string serverRoot = Path.GetFullPath(serverDirectory);
        string worldPath = Path.GetFullPath(Path.Combine(serverRoot, NormalizeLevelName(levelName)));
        string serverRootWithSlash = serverRoot.EndsWith(Path.DirectorySeparatorChar)
            ? serverRoot
            : serverRoot + Path.DirectorySeparatorChar;

        if (!worldPath.StartsWith(serverRootWithSlash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid world folder path.");

        return worldPath;
    }

    public static string NormalizeLevelName(string? levelName)
    {
        string value = (levelName ?? string.Empty).Trim();
        if (value.Length == 0 || value == "." || value == "..")
            return DefaultLevelName;

        if (Path.IsPathRooted(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':'))
        {
            return DefaultLevelName;
        }

        return value;
    }

    public static void WriteInitialFiles(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Server ??= new ServerConfig();
        config.Settings ??= new StartingProfile();

        if (string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
            throw new InvalidOperationException("Server directory is missing.");

        Directory.CreateDirectory(config.Server.ServerDirectory);

        string eulaPath = Path.Combine(config.Server.ServerDirectory, "eula.txt");
        File.WriteAllText(eulaPath, "eula=true", Utf8NoBom);

        ApplyStartProfile(config, forceRewriteAll: true);
    }

    public static void CleanupUnusedServerJars(string serverDirectory, string currentJarPath)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory) || string.IsNullOrWhiteSpace(currentJarPath) || !Directory.Exists(serverDirectory))
            return;

        string keep = Path.GetFullPath(currentJarPath);

        foreach (string path in Directory.EnumerateFiles(serverDirectory, "twitchcraft-server-*.jar"))
        {
            if (string.Equals(Path.GetFullPath(path), keep, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to delete unused TwitchCraft-managed server jar", ex);
            }
        }
    }

    public static string ApplyStartProfile(BotConfig config) => ApplyStartProfile(config, forceRewriteAll: false);

    private static string ApplyStartProfile(BotConfig config, bool forceRewriteAll)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Server ??= new ServerConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Settings ??= new StartingProfile();

        string propsPath = GetPropertiesPath(config);
        if (string.IsNullOrWhiteSpace(propsPath))
            return string.Empty;

        Dictionary<string, string> props = forceRewriteAll
            ? new(StringComparer.OrdinalIgnoreCase)
            : LoadProperties(propsPath);

        bool multiplayer = config.Settings.MultiplayerEnabled;
        bool onlineMode = !multiplayer || config.Settings.RequireOnlineMode;

        bool writingInitialProperties = forceRewriteAll || props.Count == 0;
        string serverPort = config.Server.Port.ToString(CultureInfo.InvariantCulture);

        props["rcon.port"] = config.Server.RCON.Port.ToString(CultureInfo.InvariantCulture);
        props["enable-query"] = "true";
        props["query.port"] = serverPort;
        props["level-name"] = props.TryGetValue("level-name", out string? levelName) ? NormalizeLevelName(levelName) : DefaultLevelName;
        props["motd"] = multiplayer
            ? "§c§lT§f§lw§9§li§c§lt§f§lc§9§lh§c§lC§f§lr§9§la§c§lf§f§lt§9§l §c§l-§f§l §9§lM§c§lu§f§ll§9§lt§c§li§f§lp§9§ll§c§la§f§ly§9§le§c§lr§r"
            : "§c§lT§f§lw§9§li§c§lt§f§lc§9§lh§c§lC§f§lr§9§la§c§lf§f§lt§r";
        props["pvp"] = config.Settings.MultiplayerPVPEnabled ? "true" : "false";
        props["difficulty"] = ToMinecraftDifficulty(config.Settings.Difficulty);
        props["max-players"] = multiplayer ? Math.Max(2, config.Server.MaxPlayers).ToString(CultureInfo.InvariantCulture) : "1";
        props["online-mode"] = onlineMode ? "true" : "false";
        props["server-ip"] = config.Server.BindIP ?? string.Empty;
        props["server-port"] = serverPort;
        props["enable-rcon"] = "true";
        props["rcon.password"] = ConfigurationStore.NormalizeRconPassword(config.Server.RCON.Password);
        props["hardcore"] = config.Settings.HardcoreEnabled ? "true" : "false";

        SetDefaultProperty(props, "enable-jmx-monitoring", "false", writingInitialProperties);
        SetDefaultProperty(props, "gamemode", "survival", writingInitialProperties);
        SetDefaultProperty(props, "enable-command-block", "false", writingInitialProperties);
        SetDefaultProperty(props, "generator-settings", "{}", writingInitialProperties);
        SetDefaultProperty(props, "enforce-secure-profile", "true", writingInitialProperties);
        SetDefaultProperty(props, "network-compression-threshold", "256", writingInitialProperties);
        SetDefaultProperty(props, "max-tick-time", "60000", writingInitialProperties);
        SetDefaultProperty(props, "use-native-transport", "true", writingInitialProperties);
        SetDefaultProperty(props, "enable-status", "true", writingInitialProperties);
        SetDefaultProperty(props, "allow-flight", "false", writingInitialProperties);
        SetDefaultProperty(props, "broadcast-rcon-to-ops", "false", writingInitialProperties);
        SetDefaultProperty(props, "view-distance", "12", writingInitialProperties);
        SetDefaultProperty(props, "resource-pack-prompt", string.Empty, writingInitialProperties);
        SetDefaultProperty(props, "allow-nether", "true", writingInitialProperties);
        SetDefaultProperty(props, "sync-chunk-writes", "true", writingInitialProperties);
        SetDefaultProperty(props, "op-permission-level", "4", writingInitialProperties);
        SetDefaultProperty(props, "prevent-proxy-connections", "false", writingInitialProperties);
        SetDefaultProperty(props, "hide-online-players", "false", writingInitialProperties);
        SetDefaultProperty(props, "resource-pack", string.Empty, writingInitialProperties);
        SetDefaultProperty(props, "entity-broadcast-range-percentage", "100", writingInitialProperties);
        SetDefaultProperty(props, "simulation-distance", "10", writingInitialProperties);
        SetDefaultProperty(props, "player-idle-timeout", "500", writingInitialProperties);
        SetDefaultProperty(props, "force-gamemode", "true", writingInitialProperties);
        SetDefaultProperty(props, "rate-limit", "0", writingInitialProperties);
        SetDefaultProperty(props, "white-list", "false", writingInitialProperties);
        SetDefaultProperty(props, "broadcast-console-to-ops", "false", writingInitialProperties);
        SetDefaultProperty(props, "previews-chat", "false", writingInitialProperties);
        SetDefaultProperty(props, "function-permission-level", "2", writingInitialProperties);
        SetDefaultProperty(props, "level-type", "minecraft:normal", writingInitialProperties);
        SetDefaultProperty(props, "text-filtering-config", string.Empty, writingInitialProperties);
        SetDefaultProperty(props, "spawn-monsters", "true", writingInitialProperties);
        SetDefaultProperty(props, "enforce-whitelist", "false", writingInitialProperties);
        SetDefaultProperty(props, "spawn-protection", "0", writingInitialProperties);
        SetDefaultProperty(props, "resource-pack-sha1", string.Empty, writingInitialProperties);
        SetDefaultProperty(props, "max-world-size", "29999984", writingInitialProperties);

        ApplyVersionSpecificProperties(props, config.Server.MinecraftVersion, writingInitialProperties);
        return SaveProperties(propsPath, props);
    }

    private static void ApplyVersionSpecificProperties(Dictionary<string, string> props, string? minecraftVersion, bool forceDefaultValues)
    {
        if (MinecraftVersionSupport.SupportsLegacySpawnProperties(minecraftVersion))
        {
            SetDefaultProperty(props, "spawn-npcs", "true", forceDefaultValues);
            SetDefaultProperty(props, "spawn-animals", "true", forceDefaultValues);
        }
        else
        {
            props.Remove("spawn-npcs");
            props.Remove("spawn-animals");
        }

        if (MinecraftVersionSupport.SupportsPauseWhenEmptySeconds(minecraftVersion))
        {
            SetDefaultProperty(props, "pause-when-empty-seconds", "300", forceDefaultValues);
        }
        else
        {
            props.Remove("pause-when-empty-seconds");
        }
    }

    private static void SetDefaultProperty(Dictionary<string, string> props, string key, string value, bool force)
    {
        if (force) props[key] = value;
        else props.TryAdd(key, value);
    }

    private static string ToMinecraftDifficulty(string? difficulty)
    {
        string value = (difficulty ?? string.Empty).Trim();
        return value.Equals("Easy", StringComparison.OrdinalIgnoreCase) ? "easy"
            : value.Equals("Hard", StringComparison.OrdinalIgnoreCase) ? "hard"
            : "normal";
    }

    private static Dictionary<string, string> LoadProperties(string propsPath)
    {
        Dictionary<string, string> props = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(propsPath))
            return props;

        foreach (string line in File.ReadLines(propsPath, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            int equals = line.IndexOf('=');
            if (equals < 0)
                continue;

            string key = line[..equals].Trim();
            string value = UnescapePropertyValue(line[(equals + 1)..]);
            props[key] = key.Equals("rcon.password", StringComparison.OrdinalIgnoreCase) ? ConfigurationStore.NormalizeRconPassword(value) : value;
        }

        return props;
    }

    private static string SaveProperties(string propsPath, Dictionary<string, string> props)
    {
        StringBuilder builder = new(capacity: Math.Min(props.Count, 256) * 80);
        List<string> editableKeys = new(props.Count);
        foreach (string key in props.Keys)
        {
            if (!ManagedServerProperties.Contains(key))
                editableKeys.Add(key);
        }

        editableKeys.Sort(StringComparer.OrdinalIgnoreCase);
        if (editableKeys.Count > 0)
        {
            builder.AppendLine("# THESE PROPERTIES CAN BE CHANGED FROM THIS FILE. THEY WILL STAY PERMANENTLY UNTIL YOU CHANGE THEM AGAIN.");
            foreach (string key in editableKeys)
                AppendProperty(builder, key, props[key]);

            builder.AppendLine();
        }

        builder.AppendLine("# THE SETTINGS BELOW ARE MANAGED BY TWITCHCRAFT");
        builder.AppendLine("# SOME CAN BE CHANGED IN YOUR CONFIG OR YOUR SETTINGS AND SOME CANNOT BE MODIFIED");

        foreach (string key in ManagedServerPropertyOrder)
        {
            if (props.TryGetValue(key, out string? value))
                AppendProperty(builder, key, value);
        }

        string content = builder.ToString();
        if (File.Exists(propsPath) && string.Equals(File.ReadAllText(propsPath, Utf8NoBom), content, StringComparison.Ordinal))
            return content;

        string tempPath = propsPath + ".tmp";
        string backupPath = propsPath + ".bak";
        File.WriteAllText(tempPath, content, Utf8NoBom);
        TwitchCraftBot_V1.FileSystemHelper.ReplaceOrMoveWithFallback(tempPath, propsPath, backupPath, "Atomic server.properties save failed; falling back to copy");
        return content;
    }

    private static void AppendProperty(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(EscapePropertyValue(value)).AppendLine();
    }

    private static string EscapePropertyValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        StringBuilder? builder = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i], escaped = c switch { '\t' => 't', '\n' => 'n', '\r' => 'r', '\f' => 'f', _ => '\0' };
            if (escaped == '\0' && c != '\\' && c != '=' && c != ':' && (i != 0 || c is not (' ' or '#' or '!'))) { builder?.Append(c); continue; }
            builder ??= new StringBuilder(value.Length + 4).Append(value, 0, i);
            builder.Append('\\').Append(escaped == '\0' ? c : escaped);
        }
        return builder?.ToString() ?? value;
    }

    private static string UnescapePropertyValue(string value)
    {
        int slash = value.IndexOf('\\');
        if (slash < 0) return value;
        StringBuilder builder = new StringBuilder(value.Length).Append(value, 0, slash);
        for (int i = slash; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                if (next == 'u')
                {
                    if (i + 4 < value.Length &&
                        TryGetHexValue(value[i + 1], out int h1) &&
                        TryGetHexValue(value[i + 2], out int h2) &&
                        TryGetHexValue(value[i + 3], out int h3) &&
                        TryGetHexValue(value[i + 4], out int h4))
                    {
                        c = (char)((h1 << 12) | (h2 << 8) | (h3 << 4) | h4);
                        i += 4;
                    }
                    else
                    {
                        builder.Append('\\').Append(next);
                        continue;
                    }
                }
                else
                {
                    c = next switch { 't' => '\t', 'n' => '\n', 'r' => '\r', 'f' => '\f', _ => next };
                }
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool TryGetHexValue(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }

        c = char.ToUpperInvariant(c);
        if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }

        value = 0;
        return false;
    }
}
