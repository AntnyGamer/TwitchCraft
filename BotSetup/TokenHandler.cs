using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace TwitchCraftBot_V1.BotSetup;

internal sealed class TokenHandler(string path)
{
    private readonly Lock _gate = new();
    private readonly string _dbPath = path;
    private readonly Dictionary<string, int> _balances = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedUsers = new(StringComparer.OrdinalIgnoreCase);
    private const string DeleteBalanceSql = "DELETE FROM TokenBalances WHERE Username = $username;";
    private const string UpsertBalanceSql = """
        INSERT INTO TokenBalances (Username, Balance)
        VALUES ($username, $balance)
        ON CONFLICT(Username) DO UPDATE SET Balance = excluded.Balance
        WHERE TokenBalances.Balance <> excluded.Balance;
        """;
    private static readonly string[] BatchParameterNames = BuildBatchParameterNames(250);

    private SqliteConnection? _connection;
    private bool _schemaInitialized;

    public int GetBalance(string user)
    {
        string normalized = Normalize(user);
        if (normalized.Length == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            if (!EnsureViewerLoadedNoLock(normalized))
            {
                return 0;
            }

            return _balances.TryGetValue(normalized, out int balance) ? balance : 0;
        }
    }

    public bool TrySpendNow(string user, int amount)
    {
        string normalized = Normalize(user);
        if (normalized.Length == 0 || amount <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!EnsureViewerLoadedNoLock(normalized))
            {
                return false;
            }

            _balances.TryGetValue(normalized, out int current);
            if (current < amount)
            {
                return false;
            }

            int newBalance = current - amount;
            SetCachedBalanceNoLock(normalized, newBalance);
            if (SaveChangedBalanceNoLock(normalized, newBalance))
            {
                return true;
            }

            SetCachedBalanceNoLock(normalized, current);
            return false;
        }
    }

    public void AdjustBalance(string user, int delta)
    {
        string normalized = Normalize(user);
        if (normalized.Length != 0 && delta != 0)
            AdjustNormalizedBalance(normalized, delta);
    }

    private void AdjustNormalizedBalance(string normalized, int delta)
    {
        lock (_gate)
        {
            if (!EnsureViewerLoadedNoLock(normalized))
            {
                return;
            }

            _balances.TryGetValue(normalized, out int current);
            int newBalance = ClampTokenBalance((long)current + delta);
            if (newBalance == current)
            {
                return;
            }

            SetCachedBalanceNoLock(normalized, newBalance);
            if (!SaveChangedBalanceNoLock(normalized, newBalance))
            {
                SetCachedBalanceNoLock(normalized, current);
            }
        }
    }

    public void AdjustBalances(IEnumerable<string> users, int delta)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (delta == 0)
            return;

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(users, out int count) ? count : 0;

        Dictionary<string, int> normalizedDeltas = new(capacity, StringComparer.OrdinalIgnoreCase);
        foreach (string user in users)
        {
            string normalized = Normalize(user);
            if (normalized.Length > 0)
            {
                normalizedDeltas[normalized] = normalizedDeltas.TryGetValue(normalized, out int current)
                    ? ClampTokenDelta((long)current + delta)
                    : delta;
            }
        }

        AdjustNormalizedDeltas(normalizedDeltas);
    }

    public void AdjustBalances(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(changes, out int count) ? count : 0;

        Dictionary<string, int> normalizedDeltas = new(capacity, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> change in changes)
        {
            string normalized = Normalize(change.Key);
            if (normalized.Length == 0 || change.Value == 0)
            {
                continue;
            }

            normalizedDeltas[normalized] = normalizedDeltas.TryGetValue(normalized, out int current)
                ? ClampTokenDelta((long)current + change.Value)
                : change.Value;
        }

        AdjustNormalizedDeltas(normalizedDeltas);
    }

    private void AdjustNormalizedDeltas(Dictionary<string, int> normalizedDeltas)
    {
        if (normalizedDeltas.Count == 0)
            return;

        if (normalizedDeltas.Count == 1)
        {
            foreach (KeyValuePair<string, int> pair in normalizedDeltas)
                AdjustNormalizedBalance(pair.Key, pair.Value);

            return;
        }

        List<string> usersToLoad = [.. normalizedDeltas.Keys];
        lock (_gate)
        {
            if (!EnsureViewersLoadedNoLock(usersToLoad))
                return;

            Dictionary<string, int> originalBalances = new(normalizedDeltas.Count, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> changedUsers = new(normalizedDeltas.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> change in normalizedDeltas)
            {
                _balances.TryGetValue(change.Key, out int current);
                int newBalance = ClampTokenBalance((long)current + change.Value);
                if (newBalance == current)
                    continue;

                originalBalances[change.Key] = current;
                SetCachedBalanceNoLock(change.Key, newBalance);
                changedUsers[change.Key] = newBalance;
            }

            if (changedUsers.Count > 0 && !SaveChangedBalancesNoLock(changedUsers))
                RestoreCachedBalancesNoLock(originalBalances);
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            _ = GetConnectionNoLock();
        }
    }

    public void CloseConnection()
    {
        lock (_gate)
        {
            try
            {
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to close viewer token database", ex);
            }
            finally
            {
                _connection = null;
                _schemaInitialized = false;
                _balances.Clear();
                _loadedUsers.Clear();
            }
        }
    }

    public bool TryExportReadableJson()
    {
        try
        {
            lock (_gate)
            {
                _ = GetConnectionNoLock();
                ExportReadableJsonNoLock();
            }

            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to export readable viewer token JSON", ex);
            return false;
        }
    }

    private bool EnsureViewerLoadedNoLock(string normalized)
    {
        if (_loadedUsers.Contains(normalized))
        {
            return true;
        }

        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Balance FROM TokenBalances WHERE Username = $username;";
            command.Parameters.Add("$username", SqliteType.Text).Value = normalized;

            object? balance = command.ExecuteScalar();
            SetCachedBalanceNoLock(
                normalized,
                balance is null || balance == DBNull.Value
                    ? 0
                    : ClampTokenBalance(Convert.ToInt64(balance, CultureInfo.InvariantCulture)));

            _loadedUsers.Add(normalized);
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load viewer token balance", ex);
            return false;
        }
    }

    private bool EnsureViewersLoadedNoLock(List<string> users)
    {
        List<string> toLoad = new(users.Count);
        foreach (string normalized in users)
        {
            if (!string.IsNullOrEmpty(normalized) && !_loadedUsers.Contains(normalized))
                toLoad.Add(normalized);
        }

        if (toLoad.Count == 0)
        {
            return true;
        }

        if (toLoad.Count == 1)
        {
            return EnsureViewerLoadedNoLock(toLoad[0]);
        }

        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            const int batchSize = 250;
            for (int startIndex = 0; startIndex < toLoad.Count; startIndex += batchSize)
            {
                int count = Math.Min(batchSize, toLoad.Count - startIndex);
                LoadViewerBalanceBatchNoLock(connection, toLoad, startIndex, count);
            }

            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to load viewer token balances", ex);
            return false;
        }
    }

    private void LoadViewerBalanceBatchNoLock(SqliteConnection connection, List<string> users, int startIndex, int count)
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
        HashSet<string> foundUsers = new(count, StringComparer.OrdinalIgnoreCase);
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                string username = Normalize(reader.GetString(0));
                if (username.Length == 0)
                {
                    continue;
                }

                int balance = ClampTokenBalance(reader.GetInt64(1));
                SetCachedBalanceNoLock(username, balance);
                foundUsers.Add(username);
            }
        }

        for (int i = 0; i < count; i++)
        {
            string username = users[startIndex + i];
            if (!foundUsers.Contains(username))
            {
                SetCachedBalanceNoLock(username, 0);
            }

            _loadedUsers.Add(username);
        }
    }

    private bool SaveChangedBalanceNoLock(string normalized, int balance)
    {
        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            SaveSingleBalanceNoLock(connection, normalized, balance);
            return true;
        }
        catch (Exception ex)
        {
            TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to save viewer token balance", ex);
            return false;
        }
    }

    private bool SaveChangedBalancesNoLock(Dictionary<string, int> changedUsers)
    {
        try
        {
            SqliteConnection connection = GetConnectionNoLock();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand upsert = CreateUpsertBalanceCommand(connection, transaction);
            using SqliteCommand delete = CreateDeleteBalanceCommand(connection, transaction);
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

    private static void SaveSingleBalanceNoLock(SqliteConnection connection, string normalized, int balance)
    {
        if (balance <= 0)
        {
            using SqliteCommand delete = CreateDeleteBalanceCommand(connection, null);
            delete.Parameters["$username"].Value = normalized;
            delete.ExecuteNonQuery();
            return;
        }

        using SqliteCommand upsert = CreateUpsertBalanceCommand(connection, null);
        upsert.Parameters["$username"].Value = normalized;
        upsert.Parameters["$balance"].Value = balance;
        upsert.ExecuteNonQuery();
    }

    private static SqliteCommand CreateUpsertBalanceCommand(SqliteConnection connection, SqliteTransaction? transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpsertBalanceSql;
        command.Parameters.Add("$username", SqliteType.Text);
        command.Parameters.Add("$balance", SqliteType.Integer);
        return command;
    }

    private static SqliteCommand CreateDeleteBalanceCommand(SqliteConnection connection, SqliteTransaction? transaction)
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
            delete.Parameters["$username"].Value = normalized;
            delete.ExecuteNonQuery();
            return;
        }

        upsert.Parameters["$username"].Value = normalized;
        upsert.Parameters["$balance"].Value = balance;
        upsert.ExecuteNonQuery();
    }

    private void ExportReadableJsonNoLock()
    {
        string exportDirectory = JSONExportWriter.GetExportDirectory(_dbPath);
        Directory.CreateDirectory(exportDirectory);
        JSONExportWriter.WriteReadMe(exportDirectory);

        JSONExportWriter.WriteJsonExportAtomic(
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
                    int balance = ClampTokenBalance(reader.GetInt64(1));
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

    private SqliteConnection GetConnectionNoLock()
    {
        if (_connection != null)
        {
            EnsureSchemaNoLock(_connection);
            return _connection;
        }

        FileSystemHelper.EnsureDirectoryForFile(_dbPath);

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

    private void RestoreCachedBalancesNoLock(Dictionary<string, int> originalBalances)
    {
        foreach (KeyValuePair<string, int> pair in originalBalances)
        {
            SetCachedBalanceNoLock(pair.Key, pair.Value);
        }
    }

    private void SetCachedBalanceNoLock(string normalized, int balance)
    {
        int safeBalance = ClampTokenBalance(balance);
        if (normalized.Length == 0 || safeBalance <= 0)
        {
            _balances.Remove(normalized);
            return;
        }

        _balances[normalized] = safeBalance;
    }

    private static int ClampTokenBalance(long balance)
        => balance <= 0 ? 0 : balance > int.MaxValue ? int.MaxValue : (int)balance;

    private static int ClampTokenDelta(long delta)
        => delta > int.MaxValue ? int.MaxValue : delta < int.MinValue ? int.MinValue : (int)delta;

    private static string Normalize(string? user) => TwitchCraftBot_V1.CommandUserHelper.NormalizeUsername(user);

    private static string[] BuildBatchParameterNames(int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < names.Length; i++)
            names[i] = "$user" + i.ToString(CultureInfo.InvariantCulture);

        return names;
    }
}
