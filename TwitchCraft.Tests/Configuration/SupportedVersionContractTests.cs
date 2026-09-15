using TwitchCraft_V1;
using TwitchCraft_V1.Setup;

namespace TwitchCraft.Tests.Configuration;

public sealed class SupportedVersionContractTests
{
    [Theory]
    [InlineData("1.20.5", "1.20.5", "1.20.5", 21, 41, 0)]
    [InlineData("1.21.0", "1.21.0", "1.21.0", 21, 48, 0)]
    [InlineData("1.21.11", "1.21.11", "1.21.11", 21, 94, 1)]
    [InlineData("26.1.0", "26.1", "26.1.0", 25, 101, 1)]
    [InlineData("26.2.0", "26.2", "26.2.0", 25, 107, 1)]
    [InlineData("26.3.0", "26.3", "26.3.0", 25, 121, 0)]
    public void TryGetVersion_ResolvesAliasesAndMetadata(
        string requested,
        string ID,
        string displayID,
        int requiredJdk,
        int packFormatMajor,
        int packFormatMinor)
    {
        Assert.True(MinecraftVersionSupport.TryGetVersion(requested, out var version));
        Assert.Equal(ID, version.ID);
        Assert.Equal(displayID, version.DisplayID);
        Assert.Equal(requiredJdk, version.RequiredJDK);
        Assert.Equal(packFormatMajor, version.DataPackFormatMajor);
        Assert.Equal(packFormatMinor, version.DataPackFormatMinor);
    }

    [Theory]
    [InlineData("1.20.5", false, false, false, false, false)]
    [InlineData("1.21.2", true, false, false, false, true)]
    [InlineData("1.21.11", true, true, true, true, true)]
    public void VersionFeatures_ChangeAtSupportedBoundaries(
        string ID,
        bool singularFunctionDirectories,
        bool serverSettingGameRules,
        bool inlineTextComponents,
        bool namespacedGameRules,
        bool supportsInfested)
    {
        Assert.True(MinecraftVersionSupport.TryGetVersion(ID, out var version));
        Assert.Equal(singularFunctionDirectories, version.UsesSingularFunctionDirectories);
        Assert.Equal(serverSettingGameRules, version.UsesServerSettingGameRules);
        Assert.Equal(inlineTextComponents, version.UsesInlineTextComponents);
        Assert.Equal(namespacedGameRules, version.UsesNamespacedGameRules);
        Assert.Equal(supportsInfested, MinecraftVersionSupport.SupportsStatusEffect(ID, "infested"));
    }

    [Theory]
    [InlineData("1.20")]
    [InlineData("1.20.0")]
    [InlineData("1.20.1")]
    [InlineData("1.20.2")]
    [InlineData("1.20.3")]
    [InlineData("1.20.4")]
    public void TryGetVersion_RejectsVersionsBefore1205(string ID)
    {
        Assert.False(MinecraftVersionSupport.TryGetVersion(ID, out _));
    }

    [Fact]
    public void CatalogBuilders_ReturnIndependentListsWithVersionSpecificEntries()
    {
        List<EffectDefinition> firstEffects = Catalogs.BuildEffects();
        List<EffectDefinition> secondEffects = Catalogs.BuildEffects();
        List<string> minimumVersionMobs = Catalogs.BuildMobs("1.20.5");
        List<string> newMobs = Catalogs.BuildMobs("1.21.11");
        List<string> trialLoot = Catalogs.BuildLoot("1.21.0");
        List<string> previousLoot = Catalogs.BuildLoot("26.2");
        List<string> newLoot = Catalogs.BuildLoot("26.3");

        Assert.NotSame(firstEffects, secondEffects);
        Assert.NotSame(firstEffects[0], secondEffects[0]);
        Assert.Contains("armadillo", minimumVersionMobs);
        Assert.DoesNotContain("bogged", minimumVersionMobs);
        Assert.Contains("nautilus", newMobs);
        Assert.Contains("chests/trial_chambers/reward_ominous", trialLoot);
        Assert.Equal(previousLoot.Count + 3, newLoot.Count);
        Assert.Contains("barrels/abandoned_camp_barrel", newLoot);
        Assert.Contains("chests/abandoned_camp_common_chest", newLoot);
        Assert.Contains("chests/abandoned_camp_secret_chest", newLoot);
    }
}
