using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

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
        TwitchCraftBot_V1.ErrorHandling.RegisterSecrets(config.Twitch.BotToken, config.Server.RCON.Password);
    }

    private static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

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

}
