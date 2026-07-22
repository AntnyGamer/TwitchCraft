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
