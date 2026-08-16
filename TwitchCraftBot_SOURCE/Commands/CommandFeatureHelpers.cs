using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TwitchCraftBot_V1;

internal static class MinecraftItemRenameHelper
{
    public static bool TryBuildRenameCommand(
        string selector,
        string selectedItemData,
        string redeemerName,
        bool usesItemComponents,
        bool usesInlineTextComponents,
        out string command,
        out string prettyItemName)
    {
        command = string.Empty;
        prettyItemName = string.Empty;

        if (!TryParseOuterCompound(selectedItemData, out List<string> topEntries))
            return false;

        if (!TryGetField(topEntries, "id", out string rawItemId, out _))
            return false;

        string itemID = Unquote(rawItemId);
        if (string.IsNullOrWhiteSpace(itemID) || string.Equals(itemID, "minecraft:air", StringComparison.OrdinalIgnoreCase))
            return false;

        prettyItemName = GetPrettyItemName(itemID);
        string displayName = redeemerName + "'s " + prettyItemName;
        int count = ReadCount(topEntries);

        if (usesItemComponents)
        {
            List<string> componentEntries = [];
            if (TryGetField(topEntries, "components", out string componentsValue, out _))
            {
                if (!TryParseOuterCompound(componentsValue, out componentEntries))
                    return false;
            }

            RemoveField(componentEntries, "minecraft:custom_name");
            if (usesInlineTextComponents)
                componentEntries.Add("\"minecraft:custom_name\":{text:'" + MinecraftCommandBuilder.EscapeSnbtString(displayName) + "'}");
            else
                componentEntries.Add("\"minecraft:custom_name\":\"{\\\"text\\\":\\\"" + MinecraftCommandBuilder.EscapeJson(displayName) + "\\\"}\"");

            string componentSuffix = BuildComponentSuffix(componentEntries);
            command = "item replace entity " + selector + " weapon.mainhand with " + itemID + componentSuffix + " " + count.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        List<string> tagEntries = [];
        if (TryGetField(topEntries, "tag", out string tagValue, out _))
        {
            if (!TryParseOuterCompound(tagValue, out tagEntries))
                return false;
        }

        int displayIndex = FindFieldIndex(tagEntries, "display");
        List<string> displayEntries = [];
        if (displayIndex >= 0)
        {
            string displayValue = GetFieldValue(tagEntries[displayIndex]);
            if (!TryParseOuterCompound(displayValue, out displayEntries))
                return false;
        }

        RemoveField(displayEntries, "Name");
        displayEntries.Insert(0, "Name:\"{\\\"text\\\":\\\"" + MinecraftCommandBuilder.EscapeJson(displayName) + "\\\"}\"");

        string displayEntry = "display:{" + string.Join(",", displayEntries) + "}";
        if (displayIndex >= 0)
            tagEntries[displayIndex] = displayEntry;
        else
            tagEntries.Add(displayEntry);

        string legacySuffix = tagEntries.Count == 0 ? string.Empty : "{" + string.Join(",", tagEntries) + "}";
        command = "item replace entity " + selector + " weapon.mainhand with " + itemID + legacySuffix + " " + count.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static string GetPrettyItemName(string itemID)
    {
        string normalized = itemID.Trim();
        int colonIndex = normalized.IndexOf(':');
        if (colonIndex >= 0 && colonIndex + 1 < normalized.Length)
            normalized = normalized[(colonIndex + 1)..];

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.Replace('_', ' '));
    }

    private static bool TryParseOuterCompound(string value, out List<string> entries)
    {
        entries = [];

        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
            return false;

        string inner = trimmed[1..^1].Trim();
        return TrySplitTopLevel(inner, out entries);
    }

    private static bool TrySplitTopLevel(string value, out List<string> results)
    {
        results = [];
        if (string.IsNullOrWhiteSpace(value))
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
            if (UpdateNestedState(c, ref braceDepth, ref bracketDepth, ref parenDepth, ref quote, ref escape))
                continue;

            if (c == ',' && braceDepth == 0 && bracketDepth == 0 && parenDepth == 0)
            {
                string part = value[start..i].Trim();
                if (part.Length > 0)
                    results.Add(part);

                start = i + 1;
            }
        }

        if (quote != '\0' || escape || braceDepth != 0 || bracketDepth != 0 || parenDepth != 0)
        {
            results.Clear();
            return false;
        }

        string last = value[start..].Trim();
        if (last.Length > 0)
            results.Add(last);

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
            if (UpdateNestedState(c, ref braceDepth, ref bracketDepth, ref parenDepth, ref quote, ref escape))
                continue;

            if (c == ':' && braceDepth == 0 && bracketDepth == 0 && parenDepth == 0)
                return i;
        }

        return -1;
    }

    private static bool UpdateNestedState(
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

    private static bool TryGetField(List<string> entries, string fieldName, out string value, out int index)
    {
        index = FindFieldIndex(entries, fieldName);
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
            if (FieldNameMatches(entries[i], fieldName))
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

    private static bool FieldNameMatches(string entry, string fieldName)
    {
        int colonIndex = FindTopLevelColon(entry);
        if (colonIndex <= 0)
            return false;

        string actual = entry[..colonIndex].Trim().Trim('"', '\'');
        return string.Equals(actual, fieldName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFieldValue(string entry)
    {
        int colonIndex = FindTopLevelColon(entry);
        return colonIndex >= 0 ? entry[(colonIndex + 1)..].Trim() : string.Empty;
    }

    private static string ConvertComponentEntryToItemSyntax(string entry)
    {
        int colonIndex = FindTopLevelColon(entry);
        if (colonIndex <= 0)
            return entry;

        string key = entry[..colonIndex].Trim().Trim('"', '\'');
        string value = entry[(colonIndex + 1)..].Trim();
        return key + "=" + value;
    }

    private static string BuildComponentSuffix(List<string> componentEntries)
    {
        StringBuilder builder = new(capacity: Math.Min(componentEntries.Count, 256) * 24 + 2);
        builder.Append('[');
        for (int i = 0; i < componentEntries.Count; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append(ConvertComponentEntryToItemSyntax(componentEntries[i]));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static int ReadCount(List<string> entries)
    {
        if (!TryGetField(entries, "count", out string rawCount, out _))
            return 1;

        string trimmed = rawCount.Trim();
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
    public static List<string> BuildScaredCommands(string selector, bool usesInlineTextComponents)
    {
        List<string> commands = new(23)
        {
            MinecraftCommandBuilder.TitleTimes(selector, 0, 100, 0),
            MinecraftCommandBuilder.Title(selector, "Scaredy Cat!", "gold", true, usesInlineTextComponents),
            MinecraftCommandBuilder.Subtitle(selector, "Stop being scared!", "yellow", usesInlineTextComponents)
        };

        for (int i = 0; i < 20; i++)
        {
            int offsetX = BotMainHandler.SecureRandomInt(-2, 3);
            int offsetZ = BotMainHandler.SecureRandomInt(-2, 3);
            int offsetY = BotMainHandler.SecureRandomInt(3, 6);
            commands.Add(
                "execute at " + selector +
                " run summon minecraft:cat ~" + FormatCoord(offsetX) +
                " ~" + offsetY.ToString(CultureInfo.InvariantCulture) +
                " ~" + FormatCoord(offsetZ));
        }

        return commands;
    }

    public static List<string> BuildSlaughterCommands(string selector, string mobLootGameRuleName) =>
    [
        "gamerule " + mobLootGameRuleName + " false",
        "execute at " + selector + " run kill @e[type=!minecraft:player,type=!minecraft:wither,type=!minecraft:ender_dragon,type=!minecraft:item,type=!minecraft:experience_orb,distance=..30]",
        "gamerule " + mobLootGameRuleName + " true"
    ];

    public static List<string> BuildJohnnyCommands(string selector, bool usesInlineTextComponents, bool usesModernEntityAttributeNbt)
    {
        (int offsetX, int offsetZ) = BotMainHandler.SecureRandomInt(4) switch
        {
            0 => (15, 0),
            1 => (-15, 0),
            2 => (0, 15),
            _ => (0, -15)
        };
        string johnnySelector = "@e[tag=tc_johnny_new,sort=nearest,limit=1,distance=..150]";
        string followRangeData = usesModernEntityAttributeNbt
            ? "attributes:[{id:'minecraft:follow_range',base:75.0}]"
            : "Attributes:[{Name:\"generic.follow_range\",Base:75.0}]";
        string summonData = usesInlineTextComponents
            ? "{CustomName:{text:'Johnny'},CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b,Tags:['tc_johnny','tc_johnny_new']," + followRangeData + "}"
            : "{CustomName:'{\"text\":\"Johnny\"}',CustomNameVisible:1b,Invulnerable:1b,PersistenceRequired:1b,Glowing:1b,Tags:[\"tc_johnny\",\"tc_johnny_new\"]," + followRangeData + "}";

        return
        [
            "execute at " + selector + " run summon minecraft:vindicator ~" + offsetX.ToString(CultureInfo.InvariantCulture) + " ~100 ~" + offsetZ.ToString(CultureInfo.InvariantCulture) + " " + summonData,
            "execute at " + selector + " as " + johnnySelector + " run effect give @s minecraft:glowing 255 0 true",
            "execute at " + selector + " as " + johnnySelector + " run tag @s remove tc_johnny_new"
        ];
    }

    private static string FormatCoord(int value)
    {
        if (value == 0)
            return string.Empty;

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
