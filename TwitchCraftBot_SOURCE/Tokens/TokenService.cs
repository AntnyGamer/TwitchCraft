using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1;

/// <summary>
/// Owns viewer balances, follower rewards, and token-database maintenance.
/// </summary>
public sealed class TokenService
{
    private readonly TokenHandler _store;
    private readonly Func<int> _maximumBalance;

    internal TokenService(string storePath, Func<int> maximumBalance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(maximumBalance);

        _store = new TokenHandler(storePath);
        _maximumBalance = maximumBalance;
    }

    public int GetBalance(string user) => _store.GetBalance(user);

    internal IReadOnlyList<KeyValuePair<string, int>> GetTopBalances(int limit)
        => _store.GetTopBalances(limit);

    internal TokenRankResult? GetRank(string user) => _store.GetRank(user);

    public bool TrySpend(string user, int amount)
        => amount > 0 && _store.TrySpend(user, amount);

    public int Adjust(string user, int delta)
        => delta == 0 ? 0 : _store.AdjustBalance(user, delta, MaximumBalance);

    public int Adjust(IEnumerable<string> users, int delta)
    {
        ArgumentNullException.ThrowIfNull(users);
        return delta == 0 || IsEmptyCollection(users)
            ? 0
            : _store.AdjustBalances(users, delta, MaximumBalance);
    }

    public void Adjust(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!IsEmptyCollection(changes))
            _store.AdjustBalances(changes, MaximumBalance);
    }

    public int Award(string user, int amount)
        => amount <= 0 ? 0 : _store.AdjustBalance(user, amount, MaximumBalance);

    public int Award(IEnumerable<string> users, int amount)
    {
        ArgumentNullException.ThrowIfNull(users);
        return amount <= 0 || IsEmptyCollection(users)
            ? 0
            : _store.AdjustBalances(users, amount, MaximumBalance);
    }

    internal FollowRewardResult TryRewardFollower(
        string userID,
        string userLogin,
        DateTimeOffset followedAt,
        int rewardAmount,
        out int awardedAmount)
        => _store.TryRewardFollower(
            userID,
            userLogin,
            followedAt,
            rewardAmount,
            out awardedAmount,
            MaximumBalance);

    internal void Load(int maximumBalance) { _store.Load(); _store.ApplyMaximumBalance(maximumBalance); }

    internal bool TryBackup(string destinationPath) => _store.TryBackup(destinationPath);

    internal bool TryOptimize() => _store.TryOptimize();
    internal void ApplyMaximumBalance(int maximumBalance) => _store.ApplyMaximumBalance(maximumBalance);

    internal bool TryExportJson() => _store.TryExportJson();

    internal void Close() => _store.CloseConnection();

    private int MaximumBalance => Math.Max(0, _maximumBalance());

    private static bool IsEmptyCollection<T>(IEnumerable<T> values)
        => values is ICollection<T> { Count: 0 } || values is IReadOnlyCollection<T> { Count: 0 };
}
