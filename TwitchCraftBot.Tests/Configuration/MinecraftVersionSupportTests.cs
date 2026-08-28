using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class MinecraftVersionSupportTests
{
    [Theory]
    [InlineData("1.20.5", "1.20.5", "1.20.5", 21, 41)]
    [InlineData("1.21.0", "1.21.0", "1.21.0", 21, 48)]
    [InlineData("1.21.11", "1.21.11", "1.21.11", 21, 94)]
    [InlineData("26.1.0", "26.1", "26.1.0", 25, 101)]
    public void TryGetVersion_ResolvesAliasesAndMetadata(
        string requested,
        string id,
        string displayId,
        int requiredJdk,
        int packFormat)
    {
        Assert.True(MinecraftVersionSupport.TryGetVersion(requested, out var version));
        Assert.Equal(id, version.ID);
        Assert.Equal(displayId, version.DisplayID);
        Assert.Equal(requiredJdk, version.RequiredJDK);
        Assert.Equal(packFormat, version.DataPackFormatMajor);
    }

    [Theory]
    [InlineData("1.20.5", false, true, true, false, false, false)]
    [InlineData("1.21.2", true, false, true, false, false, true)]
    [InlineData("1.21.11", true, false, true, true, true, true)]
    public void VersionFeatures_ChangeAtSupportedBoundaries(
        string id,
        bool pauseWhenEmpty,
        bool legacySpawnProperties,
        bool itemComponents,
        bool inlineTextComponents,
        bool namespacedGameRules,
        bool supportsInfested)
    {
        Assert.True(MinecraftVersionSupport.TryGetVersion(id, out var version));
        Assert.Equal(pauseWhenEmpty, MinecraftVersionSupport.SupportsPauseWhenEmptySeconds(id));
        Assert.Equal(legacySpawnProperties, MinecraftVersionSupport.SupportsLegacySpawnProperties(id));
        Assert.Equal(itemComponents, version.UsesItemComponents);
        Assert.Equal(inlineTextComponents, version.UsesInlineTextComponents);
        Assert.Equal(namespacedGameRules, version.UsesNamespacedGameRules);
        Assert.Equal(supportsInfested, MinecraftVersionSupport.SupportsStatusEffect(id, "infested"));
    }

    [Theory]
    [InlineData("1.20")]
    [InlineData("1.20.0")]
    [InlineData("1.20.1")]
    [InlineData("1.20.2")]
    [InlineData("1.20.3")]
    [InlineData("1.20.4")]
    public void TryGetVersion_RejectsVersionsBefore1205(string id)
    {
        Assert.False(MinecraftVersionSupport.TryGetVersion(id, out _));
    }

    [Fact]
    public void CatalogBuilders_ReturnIndependentListsWithVersionSpecificEntries()
    {
        List<EffectDefinition> firstEffects = TwitchCraftCatalogs.BuildEffectList();
        List<EffectDefinition> secondEffects = TwitchCraftCatalogs.BuildEffectList();
        List<string> minimumVersionMobs = TwitchCraftCatalogs.BuildMobList("1.20.5");
        List<string> newMobs = TwitchCraftCatalogs.BuildMobList("1.21.11");
        List<string> newLoot = TwitchCraftCatalogs.BuildLootList("1.21.0");

        Assert.NotSame(firstEffects, secondEffects);
        Assert.NotSame(firstEffects[0], secondEffects[0]);
        Assert.Contains("armadillo", minimumVersionMobs);
        Assert.DoesNotContain("bogged", minimumVersionMobs);
        Assert.Contains("nautilus", newMobs);
        Assert.Contains("chests/trial_chambers/reward_ominous", newLoot);
    }
}
