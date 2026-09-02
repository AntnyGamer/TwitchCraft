using Microsoft.Data.Sqlite;
using System.Text.Json;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Economy;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class EconomyPersistenceTests
{
    [Fact]
    public void BalancePersistsAcrossCloseAndReopen()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler writer = new(databasePath);

        try
        {
            writer.AdjustBalance(" @Alice ", 15);
            Assert.Equal(15, writer.GetBalance("alice"));
        }
        finally
        {
            writer.CloseConnection();
        }

        TokenHandler reader = new(databasePath);
        try
        {
            Assert.Equal(15, reader.GetBalance("ALICE"));
        }
        finally
        {
            reader.CloseConnection();
        }
    }

    [Fact]
    public void AdjustBalances_NormalizesAndCombinesDuplicateUsers()
    {
        using TemporaryDirectory directory = new();
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
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
    public void AdjustBalances_LargeLiveRosterUpdatesEveryViewerIncludingLongUsernames()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler store = new(databasePath);
        List<string> liveRoster = Enumerable.Range(0, 600)
            .Select(index => $"viewer_{index:D4}")
            .ToList();
        liveRoster.Insert(317, "randomdudereincarnatedx3");

        try
        {
            int adjustedCount = store.AdjustBalances(liveRoster, 25);

            Assert.Equal(liveRoster.Count, adjustedCount);
            Assert.All(liveRoster, viewer => Assert.Equal(25, store.GetBalance(viewer)));
        }
        finally
        {
            store.CloseConnection();
        }

        TokenHandler reopened = new(databasePath);
        try
        {
            Assert.Equal(25, reopened.GetBalance("randomdudereincarnatedx3"));
            Assert.Equal(25, reopened.GetBalance("viewer_0599"));
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
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

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
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            store.AdjustBalance("viewer", 90);
            FollowRewardResult result = store.TryRewardFollower(
                "123456",
                "viewer",
                DateTimeOffset.UtcNow,
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
    public void TrySpend_PersistsOnlySuccessfulSpends()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler store = new(databasePath);

        try
        {
            store.AdjustBalance("viewer", 10);

            Assert.True(store.TrySpend("viewer", 4));
            Assert.False(store.TrySpend("viewer", 7));
            Assert.Equal(6, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }

        TokenHandler reader = new(databasePath);
        try
        {
            Assert.Equal(6, reader.GetBalance("viewer"));
        }
        finally
        {
            reader.CloseConnection();
        }
    }

    [Fact]
    public void GetTopBalances_SortsByBalanceThenUsernameAndHonorsLimit()
    {
        using TemporaryDirectory directory = new();
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

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
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

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
        TokenHandler store = new(databasePath);

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

        TokenHandler reader = new(databasePath);
        try
        {
            Assert.Equal(0, reader.GetBalance("viewer"));
        }
        finally
        {
            reader.CloseConnection();
        }
    }

    [Fact]
    public void FollowReward_IsPaidOnlyOncePerTwitchAccount()
    {
        using TemporaryDirectory directory = new();
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(
                FollowRewardResult.Rewarded,
                store.TryRewardFollower("123456", "FirstName", DateTimeOffset.Parse("2026-08-27T01:02:03Z", System.Globalization.CultureInfo.InvariantCulture), 50));
            Assert.Equal(
                FollowRewardResult.AlreadyRewarded,
                store.TryRewardFollower("123456", "RenamedUser", DateTimeOffset.Parse("2026-08-27T02:03:04Z", System.Globalization.CultureInfo.InvariantCulture), 50));

            Assert.Equal(50, store.GetBalance("firstname"));
            Assert.Equal(0, store.GetBalance("renameduser"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void FollowReward_DeduplicationPersistsAcrossReopen()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler writer = new(databasePath);

        try
        {
            Assert.Equal(
                FollowRewardResult.Rewarded,
                writer.TryRewardFollower("987654", "viewer", DateTimeOffset.UtcNow, 50));
        }
        finally
        {
            writer.CloseConnection();
        }

        TokenHandler reader = new(databasePath);
        try
        {
            Assert.Equal(
                FollowRewardResult.AlreadyRewarded,
                reader.TryRewardFollower("987654", "viewer", DateTimeOffset.UtcNow, 50));
            Assert.Equal(50, reader.GetBalance("viewer"));
        }
        finally
        {
            reader.CloseConnection();
        }
    }

    [Fact]
    public void FollowReward_RejectsInvalidIdentityWithoutChargingDatabase()
    {
        using TemporaryDirectory directory = new();
        TokenHandler store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("not-a-user-id", "viewer", DateTimeOffset.UtcNow, 50));
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("123", "", DateTimeOffset.UtcNow, 50));
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("123", "viewer", DateTimeOffset.UtcNow, 0));
            Assert.Equal(0, store.GetBalance("viewer"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void BackupDatabase_CreatesAReadablePointInTimeCopy()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        string backupPath = Path.Combine(directory.Path, "backup", "viewer_tokens.db");
        TokenHandler store = new(databasePath);

        try
        {
            store.AdjustBalance("alice", 42);
            Assert.True(store.TryBackup(backupPath));
            store.AdjustBalance("alice", 8);
        }
        finally
        {
            store.CloseConnection();
        }

        TokenHandler backup = new(backupPath);
        try
        {
            Assert.Equal(42, backup.GetBalance("alice"));
            Assert.True(backup.TryOptimize());
        }
        finally
        {
            backup.CloseConnection();
        }
    }

    [Fact]
    public void ExportReadableJson_ContainsSortedPositiveBalances()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler store = new(databasePath);

        try
        {
            store.AdjustBalance("Bob", 7);
            store.AdjustBalance("alice", 3);
            Assert.True(store.TryExportJson());
        }
        finally
        {
            store.CloseConnection();
        }

        string exportPath = Path.Combine(directory.Path, "exports", "viewer_tokens.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(exportPath));
        JsonElement balances = document.RootElement.GetProperty("ViewerTokens");
        JsonProperty[] properties = [.. balances.EnumerateObject()];

        Assert.Equal(["alice", "bob"], properties.Select(property => property.Name));
        Assert.Equal(3, balances.GetProperty("alice").GetInt32());
        Assert.Equal(7, balances.GetProperty("bob").GetInt32());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TwitchCraftTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EconomyDatabaseCollection
{
    public const string Name = "SQLite database tests";
}
