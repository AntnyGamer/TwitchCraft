using System;

namespace TwitchCraftBot_V1;

internal static class TextSegmentHelper
{
    public static string TrimSegment(string? value, int start, int length)
    {
        if (string.IsNullOrEmpty(value) || length <= 0)
            return string.Empty;

        int segmentStart = Math.Clamp(start, 0, value.Length);
        int segmentEnd = Math.Min(value.Length, segmentStart + length) - 1;
        while (segmentStart <= segmentEnd && char.IsWhiteSpace(value[segmentStart]))
            segmentStart++;
        while (segmentEnd >= segmentStart && char.IsWhiteSpace(value[segmentEnd]))
            segmentEnd--;

        if (segmentStart > segmentEnd)
            return string.Empty;

        return segmentStart == 0 && segmentEnd == value.Length - 1
            ? value
            : value.Substring(segmentStart, segmentEnd - segmentStart + 1);
    }
}
