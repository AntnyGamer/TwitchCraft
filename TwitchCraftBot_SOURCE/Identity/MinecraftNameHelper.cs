using System;

namespace TwitchCraftBot_V1;

internal static class MinecraftNameHelper
{
    internal static bool IsValidPlayerName(string? value)
        => !string.IsNullOrEmpty(value) && IsValidNameSegment(value.AsSpan().Trim());

    internal static bool TryNormalizePlayerName(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value))
            return false;

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        if (!IsValidNameSegment(trimmed))
            return false;

        normalized = trimmed.Length == value.Length ? value : trimmed.ToString();
        return true;
    }

    internal static bool TryNormalizePlayerName(ReadOnlySpan<char> value, out string normalized)
    {
        normalized = string.Empty;
        value = value.Trim();
        if (!IsValidNameSegment(value))
            return false;

        normalized = value.ToString();
        return true;
    }

    private static bool IsValidNameSegment(ReadOnlySpan<char> value)
    {
        if (value.Length is < 3 or > 16)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool okay = char.IsAsciiLetterOrDigit(c) || c == '_';

            if (!okay)
                return false;
        }

        return true;
    }
}
