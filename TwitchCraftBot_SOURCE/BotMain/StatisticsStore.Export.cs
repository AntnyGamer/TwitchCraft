using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

internal static partial class BotStatisticsStore
{
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

}
