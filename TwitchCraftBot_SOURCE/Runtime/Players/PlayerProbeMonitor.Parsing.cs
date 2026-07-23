using System;
using System.Globalization;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static bool TryParseEntityDataLine(string line, out string playerName, out string data)
    {
        playerName = string.Empty;
        data = string.Empty;

        if (string.IsNullOrEmpty(line))
            return false;

        int markerIndex = line.IndexOf(EntityDataMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
            return false;

        string prefix = TrimPrefixAfterLastColon(line, markerIndex);
        if (!MinecraftNameHelper.IsValidPlayerName(prefix))
            return false;

        playerName = prefix;
        int dataStart = markerIndex + EntityDataMarker.Length;
        data = TextSegmentHelper.TrimSegment(line, dataStart, line.Length - dataStart);
        return true;
    }

    private static bool TryHandlePlayerGamemodeLine(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        if (string.IsNullOrEmpty(line))
            return false;

        if (TryParseEntityDataLine(line, out playerName, out string suffix)
            && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out gameType))
        {
            return true;
        }

        return TryParseGamemodeAnnouncementLine(line, out playerName, out gameType);
    }

    private static bool TryParseMinecraftPositionValue(string value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        return text.Length >= 5 &&
               text[0] == '[' &&
               text[^1] == ']' &&
               text.Contains(',');
    }

    private bool HasPendingRespawnPositionRequest(string playerName)
    {
        lock (_respawnPositionProbeGate)
            return _pendingRespawnPositionRequests.TryGetValue(playerName, out _);
    }

    private static bool TryParseGamemodeAnnouncementLine(string line, out string playerName, out int gameType)
    {
        playerName = string.Empty;
        gameType = -1;

        string message = TrimPrefixAfterLastColon(line, line.Length);
        if (message.Length == 0 || !message.Contains("game mode", StringComparison.OrdinalIgnoreCase))
            return false;

        const string setPrefix = "Set ";
        const string possessiveMarker = "'s game mode to ";
        int possessiveIndex = message.IndexOf(possessiveMarker, StringComparison.OrdinalIgnoreCase);
        if (message.StartsWith(setPrefix, StringComparison.OrdinalIgnoreCase) && possessiveIndex > setPrefix.Length)
        {
            string candidate = message[setPrefix.Length..possessiveIndex].Trim();
            string modeText = message[(possessiveIndex + possessiveMarker.Length)..].Trim();
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
                string modeText = message[(toIndex + 4)..].Trim();
                if (MinecraftNameHelper.IsValidPlayerName(candidate) && TryParseGamemodeName(modeText, out gameType))
                {
                    playerName = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseGamemodeName(string value, out int gameType)
    {
        gameType = -1;
        string text = (value ?? string.Empty).Trim();

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

    private void HandlePlayerGamemodeResult(string playerName, int gameType)
    {
        TaskCompletionSource<int?>? waiter;

        lock (_spectatorProbeGate)
        {
            if (gameType == 3)
                _spectatorPlayers.Add(playerName);
            else
                _spectatorPlayers.Remove(playerName);

            _pendingGameTypeRequests.Remove(playerName, out waiter);
        }

        RecordTrackedPlayerGamemodeForStatistics(playerName, gameType);
        waiter?.TrySetResult(gameType);
    }

    private void HandlePlayerRespawnPositionResult(string playerName)
    {
        TaskCompletionSource<bool>? waiter;

        lock (_respawnPositionProbeGate)
        {
            _pendingRespawnPositionRequests.Remove(playerName, out waiter);
        }

        waiter?.TrySetResult(true);
    }

    private void HandleSelectedItemResult(string playerName, string itemData)
    {
        TaskCompletionSource<string?>? waiter;

        lock (_selectedItemProbeGate)
        {
            _pendingSelectedItemRequests.Remove(playerName, out waiter);
        }

        waiter?.TrySetResult(itemData);
    }

    private void HandleEntityDataLine(string line)
    {
        if (!TryParseEntityDataLine(line, out string playerName, out string suffix))
            return;

        if (HasPendingRespawnPositionRequest(playerName) && TryParseMinecraftPositionValue(suffix))
        {
            HandlePlayerRespawnPositionResult(playerName);
            return;
        }

        if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gameType))
        {
            HandlePlayerGamemodeResult(playerName, gameType);
            return;
        }

        if (suffix.Length >= 2 &&
            suffix[0] == '{' &&
            suffix[^1] == '}' &&
            suffix.Contains("minecraft:", StringComparison.OrdinalIgnoreCase))
        {
            HandleSelectedItemResult(playerName, suffix);
        }
    }

}
