using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1;

internal sealed class MinecraftStderrFilter
{
    private const string DeprecationHeader = "WARNING: A terminally deprecated method in sun.misc.Unsafe has been called";
    private const string JomlCallerPrefix = "WARNING: sun.misc.Unsafe::objectFieldOffset has been called by org.joml.MemUtil$MemUtilUnsafe";
    private const string ReportJomlCaller = "WARNING: Please consider reporting this to the maintainers of class org.joml.MemUtil$MemUtilUnsafe";
    private const string RemovalNotice = "WARNING: sun.misc.Unsafe::objectFieldOffset will be removed in a future release";
    private const string StderrPrefix = "[stderr] ";

    private readonly List<string> _candidateLines = new(4);

    internal void ProcessLine(string line, Action<string> showLine)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(showLine);

        string message = RemoveDisplayPrefix(line);
        bool matchesNextLine = _candidateLines.Count switch
        {
            0 => string.Equals(message, DeprecationHeader, StringComparison.Ordinal),
            1 => IsJomlCallerLine(message),
            2 => string.Equals(message, ReportJomlCaller, StringComparison.Ordinal),
            3 => string.Equals(message, RemovalNotice, StringComparison.Ordinal),
            _ => false
        };

        if (!matchesNextLine)
        {
            Flush(showLine);
            if (string.Equals(message, DeprecationHeader, StringComparison.Ordinal))
                _candidateLines.Add(line);
            else
                showLine(line);

            return;
        }

        _candidateLines.Add(line);
        if (_candidateLines.Count == 4)
            _candidateLines.Clear();
    }

    internal void Flush(Action<string> showLine)
    {
        ArgumentNullException.ThrowIfNull(showLine);

        foreach (string line in _candidateLines)
            showLine(line);

        _candidateLines.Clear();
    }

    private static string RemoveDisplayPrefix(string line)
        => line.StartsWith(StderrPrefix, StringComparison.Ordinal)
            ? line[StderrPrefix.Length..]
            : line;

    private static bool IsJomlCallerLine(string line)
    {
        if (!line.StartsWith(JomlCallerPrefix, StringComparison.Ordinal))
            return false;

        return line.Length == JomlCallerPrefix.Length ||
            char.IsWhiteSpace(line[JomlCallerPrefix.Length]) ||
            line[JomlCallerPrefix.Length] == '(';
    }
}
