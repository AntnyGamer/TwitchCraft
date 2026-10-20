using static TwitchCraft_V1.MinecraftCommandBuilder;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Commands;

public sealed class MinecraftCommandSafetyTests
{
    [Fact]
    public void EscapeJson_EscapesQuotesBackslashesAndControlCharacters()
    {
        string result = EscapeJson("hello \"world\"\\\r\n\t\u0001☃");

        Assert.Equal("hello \\\"world\\\"\\\\\\r\\n\\t\\u0001☃", result);
    }

    [Fact]
    public void EscapeSnbt_EscapesSingleQuotesAndControlCharacters()
    {
        Assert.Equal("it\\'s\\na\\\\b", EscapeSnbt("it's\na\\b"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("\"", "\\\"")]
    [InlineData("\\", "\\\\")]
    [InlineData("'", "'")]
    [InlineData("Player😀", "Player😀")]
    [InlineData("A\"B\\C", "A\\\"B\\\\C")]
    public void EscapeSelector_PreservesSafeTextAndEscapesSyntax(string? value, string expected)
    {
        Assert.Equal(expected, EscapeSelector(value));
    }

    [Theory]
    [InlineData(null, false, "")]
    [InlineData("ab", false, "")]
    [InlineData("abcdefghijklmnopq", false, "")]
    [InlineData("bad-name", false, "")]
    [InlineData(" Player_1 ", true, "Player_1")]
    public void TryNormalizePlayerName_EnforcesMinecraftNameRules(
        string? value,
        bool expectedResult,
        string expectedNormalized)
    {
        Assert.Equal(expectedResult, MinecraftNameHelper.TryNormalizePlayerName(value, out string normalized));
        Assert.Equal(expectedNormalized, normalized);
        Assert.Equal(expectedResult, MinecraftNameHelper.TryNormalizePlayerName(value.AsSpan(), out normalized));
        Assert.Equal(expectedNormalized, normalized);
    }

    [Fact]
    public void Tellraw_UsesVersionCorrectEscapedTextComponent()
    {
        Assert.Equal(
            "tellraw @a {\"text\":\"hello \\\"world\\\"\",\"color\":\"red\",\"bold\":true}",
            Tellraw("@a", "hello \"world\"", "red", true, false));
        Assert.Equal(
            "tellraw @a {text:'it\\'s fine',color:'gold',bold:false}",
            Tellraw("@a", "it's fine", "gold", false, true));
    }

    [Fact]
    public void BanAndKickPlayer_ReplaceControlCharactersInReasons()
    {
        Assert.Equal(
            "ban Player rude  reason",
            ModeratePlayer("Player", " rude\r\nreason\t ", ban: true));
        Assert.Equal(
            "kick Player rude  reason",
            ModeratePlayer("Player", " rude\r\nreason\t ", ban: false));
    }

    [Fact]
    public void NewGameplayAndWhitelistCommands_UseModernJavaSyntax()
    {
        Assert.Equal("execute as @a at @s run tp @s ~ ~ ~ ~180 ~", TurnAround("@a"));
        Assert.Equal("whitelist add Player", Whitelist("Player", add: true));
        Assert.Equal("whitelist remove Player", Whitelist("Player", add: false));
    }

    [Fact]
    public void AttributeCommands_UseVersionAppropriateSyntax()
    {
        const string Selector = "@a[name=\"Player\",limit=1]";
        const string UUID = "11111111-1111-1111-1111-111111111111";
        Assert.Equal(
            "execute as @a[name=\"Player\",limit=1] run attribute @s minecraft:generic.max_health modifier add 11111111-1111-1111-1111-111111111111 twitchcraft_health -4 add_value",
            AddMaxHealthModifier(Selector, UUID, -4, modernAttribute: false, namespacedID: false));
        Assert.Equal(
            "execute as @a[name=\"Player\",limit=1] run attribute @s minecraft:generic.max_health modifier add twitchcraft:heart_1 6 add_value",
            AddMaxHealthModifier(Selector, "twitchcraft:heart_1", 6, modernAttribute: false, namespacedID: true));
        Assert.Equal(
            "execute as @a[name=\"Player\",limit=1] run attribute @s minecraft:max_health modifier remove twitchcraft:heart_1",
            RemoveMaxHealthModifier(Selector, "twitchcraft:heart_1", modernAttribute: true));
        Assert.Equal(
            "execute as @a run attribute @s minecraft:generic.scale base set 0.5",
            SetScale("@a", 0.5, usesModernAttributeIDs: false));
        Assert.Equal(
            "execute as @s run attribute @s minecraft:scale base set 2",
            SetScale("@s", 2.0, usesModernAttributeIDs: true));
    }

    [Fact]
    public void Loot_FormatsOffsetsWithInvariantCompactDecimals()
    {
        string result = Loot("@s", "chests/simple_dungeon", 1.25, -2.5);

        Assert.Equal("execute at @s run loot spawn ~1.25 ~ ~-2.5 loot minecraft:chests/simple_dungeon", result);
    }
}
