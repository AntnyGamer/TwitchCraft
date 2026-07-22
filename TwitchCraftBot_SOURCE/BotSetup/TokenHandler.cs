using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace TwitchCraftBot_V1.BotSetup;

internal sealed partial class TokenHandler(string path)
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
