using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

// ===== Shared command models =====

public sealed class EffectDefinition
{
    public string ID { get; set; } = string.Empty;
    public int MinSeconds { get; set; }
    public int MaxSeconds { get; set; }
    public int MinAmplifier { get; set; }
    public int MaxAmplifier { get; set; }
}

public delegate Task ChatCommandHandler(string[] args, string sender, CancellationToken cancellationToken);

public sealed class ResolvedTarget
{
    public string Selector { get; set; } = "@a";
    public string DisplayName { get; set; } = "everyone";
    public string MinecraftName { get; set; } = string.Empty;
    public int PlayerCount { get; set; } = 1;
    public bool DefaultPlayerInclusionKnown { get; set; }
    public bool IncludesDefaultMinecraftPlayer { get; set; }
    public List<string>? TargetablePlayers { get; set; }
}

// ===== Command registry =====

[Flags]
public enum ChatCommandStatisticFlags
{
    None = 0,
    GameAffecting = 1,
    Dangerous = 2,
    Nice = 4
}

public sealed class ChatCommandRegistry(
    Dictionary<string, ChatCommandHandler>? handlers,
    Dictionary<string, ChatCommandStatisticFlags>? statisticFlags = null)
{
    private readonly Dictionary<string, ChatCommandHandler> _handlers = handlers ?? new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatCommandStatisticFlags> _statisticFlags = statisticFlags ?? new(StringComparer.OrdinalIgnoreCase);

    public bool TryResolve(string? name, out ChatCommandHandler handler)
        => _handlers.TryGetValue(name ?? string.Empty, out handler!);

    public ChatCommandStatisticFlags GetStatisticFlags(string? name)
        => _statisticFlags.TryGetValue(name ?? string.Empty, out ChatCommandStatisticFlags flags)
            ? flags
            : ChatCommandStatisticFlags.None;

    public static ChatCommandRegistry CreateDefault(BotMainHandler runtime)
    {
        var statisticFlags = new Dictionary<string, ChatCommandStatisticFlags>(64, StringComparer.OrdinalIgnoreCase);
        return new(CommandList.BuildCommandHandlers(runtime, statisticFlags), statisticFlags);
    }
}

// ===== Minecraft command string helpers =====

public static class MinecraftCommandBuilder
{
    public static string ApplyEffect(string selector, string effect, int seconds, int amplifier)
        => $"effect give {selector} minecraft:{effect} {seconds} {amplifier}";

    public static string Tellraw(string selector, string message, string color, bool bold, bool usesInlineTextComponents)
        => $"tellraw {selector} {BuildTextComponent(message, color, bold, usesInlineTextComponents)}";

    public static string ClearVerticalColumn(string selector, int height)
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
            return string.Empty;

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string SummonMob(string selector, string mob)
        => $"execute at {selector} run summon minecraft:{mob} ~ ~ ~";

    public static string Heal(string selector)
        => $"effect give {selector} minecraft:instant_health 1 1";

    public static string Lightning(string selector)
        => $"execute at {selector} run summon minecraft:lightning_bolt ~ ~ ~";

    public static string BanPlayer(string playerName, string reason)
    {
        reason = CleanCommandArgumentText(reason);
        return reason.Length == 0 ? $"ban {playerName}" : $"ban {playerName} {reason}";
    }

    public static string UnbanPlayer(string playerName)
        => $"pardon {playerName}";

    private static string CleanCommandArgumentText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

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
            return "{text:'" + EscapeSnbtString(message) + "',color:'" + EscapeSnbtString(safeColor) + "',bold:" + (bold ? "true" : "false") + "}";
        }

        return "{\"text\":\"" + EscapeJson(message) + "\",\"color\":\"" + EscapeJson(safeColor) + "\",\"bold\":" + (bold ? "true" : "false") + "}";
    }

    public static string PlayerSelector(string playerName)
        => "@a[name=\"" + EscapeSelectorValue(playerName) + "\",gamemode=!spectator]";

    public static string PlayerSelectorLimitOne(string playerName)
        => "@a[name=\"" + EscapeSelectorValue(playerName) + "\",limit=1]";

    public static string AllExceptPlayerSelector(string playerName)
        => "@a[name=!\"" + EscapeSelectorValue(playerName) + "\"]";

    public static string EscapeSelectorValue(string? value)
        => EscapeMinecraftString(value, escapeDoubleQuote: true, escapeSingleQuote: false, escapeControls: false);

    public static string EscapeJson(string? value)
        => EscapeMinecraftString(value, escapeDoubleQuote: true, escapeSingleQuote: false, escapeControls: true);

    public static string EscapeSnbtString(string? value)
        => EscapeMinecraftString(value, escapeDoubleQuote: false, escapeSingleQuote: true, escapeControls: true);

    private static string EscapeMinecraftString(string? value, bool escapeDoubleQuote, bool escapeSingleQuote, bool escapeControls)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

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
