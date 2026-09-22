using System.IO;
using Microsoft.Data.Sqlite;

namespace TwitchCraft_V1;

internal static partial class StatisticsStore
{
    private static LifetimeStatistics LoadGlobalCore(SqliteConnection connection)
    {
        LifetimeStatistics statistics = new();
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
                string commandName = StatisticNameHelper.CleanCommandName(reader.GetString(0));
                long count = ReadInt64(reader, 1);
                if (commandName.Length > 0 && count > 0)
                {
                    statistics.CommandUseCounts[commandName] = statistics.CommandUseCounts.TryGetValue(commandName, out long current) ? current + count : count;
                }
            }
        }

        statistics.Normalize();
        return statistics;
    }

    private static void ClearCore(SqliteConnection connection, long deathScore)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        ClearCommandCountsNoLock(transaction);
        ClearScoresNoLock(transaction);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE GlobalStats
            SET Deaths = 0,
                LastDeathScore = $deathScore,
                SessionsStarted = 0,
                LongestSurvivalSeconds = 0,
                ShortestSurvivalSeconds = 0,
                GameCommandsRun = 0,
                TokensSpent = 0,
                EffectsGiven = 0
            WHERE ID = 1;
            """;
        command.Parameters.Add("$deathScore", SqliteType.Integer).Value = deathScore;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string ReadViewerName(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : CommandUserHelper.NormalizeUser(reader.GetString(ordinal));
    }

    private static void ExportJsonCore(SqliteConnection connection)
    {
        string exportDirectory = JsonExportWriter.GetExportDirectory(DatabasePath);
        Directory.CreateDirectory(exportDirectory);
        JsonExportWriter.WriteReadme(exportDirectory);

        WriteStats(connection, Path.Combine(exportDirectory, "statistics.json"));
        WriteViewers(connection, Path.Combine(exportDirectory, "statistics_viewers.json"));
    }

    private static void WriteStats(SqliteConnection connection, string path)
    {
        JsonExportWriter.WriteJsonAtomic(
            path,
            writer =>
            {
                JsonExportWriter.WriteExportStart(writer);
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

                JsonExportWriter.WriteCount(writer, "Deaths", deaths);
                JsonExportWriter.WriteCount(writer, "SessionsStarted", sessionsStarted);
                JsonExportWriter.WriteCount(writer, "LongestSurvivalSeconds", longestSurvivalSeconds);
                JsonExportWriter.WriteCount(writer, "ShortestSurvivalSeconds", shortestSurvivalSeconds);
                JsonExportWriter.WriteCount(writer, "GameCommandsRun", gameCommandsRun);
                JsonExportWriter.WriteCount(writer, "TokensSpent", tokensSpent);
                JsonExportWriter.WriteCount(writer, "EffectsGiven", effectsGiven);
                writer.WriteEndObject();

                writer.WritePropertyName("CommandUseCounts");
                writer.WriteStartObject();

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT CommandName, Count FROM CommandUseCounts WHERE Count > 0 ORDER BY CommandName COLLATE NOCASE ASC;";
                    using SqliteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        string commandName = StatisticNameHelper.CleanCommandName(reader.GetString(0));
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
                JsonExportWriter.WriteExportEnd(writer);
            });
    }

    private static void WriteViewers(SqliteConnection connection, string path)
    {
        JsonExportWriter.WriteJsonAtomic(
            path,
            writer =>
            {
                JsonExportWriter.WriteExportStart(writer);
                JsonExportWriter.WriteSectionBreak(writer);
                writer.WritePropertyName("ViewerStatistics");
                writer.WriteStartObject();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT Username, DangerousScore, NiceScore FROM ViewerScores WHERE DangerousScore > 0 OR NiceScore > 0 ORDER BY Username COLLATE NOCASE ASC;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string username = CommandUserHelper.NormalizeUser(reader.GetString(0));
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
                    JsonExportWriter.WriteCount(writer, "DangerousScore", dangerous);
                    JsonExportWriter.WriteCount(writer, "NiceScore", nice);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
                JsonExportWriter.WriteExportEnd(writer);
            });
    }
}
