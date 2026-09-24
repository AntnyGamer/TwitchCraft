using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class CategorizedSettingsPersistenceTests
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Converters = { new StartingProfileJsonConverter() }
    };

    [Fact]
    public void Serialize_DefaultProfileUsesOnlyTheSevenSettingsCategories()
    {
        StartingProfile profile = new();

        JObject root = JObject.Parse(JsonConvert.SerializeObject(profile, SerializerSettings));

        Assert.Equal(
            ["Commands", "Custom Commands", "Economy", "Gameplay", "Chat and Display", "Performance", "Minecraft Server"],
            root.Properties().Select(property => property.Name));
        Assert.Null(root.Property(nameof(StartingProfile.CommandPrefix)));
        Assert.Null(root.SelectToken(nameof(StartingProfile.MultiplayerEnabled)));
        Assert.Null(root.SelectToken(nameof(StartingProfile.RemoteControlEnabled)));
        Assert.Null(root.SelectToken(nameof(StartingProfile.RequireOnlineMode)));
        Assert.NotNull(root["Economy"]?[nameof(StartingProfile.PassiveRewardsRequireActivity)]);
        Assert.NotNull(root["Economy"]?[nameof(StartingProfile.PassiveActivityWindowMinutes)]);
    }

    [Theory]
    [InlineData(nameof(StartingProfile.CommandPrefix), "Commands")]
    [InlineData(nameof(StartingProfile.FollowRewardAmount), "Economy")]
    [InlineData(nameof(StartingProfile.AutomaticBackupRetentionCount), "Performance")]
    public void Serialize_MapsASettingToItsExpectedCategory(string propertyName, string categoryName)
    {
        JObject root = JObject.Parse(JsonConvert.SerializeObject(new StartingProfile(), SerializerSettings));

        Assert.NotNull(root[categoryName]?[propertyName]);
        Assert.Single(
            root.Properties(),
            category => category.Value.Type == JTokenType.Object && category.Value[propertyName] != null);
    }

    [Fact]
    public void RoundTrip_PreservesCategorizedValuesAndCustomCommands()
    {
        StartingProfile original = new()
        {
            CommandPrefix = "?",
            Difficulty = "Hard",
            MultiplayerPvPEnabled = true,
            AutomaticBackupRetentionCount = 5,
            CommandCustomizations = new(StringComparer.OrdinalIgnoreCase)
            {
                ["heal"] = new() { Enabled = false, CooldownSeconds = 12, GlobalCooldownSeconds = 2.5 }
            }
        };

        string json = JsonConvert.SerializeObject(original, SerializerSettings);
        StartingProfile restored = Assert.IsType<StartingProfile>(
            JsonConvert.DeserializeObject<StartingProfile>(json, SerializerSettings));

        Assert.Equal("?", restored.CommandPrefix);
        Assert.Equal("Hard", restored.Difficulty);
        Assert.True(restored.MultiplayerPvPEnabled);
        Assert.Equal(5, restored.AutomaticBackupRetentionCount);
        CommandCustomization command = Assert.Single(restored.CommandCustomizations).Value;
        Assert.False(command.Enabled);
        Assert.Equal(12, command.CooldownSeconds);
        Assert.Equal(2.5, command.GlobalCooldownSeconds);
    }

    [Fact]
    public void Deserialize_MissingCategoriesPreserveCurrentDefaults()
    {
        const string json = """
            { "Commands": { "CommandPrefix": "?" } }
            """;

        StartingProfile profile = Assert.IsType<StartingProfile>(
            JsonConvert.DeserializeObject<StartingProfile>(json, SerializerSettings));

        Assert.Equal("?", profile.CommandPrefix);
        Assert.True(profile.HardcoreEnabled);
        Assert.True(profile.PassiveTokenEarningEnabled);
        Assert.Equal(StartingProfile.DefaultAutomaticBackupRetentionCount, profile.AutomaticBackupRetentionCount);
    }

    [Fact]
    public void Deserialize_IsCaseInsensitiveAndIgnoresUnknownData()
    {
        const string json = """
            {
              "commands": { "commandprefix": "??", "FutureCommandSetting": true },
              "GAMEPLAY": { "MultiplayerPVPEnabled": true },
              "Future Category": { "FutureValue": 42 }
            }
            """;

        StartingProfile profile = Assert.IsType<StartingProfile>(
            JsonConvert.DeserializeObject<StartingProfile>(json, SerializerSettings));

        Assert.Equal("??", profile.CommandPrefix);
        Assert.True(profile.MultiplayerPvPEnabled);
        Assert.Equal("Medium", profile.Difficulty);
    }

    [Fact]
    public void Deserialize_NullSettingsCreatesAUsableDefaultProfile()
    {
        StartingProfile profile = Assert.IsType<StartingProfile>(
            JsonConvert.DeserializeObject<StartingProfile>("null", SerializerSettings));

        Assert.Equal("!", profile.CommandPrefix);
        Assert.Equal(3, profile.AutomaticBackupRetentionCount);
        Assert.Empty(profile.CommandCustomizations);
    }

    [Fact]
    public void Serialize_CustomCommandsAreCaseInsensitiveSorted()
    {
        StartingProfile profile = new()
        {
            CommandCustomizations = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Zulu"] = new() { Enabled = false },
                ["alpha"] = new() { CooldownSeconds = 4 }
            }
        };

        JObject root = JObject.Parse(JsonConvert.SerializeObject(profile, SerializerSettings));
        JObject commands = Assert.IsType<JObject>(root["Custom Commands"]?[nameof(StartingProfile.CommandCustomizations)]);

        Assert.Equal(["alpha", "Zulu"], commands.Properties().Select(property => property.Name));
    }

    [Fact]
    public void Serialize_IncludesEveryPersistedSettingExactlyOnce()
    {
        JObject root = JObject.Parse(JsonConvert.SerializeObject(new StartingProfile(), SerializerSettings));
        string[] serialized = root.Properties()
            .SelectMany(category => Assert.IsType<JObject>(category.Value).Properties())
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(serialized.Length, serialized.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string[] intentionallyUnpersisted =
        [
            nameof(StartingProfile.MultiplayerEnabled),
            nameof(StartingProfile.RemoteControlEnabled),
            nameof(StartingProfile.RequireOnlineMode)
        ];
        string[] expected = typeof(StartingProfile).GetProperties()
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => property.Name)
            .Except(intentionallyUnpersisted, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] actual = serialized
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
