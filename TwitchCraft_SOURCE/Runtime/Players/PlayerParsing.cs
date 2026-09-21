using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static bool TryParseEntity(string line, out string playerName, out string data)
    {
        playerName = string.Empty;
        data = string.Empty;

        if (string.IsNullOrEmpty(line))
            return false;

        int markerIndex = line.IndexOf(EntityDataMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
            return false;

        string prefix = AfterLastColon(line, markerIndex);
        if (!MinecraftNameHelper.IsValidPlayerName(prefix))
            return false;

        playerName = prefix;
        int dataStart = markerIndex + EntityDataMarker.Length;
        data = TextSegmentHelper.TrimSegment(line, dataStart, line.Length - dataStart);
        return true;
    }

    private static bool TryHandleGamemode(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        if (string.IsNullOrEmpty(line))
            return false;

        if (TryParseEntity(line, out playerName, out string suffix)
            && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out gameType))
        {
            return true;
        }

        return TryParseGamemode(line, out playerName, out gameType);
    }

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
        lock (_respawnPositionProbeGate)
            return _pendingRespawnPositionRequests.ContainsKey(playerName);
    }

    internal static bool TryParseGamemode(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        string message = AfterLastColon(line, line.Length);
        if (message.Length == 0 || !message.Contains("game mode", StringComparison.OrdinalIgnoreCase))
            return false;

        const string setPrefix = "Set ";
        const string possessiveMarker = "'s game mode to ";
        int possessiveIndex = message.IndexOf(possessiveMarker, StringComparison.OrdinalIgnoreCase);
        if (message.StartsWith(setPrefix, StringComparison.OrdinalIgnoreCase) && possessiveIndex > setPrefix.Length)
        {
            string candidate = message[setPrefix.Length..possessiveIndex].Trim();
            ReadOnlySpan<char> modeText = message.AsSpan(possessiveIndex + possessiveMarker.Length).Trim();
            if (MinecraftNameHelper.IsValidPlayerName(candidate) && TryParseGamemodeName(modeText, out gameType))
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
                string candidate = message[ofPrefix.Length..toIndex].Trim();
                ReadOnlySpan<char> modeText = message.AsSpan(toIndex + 4).Trim();
                if (MinecraftNameHelper.IsValidPlayerName(candidate) && TryParseGamemodeName(modeText, out gameType))
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
        gameType = -1;

        if (text.Contains("survival", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 0;
            return true;
        }

        if (text.Contains("creative", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 1;
            return true;
        }

        if (text.Contains("adventure", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 2;
            return true;
        }

        if (text.Contains("spectator", StringComparison.OrdinalIgnoreCase))
        {
            gameType = 3;
            return true;
        }

        return false;
    }

    private void HandleGamemode(string playerName, int gameType)
    {
        lock (_spectatorProbeGate)
        {
            if (gameType == 3)
                _spectatorPlayers.Add(playerName);
            else
                _spectatorPlayers.Remove(playerName);

            _pendingGameTypeRequests.Remove(playerName, out TaskCompletionSource<int?>? waiter);
            waiter?.TrySetResult(gameType);
        }

        Statistics.RecordGamemode(playerName, gameType);
    }

    private void HandleRespawn(string playerName)
    {
        lock (_respawnPositionProbeGate)
        {
            _pendingRespawnPositionRequests.Remove(playerName, out TaskCompletionSource<bool>? waiter);
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
        int i = entity.IndexOf(player, StringComparison.OrdinalIgnoreCase), end = i + player.Length;
        return i >= 0 && (i == 0 || !IsPlayerNameChar(entity[i - 1])) && (end == entity.Length || !IsPlayerNameChar(entity[end]));
    }

    private static bool IsPlayerNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private bool TryHandleHealthProbe(string line)
        => line.Contains("attribute", StringComparison.OrdinalIgnoreCase) && TryHandleMaxHealth(line);

    internal static bool TryParseMaxHealthResponse(string line, string player, out double health)
    {
        int entity = line.LastIndexOf(" for entity ", StringComparison.OrdinalIgnoreCase), value = line.LastIndexOf(" is ", StringComparison.OrdinalIgnoreCase);
        health = 0;
        return entity >= 0 && value >= 0 && line.Contains("value of attribute ", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("Max Health", StringComparison.OrdinalIgnoreCase) || line.Contains("max_health", StringComparison.OrdinalIgnoreCase)) &&
            double.TryParse(line.AsSpan(value + 4).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out health) &&
            MatchesPlayer(line.AsSpan(entity + 12, value - entity - 12).Trim(), player);
    }

    private bool TryHandleMaxHealth(string line)
    {
        lock (_maxHealthProbeGate)
            foreach (string player in _pendingMaxHealthRequests.Keys)
                if (TryParseMaxHealthResponse(line, player, out double health) && _pendingMaxHealthRequests.Remove(player, out TaskCompletionSource<double?>? waiter))
                { waiter.TrySetResult(health); return true; }
        return false;
    }

    [GeneratedRegex(@"twitchcraft:heart_[0-9a-f]{32}", RegexOptions.CultureInvariant)]
    private static partial Regex ModernHeartModifierRegex();

    [GeneratedRegex(@"\{[^{}]*twitchcraft_health[^{}]*\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyHeartModifierRegex();

    [GeneratedRegex(@"uuid\s*:\s*\[I;\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyHeartUuidRegex();

    internal static List<string> ParseHeartModifierIDs(string data, bool namespaced)
    {
        List<string> ids = [];
        if (namespaced)
        {
            foreach (Match match in ModernHeartModifierRegex().Matches(data))
                ids.Add(match.Value);
            return ids;
        }

        foreach (Match modifier in LegacyHeartModifierRegex().Matches(data))
        {
            Match uuid = LegacyHeartUuidRegex().Match(modifier.Value);
            if (!uuid.Success ||
                !int.TryParse(uuid.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ||
                !int.TryParse(uuid.Groups[2].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ||
                !int.TryParse(uuid.Groups[3].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ||
                !int.TryParse(uuid.Groups[4].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
                continue;

            uint ua = unchecked((uint)a), ub = unchecked((uint)b), uc = unchecked((uint)c), ud = unchecked((uint)d);
            ids.Add($"{ua:x8}-{(ub >> 16):x4}-{(ub & 0xffff):x4}-{(uc >> 16):x4}-{(uc & 0xffff):x4}{ud:x8}");
        }
        return ids;
    }

    private void HandleEntity(string line)
    {
        if (!TryParseEntity(line, out string playerName, out string suffix))
            return;

        if (suffix.Length >= 2 && suffix[0] == '[' && suffix[^1] == ']' && (suffix.Length == 2 || suffix.Contains('{')))
        {
            lock (_maxHealthProbeGate)
                _pendingHeartAttributeRequests.Remove(playerName, out TaskCompletionSource<string?>? waiter);
            waiter?.TrySetResult(suffix);
            return;
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
            suffix.Contains("minecraft:", StringComparison.OrdinalIgnoreCase))
        {
            HandleItem(playerName, suffix);
        }
    }
}
