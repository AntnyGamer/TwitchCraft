using System;
using System.Globalization;
using System.Text;

namespace TwitchCraft_V1;

public static class MinecraftCommandBuilder
{
    public static string ApplyEffect(string selector, string effect, int seconds, int amplifier)
        => $"effect give {selector} minecraft:{effect} {seconds} {amplifier}";

    public static string Tellraw(string selector, string message, string color, bool bold, bool usesInlineTextComponents)
        => $"tellraw {selector} {BuildTextComponent(message, color, bold, usesInlineTextComponents)}";

    public static string ClearColumn(string selector, int height)
        => $"execute at {selector} run fill ~ ~ ~ ~ ~{height} ~ minecraft:air";

    public static string DropAnvil(string selector)
        => $"execute at {selector} run setblock ~ ~5 ~ minecraft:anvil";

    public static string ClearMainHand(string selector)
        => $"item replace entity {selector} weapon.mainhand with air";

    public static string Title(string selector, string text, string color, bool usesInlineTextComponents)
        => Title(selector, text, color, false, usesInlineTextComponents);

    public static string Title(string selector, string text, string color, bool bold, bool usesInlineTextComponents)
        => $"title {selector} title {BuildTextComponent(text, color, bold, usesInlineTextComponents)}";

    public static string Subtitle(string selector, string text, string color, bool usesInlineTextComponents)
        => Subtitle(selector, text, color, false, usesInlineTextComponents);

    public static string Subtitle(string selector, string text, string color, bool bold, bool usesInlineTextComponents)
        => $"title {selector} subtitle {BuildTextComponent(text, color, bold, usesInlineTextComponents)}";

    public static string TitleTimes(string selector, int fadeInTicks, int stayTicks, int fadeOutTicks)
        => $"title {selector} times {fadeInTicks} {stayTicks} {fadeOutTicks}";

    public static string SpawnPrimedTnt(string selector)
        => $"execute at {selector} run summon tnt ~ ~ ~ {{fuse:80, Fuse:80}}";

    public static string PlayTntSound(string selector)
        => $"execute as {selector} at @s run playsound minecraft:entity.tnt.primed master @s ~ ~ ~";

    public static string Loot(string selector, string lootTable, double offsetX = 0.0, double offsetZ = 0.0)
        => $"execute at {selector} run loot spawn ~{FormatCoordOffset(offsetX)} ~ ~{FormatCoordOffset(offsetZ)} loot minecraft:{lootTable}";

    private static string FormatCoordOffset(double value)
    {
        if (Math.Abs(value) < 0.0001)
        {
            return string.Empty;
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string SummonMob(string selector, string mob)
        => $"execute at {selector} run summon minecraft:{mob} ~ ~ ~";

    public static string Heal(string selector)
        => $"effect give {selector} minecraft:instant_health 1 1";

    public static string Lightning(string selector)
        => $"execute at {selector} run summon minecraft:lightning_bolt ~ ~ ~";

    public static string TurnAround(string selector)
        => $"execute as {selector} at @s run tp @s ~ ~ ~ ~180 ~";

    public static string SetScale(string selector, double scale, bool usesModernAttributeIDs)
    {
        string attribute = usesModernAttributeIDs ? "minecraft:scale" : "minecraft:generic.scale";
        return "execute as " + selector + " run attribute @s " + attribute + " base set " +
            scale.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string AddMaxHealthModifier(string selector, string ID, int delta, bool modernAttribute, bool namespacedID)
        => "execute as " + selector + " run attribute @s " + (modernAttribute ? "minecraft:max_health" : "minecraft:generic.max_health") +
            " modifier add " + ID + (namespacedID ? "" : " twitchcraft_health") + " " + delta.ToString(CultureInfo.InvariantCulture) + " add_value";

    public static string RemoveMaxHealthModifier(string selector, string ID, bool modernAttribute)
        => "execute as " + selector + " run attribute @s " + (modernAttribute ? "minecraft:max_health" : "minecraft:generic.max_health") + " modifier remove " + ID;

    public static string ModeratePlayer(string playerName, string reason, bool ban)
    {
        reason = CleanCommandArg(reason);
        string command = ban ? "ban" : "kick";
        return reason.Length == 0 ? $"{command} {playerName}" : $"{command} {playerName} {reason}";
    }

    public static string UnbanPlayer(string playerName)
        => $"pardon {playerName}";

    public static string Whitelist(string playerName, bool add)
        => $"whitelist {(add ? "add" : "remove")} {playerName}";

    private static string CleanCommandArg(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim();
        StringBuilder? builder = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!char.IsControl(c))
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
            builder.Append(' ');
        }

        return builder?.ToString().Trim() ?? value;
    }

    private static string BuildTextComponent(string message, string color, bool bold, bool usesInlineTextComponents)
    {
        string safeColor = string.IsNullOrWhiteSpace(color) ? "white" : color.Trim();
        if (usesInlineTextComponents)
        {
            return "{text:'" + EscapeSnbt(message) + "',color:'" + EscapeSnbt(safeColor) + "',bold:" + (bold ? "true" : "false") + "}";
        }

        return "{\"text\":\"" + EscapeJson(message) + "\",\"color\":\"" + EscapeJson(safeColor) + "\",\"bold\":" + (bold ? "true" : "false") + "}";
    }

    public static string PlayerSelector(string playerName)
        => "@a[name=\"" + EscapeSelector(playerName) + "\",gamemode=!spectator]";

    public static string SinglePlayerSelector(string playerName)
        => "@a[name=\"" + EscapeSelector(playerName) + "\",limit=1]";

    public static string EveryoneExceptSelector(string playerName)
        => "@a[name=!\"" + EscapeSelector(playerName) + "\"]";

    public static string EscapeSelector(string? value)
        => EscapeMinecraft(value, escapeDoubleQuote: true, escapeSingleQuote: false, escapeControls: false);

    public static string EscapeJson(string? value)
        => EscapeMinecraft(value, escapeDoubleQuote: true, escapeSingleQuote: false, escapeControls: true);

    public static string EscapeSnbt(string? value)
        => EscapeMinecraft(value, escapeDoubleQuote: false, escapeSingleQuote: true, escapeControls: true);

    private static string EscapeMinecraft(string? value, bool escapeDoubleQuote, bool escapeSingleQuote, bool escapeControls)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            string? replacement = c switch
            {
                '\\' => "\\\\",
                '"' when escapeDoubleQuote => "\\\"",
                '\'' when escapeSingleQuote => "\\'",
                '\r' when escapeControls => "\\r",
                '\n' when escapeControls => "\\n",
                '\t' when escapeControls => "\\t",
                < ' ' when escapeControls => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
                _ => null
            };

            if (replacement == null)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? value;
    }
}
