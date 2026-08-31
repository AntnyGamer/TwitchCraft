namespace TwitchCraftBot_V1;

internal static class CommandUserHelper
{
    internal static char LowerFast(char c)
        => (uint)(c - 'A') <= 25u ? (char)(c + ('a' - 'A')) : c < 128 ? c : char.ToLowerInvariant(c);

    internal static string NormalizeUser(string? user)
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
            if (LowerFast(user[start + i]) != user[start + i])
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
                destination[i] = CommandUserHelper.LowerFast(state.User[state.Start + i]);
        });
    }

    internal static bool TryNormalizeTwitchUser(string? user, out string normalized)
    {
        normalized = NormalizeUser(user);
        return IsNormalizedUser(normalized);
    }

    private static bool IsNormalizedUser(string normalized)
    {
        if (normalized.Length is < 3 or > 25)
        {
            return false;
        }

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            bool okay = char.IsAsciiLetterLower(c) ||
                char.IsAsciiDigit(c) || c == '_';

            if (!okay)
            {
                return false;
            }
        }

        return true;
    }
}
