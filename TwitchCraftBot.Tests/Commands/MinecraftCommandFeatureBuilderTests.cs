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

        Assert.Equal(23, commands.Count);
        Assert.Equal("title @s times 0 100 0", commands[0]);
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

    [Fact]
    public void BuildJohnnyCommands_UsesLegacyNameAndAttributeSyntax()
    {
        List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnnyCommands(
            "@s",
            new Random(12345),
            usesInlineTextComponents: false,
            usesModernEntityAttributeNbt: false);

        Assert.Equal(3, commands.Count);
        Assert.Contains("CustomName:'{\"text\":\"Johnny\"}'", commands[0], StringComparison.Ordinal);
        Assert.Contains(
            "Attributes:[{Name:\"generic.follow_range\",Base:75.0}]",
            commands[0],
            StringComparison.Ordinal);
        Assert.EndsWith("run tag @s remove tc_johnny_new", commands[2], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildJohnnyCommands_UsesModernInlineNameAndAttributeSyntax()
    {
        List<string> commands = MinecraftCommandFeatureBuilder.BuildJohnnyCommands(
            "@s",
            new Random(12345),
            usesInlineTextComponents: true,
            usesModernEntityAttributeNbt: true);

        Assert.Equal(3, commands.Count);
        Assert.Contains("CustomName:{text:'Johnny'}", commands[0], StringComparison.Ordinal);
        Assert.Contains(
            "attributes:[{id:'minecraft:follow_range',base:75.0}]",
            commands[0],
            StringComparison.Ordinal);
        Assert.EndsWith("run tag @s remove tc_johnny_new", commands[2], StringComparison.Ordinal);
    }
}
