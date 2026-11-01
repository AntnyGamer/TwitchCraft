using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static bool TryParsePosition(string value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        return text.Length >= 5 &&
               text[0] == '[' &&
               text[^1] == ']' &&
               text.Contains(',');
    }

    private bool HasRespawnRequest(string playerName)
    {
        lock (_respawnProbeGate)
        {
            return _pendingRespawnRequests.ContainsKey(playerName);
        }
    }

    internal static bool TryParseGamemode(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        string message = AfterLastColon(line, line.Length);
        const string setPrefix = "Set ";
        if (!message.StartsWith(setPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string possessiveMarker = "'s game mode to ";
        int possessiveIndex = message.IndexOf(possessiveMarker, StringComparison.OrdinalIgnoreCase);
        if (possessiveIndex > setPrefix.Length)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(
                    message.AsSpan(setPrefix.Length, possessiveIndex - setPrefix.Length), out string candidate) &&
                TryParseGamemodeName(message.AsSpan(possessiveIndex + possessiveMarker.Length), out gameType))
            {
                playerName = candidate;
                return true;
            }
        }

        const string ofPrefix = "Set the game mode of ";
        if (message.StartsWith(ofPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int toIndex = message.IndexOf(" to ", ofPrefix.Length, StringComparison.OrdinalIgnoreCase);
            if (toIndex > ofPrefix.Length)
            {
                if (MinecraftNameHelper.TryNormalizePlayerName(
                        message.AsSpan(ofPrefix.Length, toIndex - ofPrefix.Length), out string candidate) &&
                    TryParseGamemodeName(message.AsSpan(toIndex + 4), out gameType))
                {
                    playerName = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseGamemodeName(ReadOnlySpan<char> text, out int gameType)
    {
        if (text.Contains("survival", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 0;
        }
        else if (text.Contains("creative", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 1;
        }
        else if (text.Contains("adventure", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 2;
        }
        else
        {
            gameType = text.Contains("spectator", StringComparison.OrdinalIgnoreCase) ? 3 : -1;
        }

        return gameType >= 0;
    }

    private void HandleGamemode(string playerName, int gameType)
    {
        lock (_spectatorProbeGate)
        {
            if (gameType == 3)
            {
                _spectatorPlayers.Add(playerName);
            }
            else
            {
                _spectatorPlayers.Remove(playerName);
            }

            _pendingGamemodeRequests.Remove(playerName, out TaskCompletionSource<int?>? waiter);
            waiter?.TrySetResult(gameType);
        }

        Statistics.RecordGamemode(playerName, gameType);
    }

    private void HandleRespawn(string playerName)
    {
        lock (_respawnProbeGate)
        {
            _pendingRespawnRequests.Remove(playerName, out TaskCompletionSource<bool>? waiter);
            waiter?.TrySetResult(true);
        }
    }

    private void HandleItem(string playerName, string itemData)
    {
        lock (_selectedItemProbeGate)
        {
            _pendingSelectedItemRequests.Remove(playerName, out TaskCompletionSource<string?>? waiter);
            waiter?.TrySetResult(itemData);
        }
    }

    private static bool MatchesPlayer(ReadOnlySpan<char> entity, string player)
    {
        int i = entity.IndexOf(player, StringComparison.OrdinalIgnoreCase);
        int end = i + player.Length;
        return i >= 0 && (i == 0 || !IsPlayerNameChar(entity[i - 1])) && (end == entity.Length || !IsPlayerNameChar(entity[end]));
    }

    private static bool IsPlayerNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private bool TryHandleHealthProbe(string line)
        => line.Contains("attribute", StringComparison.OrdinalIgnoreCase) && TryHandleMaxHealth(line);

    internal static bool TryParseMaxHealthResponse(string line, string player, out double health)
    {
        int entity = line.LastIndexOf(" for entity ", StringComparison.OrdinalIgnoreCase);
        int value = line.LastIndexOf(" is ", StringComparison.OrdinalIgnoreCase);
        health = 0;
        return entity >= 0 && value >= 0 && line.Contains("value of attribute ", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("Max Health", StringComparison.OrdinalIgnoreCase) || line.Contains("max_health", StringComparison.OrdinalIgnoreCase)) &&
            double.TryParse(line.AsSpan(value + 4).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out health) &&
            MatchesPlayer(line.AsSpan(entity + 12, value - entity - 12).Trim(), player);
    }

    private bool TryHandleMaxHealth(string line)
    {
        lock (_maxHealthProbeGate)
        {
            foreach (string player in _pendingMaxHealthRequests.Keys)
            {
                if (TryParseMaxHealthResponse(line, player, out double health) && _pendingMaxHealthRequests.Remove(player, out TaskCompletionSource<double?>? waiter))
                {
                    waiter.TrySetResult(health);
                    return true;
                }
            }
        }

        return false;
    }

    [GeneratedRegex(@"twitchcraft:heart_[0-9a-f]{32}", RegexOptions.CultureInvariant)]
    private static partial Regex ModernHeartModifierRegex();

    [GeneratedRegex(@"\{[^{}]*twitchcraft_health[^{}]*\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyHeartModifierRegex();

    [GeneratedRegex(@"UUID\s*:\s*\[I;\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyHeartUUIDRegex();

    internal static List<string> ParseHeartModifierIDs(string data, bool namespaced)
    {
        List<string> ids = [];
        if (namespaced)
        {
            foreach (ValueMatch match in ModernHeartModifierRegex().EnumerateMatches(data))
            {
                ids.Add(data.Substring(match.Index, match.Length));
            }

            return ids;
        }

        foreach (ValueMatch modifier in LegacyHeartModifierRegex().EnumerateMatches(data))
        {
            Match UUID = LegacyHeartUUIDRegex().Match(data, modifier.Index, modifier.Length);
            if (!UUID.Success ||
                !int.TryParse(UUID.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ||
                !int.TryParse(UUID.Groups[2].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ||
                !int.TryParse(UUID.Groups[3].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ||
                !int.TryParse(UUID.Groups[4].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
            {
                continue;
            }

            uint ua = unchecked((uint)a);
            uint ub = unchecked((uint)b);
            uint uc = unchecked((uint)c);
            uint ud = unchecked((uint)d);
            ids.Add($"{ua:x8}-{(ub >> 16):x4}-{(ub & 0xffff):x4}-{(uc >> 16):x4}-{(uc & 0xffff):x4}{ud:x8}");
        }
        return ids;
    }

    private void HandleEntity(string line, int markerIndex)
    {
        if (markerIndex <= 0)
        {
            return;
        }

        string playerName = AfterLastColon(line, markerIndex);
        if (!MinecraftNameHelper.IsValidPlayerName(playerName))
        {
            return;
        }

        int dataStart = markerIndex + EntityDataMarker.Length;
        string suffix = TextSegmentHelper.TrimSegment(line, dataStart, line.Length - dataStart);

        if (suffix.Length >= 2 && suffix[0] == '[' && suffix[^1] == ']' && (suffix.Length == 2 || suffix.Contains('{')))
        {
            TaskCompletionSource<string?>? waiter;
            lock (_maxHealthProbeGate)
            {
                _pendingHeartAttributeRequests.Remove(playerName, out waiter);
            }

            if (waiter != null)
            {
                waiter.TrySetResult(suffix);
                return;
            }
        }

        if (HasRespawnRequest(playerName) && TryParsePosition(suffix))
        {
            HandleRespawn(playerName);
            return;
        }

        if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameType))
        {
            HandleGamemode(playerName, gameType);
            return;
        }

        if (suffix.Length >= 2 &&
            suffix[0] == '{' &&
            suffix[^1] == '}' &&
            (suffix.Length == 2 || suffix.Contains("minecraft:", StringComparison.OrdinalIgnoreCase)))
        {
            HandleItem(playerName, suffix);
        }
    }
}
