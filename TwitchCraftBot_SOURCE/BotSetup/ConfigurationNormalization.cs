using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace TwitchCraftBot_V1.BotSetup;

public sealed partial class ConfigurationStore
{
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

        if (!TryStripRemotePort(host, out string hostOnly))
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

    private static bool TryStripRemotePort(string host, out string hostOnly)
    {
        hostOnly = host;

        if (host.StartsWith('['))
        {
            int bracketEnd = host.IndexOf(']');
            if (bracketEnd <= 1)
                return false;

            string address = host[1..bracketEnd];
            string remainder = host[(bracketEnd + 1)..];
            if (remainder.Length > 0 &&
                (!remainder.StartsWith(':') || !IsValidPortText(remainder.AsSpan(1))))
                return false;

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

    public static bool ShouldWarnAboutBindIP(string? value)
    {
        if (!IPAddress.TryParse(CleanText(value), out IPAddress? IP))
            return true;

        byte[] bytes = IP.GetAddressBytes();
        return bytes.Length != 4 || bytes[0] == 25 || bytes[0] == 26 || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
    }

    private static void ResetStartMode(BotConfig config)
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
        else if (config.Server.MemoryMinGB > 256)
            config.Server.MemoryMinGB = 256;

        if (config.Server.MemoryMaxGB <= 0)
            config.Server.MemoryMaxGB = DefaultMemoryGB;
        else if (config.Server.MemoryMaxGB > 256)
            config.Server.MemoryMaxGB = 256;

        if (config.Server.MemoryMinGB > config.Server.MemoryMaxGB)
            config.Server.MemoryMinGB = config.Server.MemoryMaxGB;

        if (!IsValidPort(config.Server.RCON.Port))
            config.Server.RCON.Port = DefaultRCONPort;

        NormalizeEndpoint(config.Server);

        if (config.Settings.MinigameCooldown < 2 || config.Settings.MinigameCooldown > 30)
            config.Settings.MinigameCooldown = DefaultMinigameCooldown;

        if (double.IsNaN(config.Settings.GlobalGameCommandCooldownSeconds) ||
            config.Settings.GlobalGameCommandCooldownSeconds < 0.1 ||
            config.Settings.GlobalGameCommandCooldownSeconds > 120.0)
        {
            config.Settings.GlobalGameCommandCooldownSeconds = DefaultGlobalGameCommandCooldownSeconds;
        }

        if (config.Settings.FollowRewardAmount < 1 || config.Settings.FollowRewardAmount > 1_000_000)
            config.Settings.FollowRewardAmount = DefaultFollowRewardAmount;

        if (!double.IsFinite(config.Settings.CommandCostMultiplier) ||
            config.Settings.CommandCostMultiplier < 0.0 ||
            config.Settings.CommandCostMultiplier > 5.0)
        {
            config.Settings.CommandCostMultiplier = DefaultCommandCostMultiplier;
        }

        config.Settings.BotResponseVerbosity = NormalizeVerbosity(config.Settings.BotResponseVerbosity);

        config.Settings.CommandPrefix = NormalizeCommandPrefix(config.Settings.CommandPrefix, "!");
        config.Settings.SecondaryCommandPrefix = NormalizeCommandPrefix(config.Settings.SecondaryCommandPrefix, string.Empty);
        if (string.Equals(config.Settings.CommandPrefix, config.Settings.SecondaryCommandPrefix, StringComparison.Ordinal))
            config.Settings.SecondaryCommandPrefix = string.Empty;

        if (config.Settings.PassiveTokensPerPayout < 1 || config.Settings.PassiveTokensPerPayout > 1_000_000)
            config.Settings.PassiveTokensPerPayout = DefaultPassiveTokensPerPayout;
        NormalizePayout(config.Settings);
        if (config.Settings.MaximumTokenBalance < 0)
            config.Settings.MaximumTokenBalance = 0;
        if (config.Settings.ChannelCommandLimitPerMinute < 0 || config.Settings.ChannelCommandLimitPerMinute > 1000)
            config.Settings.ChannelCommandLimitPerMinute = 0;
        if (config.Settings.ViewerCommandLimitPerMinute < 0 || config.Settings.ViewerCommandLimitPerMinute > 1000)
            config.Settings.ViewerCommandLimitPerMinute = 0;
        config.Settings.PassiveActivityWindowMinutes = NormalizeChoice(
            config.Settings.PassiveActivityWindowMinutes, 10, 1, 2, 5, 10, 15, 30, 60, 120);
        config.Settings.AutomaticBackupIntervalHours = NormalizeChoice(config.Settings.AutomaticBackupIntervalHours, 24, 1, 6, 12, 24, 48, 168);
        config.Settings.AutomaticBackupRetentionCount = NormalizeChoice(config.Settings.AutomaticBackupRetentionCount, StartingProfile.DefaultAutomaticBackupRetentionCount, 1, 3, 5, 10, 20);
        config.Settings.MaxVisibleTwitchLogLines = NormalizeRange(config.Settings.MaxVisibleTwitchLogLines, 250, 50, 5000);
        config.Settings.MaxVisibleMinecraftLogLines = NormalizeRange(config.Settings.MaxVisibleMinecraftLogLines, 250, 50, 5000);
        config.Settings.ViewerRosterRefreshIntervalSeconds = NormalizeChoice(config.Settings.ViewerRosterRefreshIntervalSeconds, 30, 15, 30, 60, 120, 300);
        config.Settings.MinecraftRelayMessagesPerSecond = NormalizeRange(config.Settings.MinecraftRelayMessagesPerSecond, 0, 0, 100);
        config.Settings.MaxGameplayCommandQueue = NormalizeRange(config.Settings.MaxGameplayCommandQueue, 75, 10, 1000);
        config.Settings.RCONTimeoutSeconds = NormalizeRange(config.Settings.RCONTimeoutSeconds, 5, 1, 60);
        config.Settings.GracefulShutdownTimeoutSeconds = NormalizeChoice(config.Settings.GracefulShutdownTimeoutSeconds, 5, 3, 5, 10, 15, 30, 60);
        config.Settings.SQLiteOptimizeIntervalHours = NormalizeChoice(config.Settings.SQLiteOptimizeIntervalHours, 0, 0, 1, 6, 12, 24, 168);
        config.Settings.ViewDistance = NormalizeRange(config.Settings.ViewDistance, 12, 2, 32);
        config.Settings.SimulationDistance = NormalizeRange(config.Settings.SimulationDistance, 10, 2, 32);
        config.Settings.EntityBroadcastRangePercentage = NormalizeRange(config.Settings.EntityBroadcastRangePercentage, 100, 10, 1000);
        config.Settings.NetworkCompressionThreshold = NormalizeRange(config.Settings.NetworkCompressionThreshold, 256, -1, 4096);
        config.Settings.EmptyServerShutdownDelayMinutes = NormalizeChoice(config.Settings.EmptyServerShutdownDelayMinutes, 0, 0, 5, 10, 15, 30, 60, 120);
        NormalizeCommands(config.Settings);
        config.Settings.MinecraftRelayTextColor = NormalizeColor(config.Settings.MinecraftRelayTextColor);

        config.Settings.Difficulty = NormalizeDifficulty(config.Settings.Difficulty);

        config.Server.Java.ExecutablePath = CleanText(config.Server.Java.ExecutablePath);
        config.Server.Java.HomeDirectory = CleanText(config.Server.Java.HomeDirectory);
        config.Server.RCON.Password = NormalizeRconPassword(config.Server.RCON.Password);
        config.Server.MinecraftVersion = CleanText(config.Server.MinecraftVersion);
        if (MinecraftVersionSupport.TryGetVersion(config.Server.MinecraftVersion, out MinecraftVersionSupport.MinecraftVersionInfo normalizedVersion))
            config.Server.MinecraftVersion = normalizedVersion.ID;
        config.Server.ServerDirectory = CleanText(config.Server.ServerDirectory);
        config.Server.PreviousBindIP = NormalizeBindIP(config.Server.PreviousBindIP);
        if (config.Server.PreviousBindIP.Length > 0 && !IsValidBindIP(config.Server.PreviousBindIP))
            config.Server.PreviousBindIP = string.Empty;
        config.Server.JarPath = CleanText(config.Server.JarPath);
        config.Twitch.StreamerName = CleanText(config.Twitch.StreamerName);
        config.Twitch.BotName = CleanText(config.Twitch.BotName);
        config.Twitch.BotToken = TwitchCraftBot_V1.TwitchTokenHelper.NormalizeAccessToken(config.Twitch.BotToken);
        config.Twitch.RefreshToken = CleanText(config.Twitch.RefreshToken);
        config.Twitch.ClientID = CleanText(config.Twitch.ClientID);
        config.Identity.StreamerMinecraftName = CleanText(config.Identity.StreamerMinecraftName);
    }

    private static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    private static void NormalizeEndpoint(ServerConfig server)
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

    internal static string NormalizeVerbosity(string? verbosity)
    {
        string value = (verbosity ?? string.Empty).Trim();
        return value.Equals(BotResponseVerbositySettings.Reduced, StringComparison.OrdinalIgnoreCase)
            ? BotResponseVerbositySettings.Reduced
            : value.Equals(BotResponseVerbositySettings.EssentialOnly, StringComparison.OrdinalIgnoreCase)
                ? BotResponseVerbositySettings.EssentialOnly
                : BotResponseVerbositySettings.Normal;
    }

    internal static string NormalizeCommandPrefix(string? prefix, string fallback)
    {
        string value = (prefix ?? string.Empty).Trim();
        if (value.Length is 0 or > 2)
            return fallback;

        for (int i = 0; i < value.Length; i++)
            if (char.IsWhiteSpace(value[i]) || char.IsControl(value[i]))
                return fallback;

        return value;
    }

    internal static string NormalizeColor(string? color)
    {
        string value = (color ?? string.Empty).Trim().ToLowerInvariant();
        return value is "black" or "dark_blue" or "dark_green" or "dark_aqua" or "dark_red" or
            "dark_purple" or "gold" or "gray" or "dark_gray" or "blue" or "green" or "aqua" or
            "red" or "light_purple" or "yellow" or "white"
            ? value
            : "white";
    }

    private static string CleanText(string? value) => (value ?? string.Empty).Trim();

    private static int NormalizeRange(int value, int fallback, int minimum, int maximum)
        => value < minimum || value > maximum ? fallback : value;

    private static int NormalizeChoice(
        int value,
        int fallback,
        int choice1,
        int choice2,
        int choice3,
        int choice4,
        int choice5,
        int? choice6 = null,
        int? choice7 = null,
        int? choice8 = null)
        => value == choice1 || value == choice2 || value == choice3 || value == choice4 || value == choice5 ||
           value == choice6 || value == choice7 || value == choice8
            ? value
            : fallback;

    private static void NormalizeCommands(StartingProfile settings)
    {
        Dictionary<string, CommandCustomization> normalized = new(
            settings.CommandCustomizations?.Count ?? 0,
            StringComparer.OrdinalIgnoreCase);
        if (settings.CommandCustomizations != null)
        {
            foreach ((string rawName, CommandCustomization? value) in settings.CommandCustomizations)
            {
                string name = (rawName ?? string.Empty).Trim().ToLowerInvariant();
                if (name.Length is < 1 or > 32 || value == null || !IsSimpleCommandName(name))
                    continue;

                int? cooldown = value.CooldownSeconds;
                if (cooldown is < 0 or > 86400)
                    cooldown = null;

                double? globalCooldown = value.GlobalCooldownSeconds;
                if (globalCooldown.HasValue && (!double.IsFinite(globalCooldown.Value) || globalCooldown.Value < 0.0 || globalCooldown.Value > 86400.0))
                    globalCooldown = null;

                if (!value.Enabled || cooldown.HasValue || globalCooldown.HasValue)
                {
                    normalized[name] = new CommandCustomization
                    {
                        Enabled = value.Enabled,
                        CooldownSeconds = cooldown,
                        GlobalCooldownSeconds = globalCooldown
                    };
                }
            }
        }

        settings.CommandCustomizations = normalized;
    }

    private static void NormalizePayout(StartingProfile settings)
    {
        const int DefaultMinimum = 30;
        const int DefaultMaximum = 60;
        const int MinimumAllowed = 10;
        const int MaximumAllowed = 15 * 60;

        if (settings.PassiveTokenPayoutMinimumSeconds is < MinimumAllowed or > MaximumAllowed)
            settings.PassiveTokenPayoutMinimumSeconds = DefaultMinimum;
        if (settings.PassiveTokenPayoutMaximumSeconds is < MinimumAllowed or > MaximumAllowed)
            settings.PassiveTokenPayoutMaximumSeconds = DefaultMaximum;
        if (settings.PassiveTokenPayoutMinimumSeconds > settings.PassiveTokenPayoutMaximumSeconds)
            (settings.PassiveTokenPayoutMinimumSeconds, settings.PassiveTokenPayoutMaximumSeconds) =
                (settings.PassiveTokenPayoutMaximumSeconds, settings.PassiveTokenPayoutMinimumSeconds);
    }

    private static bool IsSimpleCommandName(string value)
    {
        foreach (char c in value)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        return true;
    }

    public static string NormalizeRconPassword(string? value) => CleanText(value);

    internal static string GenerateRconPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public static bool TryNormalizeRconPassword(string? value, out string password)
    {
        password = CleanText(value);
        return password.Length > 0 && password.AsSpan().IndexOfAny('\r', '\n') < 0;
    }
}
