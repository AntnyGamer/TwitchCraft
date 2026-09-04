using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TwitchCraftBot_V1;

internal sealed partial class TokenHandler
{
    public IReadOnlyList<KeyValuePair<string, int>> GetTopBalances(int limit)
    {
        if (limit <= 0)
            return [];

        limit = Math.Min(limit, 100);
        lock (_gate)
        {
            try
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT Username, Balance FROM TokenBalances WHERE Balance > 0 ORDER BY Balance DESC, Username COLLATE NOCASE ASC LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", limit);

                List<KeyValuePair<string, int>> result = new(limit);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string username = Normalize(reader.GetString(0));
                    int balance = ClampBalance(reader.GetInt64(1));
                    if (username.Length > 0 && balance > 0)
                        result.Add(new KeyValuePair<string, int>(username, balance));
                }

                return result;
            }
            catch (Exception ex)
            {
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load token leaderboard", ex);
                return [];
            }
        }
    }

    public TokenRankResult? GetRank(string user)
    {
        string normalized = Normalize(user);
        if (normalized.Length == 0)
            return null;

        lock (_gate)
        {
            try
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Username, Balance, Position
                    FROM (
                        SELECT Username,
                               Balance,
                               ROW_NUMBER() OVER (ORDER BY Balance DESC, Username COLLATE NOCASE ASC) AS Position
                        FROM TokenBalances
                        WHERE Balance > 0
                    )
                    WHERE Username = $username COLLATE NOCASE;
                    """;
                command.Parameters.AddWithValue("$username", normalized);

                using SqliteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                    return null;

                string username = Normalize(reader.GetString(0));
                int balance = ClampBalance(reader.GetInt64(1));
                long rank = reader.GetInt64(2);
                return username.Length == 0 || balance <= 0 || rank <= 0
                    ? null
                    : new TokenRankResult(username, balance, rank > int.MaxValue ? int.MaxValue : (int)rank);
            }
            catch (Exception ex)
            {
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load token rank", ex);
                return null;
            }
        }
    }

    private bool EnsureLoadedNoLock(string normalized)
    {
        if (_loadedUsers.Contains(normalized))
        {
            return true;
        }

        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            if (_selectBalanceCommand == null)
            {
                _selectBalanceCommand = connection.CreateCommand();
                _selectBalanceCommand.CommandText = "SELECT Balance FROM TokenBalances WHERE Username = $username;";
                _selectBalanceUsername = _selectBalanceCommand.Parameters.Add("$username", SqliteType.Text);
                _selectBalanceCommand.Prepare();
            }

            _selectBalanceUsername!.Value = normalized;
            object? balance = _selectBalanceCommand.ExecuteScalar();
            SetCacheNoLock(
                normalized,
                balance is null || balance == DBNull.Value
                    ? 0
                    : ClampBalance(Convert.ToInt64(balance, CultureInfo.InvariantCulture)));

            _loadedUsers.Add(normalized);
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load viewer token balance", ex);
            return false;
        }
    }

    private bool EnsureManyLoadedNoLock(IReadOnlyCollection<string> users)
    {
        List<string>? toLoad = null;
        foreach (string normalized in users)
        {
            if (!string.IsNullOrEmpty(normalized) && !_loadedUsers.Contains(normalized))
                (toLoad ??= new List<string>(users.Count)).Add(normalized);
        }

        if (toLoad == null)
            return true;

        if (toLoad.Count == 1)
            return EnsureLoadedNoLock(toLoad[0]);

        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            const int batchSize = 250;
            for (int startIndex = 0; startIndex < toLoad.Count; startIndex += batchSize)
            {
                int count = Math.Min(batchSize, toLoad.Count - startIndex);
                LoadBalancesNoLock(connection, toLoad, startIndex, count);
            }

            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load viewer token balances", ex);
            return false;
        }
    }

    private void LoadBalancesNoLock(SqliteConnection connection, List<string> users, int startIndex, int count)
    {
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder query = new("SELECT Username, Balance FROM TokenBalances WHERE Username IN (", 64 + count * 8);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                query.Append(", ");

            string parameterName = BatchParameterNames[i];
            query.Append(parameterName);
            command.Parameters.Add(parameterName, SqliteType.Text).Value = users[startIndex + i];
        }

        query.Append(");");
        command.CommandText = query.ToString();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string username = Normalize(reader.GetString(0));
                if (username.Length == 0)
                    continue;

                SetCacheNoLock(username, ClampBalance(reader.GetInt64(1)));
                _loadedUsers.Add(username);
            }
        }

        for (int i = 0; i < count; i++)
        {
            string username = users[startIndex + i];
            if (!_loadedUsers.Contains(username))
                SetCacheNoLock(username, 0);

            _loadedUsers.Add(username);
        }
    }

    private bool SaveChangedNoLock(string normalized, int balance)
    {
        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            SaveOneNoLock(connection, normalized, balance);
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to save viewer token balance", ex);
            return false;
        }
    }

    private bool SaveChangesNoLock(Dictionary<string, int> changedUsers)
    {
        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand upsert = CreateUpsertCommand(connection, transaction);
            using SqliteCommand delete = CreateDeleteCommand(connection, transaction);
            upsert.Prepare();
            delete.Prepare();

            foreach (KeyValuePair<string, int> pair in changedUsers)
                SaveBalanceNoLock(upsert, delete, pair.Key, pair.Value);

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to save viewer token balance", ex);
            return false;
        }
    }

    private int SaveOneByOneNoLock(
        Dictionary<string, int> changedUsers,
        Dictionary<string, int> originalBalances)
    {
        int savedCount = 0;
        SqliteConnection connection;
        try
        {
            connection = GetConnectionNoLock();
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to retry viewer token balances individually", ex);
            return 0;
        }

        foreach (KeyValuePair<string, int> pair in changedUsers)
        {
            try
            {
                SaveOneNoLock(connection, pair.Key, pair.Value);
                SetCacheNoLock(pair.Key, pair.Value);
                savedCount++;
            }
            catch (Exception ex)
            {
                if (originalBalances.TryGetValue(pair.Key, out int originalBalance))
                    SetCacheNoLock(pair.Key, originalBalance);

                // Recreate prepared commands after a failed statement before
                // continuing with the rest of the roster.
                DisposeCommandsNoLock();
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to retry a viewer token balance", ex);
            }
        }

        return savedCount;
    }

    private void SaveOneNoLock(SqliteConnection connection, string normalized, int balance)
    {
        if (balance <= 0)
        {
            if (_deleteBalanceCommand == null)
            {
                _deleteBalanceCommand = CreateDeleteCommand(connection, null);
                _deleteBalanceUsername = _deleteBalanceCommand.Parameters[0];
                _deleteBalanceCommand.Prepare();
            }

            _deleteBalanceUsername!.Value = normalized;
            _deleteBalanceCommand.ExecuteNonQuery();
            return;
        }

        if (_upsertBalanceCommand == null)
        {
            _upsertBalanceCommand = CreateUpsertCommand(connection, null);
            _upsertBalanceUsername = _upsertBalanceCommand.Parameters[0];
            _upsertBalanceValue = _upsertBalanceCommand.Parameters[1];
            _upsertBalanceCommand.Prepare();
        }

        _upsertBalanceUsername!.Value = normalized;
        _upsertBalanceValue!.Value = balance;
        _upsertBalanceCommand.ExecuteNonQuery();
    }

    private static SqliteCommand CreateUpsertCommand(SqliteConnection connection, SqliteTransaction? transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpsertBalanceSql;
        command.Parameters.Add("$username", SqliteType.Text);
        command.Parameters.Add("$balance", SqliteType.Integer);
        return command;
    }

    private static SqliteCommand CreateDeleteCommand(SqliteConnection connection, SqliteTransaction? transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteBalanceSql;
        command.Parameters.Add("$username", SqliteType.Text);
        return command;
    }

    private static void SaveBalanceNoLock(SqliteCommand upsert, SqliteCommand delete, string normalized, int balance)
    {
        if (balance <= 0)
        {
            delete.Parameters[0].Value = normalized;
            delete.ExecuteNonQuery();
            return;
        }

        upsert.Parameters[0].Value = normalized;
        upsert.Parameters[1].Value = balance;
        upsert.ExecuteNonQuery();
    }

    private void ExportJsonNoLock()
    {
        string exportDirectory = JSONExportWriter.GetExportDirectory(_dbPath);
        Directory.CreateDirectory(exportDirectory);
        JSONExportWriter.WriteReadme(exportDirectory);

        JSONExportWriter.WriteJsonAtomic(
            Path.Combine(exportDirectory, "viewer_tokens.json"),
            writer =>
            {
                JSONExportWriter.WriteExportStart(writer);
                JSONExportWriter.WriteSectionBreak(writer);
                writer.WritePropertyName("ViewerTokens");
                writer.WriteStartObject();

                SqliteConnection connection = GetConnectionNoLock();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT Username, Balance FROM TokenBalances WHERE Balance > 0 ORDER BY Username COLLATE NOCASE ASC;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string username = Normalize(reader.GetString(0));
                    int balance = ClampBalance(reader.GetInt64(1));
                    if (username.Length == 0 || balance <= 0)
                    {
                        continue;
                    }

                    writer.WritePropertyName(username);
                    writer.WriteValue(balance);
                }

                writer.WriteEndObject();
                JSONExportWriter.WriteExportEnd(writer);
            });
    }

    private void DisposeCommandsNoLock()
    {
        DisposeCommand(ref _selectBalanceCommand);
        DisposeCommand(ref _upsertBalanceCommand);
        DisposeCommand(ref _deleteBalanceCommand);
        _selectBalanceUsername = null;
        _upsertBalanceUsername = null;
        _upsertBalanceValue = null;
        _deleteBalanceUsername = null;
    }

    private static void DisposeCommand(ref SqliteCommand? command)
    {
        SqliteCommand? commandToDispose = command;
        command = null;
        try
        {
            commandToDispose?.Dispose();
        }
        catch
        {
        }
    }

    private SqliteConnection GetConnectionNoLock()
    {
        if (_connection != null)
        {
            EnsureSchemaNoLock(_connection);
            return _connection;
        }

        FileSystemHelper.EnsureParentDir(_dbPath);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _dbPath,
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

    private void EnsureSchemaNoLock(SqliteConnection connection)
    {
        if (_schemaInitialized)
        {
            return;
        }

        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS TokenBalances (
                    Username TEXT PRIMARY KEY COLLATE NOCASE,
                    Balance INTEGER NOT NULL CHECK (Balance >= 0)
                );
                CREATE INDEX IF NOT EXISTS IX_TokenBalances_Balance ON TokenBalances (Balance DESC, Username COLLATE NOCASE ASC);
                CREATE TABLE IF NOT EXISTS RewardedFollows (
                    TwitchUserID TEXT PRIMARY KEY,
                    Username TEXT NOT NULL COLLATE NOCASE,
                    FollowedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_RewardedFollows_Username ON RewardedFollows (Username COLLATE NOCASE ASC);
                """;
            command.ExecuteNonQuery();
            _schemaInitialized = true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to initialize viewer token database", ex);
            throw;
        }
    }
}
