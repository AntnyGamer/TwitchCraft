using System;

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
}
