using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Economy;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class EconomyBalanceTests
{
    [Fact]
    public void AdjustBalances_HandlesSingleAndDuplicateNormalizedUsers()
    {
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.True(store.AdjustBalances([new KeyValuePair<string, int>(" Solo ", 4)]));
            Assert.Equal(4, store.GetBalance("solo"));

            int adjustedCount = store.AdjustBalances(["Alice", " alice ", "@BOB", "bob"], 3);
            Assert.Equal(2, adjustedCount);
            Assert.Equal(6, store.GetBalance("alice"));
            Assert.Equal(6, store.GetBalance("BOB"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void BalanceWrites_PersistWholeLiveRosterAndNormalizedSingleUserEdits()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenStore store = new(databasePath);
        List<string> liveRoster = Enumerable.Range(0, 600)
            .Select(index => $"viewer_{index:D4}")
            .ToList();
        liveRoster.Insert(317, "randomdudereincarnatedx3");

        try
        {
            int adjustedCount = store.AdjustBalances(liveRoster, 25);

            Assert.Equal(liveRoster.Count, adjustedCount);
            Assert.All(liveRoster, viewer => Assert.Equal(25, store.GetBalance(viewer)));
            Assert.Equal(5, store.AdjustBalance(" @RandomDudeReincarnatedX3 ", 5));
            Assert.Equal(30, store.GetBalance("randomdudereincarnatedx3"));
        }
        finally
        {
            store.CloseConnection();
        }

        TokenStore reopened = new(databasePath);
        try
        {
            Assert.All(liveRoster, viewer => Assert.Equal(
                viewer == "randomdudereincarnatedx3" ? 30 : 25, reopened.GetBalance(viewer.ToUpperInvariant())));
        }
        finally
        {
            reopened.CloseConnection();
        }
    }

    [Fact]
    public void PositiveAwards_RespectMaximumBalanceWithoutBreakingSpending()
    {
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("viewer", 90);
            store.AdjustBalance("viewer", 25, maximumBalance: 100);
            Assert.Equal(100, store.GetBalance("viewer"));

            Assert.True(store.TrySpend("viewer", 30));
            store.AdjustBalance("viewer", 50, maximumBalance: 100);
            Assert.Equal(100, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void FollowReward_ReportsActualAwardWhenMaximumBalanceIsReached()
    {
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("viewer", 90);
            FollowRewardResult result = store.TryRewardFollower(
                "123456",
                "viewer",
                new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero),
                100,
                out int awarded,
                maximumBalance: 100);

            Assert.Equal(FollowRewardResult.Rewarded, result);
            Assert.Equal(10, awarded);
            Assert.Equal(100, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void GetTopBalances_SortsByBalanceThenUsernameAndHonorsLimit()
    {
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("charlie", 25);
            store.AdjustBalance("Bob", 50);
            store.AdjustBalance("alice", 50);
            store.AdjustBalance("delta", 10);

            IReadOnlyList<KeyValuePair<string, int>> leaders = store.GetTopBalances(3);

            Assert.Equal(["alice", "bob", "charlie"], leaders.Select(entry => entry.Key));
            Assert.Equal([50, 50, 25], leaders.Select(entry => entry.Value));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void GetRank_ReturnsExactLeaderboardPositionAndBalance()
    {
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("charlie", 25);
            store.AdjustBalance("Bob", 50);
            store.AdjustBalance("alice", 50);
            store.AdjustBalance("delta", 10);

            Assert.Equal(new TokenRankResult("alice", 50, 1), store.GetRank("@ALICE"));
            Assert.Equal(new TokenRankResult("bob", 50, 2), store.GetRank("bob"));
            Assert.Equal(new TokenRankResult("charlie", 25, 3), store.GetRank("Charlie"));
            Assert.Null(store.GetRank("not_ranked"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void BalanceClampsAtValidLimitsAndDeletesZeroRows()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenStore store = new(databasePath);

        try
        {
            store.AdjustBalance("viewer", int.MaxValue);
            store.AdjustBalance("viewer", 1);
            Assert.Equal(int.MaxValue, store.GetBalance("viewer"));

            store.AdjustBalance("viewer", -int.MaxValue);
            Assert.Equal(0, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }

        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TokenBalances WHERE Username = 'viewer';";
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }

        TokenStore reader = new(databasePath);
        try
        {
            Assert.Equal(0, reader.GetBalance("viewer"));
        }
        finally
        {
            reader.CloseConnection();
        }
    }
}
