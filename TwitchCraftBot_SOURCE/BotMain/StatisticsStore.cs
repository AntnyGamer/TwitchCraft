using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed class BotStatisticsSnapshot
{
    public bool StatisticsEnabled { get; set; } = true;
    public long SessionGameCommandsRun { get; set; }
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

internal static class BotStatisticsStore
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

    public static BotLifetimeStatistics LoadGlobalOnly()
    {
        lock (IoGate)
        {
            try
            {
                return LoadGlobalOnlyCore(GetConnectionNoLock());
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to load statistics database", ex);
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
                DisposeCachedCommandsNoLock();
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

    public static bool TryExportReadableJson()
    {
        try
        {
            lock (IoGate)
            {
                ExportReadableJsonCore(GetConnectionNoLock());
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to export readable statistics JSON", ex);
            return false;
        }
    }

    public static bool ApplyGameCommandDeltaNormalized(string commandName, long tokensSpent, string normalizedViewer, long dangerousScore, long niceScore)
    {
        long safeDangerousScore = Math.Max(0L, dangerousScore);
        long safeNiceScore = Math.Max(0L, niceScore);
        string safeViewer = (safeDangerousScore > 0 || safeNiceScore > 0) && !string.IsNullOrEmpty(normalizedViewer)
            ? normalizedViewer
            : string.Empty;

        return ApplyGameCommandDeltaCore(
            commandName ?? string.Empty,
            Math.Max(0L, tokensSpent),
            safeViewer,
            safeDangerousScore,
            safeNiceScore);
    }

    private static bool ApplyGameCommandDeltaCore(string command, long safeTokensSpent, string normalizedViewer, long safeDangerousScore, long safeNiceScore)
    {
        try
        {
            lock (IoGate)
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteTransaction transaction = connection.BeginTransaction();

                ExecuteIncrementCommandTotalsNoLock(transaction, safeTokensSpent);

                if (command.Length > 0)
                {
                    ExecuteIncrementCommandUseNoLock(transaction, command);
                }

                if (normalizedViewer.Length > 0 && (safeDangerousScore > 0 || safeNiceScore > 0))
                {
                    ExecuteUpsertViewerScoreNoLock(transaction, normalizedViewer, safeDangerousScore, safeNiceScore);
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

    public static bool ApplyEffectsGivenDelta(long effectsGiven)
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
                ExecuteIncrementEffectsGivenNoLock(null, safeEffects);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save effect statistics", ex);
            return false;
        }
    }

    public static bool ApplySessionStartedDelta()
    {
        try
        {
            lock (IoGate)
            {
                ExecuteIncrementSessionsStartedNoLock(null);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to save session statistics", ex);
            return false;
        }
    }

    public static bool SaveDeathScoreBaseline(long deathScore)
    {
        long safeDeathScore = Math.Max(0L, deathScore);

        try
        {
            lock (IoGate)
            {
                ExecuteSaveDeathScoreBaselineNoLock(null, safeDeathScore);
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
                long lastDeathScore = GetLastDeathScoreNoLock(transaction);

                if (safeDeathScore <= lastDeathScore)
                {
                    if (safeDeathScore < lastDeathScore)
                    {
                        ExecuteSaveDeathScoreBaselineNoLock(transaction, safeDeathScore);
                    }

                    transaction.Commit();
                    return true;
                }

                deathCount = safeDeathScore - lastDeathScore;
                ExecuteApplyDeathScoreNoLock(transaction, deathCount, safeDeathScore, safeSurvivedSeconds);

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
                ClearAllCore(GetConnectionNoLock());
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
        string normalizedExcludedViewer = CommandUserHelper.NormalizeUsername(excludedViewer);
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

    private static BotLifetimeStatistics LoadGlobalOnlyCore(SqliteConnection connection)
    {
        BotLifetimeStatistics statistics = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Deaths, LastDeathScore, SessionsStarted, LongestSurvivalSeconds, ShortestSurvivalSeconds,
                       GameCommandsRun, TokensSpent, EffectsGiven
                FROM GlobalStats
                WHERE ID = 1
                LIMIT 1;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                statistics.Deaths = ReadInt64(reader, 0);
                statistics.LastDeathScore = ReadInt64(reader, 1);
                statistics.SessionsStarted = ReadInt64(reader, 2);
                statistics.LongestSurvivalSeconds = ReadInt64(reader, 3);
                statistics.ShortestSurvivalSeconds = ReadInt64(reader, 4);
                statistics.GameCommandsRun = ReadInt64(reader, 5);
                statistics.TokensSpent = ReadInt64(reader, 6);
                statistics.EffectsGiven = ReadInt64(reader, 7);
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CommandName, Count FROM CommandUseCounts WHERE Count > 0;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string commandName = StatisticNameHelper.NormalizeCommandName(reader.GetString(0));
                long count = ReadInt64(reader, 1);
                if (commandName.Length > 0 && count > 0)
                {
                    statistics.CommandUseCounts[commandName] = count;
                }
            }
        }

        statistics.Normalize();
        return statistics;
    }

    private static void ClearAllCore(SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        ExecuteClearCommandUseCountsNoLock(transaction);
        ExecuteClearViewerScoresNoLock(transaction);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE GlobalStats
            SET Deaths = 0,
                LastDeathScore = 0,
                SessionsStarted = 0,
                LongestSurvivalSeconds = 0,
                ShortestSurvivalSeconds = 0,
                GameCommandsRun = 0,
                TokensSpent = 0,
                EffectsGiven = 0
            WHERE ID = 1;
            """);
        transaction.Commit();
    }

    private static string ReadViewerName(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : CommandUserHelper.NormalizeUsername(reader.GetString(ordinal));
    }

    private static void ExportReadableJsonCore(SqliteConnection connection)
    {
        string exportDirectory = JSONExportWriter.GetExportDirectory(DatabasePath);
        Directory.CreateDirectory(exportDirectory);
        JSONExportWriter.WriteReadMe(exportDirectory);

        WriteStatisticsExport(connection, Path.Combine(exportDirectory, "statistics.json"));
        WriteViewerStatisticsExport(connection, Path.Combine(exportDirectory, "statistics_viewers.json"));
    }

    private static void WriteStatisticsExport(SqliteConnection connection, string path)
    {
        JSONExportWriter.WriteJsonExportAtomic(
            path,
            writer =>
            {
                JSONExportWriter.WriteExportStart(writer);
                writer.WritePropertyName("Global");
                writer.WriteStartObject();

                long deaths = 0;
                long sessionsStarted = 0;
                long longestSurvivalSeconds = 0;
                long shortestSurvivalSeconds = 0;
                long gameCommandsRun = 0;
                long tokensSpent = 0;
                long effectsGiven = 0;

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = """
                        SELECT Deaths, LastDeathScore, SessionsStarted, LongestSurvivalSeconds, ShortestSurvivalSeconds,
                               GameCommandsRun, TokensSpent, EffectsGiven
                        FROM GlobalStats
                        WHERE ID = 1
                        LIMIT 1;
                        """;

                    using SqliteDataReader reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        deaths = ReadInt64(reader, 0);
                        sessionsStarted = ReadInt64(reader, 2);
                        longestSurvivalSeconds = ReadInt64(reader, 3);
                        shortestSurvivalSeconds = ReadInt64(reader, 4);
                        gameCommandsRun = ReadInt64(reader, 5);
                        tokensSpent = ReadInt64(reader, 6);
                        effectsGiven = ReadInt64(reader, 7);
                    }
                }

                JSONExportWriter.WriteNonNegativeLongProperty(writer, "Deaths", deaths);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "SessionsStarted", sessionsStarted);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "LongestSurvivalSeconds", longestSurvivalSeconds);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "ShortestSurvivalSeconds", shortestSurvivalSeconds);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "GameCommandsRun", gameCommandsRun);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "TokensSpent", tokensSpent);
                JSONExportWriter.WriteNonNegativeLongProperty(writer, "EffectsGiven", effectsGiven);
                writer.WriteEndObject();

                writer.WritePropertyName("CommandUseCounts");
                writer.WriteStartObject();

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT CommandName, Count FROM CommandUseCounts WHERE Count > 0 ORDER BY CommandName COLLATE NOCASE ASC;";
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        string commandName = StatisticNameHelper.NormalizeCommandName(reader.GetString(0));
                        long count = ReadInt64(reader, 1);
                        if (commandName.Length == 0 || count <= 0)
                        {
                            continue;
                        }

                        writer.WritePropertyName("!" + commandName);
                        writer.WriteValue(count);
                    }
                }

                writer.WriteEndObject();
                JSONExportWriter.WriteExportEnd(writer);
            });
    }

    private static void WriteViewerStatisticsExport(SqliteConnection connection, string path)
    {
        JSONExportWriter.WriteJsonExportAtomic(
            path,
            writer =>
            {
                JSONExportWriter.WriteExportStart(writer);
                JSONExportWriter.WriteSectionBreak(writer);
                writer.WritePropertyName("ViewerStatistics");
                writer.WriteStartObject();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT Username, DangerousScore, NiceScore FROM ViewerScores WHERE DangerousScore > 0 OR NiceScore > 0 ORDER BY Username COLLATE NOCASE ASC;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string username = CommandUserHelper.NormalizeUsername(reader.GetString(0));
                    long dangerous = ReadInt64(reader, 1);
                    long nice = ReadInt64(reader, 2);
                    if (username.Length == 0 || (dangerous <= 0 && nice <= 0))
                    {
                        continue;
                    }

                    writer.WritePropertyName(username);
                    writer.WriteStartObject();
                    writer.WritePropertyName("Username");
                    writer.WriteValue(username);
                    JSONExportWriter.WriteNonNegativeLongProperty(writer, "DangerousScore", dangerous);
                    JSONExportWriter.WriteNonNegativeLongProperty(writer, "NiceScore", nice);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                JSONExportWriter.WriteExportEnd(writer);
            });
    }

    private static SqliteConnection GetConnectionNoLock()
    {
        if (_connection != null)
        {
            EnsureSchemaNoLock(_connection);
            return _connection;
        }

        ConfigurationStore.CheckRootFolder();
        FileSystemHelper.EnsureDirectoryForFile(DatabasePath);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA busy_timeout=5000;
                """;
            command.ExecuteNonQuery();
        }

        EnsureSchemaNoLock(_connection);
        return _connection;
    }

    private static void EnsureSchemaNoLock(SqliteConnection connection)
    {
        if (_schemaInitialized)
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS GlobalStats (
                ID INTEGER PRIMARY KEY CHECK (ID = 1),
                Deaths INTEGER NOT NULL DEFAULT 0 CHECK (Deaths >= 0),
                LastDeathScore INTEGER NOT NULL DEFAULT 0 CHECK (LastDeathScore >= 0),
                SessionsStarted INTEGER NOT NULL DEFAULT 0 CHECK (SessionsStarted >= 0),
                LongestSurvivalSeconds INTEGER NOT NULL DEFAULT 0 CHECK (LongestSurvivalSeconds >= 0),
                ShortestSurvivalSeconds INTEGER NOT NULL DEFAULT 0 CHECK (ShortestSurvivalSeconds >= 0),
                GameCommandsRun INTEGER NOT NULL DEFAULT 0 CHECK (GameCommandsRun >= 0),
                TokensSpent INTEGER NOT NULL DEFAULT 0 CHECK (TokensSpent >= 0),
                EffectsGiven INTEGER NOT NULL DEFAULT 0 CHECK (EffectsGiven >= 0)
            );

            INSERT OR IGNORE INTO GlobalStats (ID) VALUES (1);

            CREATE TABLE IF NOT EXISTS CommandUseCounts (
                CommandName TEXT PRIMARY KEY COLLATE NOCASE,
                Count INTEGER NOT NULL CHECK (Count >= 0)
            );

            CREATE TABLE IF NOT EXISTS ViewerScores (
                Username TEXT PRIMARY KEY COLLATE NOCASE,
                DangerousScore INTEGER NOT NULL DEFAULT 0 CHECK (DangerousScore >= 0),
                NiceScore INTEGER NOT NULL DEFAULT 0 CHECK (NiceScore >= 0)
            );

            CREATE INDEX IF NOT EXISTS IX_CommandUseCounts_Count
                ON CommandUseCounts (Count DESC, CommandName COLLATE NOCASE ASC);

            CREATE INDEX IF NOT EXISTS IX_ViewerScores_Dangerous
                ON ViewerScores (DangerousScore DESC, Username COLLATE NOCASE ASC)
                WHERE DangerousScore > 0;

            CREATE INDEX IF NOT EXISTS IX_ViewerScores_Nice
                ON ViewerScores (NiceScore DESC, Username COLLATE NOCASE ASC)
                WHERE NiceScore > 0;
            """;
        command.ExecuteNonQuery();
        _schemaInitialized = true;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand CreatePreparedCommandNoLock(string commandText)
    {
        SqliteCommand command = GetConnectionNoLock().CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    private static void ExecuteIncrementCommandTotalsNoLock(SqliteTransaction transaction, long tokensSpent)
    {
        if (_incrementCommandTotalsCommand == null)
        {
            _incrementCommandTotalsCommand = CreatePreparedCommandNoLock("""
                UPDATE GlobalStats
                SET GameCommandsRun = GameCommandsRun + 1,
                    TokensSpent = TokensSpent + $tokensSpent
                WHERE ID = 1;
                """);
            _incrementCommandTotalsTokensSpent = _incrementCommandTotalsCommand.Parameters.Add("$tokensSpent", SqliteType.Integer);
            _incrementCommandTotalsCommand.Prepare();
        }

        _incrementCommandTotalsCommand.Transaction = transaction;
        _incrementCommandTotalsTokensSpent!.Value = tokensSpent;
        _incrementCommandTotalsCommand.ExecuteNonQuery();
    }

    private static void ExecuteIncrementCommandUseNoLock(SqliteTransaction transaction, string commandName)
    {
        if (_incrementCommandUseCommand == null)
        {
            _incrementCommandUseCommand = CreatePreparedCommandNoLock("""
                INSERT INTO CommandUseCounts (CommandName, Count)
                VALUES ($commandName, 1)
                ON CONFLICT(CommandName) DO UPDATE SET Count = Count + 1;
                """);
            _incrementCommandUseName = _incrementCommandUseCommand.Parameters.Add("$commandName", SqliteType.Text);
            _incrementCommandUseCommand.Prepare();
        }

        _incrementCommandUseCommand.Transaction = transaction;
        _incrementCommandUseName!.Value = commandName;
        _incrementCommandUseCommand.ExecuteNonQuery();
    }

    private static void ExecuteUpsertViewerScoreNoLock(SqliteTransaction transaction, string username, long dangerousScore, long niceScore)
    {
        if (_upsertViewerScoreCommand == null)
        {
            _upsertViewerScoreCommand = CreatePreparedCommandNoLock("""
                INSERT INTO ViewerScores (Username, DangerousScore, NiceScore)
                VALUES ($username, $dangerousScore, $niceScore)
                ON CONFLICT(Username) DO UPDATE SET
                    DangerousScore = ViewerScores.DangerousScore + excluded.DangerousScore,
                    NiceScore = ViewerScores.NiceScore + excluded.NiceScore;
                """);
            _upsertViewerScoreUsername = _upsertViewerScoreCommand.Parameters.Add("$username", SqliteType.Text);
            _upsertViewerScoreDangerous = _upsertViewerScoreCommand.Parameters.Add("$dangerousScore", SqliteType.Integer);
            _upsertViewerScoreNice = _upsertViewerScoreCommand.Parameters.Add("$niceScore", SqliteType.Integer);
            _upsertViewerScoreCommand.Prepare();
        }

        _upsertViewerScoreCommand.Transaction = transaction;
        _upsertViewerScoreUsername!.Value = username;
        _upsertViewerScoreDangerous!.Value = dangerousScore;
        _upsertViewerScoreNice!.Value = niceScore;
        _upsertViewerScoreCommand.ExecuteNonQuery();
    }

    private static void ExecuteIncrementEffectsGivenNoLock(SqliteTransaction? transaction, long effectsGiven)
    {
        if (_incrementEffectsGivenCommand == null)
        {
            _incrementEffectsGivenCommand = CreatePreparedCommandNoLock("UPDATE GlobalStats SET EffectsGiven = EffectsGiven + $effectsGiven WHERE ID = 1;");
            _incrementEffectsGivenAmount = _incrementEffectsGivenCommand.Parameters.Add("$effectsGiven", SqliteType.Integer);
            _incrementEffectsGivenCommand.Prepare();
        }

        _incrementEffectsGivenCommand.Transaction = transaction;
        _incrementEffectsGivenAmount!.Value = effectsGiven;
        _incrementEffectsGivenCommand.ExecuteNonQuery();
    }

    private static void ExecuteIncrementSessionsStartedNoLock(SqliteTransaction? transaction)
    {
        if (_incrementSessionsStartedCommand == null)
        {
            _incrementSessionsStartedCommand = CreatePreparedCommandNoLock("UPDATE GlobalStats SET SessionsStarted = SessionsStarted + 1 WHERE ID = 1;");
            _incrementSessionsStartedCommand.Prepare();
        }
        _incrementSessionsStartedCommand.Transaction = transaction;
        _incrementSessionsStartedCommand.ExecuteNonQuery();
    }

    private static void ExecuteSaveDeathScoreBaselineNoLock(SqliteTransaction? transaction, long deathScore)
    {
        if (_saveDeathScoreBaselineCommand == null)
        {
            _saveDeathScoreBaselineCommand = CreatePreparedCommandNoLock("UPDATE GlobalStats SET LastDeathScore = $deathScore WHERE ID = 1;");
            _saveDeathScoreBaselineValue = _saveDeathScoreBaselineCommand.Parameters.Add("$deathScore", SqliteType.Integer);
            _saveDeathScoreBaselineCommand.Prepare();
        }

        _saveDeathScoreBaselineCommand.Transaction = transaction;
        _saveDeathScoreBaselineValue!.Value = deathScore;
        _saveDeathScoreBaselineCommand.ExecuteNonQuery();
    }

    private static void ExecuteApplyDeathScoreNoLock(SqliteTransaction transaction, long deathCount, long deathScore, long survivedSeconds)
    {
        if (_applyDeathScoreCommand == null)
        {
            _applyDeathScoreCommand = CreatePreparedCommandNoLock("""
                UPDATE GlobalStats
                SET Deaths = Deaths + $deathCount,
                    LastDeathScore = $deathScore,
                    LongestSurvivalSeconds = CASE
                        WHEN $survivedSeconds > LongestSurvivalSeconds THEN $survivedSeconds
                        ELSE LongestSurvivalSeconds
                    END,
                    ShortestSurvivalSeconds = CASE
                        WHEN $survivedSeconds > 0 AND (ShortestSurvivalSeconds = 0 OR $survivedSeconds < ShortestSurvivalSeconds) THEN $survivedSeconds
                        ELSE ShortestSurvivalSeconds
                    END
                WHERE ID = 1;
                """);
            _applyDeathScoreDeathCount = _applyDeathScoreCommand.Parameters.Add("$deathCount", SqliteType.Integer);
            _applyDeathScoreDeathScore = _applyDeathScoreCommand.Parameters.Add("$deathScore", SqliteType.Integer);
            _applyDeathScoreSurvivedSeconds = _applyDeathScoreCommand.Parameters.Add("$survivedSeconds", SqliteType.Integer);
            _applyDeathScoreCommand.Prepare();
        }

        _applyDeathScoreCommand.Transaction = transaction;
        _applyDeathScoreDeathCount!.Value = deathCount;
        _applyDeathScoreDeathScore!.Value = deathScore;
        _applyDeathScoreSurvivedSeconds!.Value = survivedSeconds;
        _applyDeathScoreCommand.ExecuteNonQuery();
    }

    private static void ExecuteClearCommandUseCountsNoLock(SqliteTransaction transaction)
    {
        if (_clearCommandUseCountsCommand == null)
        {
            _clearCommandUseCountsCommand = CreatePreparedCommandNoLock("DELETE FROM CommandUseCounts;");
            _clearCommandUseCountsCommand.Prepare();
        }
        _clearCommandUseCountsCommand.Transaction = transaction;
        _clearCommandUseCountsCommand.ExecuteNonQuery();
    }

    private static void ExecuteClearViewerScoresNoLock(SqliteTransaction transaction)
    {
        if (_clearViewerScoresCommand == null)
        {
            _clearViewerScoresCommand = CreatePreparedCommandNoLock("DELETE FROM ViewerScores;");
            _clearViewerScoresCommand.Prepare();
        }
        _clearViewerScoresCommand.Transaction = transaction;
        _clearViewerScoresCommand.ExecuteNonQuery();
    }

    private static void DisposeCachedCommandsNoLock()
    {
        DisposeCommand(ref _incrementCommandTotalsCommand);
        DisposeCommand(ref _incrementCommandUseCommand);
        DisposeCommand(ref _upsertViewerScoreCommand);
        DisposeCommand(ref _incrementEffectsGivenCommand);
        DisposeCommand(ref _incrementSessionsStartedCommand);
        DisposeCommand(ref _saveDeathScoreBaselineCommand);
        DisposeCommand(ref _getLastDeathScoreCommand);
        DisposeCommand(ref _applyDeathScoreCommand);
        DisposeCommand(ref _clearCommandUseCountsCommand);
        DisposeCommand(ref _clearViewerScoresCommand);
        _incrementCommandTotalsTokensSpent = null;
        _incrementCommandUseName = null;
        _upsertViewerScoreUsername = null;
        _upsertViewerScoreDangerous = null;
        _upsertViewerScoreNice = null;
        _incrementEffectsGivenAmount = null;
        _saveDeathScoreBaselineValue = null;
        _applyDeathScoreDeathCount = null;
        _applyDeathScoreDeathScore = null;
        _applyDeathScoreSurvivedSeconds = null;
    }

    private static void DisposeCommand(ref SqliteCommand? command)
    {
        try
        {
            command?.Dispose();
        }
        finally
        {
            command = null;
        }
    }

    private static long GetLastDeathScoreNoLock(SqliteTransaction? transaction)
    {
        if (_getLastDeathScoreCommand == null)
        {
            _getLastDeathScoreCommand = CreatePreparedCommandNoLock("SELECT LastDeathScore FROM GlobalStats WHERE ID = 1 LIMIT 1;");
            _getLastDeathScoreCommand.Prepare();
        }

        _getLastDeathScoreCommand.Transaction = transaction;
        object? result = _getLastDeathScoreCommand.ExecuteScalar();
        return result == null || result == DBNull.Value
            ? 0L
            : Math.Max(0L, Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    private static long ReadInt64(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? 0L : Math.Max(0L, reader.GetInt64(ordinal));
    }

}
