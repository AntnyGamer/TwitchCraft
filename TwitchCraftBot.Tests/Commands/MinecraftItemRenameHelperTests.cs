using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

public sealed class MinecraftItemRenameHelperTests
{
    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{count:1b}")]
    [InlineData("{id:'minecraft:air',count:1b}")]
    public void TryBuildRenameCommand_RejectsInvalidOrEmptySelections(string selectedItemData)
    {
        bool result = MinecraftItemRenameHelper.TryBuildRenameCommand(
            "@s",
            selectedItemData,
            "Alice",
            usesItemComponents: false,
            usesInlineTextComponents: false,
            out string command,
            out string prettyItemName);

        Assert.False(result);
        Assert.Empty(command);
        Assert.Empty(prettyItemName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TryBuildRenameCommand_UsesSyntaxRequiredByTheMinecraftVersion(
        bool usesItemComponents,
        bool usesInlineTextComponents)
    {
        string selectedItemData = usesItemComponents
            ? "{id:'minecraft:diamond_sword',count:2,components:{\"minecraft:damage\":5}}"
            : "{id:'minecraft:diamond_sword',Count:2b,tag:{Damage:5}}";

        bool result = MinecraftItemRenameHelper.TryBuildRenameCommand(
            "@s",
            selectedItemData,
            "Alice",
            usesItemComponents,
            usesInlineTextComponents,
            out string command,
            out string prettyItemName);

        Assert.True(result);
        Assert.Equal("Diamond Sword", prettyItemName);
        Assert.StartsWith(
            "item replace entity @s weapon.mainhand with minecraft:diamond_sword",
            command,
            StringComparison.Ordinal);
        Assert.EndsWith(" 2", command, StringComparison.Ordinal);

        if (!usesItemComponents)
        {
            Assert.Contains("Damage:5", command, StringComparison.Ordinal);
            Assert.Contains("display:{Name:", command, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("minecraft:damage=5", command, StringComparison.Ordinal);
            Assert.Contains(
                usesInlineTextComponents
                    ? "minecraft:custom_name={text:'Alice\\'s Diamond Sword'}"
                    : "minecraft:custom_name=\"{\\\"text\\\":\\\"Alice's Diamond Sword\\\"}\"",
                command,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TryBuildRenameCommand_ReplacesExistingCustomNameWithoutDroppingOtherComponents()
    {
        bool result = MinecraftItemRenameHelper.TryBuildRenameCommand(
            "@p",
            "{id:'minecraft:stick',count:3,components:{\"minecraft:custom_name\":'old',\"minecraft:damage\":1}}",
            "Bob",
            usesItemComponents: true,
            usesInlineTextComponents: true,
            out string command,
            out string prettyItemName);

        Assert.True(result);
        Assert.Equal("Stick", prettyItemName);
        Assert.DoesNotContain("old", command, StringComparison.Ordinal);
        Assert.Contains("minecraft:damage=1", command, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(command, "minecraft:custom_name"));
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
