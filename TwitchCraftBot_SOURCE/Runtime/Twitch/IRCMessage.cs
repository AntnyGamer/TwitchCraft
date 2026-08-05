using System;

namespace TwitchCraftBot_V1;

internal sealed class IRCMessage
{
    public int Bits { get; private set; }

    public bool IsModerator { get; private set; }

    public string Command { get; private set; } = string.Empty;

    public string SenderLogin { get; private set; } = string.Empty;

    public string Trailing { get; private set; } = string.Empty;

    public bool TryParse(string line)
    {
        Reset();
        if (string.IsNullOrEmpty(line) || char.IsWhiteSpace(line[0]))
            return false;

        int index = 0;

        if (line[index] == '@')
        {
            int tagEnd = line.IndexOf(' ');
            if (tagEnd < 0)
                return false;

            ParseTags(line, tagEnd, this);
            index = tagEnd + 1;
        }

        int senderStart = -1;
        int senderLength = 0;
        if (index < line.Length && line[index] == ':')
        {
            int prefixEnd = line.IndexOf(' ', index);
            if (prefixEnd < 0)
                return false;

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
            Command = TextSegmentHelper.TrimSegment(line, index, line.Length - index);
            SenderLogin = ExtractSender(line, senderStart, senderLength);
            return true;
        }

        Command = TextSegmentHelper.TrimSegment(line, index, commandEnd - index);
        index = commandEnd + 1;

        if (index < line.Length && line[index] == ':')
        {
            Trailing = line[(index + 1)..];
        }
        else
        {
            int trailingStart = line.IndexOf(" :", index, StringComparison.Ordinal);
            if (trailingStart >= 0)
                Trailing = line[(trailingStart + 2)..];
        }

        SenderLogin = ExtractSender(line, senderStart, senderLength);
        return true;
    }

    public static bool TryParse(string line, out IRCMessage? message)
    {
        IRCMessage parsed = new();
        if (!parsed.TryParse(line))
        {
            message = null;
            return false;
        }

        message = parsed;
        return true;
    }

    private void Reset()
    {
        Bits = 0;
        IsModerator = false;
        Command = string.Empty;
        SenderLogin = string.Empty;
        Trailing = string.Empty;
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
            if (!char.IsAsciiDigit(c))
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
