using System;
using System.IO;

namespace TwitchCraft_V1.Setup;

internal static class SetupInputValidator
{
    public static bool CanStart(
        string minecraftVersion,
        string bindIP,
        string clientID,
        string authorizedClientID,
        string botToken,
        string channel,
        string botName)
        => GetBlockingReason(
            minecraftVersion,
            bindIP,
            clientID,
            authorizedClientID,
            botToken,
            channel,
            botName) == null;

    public static string? GetBlockingReason(
        string minecraftVersion,
        string bindIP,
        string clientID,
        string authorizedClientID,
        string botToken,
        string channel,
        string botName)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return "Select a Minecraft version before starting.";
        if (!MinecraftVersionSupport.TryGetVersion(minecraftVersion, out _))
            return "Minecraft version '" + minecraftVersion.Trim() + "' is not supported by this TwitchCraft build. Select one of the versions in the list.";
        if (!ConfigurationStore.IsValidBindIP(bindIP))
            return "Enter a valid Minecraft Bind IP before starting.";

        string normalizedClientID = (clientID ?? string.Empty).Trim();
        if (normalizedClientID.Length == 0)
            return "This TwitchCraft build is missing its Twitch Client ID.";
        if (string.IsNullOrWhiteSpace(botToken) ||
            !string.Equals(normalizedClientID, (authorizedClientID ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            return "Authorize Twitch before starting.";
        }

        if (!CommandUserHelper.TryNormalizeTwitchUser(channel, out _))
            return "Enter a valid Twitch channel name before starting.";
        if (!CommandUserHelper.TryNormalizeTwitchUser(botName, out _))
            return "Twitch authorization must return a valid bot account before starting.";

        return null;
    }

    internal static void ValidateRuntimeConfig(TwitchCraftConfig config)
    {
        if (config == null)
            throw new InvalidOperationException("Config is missing.");

        config.Server ??= new ServerConfig();
        config.Server.Java ??= new JavaConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Twitch ??= new TwitchConfig();
        config.Settings ??= new StartingProfile();

        if (!MinecraftVersionSupport.TryGetVersion(config.Server.MinecraftVersion, out _))
            throw new InvalidOperationException("Minecraft version '" + (config.Server.MinecraftVersion ?? string.Empty).Trim() + "' is not supported by this TwitchCraft build.");

        bool remoteController = config.Settings.RemoteControlEnabled;

        if (remoteController)
        {
            if (!ConfigurationStore.IsValidRemoteHost(config.Server.RemoteHost))
                throw new InvalidOperationException("Remote controller host is missing or invalid.");
        }
        else
        {
            string javaExecutablePath = (config.Server.Java.ExecutablePath ?? string.Empty).Trim();
            if (javaExecutablePath.Length == 0)
                throw new InvalidOperationException("Java executable path is missing.");

            if (!File.Exists(javaExecutablePath))
                throw new InvalidOperationException("Java executable path does not exist: " + javaExecutablePath);

            string serverDirectory = (config.Server.ServerDirectory ?? string.Empty).Trim();
            if (serverDirectory.Length == 0)
                throw new InvalidOperationException("Minecraft server directory is missing.");

            if (!Directory.Exists(serverDirectory))
                throw new InvalidOperationException("Minecraft server directory does not exist: " + serverDirectory);

            string jarPath = string.IsNullOrWhiteSpace(config.Server.JarPath)
                ? Path.Combine(serverDirectory, "server.jar")
                : config.Server.JarPath.Trim();
            if (!File.Exists(jarPath))
                throw new InvalidOperationException("Minecraft server jar path does not exist: " + jarPath);

            if (config.Server.MemoryMinGB <= 0 || config.Server.MemoryMaxGB <= 0 || config.Server.MemoryMinGB > config.Server.MemoryMaxGB || config.Server.MemoryMaxGB > 256)
                throw new InvalidOperationException("Minecraft server RAM must be between 1 and 256 GB, and minimum RAM less than or equal to maximum RAM.");

            if (!ConfigurationStore.IsValidBindIP(config.Server.BindIP))
                throw new InvalidOperationException("Minecraft server address is invalid.");
        }

        if (config.Server.Port is < 1 or > 65535)
            throw new InvalidOperationException("Minecraft server port must be between 1 and 65535.");

        if (config.Server.RCON.Port is < 1 or > 65535)
            throw new InvalidOperationException("RCON port must be between 1 and 65535.");

        if (!remoteController && config.Server.Port == config.Server.RCON.Port)
            throw new InvalidOperationException("Minecraft server port and RCON port cannot be the same.");

        if (!ConfigurationStore.TryNormalizeRCONPassword(config.Server.RCON.Password, out string normalizedRCONPassword))
            throw new InvalidOperationException("RCON password is missing or invalid.");

        config.Server.RCON.Password = normalizedRCONPassword;

        if (string.IsNullOrWhiteSpace(config.Twitch.BotToken))
            throw new InvalidOperationException("Twitch bot token is missing.");

        if (string.IsNullOrWhiteSpace(config.Twitch.StreamerName))
            throw new InvalidOperationException("Twitch channel name is missing.");
    }
}
