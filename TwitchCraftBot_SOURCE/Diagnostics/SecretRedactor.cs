using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace TwitchCraftBot_V1;

internal static class SecretRedactor
{
    internal const string Replacement = "[REDACTED]";

    private const RegexOptions PatternOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly Regex AuthorizationHeaderPattern = new(@"(\bAuthorization\s*:\s*(?:Bearer|OAuth)\s+)[^\s,;]+", PatternOptions);
    private static readonly Regex PassOauthPattern = new(@"(\bPASS\s+oauth:)[^\s]+", PatternOptions);
    private static readonly Regex OauthTokenPattern = new(@"(\boauth:)[^\s,;]+", PatternOptions);
    private static readonly Regex RconPasswordPattern = new(@"(\brcon\.password\s*=\s*)[^\r\n]*", PatternOptions);
    private static readonly Regex WindowsUserPathPattern = new(@"\b([A-Z]:\\Users\\)[^\\\r\n]+", PatternOptions);
    private static readonly Lock SecretGate = new();
    private static readonly HashSet<string> RegisteredSecrets = new(StringComparer.Ordinal);
    private static string[] _secretSnapshot = [];

    internal static void Register(params string?[] secrets)
    {
        if (secrets == null || secrets.Length == 0)
            return;

        bool changed = false;
        lock (SecretGate)
        {
            foreach (string? candidate in secrets)
            {
                string secret = candidate?.Trim() ?? string.Empty;
                if (secret.Length > 0 && RegisteredSecrets.Add(secret))
                    changed = true;
            }

            if (changed)
            {
                string[] snapshot = [.. RegisteredSecrets];
                Array.Sort(snapshot, static (left, right) => right.Length.CompareTo(left.Length));
                Volatile.Write(ref _secretSnapshot, snapshot);
            }
        }
    }

    internal static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        string redacted = value;
        foreach (string secret in Volatile.Read(ref _secretSnapshot))
            redacted = redacted.Replace(secret, Replacement, StringComparison.Ordinal);

        redacted = AuthorizationHeaderPattern.Replace(redacted, "$1" + Replacement);
        redacted = PassOauthPattern.Replace(redacted, "$1" + Replacement);
        redacted = OauthTokenPattern.Replace(redacted, "$1" + Replacement);
        redacted = RconPasswordPattern.Replace(redacted, "$1" + Replacement);
        redacted = WindowsUserPathPattern.Replace(redacted, "$1[USER]");
        return redacted;
    }
}
