using System;

namespace TwitchCraftBot_V1;

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

    internal static string BuildIrcPassword(string? token)
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
