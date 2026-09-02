using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

internal static partial class BotStatisticsStore
{
    private static SqliteConnection GetConnectionNoLock()
    {
        if (_connection != null)
        {
            EnsureSchemaNoLock(_connection);
            return _connection;
        }

        ConfigurationStore.EnsureWorkDir();
        FileSystemHelper.EnsureParentDir(DatabasePath);

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

    private static SqliteCommand PrepareCommandNoLock(string commandText)
    {
        SqliteCommand command = GetConnectionNoLock().CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    private static void AddCommandTotalsNoLock(SqliteTransaction transaction, long tokensSpent)
    {
        if (_incrementCommandTotalsCommand == null)
        {
            _incrementCommandTotalsCommand = PrepareCommandNoLock("""
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

    private static void AddCommandUseNoLock(SqliteTransaction transaction, string commandName)
    {
        if (_incrementCommandUseCommand == null)
        {
            _incrementCommandUseCommand = PrepareCommandNoLock("""
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

    private static void SaveViewerScoreNoLock(SqliteTransaction transaction, string username, long dangerousScore, long niceScore)
    {
        if (_upsertViewerScoreCommand == null)
        {
            _upsertViewerScoreCommand = PrepareCommandNoLock("""
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

    private static void AddEffectsNoLock(long effectsGiven)
    {
        if (_incrementEffectsGivenCommand == null)
        {
            _incrementEffectsGivenCommand = PrepareCommandNoLock("UPDATE GlobalStats SET EffectsGiven = EffectsGiven + $effectsGiven WHERE ID = 1;");
            _incrementEffectsGivenAmount = _incrementEffectsGivenCommand.Parameters.Add("$effectsGiven", SqliteType.Integer);
            _incrementEffectsGivenCommand.Prepare();
        }

        _incrementEffectsGivenAmount!.Value = effectsGiven;
        _incrementEffectsGivenCommand.ExecuteNonQuery();
    }

    private static void AddSessionNoLock()
    {
        if (_incrementSessionsStartedCommand == null)
        {
            _incrementSessionsStartedCommand = PrepareCommandNoLock("UPDATE GlobalStats SET SessionsStarted = SessionsStarted + 1 WHERE ID = 1;");
            _incrementSessionsStartedCommand.Prepare();
        }
        _incrementSessionsStartedCommand.ExecuteNonQuery();
    }

    private static void SaveDeathBaselineNoLock(SqliteTransaction? transaction, long deathScore)
    {
        if (_saveDeathScoreBaselineCommand == null)
        {
            _saveDeathScoreBaselineCommand = PrepareCommandNoLock("UPDATE GlobalStats SET LastDeathScore = $deathScore WHERE ID = 1;");
            _saveDeathScoreBaselineValue = _saveDeathScoreBaselineCommand.Parameters.Add("$deathScore", SqliteType.Integer);
            _saveDeathScoreBaselineCommand.Prepare();
        }

        _saveDeathScoreBaselineCommand.Transaction = transaction;
        _saveDeathScoreBaselineValue!.Value = deathScore;
        _saveDeathScoreBaselineCommand.ExecuteNonQuery();
    }

    private static void UpdateDeathScoreNoLock(SqliteTransaction transaction, long deathCount, long deathScore, long survivedSeconds)
    {
        if (_applyDeathScoreCommand == null)
        {
            _applyDeathScoreCommand = PrepareCommandNoLock("""
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

    private static void ClearCommandCountsNoLock(SqliteTransaction transaction)
    {
        if (_clearCommandUseCountsCommand == null)
        {
            _clearCommandUseCountsCommand = PrepareCommandNoLock("DELETE FROM CommandUseCounts;");
            _clearCommandUseCountsCommand.Prepare();
        }
        _clearCommandUseCountsCommand.Transaction = transaction;
        _clearCommandUseCountsCommand.ExecuteNonQuery();
    }

    private static void ClearScoresNoLock(SqliteTransaction transaction)
    {
        if (_clearViewerScoresCommand == null)
        {
            _clearViewerScoresCommand = PrepareCommandNoLock("DELETE FROM ViewerScores;");
            _clearViewerScoresCommand.Prepare();
        }
        _clearViewerScoresCommand.Transaction = transaction;
        _clearViewerScoresCommand.ExecuteNonQuery();
    }

    private static void DisposeCommandsNoLock()
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

    private static long GetDeathScoreNoLock(SqliteTransaction? transaction)
    {
        if (_getLastDeathScoreCommand == null)
        {
            _getLastDeathScoreCommand = PrepareCommandNoLock("SELECT LastDeathScore FROM GlobalStats WHERE ID = 1 LIMIT 1;");
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
