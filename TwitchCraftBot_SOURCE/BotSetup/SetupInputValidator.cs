using System;

namespace TwitchCraftBot_V1.BotSetup;

internal static class SetupInputValidator
{
    public static bool CanStart(
        string minecraftVersion,
        string bindIp,
        string clientId,
        string authorizedClientId,
        string botToken,
        string channel,
        string botName)
        => GetBlockingReason(
            minecraftVersion,
            bindIp,
            clientId,
            authorizedClientId,
            botToken,
            channel,
            botName) == null;

    public static string? GetBlockingReason(
        string minecraftVersion,
        string bindIp,
        string clientId,
        string authorizedClientId,
        string botToken,
        string channel,
        string botName)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return "Select a Minecraft version before starting.";
        if (!MinecraftVersionSupport.TryGetVersion(minecraftVersion, out _))
            return "Select a Minecraft version supported by this TwitchCraft build.";
        if (!ConfigurationStore.IsValidBindIP(bindIp))
            return "Enter a valid Minecraft Bind IP before starting.";

        string normalizedClientId = (clientId ?? string.Empty).Trim();
        if (normalizedClientId.Length == 0)
            return "Enter your Twitch Client ID before starting.";
        if (string.IsNullOrWhiteSpace(botToken) ||
            !string.Equals(normalizedClientId, (authorizedClientId ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            return "Authorize Twitch with the current Client ID before starting.";
        }

        if (!CommandUserHelper.TryNormalizeTwitchUsername(channel, out _))
            return "Enter a valid Twitch channel name before starting.";
        if (!CommandUserHelper.TryNormalizeTwitchUsername(botName, out _))
            return "Twitch authorization must return a valid bot account before starting.";

        return null;
    }
}
