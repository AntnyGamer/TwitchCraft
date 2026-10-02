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

    [Theory]
    [InlineData(90, 10)]
    [InlineData(100, 0)]
    public void FollowReward_ReportsActualAwardAndConsumesRewardAtMaximumBalance(int initialBalance, int expectedAward)
    {
        DateTimeOffset followedAt = new(2026, 8, 27, 1, 2, 3, TimeSpan.Zero);
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("viewer", initialBalance);
            FollowRewardResult result = store.TryRewardFollower(
                "123456",
                "viewer",
                followedAt,
                100,
                out int awarded,
                maximumBalance: 100);

            Assert.Equal(FollowRewardResult.Rewarded, result);
            Assert.Equal(expectedAward, awarded);
            Assert.Equal(100, store.GetBalance("viewer"));

            Assert.True(store.TrySpend("viewer", 20));
            Assert.Equal(FollowRewardResult.AlreadyRewarded, store.TryRewardFollower(
                "123456", "viewer", followedAt.AddHours(1), 100, out int repeatedAward, maximumBalance: 100));
            Assert.Equal(0, repeatedAward);
            Assert.Equal(80, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Theory]
    [InlineData(90, 50, 25, 0, nameof(TokenAdjustmentStatus.Adjusted), 115, 25)]
    [InlineData(90, 50, -50, 0, nameof(TokenAdjustmentStatus.Adjusted), 40, -50)]
    [InlineData(90, 100, 25, 0, nameof(TokenAdjustmentStatus.Insufficient), 90, 0)]
    [InlineData(90, 50, 25, 100, nameof(TokenAdjustmentStatus.Adjusted), 100, 10)]
    public void Gamble_ChecksStakeAndPersistsActualBalanceChange(
        int initialBalance, int stake, int delta, int maximumBalance,
        string expectedStatus, int expectedBalance, int expectedDelta)
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenService tokens = new(databasePath, () => maximumBalance);
        try
        {
            Assert.Equal(initialBalance, tokens.Award("viewer", initialBalance));

            TokenAdjustmentStatus result = tokens.TryGamble(
                " @ViEwEr ", stake, delta, out int balance, out int appliedDelta);
            Assert.Equal(Enum.Parse<TokenAdjustmentStatus>(expectedStatus), result);
            Assert.Equal(expectedBalance, balance);
            Assert.Equal(expectedDelta, appliedDelta);
            Assert.Equal(expectedBalance, tokens.GetBalance("viewer"));
        }
        finally
        {
            tokens.Close();
        }

        TokenStore reopened = new(databasePath);
        try
        {
            Assert.Equal(expectedBalance, reopened.GetBalance("viewer"));
        }
        finally
        {
            reopened.CloseConnection();
        }
    }

    [Fact]
    public void LowerMaximumBalance_ClampsCachedAndUncachedBalancesDurably()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenStore store = new(databasePath);
        try
        {
            store.AdjustBalance("cached", 250);
            store.AdjustBalance("uncached", 300);
            store.AdjustBalance("below_limit", 20);
            store.CloseConnection();
            Assert.Equal(250, store.GetBalance("cached"));
            Assert.Equal(20, store.GetBalance("below_limit"));

            store.ApplyMaximumBalance(100);

            Assert.Equal(100, store.GetBalance("cached"));
            Assert.Equal(100, store.GetBalance("uncached"));
            Assert.Equal(20, store.GetBalance("below_limit"));
            Assert.False(store.TrySpend("cached", 101));
        }
        finally
        {
            store.CloseConnection();
        }

        TokenStore reopened = new(databasePath);
        try
        {
            Assert.Equal(100, reopened.GetBalance("cached"));
            Assert.Equal(100, reopened.GetBalance("uncached"));
            Assert.Equal(20, reopened.GetBalance("below_limit"));
        }
        finally
        {
            reopened.CloseConnection();
        }
    }

    [Fact]
    public void TokenLeaderboard_SortsByBalanceThenUsernameAndHonorsLimit()
    {
        using TemporaryDirectory directory = new();
        TokenService tokens = new(Path.Combine(directory.Path, "viewer_tokens.db"), static () => 0);

        try
        {
            tokens.Award("charlie", 25);
            tokens.Award("Bob", 50);
            tokens.Award("alice", 50);
            tokens.Award("delta", 10);

            Assert.True(tokens.TryGetTopBalances(3, out IReadOnlyList<KeyValuePair<string, int>> leaders));
            Assert.Equal(["alice", "bob", "charlie"], leaders.Select(entry => entry.Key));
            Assert.Equal([50, 50, 25], leaders.Select(entry => entry.Value));
        }
        finally
        {
            tokens.Close();
        }
    }

    [Fact]
    public void TokenRank_ReturnsExactLeaderboardPositionAndBalance()
    {
        using TemporaryDirectory directory = new();
        TokenService tokens = new(Path.Combine(directory.Path, "viewer_tokens.db"), static () => 0);

        try
        {
            tokens.Award("charlie", 25);
            tokens.Award("Bob", 50);
            tokens.Award("alice", 50);
            tokens.Award("delta", 10);

            Assert.True(tokens.TryGetRank("@ALICE", out TokenRankResult? alice));
            Assert.True(tokens.TryGetRank("bob", out TokenRankResult? bob));
            Assert.True(tokens.TryGetRank("Charlie", out TokenRankResult? charlie));
            Assert.True(tokens.TryGetRank("not_ranked", out TokenRankResult? missing));
            Assert.Equal(new TokenRankResult("alice", 50, 1), alice);
            Assert.Equal(new TokenRankResult("bob", 50, 2), bob);
            Assert.Equal(new TokenRankResult("charlie", 25, 3), charlie);
            Assert.Null(missing);
        }
        finally
        {
            tokens.Close();
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
