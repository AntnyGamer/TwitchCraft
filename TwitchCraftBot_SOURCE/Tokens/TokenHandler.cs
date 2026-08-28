using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace TwitchCraftBot_V1.BotSetup;

internal enum FollowRewardResult
{
    Failed,
    AlreadyRewarded,
    Rewarded
}

internal readonly record struct TokenRankResult(string Username, int Balance, int Rank);

internal sealed partial class TokenHandler(string path)
{
    private readonly Lock _gate = new();
    private readonly string _dbPath = path;
    private readonly Dictionary<string, int> _balances = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedUsers = new(StringComparer.Ordinal);
    private const string DeleteBalanceSql = "DELETE FROM TokenBalances WHERE Username = $username;";
    private const string UpsertBalanceSql = """
        INSERT INTO TokenBalances (Username, Balance)
        VALUES ($username, $balance)
        ON CONFLICT(Username) DO UPDATE SET Balance = excluded.Balance
        WHERE TokenBalances.Balance <> excluded.Balance;
        """;
    private static readonly string[] BatchParameterNames = BuildBatchParameterNames(250);

    private SqliteConnection? _connection;
    private SqliteCommand? _selectBalanceCommand;
    private SqliteParameter? _selectBalanceUsername;
    private SqliteCommand? _upsertBalanceCommand;
    private SqliteParameter? _upsertBalanceUsername;
    private SqliteParameter? _upsertBalanceValue;
    private SqliteCommand? _deleteBalanceCommand;
    private SqliteParameter? _deleteBalanceUsername;
    private bool _schemaInitialized;

    internal bool TryBackupDatabase(string destinationPath)
    {
        try
        {
            lock (_gate)
            {
                FileSystemHelper.EnsureDirectoryForFile(destinationPath);
                using SqliteConnection destination = new(new SqliteConnectionStringBuilder
                {
                    DataSource = destinationPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString());
                destination.Open();
                GetConnectionNoLock().BackupDatabase(destination);
                return true;
            }
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to back up viewer token database", ex);
            return false;
        }
    }

    internal bool TryOptimizeDatabase()
    {
        try
        {
            lock (_gate)
            {
                using SqliteCommand command = GetConnectionNoLock().CreateCommand();
                command.CommandText = "PRAGMA optimize;";
                command.ExecuteNonQuery();
                return true;
            }
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to optimize viewer token database", ex);
            return false;
        }
    }

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

    public int AdjustBalance(string user, int delta, int maximumBalance = 0)
    {
        string normalized = Normalize(user);
        if (normalized.Length != 0 && delta != 0)
            return AdjustNormalizedBalance(normalized, delta, maximumBalance);

        return 0;
    }

    public FollowRewardResult TryRewardFollowerOnce(
        string twitchUserId,
        string username,
        DateTimeOffset followedAt,
        int amount,
        int maximumBalance = 0)
        => TryRewardFollowerOnce(twitchUserId, username, followedAt, amount, out _, maximumBalance);

    public FollowRewardResult TryRewardFollowerOnce(
        string twitchUserId,
        string username,
        DateTimeOffset followedAt,
        int amount,
        out int awardedAmount,
        int maximumBalance = 0)
    {
        awardedAmount = 0;
        string normalizedUserId = (twitchUserId ?? string.Empty).Trim();
        string normalizedUsername = Normalize(username);
        if (normalizedUserId.Length == 0 || normalizedUsername.Length == 0 || amount <= 0)
            return FollowRewardResult.Failed;

        for (int i = 0; i < normalizedUserId.Length; i++)
            if (!char.IsAsciiDigit(normalizedUserId[i]))
                return FollowRewardResult.Failed;

        lock (_gate)
        {
            if (!EnsureViewerLoadedNoLock(normalizedUsername))
                return FollowRewardResult.Failed;

            try
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand reward = connection.CreateCommand();
                reward.Transaction = transaction;
                reward.CommandText = """
                    INSERT INTO RewardedFollows (TwitchUserID, Username, FollowedAtUtc)
                    VALUES ($userId, $username, $followedAt)
                    ON CONFLICT(TwitchUserID) DO NOTHING;
                    """;
                reward.Parameters.AddWithValue("$userId", normalizedUserId);
                reward.Parameters.AddWithValue("$username", normalizedUsername);
                reward.Parameters.AddWithValue("$followedAt", followedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

                if (reward.ExecuteNonQuery() == 0)
                {
                    transaction.Rollback();
                    return FollowRewardResult.AlreadyRewarded;
                }

                _balances.TryGetValue(normalizedUsername, out int current);
                int newBalance = ClampAdjustedBalance(current, amount, maximumBalance);
                if (newBalance != current)
                {
                    using SqliteCommand upsert = CreateUpsertBalanceCommand(connection, transaction);
                    using SqliteCommand delete = CreateDeleteBalanceCommand(connection, transaction);
                    SaveBalanceNoLock(upsert, delete, normalizedUsername, newBalance);
                }

                transaction.Commit();
                SetCachedBalanceNoLock(normalizedUsername, newBalance);
                awardedAmount = newBalance - current;
                return FollowRewardResult.Rewarded;
            }
            catch (Exception ex)
            {
                TwitchCraftBot_V1.ErrorHandling.LogNonFatal("Failed to save Twitch follow reward", ex);
                return FollowRewardResult.Failed;
            }
        }
    }

    private int AdjustNormalizedBalance(string normalized, int delta, int maximumBalance = 0)
    {
        lock (_gate)
        {
            if (!EnsureViewerLoadedNoLock(normalized))
            {
                return 0;
            }

            _balances.TryGetValue(normalized, out int current);
            int newBalance = ClampAdjustedBalance(current, delta, maximumBalance);
            if (newBalance == current)
            {
                return 0;
            }

            SetCachedBalanceNoLock(normalized, newBalance);
            if (!SaveChangedBalanceNoLock(normalized, newBalance))
            {
                SetCachedBalanceNoLock(normalized, current);
                return 0;
            }

            return newBalance - current;
        }
    }

    public int AdjustBalances(IEnumerable<string> users, int delta, int maximumBalance = 0)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (delta == 0)
            return 0;

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(users, out int count) ? count : 0;

        Dictionary<string, int> normalizedDeltas = new(capacity, StringComparer.Ordinal);
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

        return AdjustNormalizedDeltas(normalizedDeltas, maximumBalance);
    }

    public void AdjustBalances(IEnumerable<KeyValuePair<string, int>> changes, int maximumBalance = 0)
    {
        ArgumentNullException.ThrowIfNull(changes);

        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(changes, out int count) ? count : 0;

        Dictionary<string, int> normalizedDeltas = new(capacity, StringComparer.Ordinal);
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

        _ = AdjustNormalizedDeltas(normalizedDeltas, maximumBalance);
    }

    private int AdjustNormalizedDeltas(Dictionary<string, int> normalizedDeltas, int maximumBalance)
    {
        if (normalizedDeltas.Count == 0)
            return 0;

        if (normalizedDeltas.Count == 1)
        {
            foreach (KeyValuePair<string, int> pair in normalizedDeltas)
                return AdjustNormalizedBalance(pair.Key, pair.Value, maximumBalance) != 0 ? 1 : 0;

            return 0;
        }

        List<string> usersToLoad = [.. normalizedDeltas.Keys];
        lock (_gate)
        {
            if (!EnsureViewersLoadedNoLock(usersToLoad))
                return 0;

            Dictionary<string, int> originalBalances = new(normalizedDeltas.Count, StringComparer.Ordinal);
            Dictionary<string, int> changedUsers = new(normalizedDeltas.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> change in normalizedDeltas)
            {
                _balances.TryGetValue(change.Key, out int current);
                int newBalance = ClampAdjustedBalance(current, change.Value, maximumBalance);
                if (newBalance == current)
                    continue;

                originalBalances[change.Key] = current;
                SetCachedBalanceNoLock(change.Key, newBalance);
                changedUsers[change.Key] = newBalance;
            }

            if (changedUsers.Count == 0)
                return 0;

            if (SaveChangedBalancesNoLock(changedUsers))
                return changedUsers.Count;

            // A bulk transaction is all-or-nothing. Restore the cache, then retry
            // viewers individually so one database error cannot silently skip the
            // entire live roster.
            if (changedUsers.Count > 0)
            {
                RestoreCachedBalancesNoLock(originalBalances);
                return SaveChangedBalancesIndividuallyNoLock(changedUsers, originalBalances);
            }

            return 0;
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
                DisposePreparedCommandsNoLock();
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

    private static int ClampAdjustedBalance(int current, int delta, int maximumBalance)
    {
        int adjusted = ClampTokenBalance((long)current + delta);
        return delta > 0 && maximumBalance > 0 ? Math.Min(adjusted, maximumBalance) : adjusted;
    }

    private static int ClampTokenDelta(long delta)
        => delta > int.MaxValue ? int.MaxValue : delta < int.MinValue ? int.MinValue : (int)delta;

    private static string Normalize(string? user) => TwitchCraftBot_V1.CommandUserHelper.NormalizeUsername(user);

    private static string[] BuildBatchParameterNames(int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < names.Length; i++)
            names[i] = string.Create(CultureInfo.InvariantCulture, $"$user{i}");

        return names;
    }
}
