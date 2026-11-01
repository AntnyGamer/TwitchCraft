using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TwitchCraft_V1;

internal sealed partial class TokenStore
{
    private FileStream? _databaseSessionLock;
    private const string AddStakeSql = """
        INSERT INTO PendingMinigameBets (Username, Amount) VALUES ($username, $amount)
        ON CONFLICT(Username) DO UPDATE SET Amount = Amount + excluded.Amount;
        """;

    internal void AcquireDatabaseSessionLock()
    {
        lock (_gate)
            _databaseSessionLock ??= new FileStream(_dbPath + ".minigame.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal bool TrySpendForMinigameBet(string user, int amount)
    {
        string name = Normalize(user);
        if (name.Length == 0 || amount <= 0)
            return false;

        lock (_gate)
        {
            if (!EnsureLoadedNoLock(name))
                return false;
            _balances.TryGetValue(name, out int balance);
            if (balance < amount)
                return false;

            try
            {
                SqliteConnection connection = GetConnectionNoLock();
                using SqliteTransaction transaction = connection.BeginTransaction();
                int remaining = balance - amount;
                using SqliteCommand stake = connection.CreateCommand();
                stake.Transaction = transaction;
                stake.CommandText = remaining == 0 ? DeleteBalanceSql + AddStakeSql : UpsertBalanceSql + AddStakeSql;
                stake.Parameters.AddWithValue("$username", name);
                if (remaining > 0)
                    stake.Parameters.AddWithValue("$balance", remaining);
                stake.Parameters.AddWithValue("$amount", amount);
                stake.ExecuteNonQuery();
                transaction.Commit();

                SetCacheNoLock(name, remaining);
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to record minigame bet", ex);
                return false;
            }
        }
    }

    internal bool TryCompleteMinigameBets(
        IReadOnlyList<KeyValuePair<string, int>> payouts,
        int maximumBalance,
        bool refund = false)
    {
        ArgumentNullException.ThrowIfNull(payouts);
        lock (_gate)
        {
            try
            {
                SqliteConnection connection = GetConnectionNoLock();
                Dictionary<string, int> pending = new(StringComparer.Ordinal);
                using (SqliteCommand read = connection.CreateCommand())
                {
                    read.CommandText = "SELECT Username, Amount FROM PendingMinigameBets;";
                    using SqliteDataReader reader = read.ExecuteReader();
                    while (reader.Read())
                        pending.Add(Normalize(reader.GetString(0)), reader.GetInt32(1));
                }

                if (pending.Count == 0)
                    return refund;

                Dictionary<string, int> adjustments = refund ? pending : new(StringComparer.Ordinal);
                if (!refund)
                {
                    foreach (KeyValuePair<string, int> payout in payouts)
                    {
                        string name = Normalize(payout.Key);
                        if (name.Length == 0 || payout.Value <= 0 || !pending.ContainsKey(name))
                            return false;
                        adjustments[name] = adjustments.TryGetValue(name, out int current)
                            ? checked(current + payout.Value)
                            : payout.Value;
                    }
                }

                if (!EnsureManyLoadedNoLock(adjustments.Keys))
                    return false;

                Dictionary<string, int> balances = new(adjustments.Count, StringComparer.Ordinal);
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand upsert = CreateUpsertCommand(connection, transaction);
                using SqliteCommand delete = CreateDeleteCommand(connection, transaction);
                foreach (KeyValuePair<string, int> adjustment in adjustments)
                {
                    _balances.TryGetValue(adjustment.Key, out int current);
                    int updated = ClampAdjusted(current, adjustment.Value, maximumBalance);
                    if (updated == current)
                        continue;
                    SaveBalanceNoLock(upsert, delete, adjustment.Key, updated);
                    balances.Add(adjustment.Key, updated);
                }

                using SqliteCommand clear = connection.CreateCommand();
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM PendingMinigameBets;";
                clear.ExecuteNonQuery();
                transaction.Commit();

                foreach (KeyValuePair<string, int> balance in balances)
                    SetCacheNoLock(balance.Key, balance.Value);
                return true;
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to settle or recover minigame bets", ex);
                return false;
            }
        }
    }
}
