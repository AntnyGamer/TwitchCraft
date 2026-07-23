using System;

namespace TwitchCraftBot_V1;

internal static class MinecraftNameHelper
{
    internal static bool IsValidPlayerName(string? value)
        => !string.IsNullOrEmpty(value) && IsValidPlayerNameSegment(value.AsSpan().Trim());

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
