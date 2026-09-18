using System;

namespace TwitchCraft_V1;

internal readonly struct ParsedCommand
{
    private readonly string? _name;
    private readonly string[]? _argumentArray;

    public string Name => _name ?? string.Empty;

    public string[] ArgumentArray => _argumentArray ?? [];

    private ParsedCommand(string name, string[]? argumentArray = null)
    {
        _name = name;
        _argumentArray = argumentArray;
    }

    internal static bool TryMatchPrefix(
        string payload,
        string primaryPrefix,
        string secondaryPrefix,
        out string matchedPrefix)
    {
        matchedPrefix = string.Empty;
        if (string.IsNullOrEmpty(payload))
            return false;

        if (secondaryPrefix.Length > primaryPrefix.Length && payload.StartsWith(secondaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = secondaryPrefix;
            return true;
        }
        if (payload.StartsWith(primaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = primaryPrefix;
            return true;
        }
        if (secondaryPrefix.Length > 0 && payload.StartsWith(secondaryPrefix, StringComparison.Ordinal))
        {
            matchedPrefix = secondaryPrefix;
            return true;
        }

        return false;
    }

    public static ParsedCommand Parse(string payload)
        => Parse(payload, "!");

    public static ParsedCommand Parse(string payload, string prefix)
    {
        if (string.IsNullOrEmpty(payload) || string.IsNullOrEmpty(prefix) ||
            !payload.StartsWith(prefix, StringComparison.Ordinal))
            return default;

        int start = prefix.Length;
        int end = payload.Length - 1;
        while (start <= end && char.IsWhiteSpace(payload[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(payload[end]))
            end--;

        if (start > end)
            return default;

        int nameEnd = payload.IndexOf(' ', start, end - start + 1);
        if (nameEnd < 0)
            return new ParsedCommand(LowerSegment(payload, start, end - start + 1));

        return new ParsedCommand(
            LowerSegment(payload, start, nameEnd - start),
            SplitArgs(payload.AsSpan(nameEnd + 1, end - nameEnd)));
    }

    internal static string LowerSegment(string value, int start, int length)
    {
        if (length <= 0)
            return string.Empty;

        for (int i = 0; i < length; i++)
        {
            if (CommandUserHelper.LowerFast(value[start + i]) == value[start + i])
                continue;

            return string.Create(length, (Value: value, Start: start), static (destination, state) =>
            {
                for (int j = 0; j < destination.Length; j++)
                    destination[j] = CommandUserHelper.LowerFast(state.Value[state.Start + j]);
            });
        }

        return start == 0 && length == value.Length ? value : value.Substring(start, length);
    }

    private static string[] SplitArgs(ReadOnlySpan<char> args)
    {
        int count = 0;
        int firstStart = -1;
        int lastNonSpace = -1;
        bool inArgument = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == ' ')
            {
                inArgument = false;
                continue;
            }

            lastNonSpace = i;

            if (!inArgument)
            {
                if (count == 0)
                    firstStart = i;

                count++;
                inArgument = true;
            }
        }

        if (count == 0)
            return [];

        if (count == 1)
            return [args[firstStart..(lastNonSpace + 1)].ToString()];

        string[] result = new string[count];
        int resultIndex = 0;
        int start = -1;

        for (int i = 0; i <= args.Length; i++)
        {
            if (i < args.Length && args[i] != ' ')
            {
                if (start < 0)
                    start = i;

                continue;
            }

            if (start >= 0)
            {
                result[resultIndex++] = args[start..i].ToString();
                start = -1;
            }
        }

        return result;
    }
}
