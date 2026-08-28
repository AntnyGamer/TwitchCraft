using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

public sealed class MinecraftCommandBuilderTests
{
    [Fact]
    public void EscapeJson_EscapesQuotesBackslashesAndControlCharacters()
    {
        string result = MinecraftCommandBuilder.EscapeJson("hello \"world\"\\\r\n\t\u0001☃");

        Assert.Equal("hello \\\"world\\\"\\\\\\r\\n\\t\\u0001☃", result);
    }

    [Fact]
    public void EscapeSnbtString_EscapesSingleQuotesAndControlCharacters()
    {
        string result = MinecraftCommandBuilder.EscapeSnbtString("it's\na\\b");

        Assert.Equal("it\\'s\\na\\\\b", result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("\"", "\\\"")]
    [InlineData("\\", "\\\\")]
    [InlineData("'", "'")]
    [InlineData("Player😀", "Player😀")]
    [InlineData("plain unicode ☃", "plain unicode ☃")]
    [InlineData("A\"B\\C", "A\\\"B\\\\C")]
    public void EscapeSelectorValue_PreservesSafeTextAndEscapesSyntax(string? value, string expected)
    {
        Assert.Equal(expected, MinecraftCommandBuilder.EscapeSelectorValue(value));
    }

    [Fact]
    public void Tellraw_UsesEscapedLegacyJsonTextComponent()
    {
        string result = MinecraftCommandBuilder.Tellraw("@a", "hello \"world\"", "red", true, false);

        Assert.Equal("tellraw @a {\"text\":\"hello \\\"world\\\"\",\"color\":\"red\",\"bold\":true}", result);
    }

    [Fact]
    public void Tellraw_UsesEscapedInlineTextComponent()
    {
        string result = MinecraftCommandBuilder.Tellraw("@a", "it's fine", "gold", false, true);

        Assert.Equal("tellraw @a {text:'it\\'s fine',color:'gold',bold:false}", result);
    }

    [Fact]
    public void BanAndKickPlayer_ReplaceControlCharactersInReasons()
    {
        Assert.Equal(
            "ban Player rude  reason",
            MinecraftCommandBuilder.BanPlayer("Player", " rude\r\nreason\t "));
        Assert.Equal(
            "kick Player rude  reason",
            MinecraftCommandBuilder.KickPlayer("Player", " rude\r\nreason\t "));
    }

    [Fact]
    public void NewGameplayAndWhitelistCommands_UseModernJavaSyntax()
    {
        Assert.Equal("execute as @a at @s run tp @s ~ ~ ~ ~180 ~", MinecraftCommandBuilder.TurnAround("@a"));
        Assert.Equal("whitelist add Player", MinecraftCommandBuilder.WhitelistAdd("Player"));
        Assert.Equal("whitelist remove Player", MinecraftCommandBuilder.WhitelistRemove("Player"));
    }

    [Fact]
    public void SetScale_UsesTheAttributeNameForTheSelectedMinecraftVersion()
    {
        Assert.Equal(
            "execute as @a run attribute @s minecraft:generic.scale base set 0.5",
            MinecraftCommandBuilder.SetScale("@a", 0.5, usesModernAttributeIds: false));
        Assert.Equal(
            "execute as @s run attribute @s minecraft:scale base set 2",
            MinecraftCommandBuilder.SetScale("@s", 2.0, usesModernAttributeIds: true));
    }

    [Fact]
    public void Loot_FormatsOffsetsWithInvariantCompactDecimals()
    {
        string result = MinecraftCommandBuilder.Loot("@s", "chests/simple_dungeon", 1.25, -2.5);

        Assert.Equal("execute at @s run loot spawn ~1.25 ~ ~-2.5 loot minecraft:chests/simple_dungeon", result);
    }
}
