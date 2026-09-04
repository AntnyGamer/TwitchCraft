using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace TwitchCraftBot_V1.BotSetup;

/// <summary>
/// Keeps the runtime settings model simple while making config.json mirror the Settings UI.
/// </summary>
internal sealed class StartingProfileJsonConverter : JsonConverter<StartingProfile>
{
    internal static readonly (string Category, string[] Properties)[] CategoryOrder =
    [
        ("Commands",
        [
            nameof(StartingProfile.CommandPrefix),
            nameof(StartingProfile.SecondaryCommandPrefix),
            nameof(StartingProfile.ViewerCommandsPaused),
            nameof(StartingProfile.ModeratorsCanUseStreamerCommands),
            nameof(StartingProfile.ViewerCommandLimitPerMinute),
            nameof(StartingProfile.ChannelCommandLimitPerMinute),
            nameof(StartingProfile.GlobalGameCommandCooldownEnabled),
            nameof(StartingProfile.GlobalGameCommandCooldownSeconds),
            nameof(StartingProfile.ShowExactCooldownRemaining),
            nameof(StartingProfile.BotResponseVerbosity),
            nameof(StartingProfile.RespondToUnknownCommands),
            nameof(StartingProfile.MentionViewersInBotReplies)
        ]),
        ("Custom Commands",
        [
            nameof(StartingProfile.CommandCustomizations)
        ]),
        ("Economy",
        [
            nameof(StartingProfile.PassiveTokenEarningEnabled),
            nameof(StartingProfile.PassiveTokensPerPayout),
            nameof(StartingProfile.PassiveTokenPayoutMinimumSeconds),
            nameof(StartingProfile.PassiveTokenPayoutMaximumSeconds),
            nameof(StartingProfile.PassiveRewardsRequireActivity),
            nameof(StartingProfile.PassiveActivityWindowMinutes),
            nameof(StartingProfile.MaximumTokenBalance),
            nameof(StartingProfile.CommandCostMultiplier),
            nameof(StartingProfile.AutomaticFollowRewardsEnabled),
            nameof(StartingProfile.FollowRewardAmount),
            nameof(StartingProfile.AutomaticBitRewardsEnabled)
        ]),
        ("Gameplay",
        [
            nameof(StartingProfile.MinigamesEnabled),
            nameof(StartingProfile.MinigameCooldown),
            nameof(StartingProfile.HardcoreEnabled),
            nameof(StartingProfile.Difficulty),
            nameof(StartingProfile.MultiplayerPVPEnabled),
            nameof(StartingProfile.AllowAllPlayerTarget),
            nameof(StartingProfile.AllowRandomPlayerTarget)
        ]),
        ("Chat & Display",
        [
            nameof(StartingProfile.NonCommandChatRelayEnabled),
            nameof(StartingProfile.IncludeRelayTimestamps),
            nameof(StartingProfile.MinecraftRelayTextColor),
            nameof(StartingProfile.MinecraftRelayMessagesPerSecond),
            nameof(StartingProfile.ShowConnectionHealth)
        ]),
        ("Performance",
        [
            nameof(StartingProfile.LowResourceModeEnabled),
            nameof(StartingProfile.PauseUIUpdatesWhenMinimized),
            nameof(StartingProfile.ViewerRosterRefreshIntervalSeconds),
            nameof(StartingProfile.MaxVisibleTwitchLogLines),
            nameof(StartingProfile.MaxVisibleMinecraftLogLines),
            nameof(StartingProfile.MaxGameplayCommandQueue),
            nameof(StartingProfile.StatisticsEnabled),
            nameof(StartingProfile.SQLiteOptimizeIntervalHours),
            nameof(StartingProfile.AutomaticBackupsEnabled),
            nameof(StartingProfile.AutomaticBackupIntervalHours),
            nameof(StartingProfile.AutomaticBackupRetentionCount)
        ]),
        ("Minecraft Server",
        [
            nameof(StartingProfile.ViewDistance),
            nameof(StartingProfile.SimulationDistance),
            nameof(StartingProfile.EntityBroadcastRangePercentage),
            nameof(StartingProfile.NetworkCompressionThreshold),
            nameof(StartingProfile.RCONTimeoutSeconds),
            nameof(StartingProfile.GracefulShutdownTimeoutSeconds),
            nameof(StartingProfile.EmptyServerShutdownDelayMinutes),
            nameof(StartingProfile.WhitelistEnabled)
        ])
    ];

    private static readonly Dictionary<string, PropertyInfo> ProfileProperties = CreatePropertyMap();

    public override void WriteJson(JsonWriter writer, StartingProfile? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        JObject root = [];
        foreach ((string categoryName, string[] propertyNames) in CategoryOrder)
        {
            JObject category = [];
            foreach (string propertyName in propertyNames)
            {
                object? propertyValue = ProfileProperties[propertyName].GetValue(value);
                category.Add(propertyName, CreatePropertyToken(propertyName, propertyValue, serializer));
            }

            root.Add(categoryName, category);
        }

        root.WriteTo(writer);
    }

    public override StartingProfile ReadJson(
        JsonReader reader,
        Type objectType,
        StartingProfile? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return existingValue ?? new StartingProfile();

        JObject root = JObject.Load(reader);
        JObject flattened = [];

        foreach ((string categoryName, string[] propertyNames) in CategoryOrder)
        {
            if (root.GetValue(categoryName, StringComparison.OrdinalIgnoreCase) is not JObject category)
                continue;

            foreach (string propertyName in propertyNames)
            {
                JToken? token = category.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
                if (token != null)
                    flattened[propertyName] = token.DeepClone();
            }
        }

        StartingProfile target = existingValue ?? new StartingProfile();
        using JsonReader flattenedReader = flattened.CreateReader();
        serializer.Populate(flattenedReader, target);
        return target;
    }

    private static JToken CreatePropertyToken(string propertyName, object? propertyValue, JsonSerializer serializer)
    {
        if (propertyValue == null)
            return JValue.CreateNull();

        if (propertyName == nameof(StartingProfile.CommandCustomizations) &&
            propertyValue is Dictionary<string, CommandCustomization> customizations)
        {
            JObject ordered = [];
            List<string> names = [.. customizations.Keys];
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                ordered.Add(name, JToken.FromObject(customizations[name], serializer));
            return ordered;
        }

        return JToken.FromObject(propertyValue, serializer);
    }

    private static Dictionary<string, PropertyInfo> CreatePropertyMap()
    {
        Dictionary<string, PropertyInfo> properties = new(StringComparer.OrdinalIgnoreCase);
        foreach (PropertyInfo property in typeof(StartingProfile).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.CanWrite)
                properties[property.Name] = property;
        }

        return properties;
    }
}
