using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1;

public sealed class BotStatisticsSnapshot
{
    public bool StatisticsEnabled { get; set; } = true;
    public long SessionGameCommandsRun { get; set; }
    public long SessionDangerousCommandsRun { get; set; }
    public long SessionNiceCommandsRun { get; set; }
    public string SessionMostUsedCommand { get; set; } = string.Empty;
    public long SessionTokensSpent { get; set; }
    public long SessionEffectsGiven { get; set; }
    public string SessionMostDangerousViewer { get; set; } = string.Empty;
    public string SessionNicestViewer { get; set; } = string.Empty;
    public TimeSpan? SessionTimeSurvived { get; set; }
    public long SessionDeaths { get; set; }

    public long TotalGameCommandsRun { get; set; }
    public string TotalMostUsedCommand { get; set; } = string.Empty;
    public long TotalTokensSpent { get; set; }
    public long TotalEffectsGiven { get; set; }
    public string TotalMostDangerousViewer { get; set; } = string.Empty;
    public string TotalNicestViewer { get; set; } = string.Empty;
    public long TotalDeaths { get; set; }
    public TimeSpan? LongestTimeSurvived { get; set; }
    public TimeSpan? ShortestTimeSurvived { get; set; }
    public long SessionsStarted { get; set; }
}

internal static class StatisticNameHelper
{
    public static string NormalizeCommandName(string? commandName)
    {
        if (string.IsNullOrEmpty(commandName))
            return string.Empty;

        int start = 0;
        int end = commandName.Length - 1;
        while (start <= end && char.IsWhiteSpace(commandName[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(commandName[end]))
            end--;

        if (start <= end && commandName[start] == '!')
        {
            start++;
            while (start <= end && char.IsWhiteSpace(commandName[start]))
                start++;
            while (end >= start && char.IsWhiteSpace(commandName[end]))
                end--;
        }

        return start > end
            ? string.Empty
            : ParsedCommand.ToLowerInvariantSegment(commandName, start, end - start + 1);
    }
}

internal class BotCommandStatisticsBucket
{
    public long GameCommandsRun { get; set; }
    public long TokensSpent { get; set; }
    public long EffectsGiven { get; set; }
    public Dictionary<string, long> CommandUseCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public virtual void Normalize()
    {
        GameCommandsRun = Math.Max(0L, GameCommandsRun);
        TokensSpent = Math.Max(0L, TokensSpent);
        EffectsGiven = Math.Max(0L, EffectsGiven);
        CommandUseCounts = NormalizeCommandMap(CommandUseCounts);
    }

    protected static Dictionary<string, long> NormalizeCommandMap(Dictionary<string, long>? source)
    {
        if (source == null)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, long> normalized = new(source.Count, StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, long> pair in source)
        {
            string command = StatisticNameHelper.NormalizeCommandName(pair.Key);
            if (command.Length == 0 || pair.Value <= 0)
            {
                continue;
            }

            normalized[command] = normalized.TryGetValue(command, out long current)
                ? current + pair.Value
                : pair.Value;
        }

        return normalized;
    }

    protected static Dictionary<string, long> NormalizeScoreMap(Dictionary<string, long>? source)
    {
        if (source == null)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, long> normalized = new(source.Count, StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, long> pair in source)
        {
            string viewer = CommandUserHelper.NormalizeUsername(pair.Key);
            if (viewer.Length == 0 || pair.Value <= 0)
            {
                continue;
            }

            normalized[viewer] = normalized.TryGetValue(viewer, out long current)
                ? current + pair.Value
                : pair.Value;
        }

        return normalized;
    }
}

internal sealed class BotSessionStatistics : BotCommandStatisticsBucket
{
    public Dictionary<string, long> DangerousViewerScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> NiceViewerScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? CurrentLifeStartedUtc { get; set; }
    public long CurrentLifeAccumulatedSeconds { get; set; }
    public bool CurrentLifeHasStarted { get; set; }
    public bool CurrentPlayerIsSpectator { get; set; }
    public bool CurrentLifeWaitingForRespawn { get; set; }
    public bool DeathScoreBaselineSet { get; set; }
    public int? LastDeathScore { get; set; }
    public long Deaths { get; set; }

    public override void Normalize()
    {
        base.Normalize();
        DangerousViewerScores = NormalizeScoreMap(DangerousViewerScores);
        NiceViewerScores = NormalizeScoreMap(NiceViewerScores);
    }
}

internal sealed class BotLifetimeStatistics : BotCommandStatisticsBucket
{
    public long Deaths { get; set; }
    public long LastDeathScore { get; set; }
    public long SessionsStarted { get; set; }
    public long LongestSurvivalSeconds { get; set; }
    public long ShortestSurvivalSeconds { get; set; }

    public override void Normalize()
    {
        base.Normalize();
        Deaths = Math.Max(0L, Deaths);
        LastDeathScore = Math.Max(0L, LastDeathScore);
        SessionsStarted = Math.Max(0L, SessionsStarted);
        LongestSurvivalSeconds = Math.Max(0L, LongestSurvivalSeconds);
        ShortestSurvivalSeconds = Math.Max(0L, ShortestSurvivalSeconds);
    }
}
