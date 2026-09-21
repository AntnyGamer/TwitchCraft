using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TwitchCraft_V1;

internal static class MinecraftItemComponentHelper
{
    public static bool TryBuildRenameCommand(
        string selector,
        string selectedItemData,
        string redeemerName,
        bool usesInlineTextComponents,
        out string command,
        out string prettyItemName)
    {
        command = string.Empty;
        prettyItemName = string.Empty;

        if (!TryParseCompound(selectedItemData, out List<string> topEntries) ||
            !TryGetField(topEntries, "id", out string rawItemID))
            return false;

        string itemID = Unquote(rawItemID);
        if (string.IsNullOrWhiteSpace(itemID) || string.Equals(itemID, "minecraft:air", StringComparison.OrdinalIgnoreCase))
            return false;

        prettyItemName = GetItemName(itemID);
        string displayName = redeemerName + "'s " + prettyItemName;
        int count = ReadCount(topEntries);

        List<string> componentEntries = [];
        if (TryGetField(topEntries, "components", out string componentsValue) &&
            !TryParseCompound(componentsValue, out componentEntries))
            return false;

        RemoveField(componentEntries, "minecraft:custom_name");
        if (usesInlineTextComponents)
            componentEntries.Add("\"minecraft:custom_name\":{text:'" + MinecraftCommandBuilder.EscapeSnbt(displayName) + "'}");
        else
            componentEntries.Add("\"minecraft:custom_name\":\"{\\\"text\\\":\\\"" + MinecraftCommandBuilder.EscapeJson(displayName) + "\\\"}\"");

        string componentSuffix = BuildComponentSuffix(componentEntries);
        command = "item replace entity " + selector + " weapon.mainhand with " + itemID + componentSuffix + " " + count.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    internal static bool TryBuildEnchantCommand(
        string selector,
        string selectedItemData,
        string enchantID,
        int level,
        bool usesFlattenedEnchantmentsComponent,
        out string command,
        out string prettyItemName)
    {
        command = string.Empty;
        prettyItemName = string.Empty;

        if (string.IsNullOrWhiteSpace(selector) || string.IsNullOrWhiteSpace(enchantID) || level <= 0 ||
            !TryParseCompound(selectedItemData, out List<string> topEntries) ||
            !TryGetField(topEntries, "id", out string rawItemID))
        {
            return false;
        }

        string itemID = Unquote(rawItemID).Trim();
        if (itemID.Length == 0 || string.Equals(itemID, "minecraft:air", StringComparison.OrdinalIgnoreCase))
            return false;

        List<string> componentEntries = [];
        if (TryGetField(topEntries, "components", out string componentsValue) &&
            !TryParseCompound(componentsValue, out componentEntries))
        {
            return false;
        }

        string normalizedEnchantID = enchantID.Trim();
        if (normalizedEnchantID.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
            normalizedEnchantID = normalizedEnchantID["minecraft:".Length..];
        if (normalizedEnchantID.Length == 0)
            return false;

        string namespacedEnchantID = "minecraft:" + normalizedEnchantID;
        int enchantmentsIndex = FindFieldIndex(componentEntries, "minecraft:enchantments");
        List<string> enchantmentComponentEntries = [];
        if (enchantmentsIndex >= 0)
        {
            string existingEnchantments = GetFieldValue(componentEntries[enchantmentsIndex]);
            if (!TryParseCompound(existingEnchantments, out enchantmentComponentEntries))
                return false;
        }

        if (usesFlattenedEnchantmentsComponent)
        {
            RemoveField(enchantmentComponentEntries, namespacedEnchantID);
            enchantmentComponentEntries.Add("\"" + namespacedEnchantID + "\":" + level.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            int levelsIndex = FindFieldIndex(enchantmentComponentEntries, "levels");
            List<string> levelEntries = [];
            if (levelsIndex >= 0 &&
                !TryParseCompound(GetFieldValue(enchantmentComponentEntries[levelsIndex]), out levelEntries))
                return false;

            RemoveField(levelEntries, namespacedEnchantID);
            levelEntries.Add("\"" + namespacedEnchantID + "\":" + level.ToString(CultureInfo.InvariantCulture));
            string levelsEntry = "levels:{" + string.Join(",", levelEntries) + "}";
            if (levelsIndex >= 0)
                enchantmentComponentEntries[levelsIndex] = levelsEntry;
            else
                enchantmentComponentEntries.Insert(0, levelsEntry);
        }

        string enchantmentsEntry = "\"minecraft:enchantments\":{" + string.Join(",", enchantmentComponentEntries) + "}";
        if (enchantmentsIndex >= 0)
            componentEntries[enchantmentsIndex] = enchantmentsEntry;
        else
            componentEntries.Add(enchantmentsEntry);

        int count = ReadCount(topEntries);
        prettyItemName = GetItemName(itemID);
        command = "item replace entity " + selector + " weapon.mainhand with " + itemID +
            BuildComponentSuffix(componentEntries) + " " + count.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    internal static string GetItemName(string itemID)
    {
        string normalized = itemID.Trim();
        int colonIndex = normalized.IndexOf(':');
        if (colonIndex >= 0 && colonIndex + 1 < normalized.Length)
            normalized = normalized[(colonIndex + 1)..];

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.Replace('_', ' '));
    }

    private static bool TryParseCompound(string value, out List<string> entries)
    {
        entries = [];

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
            return false;

        return TrySplitTopLevel(trimmed[1..^1].Trim(), entries);
    }

    private static bool TrySplitTopLevel(ReadOnlySpan<char> value, List<string> results)
    {
        if (value.IsWhiteSpace())
            return true;

        int start = 0;
        int braceDepth = 0;
        int bracketDepth = 0;
        int parenDepth = 0;
        char quote = '\0';
        bool escape = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (UpdateState(c, ref braceDepth, ref bracketDepth, ref parenDepth, ref quote, ref escape))
                continue;

            if (c == ',' && braceDepth == 0 && bracketDepth == 0 && parenDepth == 0)
            {
                ReadOnlySpan<char> part = value[start..i].Trim();
                if (part.Length > 0)
                    results.Add(part.ToString());

                start = i + 1;
            }
        }

        if (quote != '\0' || escape || braceDepth != 0 || bracketDepth != 0 || parenDepth != 0)
        {
            results.Clear();
            return false;
        }

        ReadOnlySpan<char> last = value[start..].Trim();
        if (last.Length > 0)
            results.Add(last.ToString());

        return true;
    }

    private static int FindTopLevelColon(string value)
    {
        int braceDepth = 0;
        int bracketDepth = 0;
        int parenDepth = 0;
        char quote = '\0';
        bool escape = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (UpdateState(c, ref braceDepth, ref bracketDepth, ref parenDepth, ref quote, ref escape))
                continue;

            if (c == ':' && braceDepth == 0 && bracketDepth == 0 && parenDepth == 0)
                return i;
        }

        return -1;
    }

    private static bool UpdateState(
        char c,
        ref int braceDepth,
        ref int bracketDepth,
        ref int parenDepth,
        ref char quote,
        ref bool escape)
    {
        if (quote != '\0')
        {
            if (escape)
            {
                escape = false;
                return true;
            }

            if (c == '\\')
            {
                escape = true;
                return true;
            }

            if (c == quote)
                quote = '\0';

            return true;
        }

        if (c == '\'' || c == '"')
        {
            quote = c;
            return true;
        }

        switch (c)
        {
            case '{':
                braceDepth++;
                return true;
            case '}':
                if (braceDepth > 0)
                    braceDepth--;
                return true;
            case '[':
                bracketDepth++;
                return true;
            case ']':
                if (bracketDepth > 0)
                    bracketDepth--;
                return true;
            case '(':
                parenDepth++;
                return true;
            case ')':
                if (parenDepth > 0)
                    parenDepth--;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetField(List<string> entries, string fieldName, out string value)
    {
        int index = FindFieldIndex(entries, fieldName);
        if (index < 0)
        {
            value = string.Empty;
            return false;
        }

        value = GetFieldValue(entries[index]);
        return true;
    }

    private static int FindFieldIndex(List<string> entries, string fieldName)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (NameMatches(entries[i], fieldName))
                return i;
        }

        return -1;
    }

    private static void RemoveField(List<string> entries, string fieldName)
    {
        int index = FindFieldIndex(entries, fieldName);
        if (index >= 0)
            entries.RemoveAt(index);
    }

    private static bool NameMatches(string entry, string fieldName)
    {
        int colonIndex = FindTopLevelColon(entry);
        if (colonIndex <= 0)
            return false;

        ReadOnlySpan<char> actual = entry.AsSpan(0, colonIndex).Trim().Trim("\"'");
        return actual.Equals(fieldName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFieldValue(string entry)
    {
        int colonIndex = FindTopLevelColon(entry);
        return colonIndex >= 0 ? entry.AsSpan(colonIndex + 1).Trim().ToString() : string.Empty;
    }

    private static string BuildComponentSuffix(List<string> componentEntries)
    {
        StringBuilder builder = new(capacity: Math.Min(componentEntries.Count, 256) * 24 + 2);
        builder.Append('[');
        for (int i = 0; i < componentEntries.Count; i++)
        {
            if (i > 0)
                builder.Append(',');

            string entry = componentEntries[i];
            int colonIndex = FindTopLevelColon(entry);
            if (colonIndex <= 0)
            {
                builder.Append(entry);
                continue;
            }

            builder.Append(entry.AsSpan(0, colonIndex).Trim().Trim("\"'"));
            builder.Append('=');
            builder.Append(entry.AsSpan(colonIndex + 1).Trim());
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static int ReadCount(List<string> entries)
    {
        if (!TryGetField(entries, "count", out string rawCount))
            return 1;

        ReadOnlySpan<char> trimmed = rawCount.AsSpan().Trim();
        while (trimmed.Length > 0 && char.IsLetter(trimmed[^1]))
            trimmed = trimmed[..^1];

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0
            ? count
            : 1;
    }

    private static string Unquote(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
            return trimmed[1..^1];

        return trimmed;
    }
}

internal static class MinecraftCommandFeatureBuilder
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

    public static List<string> BuildSlaughter(string selector, string mobLootGameRuleName) =>
    [
        "gamerule " + mobLootGameRuleName + " false",
        "execute at " + selector + " as @e[type=!minecraft:player,type=!minecraft:wither,type=!minecraft:ender_dragon,type=!minecraft:armor_stand,distance=..30] if data entity @s Health run kill @s",
        "gamerule " + mobLootGameRuleName + " true"
    ];

    public static List<string> BuildJohnny(string selector, Random random, bool usesInlineTextComponents, bool usesModernEntityAttributeNbt)
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

    public static List<string> BuildChargedCreeper(string selector, Random random, bool usesInlineTextComponents, bool usesModernEntityAttributeNbt)
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

    private static List<string> BuildPursuer(
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
        string newEntityTag = entityTag + "_new_" + Guid.NewGuid().ToString("N");
        string entitySelector = "@e[tag=" + newEntityTag + ",sort=nearest,limit=1,distance=..150]";
        string followRangeData = usesModernEntityAttributeNbt
            ? "attributes:[{id:'minecraft:follow_range',base:80.0}]"
            : "Attributes:[{Name:\"generic.follow_range\",Base:80.0}]";
        string summonData = usesInlineTextComponents
            ? "{CustomName:{text:'" + MinecraftCommandBuilder.EscapeSnbt(displayName) + "'},CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b," + additionalNbt + "Tags:['" + entityTag + "','" + newEntityTag + "']," + followRangeData + "}"
            : "{CustomName:'{\"text\":\"" + MinecraftCommandBuilder.EscapeJson(displayName) + "\"}',CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b," + additionalNbt + "Tags:[\"" + entityTag + "\",\"" + newEntityTag + "\"]," + followRangeData + "}";

        return
        [
            "execute at " + selector + " run summon minecraft:" + entityID + " ~" + offsetX.ToString(CultureInfo.InvariantCulture) + " ~100 ~" + offsetZ.ToString(CultureInfo.InvariantCulture) + " " + summonData,
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
            return string.Empty;

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
