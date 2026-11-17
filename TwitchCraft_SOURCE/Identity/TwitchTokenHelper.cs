using System;

namespace TwitchCraft_V1;

internal static class TwitchTokenHelper
{
    internal static string NormalizeAccessToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> value = token.AsSpan().Trim();

        if (value.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[6..].Trim();
        }

        return value.Length == token.Length ? token : value.ToString();
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
