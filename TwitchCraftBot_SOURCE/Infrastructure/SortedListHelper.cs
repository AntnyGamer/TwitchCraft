using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1;

internal static class SortedListHelper
{
    public static int FindIndex(IReadOnlyList<string> values, string value, StringComparer comparer)
    {
        int low = 0;
        int high = values.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = comparer.Compare(values[middle], value);
            if (comparison == 0)
                return middle;

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return ~low;
    }

    public static bool Contains(IReadOnlyList<string> values, string value, StringComparer comparer)
        => FindIndex(values, value, comparer) >= 0;

    public static bool EqualInOrder(IReadOnlyList<string> left, IReadOnlyList<string> right, StringComparer comparer)
    {
        if (ReferenceEquals(left, right))
            return true;

        int count = left.Count;
        if (count != right.Count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public static void SortAndDeduplicate(List<string> values, StringComparer comparer)
    {
        int count = values.Count;
        if (count <= 1)
            return;

        values.Sort(comparer);
        int writeIndex = 1;
        for (int readIndex = 1; readIndex < count; readIndex++)
        {
            if (!comparer.Equals(values[readIndex], values[writeIndex - 1]))
                values[writeIndex++] = values[readIndex];
        }

        if (writeIndex < count)
            values.RemoveRange(writeIndex, count - writeIndex);
    }

    public static List<string> NormalizePlayerNames(List<string>? players, StringComparer comparer)
    {
        if (players is null)
            return [];

        if (players.Count == 0 || IsNormalizedList(players, comparer))
            return players;

        return NormalizePlayerNames((IEnumerable<string>)players, comparer);
    }

    public static List<string> NormalizePlayerNames(IEnumerable<string> players, StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(players);

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(players, out int count) ? count : 0;

        List<string> normalized = new(capacity);
        foreach (string player in players)
        {
            if (MinecraftNameHelper.TryNormalizePlayerName(player, out string normalizedPlayer))
                normalized.Add(normalizedPlayer);
        }

        SortAndDeduplicate(normalized, comparer);
        return normalized;
    }

    private static bool IsNormalizedList(List<string> players, StringComparer comparer)
    {
        int count = players.Count;
        if (!MinecraftNameHelper.TryNormalizePlayerName(players[0], out string previous) ||
            !string.Equals(previous, players[0], StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = 1; i < count; i++)
        {
            if (!MinecraftNameHelper.TryNormalizePlayerName(players[i], out string current) ||
                !string.Equals(current, players[i], StringComparison.Ordinal) ||
                comparer.Compare(previous, current) >= 0)
            {
                return false;
            }

            previous = current;
        }

        return true;
    }
}
