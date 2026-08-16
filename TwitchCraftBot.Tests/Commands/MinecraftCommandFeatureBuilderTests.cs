using System.Text.RegularExpressions;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Commands;

public sealed class MinecraftCommandFeatureBuilderTests
{
    [Fact]
    public void BuildScaredCommands_CreatesTwentyCatsWithinConfiguredOffsets()
    {
        List<string> commands = MinecraftCommandFeatureBuilder.BuildScaredCommands(
            "@s",
            new Random(12345),
            usesInlineTextComponents: false);
        Regex summonPattern = new(
            @"^execute at @s run summon minecraft:cat ~(-?[0-2])? ~[3-5] ~(-?[0-2])?$",
            RegexOptions.CultureInvariant);

        Assert.Equal(
            [
                "title @s times 0 100 0",
                "title @s title {\"text\":\"Scaredy Cat!\",\"color\":\"gold\",\"bold\":true}",
                "title @s subtitle {\"text\":\"Stop being scared!\",\"color\":\"yellow\",\"bold\":false}"
            ],
            commands.Take(3));
        Assert.Equal(23, commands.Count);
        Assert.All(commands.Skip(3), command => Assert.Matches(summonPattern, command));
    }

    [Fact]
    public void BuildSlaughterCommands_DisablesAndRestoresMobLootAroundTheKill()
    {
        List<string> commands = MinecraftCommandFeatureBuilder.BuildSlaughterCommands(
            "@p",
            "minecraft:doMobLoot");

        Assert.Equal(
            [
                "gamerule minecraft:doMobLoot false",
                "execute at @p run kill @e[type=!minecraft:player,type=!minecraft:wither,type=!minecraft:ender_dragon,type=!minecraft:item,type=!minecraft:experience_orb,distance=..30]",
                "gamerule minecraft:doMobLoot true"
            ],
            commands);
    }

    [Theory]
    [InlineData(
        false,
        false,
        "CustomName:'{\"text\":\"Johnny\"}'",
        "Attributes:[{Name:\"generic.follow_range\",Base:75.0}]")]
    [InlineData(
        true,
        true,
        "CustomName:{text:'Johnny'}",
        "attributes:[{id:'minecraft:follow_range',base:75.0}]")]
    public void BuildJohnnyCommands_UsesVersionAppropriateNameAndAttributeSyntax(
        bool usesInlineTextComponents,
        bool usesModernEntityAttributeNbt,
        string expectedName,
        string expectedAttributes)
    {
        List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnnyCommands(
            "@s",
            new Random(12345),
            usesInlineTextComponents,
            usesModernEntityAttributeNbt);

        Assert.Equal(3, commands.Count);
        Assert.Contains(expectedName, commands[0], StringComparison.Ordinal);
        Assert.Contains(expectedAttributes, commands[0], StringComparison.Ordinal);
        Assert.EndsWith("run tag @s remove tc_johnny_new", commands[2], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScaredCommands_WithTheSameSeedProducesTheSameCommands()
    {
        List<string> first = MinecraftCommandFeatureBuilder.BuildScaredCommands("@s", new Random(8675309), false);
        List<string> second = MinecraftCommandFeatureBuilder.BuildScaredCommands("@s", new Random(8675309), false);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildJohnnyCommands_WithTheSameSeedProducesTheSameCommands()
    {
        List<string> first = MinecraftCommandFeatureBuilder.BuildJohnnyCommands("@s", new Random(8675309), false, false);
        List<string> second = MinecraftCommandFeatureBuilder.BuildJohnnyCommands("@s", new Random(8675309), false, false);

        Assert.Equal(first, second);
    }
}
