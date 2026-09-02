using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

internal static partial class BotStatisticsStore
{
    private const string StatisticsDatabaseFileName = "statistics.db";

    private static readonly Lock IoGate = new();
    private static readonly string DatabasePath = Path.Combine(ConfigurationStore.WorkingDirectory, StatisticsDatabaseFileName);
    private static SqliteConnection? _connection;
    private static bool _schemaInitialized;
    private static SqliteCommand? _incrementCommandTotalsCommand;
    private static SqliteParameter? _incrementCommandTotalsTokensSpent;
    private static SqliteCommand? _incrementCommandUseCommand;
    private static SqliteParameter? _incrementCommandUseName;
    private static SqliteCommand? _upsertViewerScoreCommand;
    private static SqliteParameter? _upsertViewerScoreUsername;
    private static SqliteParameter? _upsertViewerScoreDangerous;
    private static SqliteParameter? _upsertViewerScoreNice;
    private static SqliteCommand? _incrementEffectsGivenCommand;
    private static SqliteParameter? _incrementEffectsGivenAmount;
    private static SqliteCommand? _incrementSessionsStartedCommand;
    private static SqliteCommand? _saveDeathScoreBaselineCommand;
    private static SqliteParameter? _saveDeathScoreBaselineValue;
    private static SqliteCommand? _getLastDeathScoreCommand;
    private static SqliteCommand? _applyDeathScoreCommand;
    private static SqliteParameter? _applyDeathScoreDeathCount;
    private static SqliteParameter? _applyDeathScoreDeathScore;
    private static SqliteParameter? _applyDeathScoreSurvivedSeconds;
    private static SqliteCommand? _clearCommandUseCountsCommand;
    private static SqliteCommand? _clearViewerScoresCommand;

    public static BotLifetimeStatistics LoadGlobal()
    {
        lock (IoGate)
        {
            try
            {
                return LoadGlobalCore(GetConnectionNoLock());
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to load statistics database", ex);
                ErrorHandling.ShowStatsWarning();
                return new BotLifetimeStatistics();
            }
        }
    }

    public static void CloseConnection()
    {
        lock (IoGate)
        {
            try
            {
                DisposeCommandsNoLock();
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to close statistics database", ex);
            }
            finally
            {
                _connection = null;
                _schemaInitialized = false;
            }
        }
    }

    public static bool TryExportJson()
    {
        try
        {
            lock (IoGate)
            {
                ExportJsonCore(GetConnectionNoLock());
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to export readable statistics JSON", ex);
            return false;
        }
    }

    public static bool ApplyCommandDelta(string commandName, long tokensSpent, string normalizedViewer, long dangerousScore, long niceScore)
    {
        long safeDangerousScore = Math.Max(0L, dangerousScore);
        long safeNiceScore = Math.Max(0L, niceScore);
        string safeViewer = (safeDangerousScore > 0 || safeNiceScore > 0) && !string.IsNullOrEmpty(normalizedViewer)
            ? normalizedViewer
            : string.Empty;

        return ApplyCommandDeltaCore(
            commandName ?? string.Empty,
            Math.Max(0L, tokensSpent),
            safeViewer,
            safeDangerousScore,
            safeNiceScore);
    }

    private static bool ApplyCommandDeltaCore(string command, long safeTokensSpent, string normalizedViewer, long safeDangerousScore, long safeNiceScore)
    {
        try
        {
            lock (IoGate)
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteTransaction transaction = connection.BeginTransaction();

                AddCommandTotalsNoLock(transaction, safeTokensSpent);

                if (command.Length > 0)
                {
                    AddCommandUseNoLock(transaction, command);
                }

                if (normalizedViewer.Length > 0 && (safeDangerousScore > 0 || safeNiceScore > 0))
                {
                    SaveViewerScoreNoLock(transaction, normalizedViewer, safeDangerousScore, safeNiceScore);
                }

                transaction.Commit();
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save command statistics", ex);
            return false;
        }
    }

    public static bool ApplyEffectsDelta(long effectsGiven)
    {
        long safeEffects = Math.Max(0L, effectsGiven);
        if (safeEffects <= 0)
        {
            return true;
        }

        try
        {
            lock (IoGate)
            {
                AddEffectsNoLock(safeEffects);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save effect statistics", ex);
            return false;
        }
    }

    public static bool ApplySessionDelta()
    {
        try
        {
            lock (IoGate)
            {
                AddSessionNoLock();
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save session statistics", ex);
            return false;
        }
    }

    public static bool SaveDeathBaseline(long deathScore)
    {
        long safeDeathScore = Math.Max(0L, deathScore);

        try
        {
            lock (IoGate)
            {
                SaveDeathBaselineNoLock(null, safeDeathScore);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save death score baseline", ex);
            return false;
        }
    }

    public static bool ApplyDeathScore(long deathScore, long survivedSeconds, out long deathCount)
    {
        long safeDeathScore = Math.Max(0L, deathScore);
        long safeSurvivedSeconds = Math.Max(0L, survivedSeconds);
        deathCount = 0;

        try
        {
            lock (IoGate)
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteTransaction transaction = connection.BeginTransaction();
                long lastDeathScore = GetDeathScoreNoLock(transaction);

                if (safeDeathScore <= lastDeathScore)
                {
                    if (safeDeathScore < lastDeathScore)
                    {
                        SaveDeathBaselineNoLock(transaction, safeDeathScore);
                    }

                    transaction.Commit();
                    return true;
                }

                deathCount = safeDeathScore - lastDeathScore;
                UpdateDeathScoreNoLock(transaction, deathCount, safeDeathScore, safeSurvivedSeconds);

                transaction.Commit();
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save death statistics", ex);
            deathCount = 0;
            return false;
        }
    }

    public static bool ClearAll()
    {
        try
        {
            lock (IoGate)
            {
                ClearCore(GetConnectionNoLock());
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to reset statistics database", ex);
            return false;
        }
    }

    public static (string DangerousViewer, string NiceViewer) GetTopViewers(string excludedViewer)
    {
        string normalizedExcludedViewer = CommandUserHelper.NormalizeUser(excludedViewer);
        string excludedClause = normalizedExcludedViewer.Length == 0 ? string.Empty : "AND Username <> $excludedViewer";

        lock (IoGate)
        {
            try
            {
                using SqliteCommand command = GetConnectionNoLock().CreateCommand();
                command.CommandText = $"""
                    SELECT
                        (SELECT Username FROM ViewerScores WHERE DangerousScore > 0 {excludedClause} ORDER BY DangerousScore DESC, Username COLLATE NOCASE ASC LIMIT 1),
                        (SELECT Username FROM ViewerScores WHERE NiceScore > 0 {excludedClause} ORDER BY NiceScore DESC, Username COLLATE NOCASE ASC LIMIT 1);
                    """;
                if (normalizedExcludedViewer.Length > 0)
                    command.Parameters.Add("$excludedViewer", SqliteType.Text).Value = normalizedExcludedViewer;

                using SqliteDataReader reader = command.ExecuteReader();
                return reader.Read()
                    ? (ReadViewerName(reader, 0), ReadViewerName(reader, 1))
                    : (string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to query top viewer statistics", ex);
                return (string.Empty, string.Empty);
            }
        }
    }

}
