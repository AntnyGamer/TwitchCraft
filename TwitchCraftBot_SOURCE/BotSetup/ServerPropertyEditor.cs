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
        "difficulty",
        "hardcore",
        "pvp",
        "view-distance",
        "simulation-distance",
        "entity-broadcast-range-percentage",
        "network-compression-threshold",
        "white-list",
        "enforce-whitelist",
        "max-players",
        "motd",
        "online-mode",
        "server-ip",
        "server-port",
        "enable-query",
        "query.port",
        "enable-rcon",
        "rcon.port"
    ];

    private static readonly HashSet<string> ManagedServerProperties = new(ManagedServerPropertyOrder, StringComparer.OrdinalIgnoreCase) { "rcon.password" };

    public static string GetPropertiesPath(BotConfig config)
    {
        if (config?.Server == null || string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
            return string.Empty;

        return Path.Combine(config.Server.ServerDirectory, "server.properties");
    }

    public static string GetLevelName(BotConfig config)
    {
        string propsPath = GetPropertiesPath(config);
        if (string.IsNullOrWhiteSpace(propsPath) || !File.Exists(propsPath))
            return DefaultLevelName;

        string? levelName = null;
        foreach (string line in File.ReadLines(propsPath, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            int equals = line.IndexOf('=');
            if (equals >= 0 && MemoryExtensions.Equals(line.AsSpan(0, equals).Trim(), "level-name".AsSpan(), StringComparison.OrdinalIgnoreCase))
                levelName = UnescapeValue(line[(equals + 1)..]);
        }

        return levelName == null ? DefaultLevelName : NormalizeLevelName(levelName);
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
            value.AsSpan().IndexOfAny('/', '\\', ':') >= 0)
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

        ApplyProfile(config);
    }

    public static void CleanupServerJars(string serverDirectory, string currentJarPath)
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

    public static string ApplyProfile(BotConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Server ??= new ServerConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Settings ??= new StartingProfile();

        string propsPath = GetPropertiesPath(config);
        if (string.IsNullOrWhiteSpace(propsPath))
            return string.Empty;

        Dictionary<string, string> props = LoadPropertiesForUpdate(propsPath, out string existingContent);
        MinecraftVersionSupport.MinecraftVersionInfo version = MinecraftVersionSupport.GetVersion(config.Server.MinecraftVersion);

        bool multiplayer = config.Settings.MultiplayerEnabled;
        bool onlineMode = !multiplayer || config.Settings.RequireOnlineMode;

        string serverPort = config.Server.Port.ToString(CultureInfo.InvariantCulture);

        props["rcon.port"] = config.Server.RCON.Port.ToString(CultureInfo.InvariantCulture);
        props["enable-query"] = "true";
        props["query.port"] = serverPort;
        // TwitchCraft validates level-name because it becomes a local folder path, but a valid
        // user-selected world name is preserved and remains in the editable properties section.
        props["level-name"] = props.TryGetValue("level-name", out string? levelName) ? NormalizeLevelName(levelName) : DefaultLevelName;
        props["motd"] = multiplayer
            ? "§c§lT§f§lw§9§li§c§lt§f§lc§9§lh§c§lC§f§lr§9§la§c§lf§f§lt§9§l §c§l-§f§l §9§lM§c§lu§f§ll§9§lt§c§li§f§lp§9§ll§c§la§f§ly§9§le§c§lr§r"
            : "§c§lT§f§lw§9§li§c§lt§f§lc§9§lh§c§lC§f§lr§9§la§c§lf§f§lt§r";
        props["difficulty"] = ToDifficulty(config.Settings.Difficulty);
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
        props["white-list"] = props["enforce-whitelist"] = config.Settings.WhitelistEnabled ? "true" : "false";

        SetDefault(props, "enable-jmx-monitoring", "false");
        SetDefault(props, "gamemode", "survival");
        SetDefault(props, "generator-settings", "{}");
        SetDefault(props, "enforce-secure-profile", "true");
        SetDefault(props, "max-tick-time", "60000");
        SetDefault(props, "use-native-transport", "true");
        SetDefault(props, "enable-status", "true");
        SetDefault(props, "allow-flight", "false");
        SetDefault(props, "broadcast-rcon-to-ops", "false");
        SetDefault(props, "resource-pack-prompt", string.Empty);
        SetDefault(props, "sync-chunk-writes", "true");
        SetDefault(props, "op-permission-level", "4");
        SetDefault(props, "prevent-proxy-connections", "false");
        SetDefault(props, "hide-online-players", "false");
        SetDefault(props, "resource-pack", string.Empty);
        SetDefault(props, "player-idle-timeout", "500");
        SetDefault(props, "force-gamemode", "true");
        SetDefault(props, "rate-limit", "0");
        SetDefault(props, "broadcast-console-to-ops", "false");
        SetDefault(props, "previews-chat", "false");
        SetDefault(props, "function-permission-level", "2");
        SetDefault(props, "level-type", "minecraft:normal");
        SetDefault(props, "text-filtering-config", string.Empty);
        SetDefault(props, "spawn-protection", "0");
        SetDefault(props, "resource-pack-sha1", string.Empty);
        SetDefault(props, "max-world-size", "29999984");

        if (version.UsesServerSettingGameRules)
        {
            props.Remove("pvp");
            props.Remove("allow-nether");
            props.Remove("spawn-monsters");
            props.Remove("enable-command-block");
        }
        else
        {
            props["pvp"] = config.Settings.MultiplayerPVPEnabled ? "true" : "false";
            SetDefault(props, "allow-nether", "true");
            SetDefault(props, "spawn-monsters", "true");
            SetDefault(props, "enable-command-block", "false");
        }

        ApplyVersion(props, version);
        return SaveProperties(propsPath, props, existingContent);
    }

    private static void ApplyVersion(Dictionary<string, string> props, MinecraftVersionSupport.MinecraftVersionInfo version)
    {
        if (version.DataPackFormatMajor >= 57)
        {
            props.Remove("spawn-npcs");
            props.Remove("spawn-animals");
            SetDefault(props, "pause-when-empty-seconds", "1");
        }
        else
        {
            SetDefault(props, "spawn-npcs", "true");
            SetDefault(props, "spawn-animals", "true");
            props.Remove("pause-when-empty-seconds");
        }
    }

    private static void SetDefault(Dictionary<string, string> props, string key, string value)
        => props.TryAdd(key, value);

    private static string ToDifficulty(string? difficulty)
    {
        string value = (difficulty ?? string.Empty).Trim();
        return value.Equals("Easy", StringComparison.OrdinalIgnoreCase) ? "easy"
            : value.Equals("Hard", StringComparison.OrdinalIgnoreCase) ? "hard"
            : "normal";
    }

    private static Dictionary<string, string> LoadPropertiesForUpdate(string propsPath, out string existingContent)
    {
        Dictionary<string, string> props = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(propsPath))
        {
            existingContent = string.Empty;
            return props;
        }

        existingContent = File.ReadAllText(propsPath, Encoding.UTF8);
        using StringReader reader = new(existingContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
            AddProperty(props, line);

        return props;
    }

    private static void AddProperty(Dictionary<string, string> props, string line)
    {
        if (line.Length == 0 || line[0] == '#')
            return;

        int equals = line.IndexOf('=');
        if (equals < 0)
            return;

        string key = line[..equals].Trim();
        string value = UnescapeValue(line[(equals + 1)..]);
        props[key] = key.Equals("rcon.password", StringComparison.OrdinalIgnoreCase)
            ? ConfigurationStore.NormalizeRconPassword(value)
            : value;
    }

    private static string SaveProperties(string propsPath, Dictionary<string, string> props, string existingContent)
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
            builder.AppendLine("# THESE SETTINGS CAN BE CHANGED DIRECTLY IN THIS FILE.");
            builder.AppendLine("# TWITCHCRAFT WILL PRESERVE YOUR CHANGES.");
            foreach (string key in editableKeys)
                AppendProperty(builder, key, props[key]);

            builder.AppendLine();
        }

        builder.AppendLine("# THE SETTINGS BELOW ARE MANAGED BY TWITCHCRAFT.");
        builder.AppendLine("# IT IS RECOMMENDED TO CHANGE THEM IN TWITCHCRAFT AS DIRECT EDITS HERE MAY BE REPLACED.");
        foreach (string key in ManagedServerPropertyOrder)
            if (props.TryGetValue(key, out string? value)) AppendProperty(builder, key, value);
        builder.AppendLine().AppendLine("# RCON PASSWORD - MANAGED BY TWITCHCRAFT - KEEP SECURE").AppendLine("# IF LOCALLY HOSTING, CHANGE IT IN TWITCHCRAFT: SETTINGS > DANGEROUS");
        if (props.TryGetValue("rcon.password", out string? rconPassword)) AppendProperty(builder, "rcon.password", rconPassword);

        string content = builder.ToString();
        if (string.Equals(existingContent, content, StringComparison.Ordinal))
            return content;

        string tempPath = propsPath + ".tmp";
        string backupPath = propsPath + ".bak";
        File.WriteAllText(tempPath, content, Utf8NoBom);
        TwitchCraftBot_V1.FileSystemHelper.ReplaceFile(tempPath, propsPath, backupPath, "Atomic server.properties save failed; falling back to copy");
        return content;
    }

    private static void AppendProperty(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append('=').Append(EscapeValue(value)).AppendLine();
    }

    private static string EscapeValue(string? value)
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

    private static string UnescapeValue(string value)
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
