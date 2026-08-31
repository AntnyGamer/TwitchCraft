using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Commands;

public sealed class EnchantmentCommandBehaviorTests
{
    [Fact]
    public void PickEnchant_AlwaysUsesTheVanillaMaximumLevelRange()
    {
        Dictionary<string, int> maximumLevels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["aqua_affinity"] = 1,
            ["bane_of_arthropods"] = 5,
            ["binding_curse"] = 1,
            ["blast_protection"] = 4,
            ["breach"] = 4,
            ["channeling"] = 1,
            ["density"] = 5,
            ["depth_strider"] = 3,
            ["efficiency"] = 5,
            ["feather_falling"] = 4,
            ["fire_aspect"] = 2,
            ["fire_protection"] = 4,
            ["flame"] = 1,
            ["fortune"] = 3,
            ["frost_walker"] = 2,
            ["impaling"] = 5,
            ["infinity"] = 1,
            ["knockback"] = 2,
            ["looting"] = 3,
            ["loyalty"] = 3,
            ["luck_of_the_sea"] = 3,
            ["lure"] = 3,
            ["mending"] = 1,
            ["multishot"] = 1,
            ["piercing"] = 4,
            ["power"] = 5,
            ["projectile_protection"] = 4,
            ["protection"] = 4,
            ["punch"] = 2,
            ["quick_charge"] = 3,
            ["respiration"] = 3,
            ["riptide"] = 3,
            ["sharpness"] = 5,
            ["silk_touch"] = 1,
            ["smite"] = 5,
            ["soul_speed"] = 3,
            ["sweeping_edge"] = 3,
            ["swift_sneak"] = 3,
            ["thorns"] = 3,
            ["unbreaking"] = 3,
            ["vanishing_curse"] = 1,
            ["wind_burst"] = 3
        };

        for (int seed = 0; seed < 500; seed++)
        {
            MinecraftItemEnchantHelper.PickEnchant(
                new Random(seed),
                supportsMaceEnchantments: true,
                out string enchantID,
                out string prettyName,
                out int level);

            Assert.True(maximumLevels.TryGetValue(enchantID, out int maximum));
            Assert.NotEmpty(prettyName);
            Assert.InRange(level, 1, maximum);
        }
    }

    [Fact]
    public void PickEnchant_OnlyIncludesMaceEnchantmentsWhenSupported()
    {
        HashSet<string> maceEnchantments = new(StringComparer.OrdinalIgnoreCase) { "breach", "density", "wind_burst" };
        bool foundMaceEnchant = false;

        for (int seed = 0; seed < 500; seed++)
        {
            MinecraftItemEnchantHelper.PickEnchant(new Random(seed), false, out string oldVersionEnchant, out _, out _);
            Assert.DoesNotContain(oldVersionEnchant, maceEnchantments);

            MinecraftItemEnchantHelper.PickEnchant(new Random(seed), true, out string newVersionEnchant, out _, out _);
            foundMaceEnchant |= maceEnchantments.Contains(newVersionEnchant);
        }

        Assert.True(foundMaceEnchant);
    }

    [Fact]
    public void TryBuildEnchantCommand_AddsConflictingEnchantToAnyItemUsing1205ComponentShape()
    {
        const string selectedItem = "{id:'minecraft:stone',count:64,components:{\"minecraft:enchantments\":{levels:{\"minecraft:sharpness\":5},show_in_tooltip:1b},\"minecraft:custom_name\":'Rock'}}";

        bool result = MinecraftItemRenameHelper.TryBuildEnchantCommand(
            "@s",
            selectedItem,
            "smite",
            4,
            usesFlattenedEnchantmentsComponent: false,
            out string command,
            out string prettyItemName);

        Assert.True(result);
        Assert.Equal("Stone", prettyItemName);
        Assert.StartsWith("item replace entity @s weapon.mainhand with minecraft:stone[", command, StringComparison.Ordinal);
        Assert.Contains("minecraft:sharpness\":5", command, StringComparison.Ordinal);
        Assert.Contains("minecraft:smite\":4", command, StringComparison.Ordinal);
        Assert.Contains("show_in_tooltip:1b", command, StringComparison.Ordinal);
        Assert.Contains("minecraft:custom_name='Rock'", command, StringComparison.Ordinal);
        Assert.EndsWith(" 64", command, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildEnchantCommand_ReplacesSameEnchantLevelWithoutRemovingConflicts()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{\"minecraft:enchantments\":{levels:{\"minecraft:sharpness\":5,\"minecraft:smite\":2}}}}";

        Assert.True(MinecraftItemRenameHelper.TryBuildEnchantCommand(
            "@s",
            selectedItem,
            "smite",
            4,
            usesFlattenedEnchantmentsComponent: false,
            out string command,
            out _));

        Assert.Contains("minecraft:sharpness\":5", command, StringComparison.Ordinal);
        Assert.Contains("minecraft:smite\":4", command, StringComparison.Ordinal);
        Assert.DoesNotContain("minecraft:smite\":2", command, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(command, "minecraft:smite"));
    }

    [Fact]
    public void TryBuildEnchantCommand_UsesFlattenedComponentShapeFor1215AndNewer()
    {
        const string selectedItem = "{id:'minecraft:stick',count:1,components:{\"minecraft:enchantments\":{\"minecraft:infinity\":1}}}";

        Assert.True(MinecraftItemRenameHelper.TryBuildEnchantCommand(
            "@p",
            selectedItem,
            "mending",
            1,
            usesFlattenedEnchantmentsComponent: true,
            out string command,
            out string prettyItemName));

        Assert.Equal("Stick", prettyItemName);
        Assert.Contains("minecraft:enchantments={\"minecraft:infinity\":1,\"minecraft:mending\":1}", command, StringComparison.Ordinal);
        Assert.DoesNotContain("levels:", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{id:'minecraft:air',count:1}")]
    public void TryBuildEnchantCommand_RejectsAnEmptyHand(string selectedItemData)
    {
        Assert.False(MinecraftItemRenameHelper.TryBuildEnchantCommand(
            "@s",
            selectedItemData,
            "sharpness",
            5,
            usesFlattenedEnchantmentsComponent: false,
            out string command,
            out _));
        Assert.Empty(command);
    }

    [Fact]
    public void BuildEnchant_ProvidesAChargeableEmptyHandFallback()
    {
        Assert.Equal(
            "enchant @s minecraft:sharpness 5",
            MinecraftItemEnchantHelper.BuildEnchant("@s", "sharpness", 5));
    }

    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
