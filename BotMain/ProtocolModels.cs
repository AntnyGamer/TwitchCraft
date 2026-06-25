using System;

namespace TwitchCraftBot_V1;

internal static class CommandUserHelper
{
    internal static char ToLowerInvariantFast(char c)
        => (uint)(c - 'A') <= 25u ? (char)(c + ('a' - 'A')) : c < 128 ? c : char.ToLowerInvariant(c);

    internal static string NormalizeUsername(string? user)
    {
        if (string.IsNullOrEmpty(user))
            return string.Empty;

        int start = 0;
        int end = user.Length - 1;
        while (start <= end && char.IsWhiteSpace(user[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(user[end]))
            end--;
        while (start <= end && user[start] == '@')
            start++;

        int length = end - start + 1;
        if (length <= 0)
            return string.Empty;

        bool needsNewString = start != 0 || length != user.Length;
        for (int i = 0; i < length; i++)
        {
            if (ToLowerInvariantFast(user[start + i]) != user[start + i])
            {
                needsNewString = true;
                break;
            }
        }

        if (!needsNewString)
            return user;

        return string.Create(length, (User: user, Start: start), static (destination, state) =>
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = CommandUserHelper.ToLowerInvariantFast(state.User[state.Start + i]);
        });
    }

    internal static bool TryNormalizeTwitchUsername(string? user, out string normalized)
    {
        normalized = NormalizeUsername(user);
        return IsNormalizedTwitchUsername(normalized);
    }

    private static bool IsNormalizedTwitchUsername(string normalized)
    {
        if (normalized.Length is < 3 or > 25)
        {
            return false;
        }

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            bool okay =
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c == '_';

            if (!okay)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class MinecraftNameHelper
{
    internal static bool IsValidPlayerName(string? value)
        => TryNormalizePlayerName(value, out _);

    internal static bool TryNormalizePlayerName(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value))
            return false;

        int start = 0;
        int end = value.Length - 1;
        while (start <= end && char.IsWhiteSpace(value[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(value[end]))
            end--;

        int length = end - start + 1;
        if (length is < 3 or > 16)
            return false;

        ReadOnlySpan<char> segment = value.AsSpan(start, length);
        if (!IsValidPlayerNameSegment(segment))
            return false;

        normalized = start == 0 && length == value.Length ? value : value.Substring(start, length);
        return true;
    }

    internal static bool TryNormalizePlayerName(ReadOnlySpan<char> value, out string normalized)
    {
        normalized = string.Empty;
        value = value.Trim();
        if (!IsValidPlayerNameSegment(value))
            return false;

        normalized = value.ToString();
        return true;
    }

    private static bool IsValidPlayerNameSegment(ReadOnlySpan<char> value)
    {
        if (value.Length is < 3 or > 16)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool okay =
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_';

            if (!okay)
                return false;
        }

        return true;
    }
}

internal static class TwitchTokenHelper
{
    internal static string NormalizeAccessToken(string? token)
    {
        string value = (token ?? string.Empty).Trim();

        if (value.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[6..].Trim();
        }

        return value;
    }

    internal static string BuildIRCPassword(string? token)
    {
        string accessToken = NormalizeAccessToken(token);
        return accessToken.Length == 0 ? string.Empty : "oauth:" + accessToken;
    }

    internal static string BuildBearerHeader(string? token)
    {
        string accessToken = NormalizeAccessToken(token);
        return accessToken.Length == 0 ? string.Empty : "Bearer " + accessToken;
    }

    internal static string BuildValidateHeader(string? token)
    {
        string accessToken = NormalizeAccessToken(token);
        return accessToken.Length == 0 ? string.Empty : "OAuth " + accessToken;
    }

}

// ===== IRC and command parsing =====

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

    public static ParsedCommand Parse(string payload)
    {
        if (string.IsNullOrEmpty(payload) || payload[0] != '!')
            return default;

        int start = 1;
        int end = payload.Length - 1;
        while (start <= end && char.IsWhiteSpace(payload[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(payload[end]))
            end--;

        if (start > end)
            return default;

        int nameEnd = payload.IndexOf(' ', start, end - start + 1);
        if (nameEnd < 0)
            return new ParsedCommand(ToLowerInvariantSegment(payload, start, end - start + 1));

        return new ParsedCommand(
            ToLowerInvariantSegment(payload, start, nameEnd - start),
            SplitArguments(payload.AsSpan(nameEnd + 1, end - nameEnd)));
    }

    internal static string ToLowerInvariantSegment(string value, int start, int length)
    {
        if (length <= 0)
            return string.Empty;

        bool needsLowercase = false;
        for (int i = 0; i < length; i++)
        {
            if (CommandUserHelper.ToLowerInvariantFast(value[start + i]) != value[start + i])
            {
                needsLowercase = true;
                break;
            }
        }

        if (!needsLowercase)
            return start == 0 && length == value.Length ? value : value.Substring(start, length);

        return string.Create(length, (Value: value, Start: start), static (destination, state) =>
        {
            for (int i = 0; i < destination.Length; i++)
                destination[i] = CommandUserHelper.ToLowerInvariantFast(state.Value[state.Start + i]);
        });
    }

    private static string[] SplitArguments(ReadOnlySpan<char> args)
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

internal sealed class IRCMessage
{
    public int Bits { get; private set; }

    public bool IsModerator { get; private set; }

    public string Command { get; private set; } = string.Empty;

    public string SenderLogin { get; private set; } = string.Empty;

    public string Trailing { get; private set; } = string.Empty;

    public static bool TryParse(string line, out IRCMessage? message)
    {
        message = null;
        if (string.IsNullOrEmpty(line) || char.IsWhiteSpace(line[0]))
        {
            return false;
        }

        int index = 0;
        IRCMessage parsed = new();

        if (line[index] == '@')
        {
            int tagEnd = line.IndexOf(' ');
            if (tagEnd < 0)
            {
                return false;
            }

            ParseTags(line, tagEnd, parsed);
            index = tagEnd + 1;
        }

        int senderStart = -1;
        int senderLength = 0;
        if (index < line.Length && line[index] == ':')
        {
            int prefixEnd = line.IndexOf(' ', index);
            if (prefixEnd < 0)
            {
                return false;
            }

            int prefixStart = index + 1;
            int senderEnd = line.IndexOf('!', prefixStart, prefixEnd - prefixStart);
            if (senderEnd < 0)
                senderEnd = prefixEnd;

            senderStart = prefixStart;
            senderLength = senderEnd - prefixStart;
            index = prefixEnd + 1;
        }

        int commandEnd = line.IndexOf(' ', index);
        if (commandEnd < 0)
        {
            parsed.Command = TextSegmentHelper.TrimSegment(line, index, line.Length - index);
            parsed.SenderLogin = ExtractSender(line, senderStart, senderLength);
            message = parsed;
            return true;
        }

        parsed.Command = TextSegmentHelper.TrimSegment(line, index, commandEnd - index);
        index = commandEnd + 1;

        if (index < line.Length && line[index] == ':')
        {
            parsed.Trailing = line[(index + 1)..];
        }
        else
        {
            int trailingStart = line.IndexOf(" :", index, StringComparison.Ordinal);
            if (trailingStart >= 0)
            {
                parsed.Trailing = line[(trailingStart + 2)..];
            }
        }

        parsed.SenderLogin = ExtractSender(line, senderStart, senderLength);
        message = parsed;
        return true;
    }

    private static void ParseTags(string line, int tagEnd, IRCMessage message)
    {
        int start = 1;
        while (start < tagEnd)
        {
            int end = line.IndexOf(';', start, tagEnd - start);
            if (end < 0)
                end = tagEnd;

            if (end > start)
            {
                int equals = line.IndexOf('=', start, end - start);
                if (equals > start)
                {
                    int keyLength = equals - start;
                    int valueStart = equals + 1;
                    int valueLength = end - valueStart;

                    if (keyLength == 4 && TagKeyEqualsLowerExpected(line, start, keyLength, "bits"))
                    {
                        int bits = ParsePositiveIntTag(line, valueStart, valueLength);
                        if (bits > 0)
                            message.Bits = bits;
                    }
                    else if (keyLength == 3 && TagKeyEqualsLowerExpected(line, start, keyLength, "mod"))
                    {
                        if (valueLength == 1 && line[valueStart] == '1')
                            message.IsModerator = true;
                    }
                    else if (keyLength == 6 && TagKeyEqualsLowerExpected(line, start, keyLength, "badges"))
                    {
                        if (valueLength > 0 && line.IndexOf("moderator/", valueStart, valueLength, StringComparison.OrdinalIgnoreCase) >= 0)
                            message.IsModerator = true;
                    }
                }
            }

            start = end + 1;
        }
    }

    private static bool TagKeyEqualsLowerExpected(string line, int start, int length, string expected)
    {
        if (length != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            char actual = line[start + i];
            char wanted = expected[i];
            if (actual != wanted && CommandUserHelper.ToLowerInvariantFast(actual) != wanted)
                return false;
        }

        return true;
    }

    private static int ParsePositiveIntTag(string line, int start, int length)
    {
        int value = 0;
        for (int i = 0; i < length; i++)
        {
            char c = line[start + i];
            if (c < '0' || c > '9')
                return 0;

            int digit = c - '0';
            if (value > (int.MaxValue - digit) / 10)
                return 0;

            value = (value * 10) + digit;
        }

        return value;
    }

    private static string ExtractSender(string line, int start, int length)
    {
        if (start < 0 || length <= 0)
            return string.Empty;

        int end = start + length - 1;
        while (start <= end && char.IsWhiteSpace(line[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(line[end]))
            end--;

        return start > end ? string.Empty : ParsedCommand.ToLowerInvariantSegment(line, start, end - start + 1);
    }

}
