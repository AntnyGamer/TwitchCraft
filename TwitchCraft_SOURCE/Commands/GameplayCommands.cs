using System;
using System.Collections.Generic;
using System.Globalization;

namespace TwitchCraft_V1;

internal static class GameplayCommands
{
    public static List<string> BuildScared(string selector, Random random, bool usesInlineTextComponents)
    {
        ArgumentNullException.ThrowIfNull(random);

        List<string> commands = new(23)
        {
            MinecraftCommandBuilder.TitleTimes(selector, 0, 100, 0),
            MinecraftCommandBuilder.Title(selector, "Scaredy Cat!", "gold", true, usesInlineTextComponents),
            MinecraftCommandBuilder.Subtitle(selector, "Stop being scared!", "yellow", usesInlineTextComponents)
        };

        for (int i = 0; i < 20; i++)
        {
            int offsetX = random.Next(-2, 3);
            int offsetZ = random.Next(-2, 3);
            int offsetY = random.Next(3, 6);
            commands.Add(
                "execute at " + selector +
                " run summon minecraft:cat ~" + FormatCoord(offsetX) +
                " ~" + offsetY.ToString(CultureInfo.InvariantCulture) +
                " ~" + FormatCoord(offsetZ));
        }

        return commands;
    }

    public static string SlaughterRestoreCommand(string gameRule) =>
        "execute if data storage twitchcraft:runtime {slaughter_mob_loot:1b} store result storage twitchcraft:runtime slaughter_mob_loot byte 0 run gamerule " + gameRule + " true";

    public static string[] BuildSlaughter(string selector, string mobLootGameRuleName) =>
        [
            "execute unless data storage twitchcraft:runtime {slaughter_mob_loot:1b} store result storage twitchcraft:runtime slaughter_mob_loot byte 1 run gamerule " + mobLootGameRuleName,
            "gamerule " + mobLootGameRuleName + " false",
            "execute at " + selector + " as @e[type=!minecraft:player,type=!minecraft:wither,type=!minecraft:ender_dragon,type=!minecraft:armor_stand,distance=..30] if data entity @s Health run kill @s",
            SlaughterRestoreCommand(mobLootGameRuleName)
        ];

    public static string[] BuildJohnny(string selector, Random random, bool usesInlineTextComponents, bool usesModernEntityAttributeNbt)
        => BuildPursuer(
            selector,
            random,
            usesInlineTextComponents,
            usesModernEntityAttributeNbt,
            "vindicator",
            "Johnny",
            "tc_johnny",
            string.Empty,
            "Johnny is coming!");

    public static string[] BuildChargedCreeper(string selector, Random random, bool usesInlineTextComponents, bool usesModernEntityAttributeNbt)
        => BuildPursuer(
            selector,
            random,
            usesInlineTextComponents,
            usesModernEntityAttributeNbt,
            "creeper",
            "Charged Creeper",
            "tc_charged_creeper",
            "powered:1b,",
            "A charged creeper is coming!");

    private static string[] BuildPursuer(
        string selector,
        Random random,
        bool usesInlineTextComponents,
        bool usesModernEntityAttributeNbt,
        string entityID,
        string displayName,
        string entityTag,
        string additionalNbt,
        string warning)
    {
        ArgumentNullException.ThrowIfNull(random);

        (int offsetX, int offsetZ) = random.Next(4) switch
        {
            0 => (15, 0),
            1 => (-15, 0),
            2 => (0, 15),
            _ => (0, -15)
        };
        string newEntityTag = $"{entityTag}_new_{Guid.NewGuid():N}";
        string entitySelector = "@e[tag=" + newEntityTag + ",sort=nearest,limit=1,distance=..150]";
        string followRangeData = usesModernEntityAttributeNbt
            ? "attributes:[{id:'minecraft:follow_range',base:80.0}]"
            : "Attributes:[{Name:\"generic.follow_range\",Base:80.0}]";
        string summonData = usesInlineTextComponents
            ? "{CustomName:{text:'" + MinecraftCommandBuilder.EscapeSnbt(displayName) +
                "'},CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b," + additionalNbt + "Tags:['" +
                entityTag + "','" + newEntityTag + "']," + followRangeData + "}"
            : "{CustomName:'{\"text\":\"" + MinecraftCommandBuilder.EscapeJson(displayName) +
                "\"}',CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b," + additionalNbt +
                "Tags:[\"" + entityTag + "\",\"" + newEntityTag + "\"]," + followRangeData + "}";

        return
        [
            "execute at " + selector + " run summon minecraft:" + entityID + " ~" + offsetX.ToString(CultureInfo.InvariantCulture) + " ~100 ~" +
                offsetZ.ToString(CultureInfo.InvariantCulture) + " " + summonData,
            "execute at " + selector + " as " + entitySelector + " run effect give @s minecraft:glowing 255 0 true",
            "execute at " + selector + " as " + entitySelector + " run tag @s remove " + newEntityTag,
            MinecraftCommandBuilder.TitleTimes(selector, 0, 100, 10),
            MinecraftCommandBuilder.Title(selector, " ", "white", usesInlineTextComponents),
            MinecraftCommandBuilder.Subtitle(selector, warning, "red", usesInlineTextComponents)
        ];
    }

    private static string FormatCoord(int value)
    {
        if (value == 0)
        {
            return string.Empty;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
