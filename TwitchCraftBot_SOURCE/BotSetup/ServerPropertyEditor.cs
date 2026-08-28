using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TwitchCraftBot_V1.BotSetup;

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
        "network-compression-threshold",
        "online-mode",
        "pvp",
        "query.port",
        "view-distance",
        "simulation-distance",
        "entity-broadcast-range-percentage",
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

        ApplyStartProfile(config);
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

    public static string ApplyStartProfile(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Server ??= new ServerConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Settings ??= new StartingProfile();

        string propsPath = GetPropertiesPath(config);
        if (string.IsNullOrWhiteSpace(propsPath))
            return string.Empty;

        Dictionary<string, string> props = LoadProperties(propsPath);

        bool multiplayer = config.Settings.MultiplayerEnabled;
        bool onlineMode = !multiplayer || config.Settings.RequireOnlineMode;

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
        props["view-distance"] = config.Settings.ViewDistance.ToString(CultureInfo.InvariantCulture);
        props["simulation-distance"] = config.Settings.SimulationDistance.ToString(CultureInfo.InvariantCulture);
        props["entity-broadcast-range-percentage"] = config.Settings.EntityBroadcastRangePercentage.ToString(CultureInfo.InvariantCulture);
        props["network-compression-threshold"] = config.Settings.NetworkCompressionThreshold.ToString(CultureInfo.InvariantCulture);

        SetDefaultProperty(props, "enable-jmx-monitoring", "false");
        SetDefaultProperty(props, "gamemode", "survival");
        SetDefaultProperty(props, "enable-command-block", "false");
        SetDefaultProperty(props, "generator-settings", "{}");
        SetDefaultProperty(props, "enforce-secure-profile", "true");
        SetDefaultProperty(props, "max-tick-time", "60000");
        SetDefaultProperty(props, "use-native-transport", "true");
        SetDefaultProperty(props, "enable-status", "true");
        SetDefaultProperty(props, "allow-flight", "false");
        SetDefaultProperty(props, "broadcast-rcon-to-ops", "false");
        SetDefaultProperty(props, "resource-pack-prompt", string.Empty);
        SetDefaultProperty(props, "allow-nether", "true");
        SetDefaultProperty(props, "sync-chunk-writes", "true");
        SetDefaultProperty(props, "op-permission-level", "4");
        SetDefaultProperty(props, "prevent-proxy-connections", "false");
        SetDefaultProperty(props, "hide-online-players", "false");
        SetDefaultProperty(props, "resource-pack", string.Empty);
        SetDefaultProperty(props, "player-idle-timeout", "500");
        SetDefaultProperty(props, "force-gamemode", "true");
        SetDefaultProperty(props, "rate-limit", "0");
        SetDefaultProperty(props, "white-list", "false");
        SetDefaultProperty(props, "broadcast-console-to-ops", "false");
        SetDefaultProperty(props, "previews-chat", "false");
        SetDefaultProperty(props, "function-permission-level", "2");
        SetDefaultProperty(props, "level-type", "minecraft:normal");
        SetDefaultProperty(props, "text-filtering-config", string.Empty);
        SetDefaultProperty(props, "spawn-monsters", "true");
        SetDefaultProperty(props, "enforce-whitelist", "false");
        SetDefaultProperty(props, "spawn-protection", "0");
        SetDefaultProperty(props, "resource-pack-sha1", string.Empty);
        SetDefaultProperty(props, "max-world-size", "29999984");

        ApplyVersionSpecificProperties(props, config.Server.MinecraftVersion);
        return SaveProperties(propsPath, props);
    }

    private static void ApplyVersionSpecificProperties(Dictionary<string, string> props, string minecraftVersion)
    {
        if (MinecraftVersionSupport.SupportsLegacySpawnProperties(minecraftVersion))
        {
            SetDefaultProperty(props, "spawn-npcs", "true");
            SetDefaultProperty(props, "spawn-animals", "true");
        }
        else
        {
            props.Remove("spawn-npcs");
            props.Remove("spawn-animals");
        }

        if (MinecraftVersionSupport.SupportsPauseWhenEmptySeconds(minecraftVersion))
        {
            SetDefaultProperty(props, "pause-when-empty-seconds", "300");
        }
        else
        {
            props.Remove("pause-when-empty-seconds");
        }
    }

    private static void SetDefaultProperty(Dictionary<string, string> props, string key, string value)
        => props.TryAdd(key, value);

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
