using System;
using System.Collections.Generic;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    public int GetTokens(string user) => _tokenStore.GetBalance(user);

    internal IReadOnlyList<KeyValuePair<string, int>> GetTopTokenBalances(int limit)
        => _tokenStore.GetTopBalances(limit);

    internal TokenRankResult? GetTokenRank(string user)
        => _tokenStore.GetRank(user);

    internal void CloseTokenStore() => _tokenStore.CloseConnection();

    public bool TrySpendTokens(string user, int amount)
        => amount > 0 && _tokenStore.TrySpend(user, amount);

    public int AdjustTokens(string user, int delta)
        => delta == 0 ? 0 : _tokenStore.AdjustBalance(user, delta);

    public int AdjustTokens(IEnumerable<string> users, int delta)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (delta == 0 || IsEmptyCollection(users))
        {
            return 0;
        }

        return _tokenStore.AdjustBalances(users, delta);
    }

    public void AdjustTokens(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (IsEmptyCollection(changes))
        {
            return;
        }

        _tokenStore.AdjustBalances(changes);
    }

    public int AwardTokens(string user, int amount)
        => amount <= 0 ? 0 : _tokenStore.AdjustBalance(user, amount, MaximumTokenBalance);

    public int AwardTokens(IEnumerable<string> users, int amount)
    {
        ArgumentNullException.ThrowIfNull(users);
        return amount <= 0 || IsEmptyCollection(users)
            ? 0
            : _tokenStore.AdjustBalances(users, amount, MaximumTokenBalance);
    }

    public void AwardTokens(IEnumerable<KeyValuePair<string, int>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!IsEmptyCollection(changes))
            _tokenStore.AdjustBalances(changes, MaximumTokenBalance);
    }

    private static bool IsEmptyCollection<T>(IEnumerable<T> values)
        => values is ICollection<T> { Count: 0 } || values is IReadOnlyCollection<T> { Count: 0 };
}
