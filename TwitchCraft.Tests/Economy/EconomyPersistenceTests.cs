using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Economy;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class EconomyPersistenceTests
{
    [Fact]
    public void TokenChanges_PersistOnlySuccessfulWrites()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenStore store = new(databasePath);

        try
        {
            store.AdjustBalance("viewer", 10);

            Assert.True(store.TrySpend("viewer", 4));
            Assert.False(store.TrySpend("viewer", 7));
            Assert.Equal(6, store.GetBalance("viewer"));
            Assert.Equal(0, store.GetBalance("bob"));

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_writes BEFORE INSERT ON TokenBalances WHEN NEW.Username IN ('bob', 'solo') BEGIN SELECT RAISE(ABORT, 'test'); END;";
            command.ExecuteNonQuery();
            Assert.False(store.AdjustBalances([new KeyValuePair<string, int>("solo", 4)]));
            Assert.Equal(0, store.GetBalance("solo"));
            // Fail after an earlier balance was deleted inside the batch transaction.
            Assert.False(store.AdjustBalances([
                new("viewer", -6), new("bob", 4), new("after", 5)]));
            Assert.Equal(6, store.GetBalance("viewer"));
            Assert.Equal(0, store.GetBalance("bob"));
            Assert.Equal(0, store.GetBalance("after"));
            command.CommandText = "SELECT Balance FROM TokenBalances WHERE Username = 'viewer';";
            Assert.Equal(6L, command.ExecuteScalar());
            command.CommandText = "SELECT COUNT(*) FROM TokenBalances WHERE Username IN ('bob', 'solo', 'after');";
            Assert.Equal(0L, command.ExecuteScalar());
            Assert.Null(store.TryTransfer("viewer", "bob", 4, 0));
            Assert.Equal(6, store.GetBalance("viewer"));
            Assert.Equal(0, store.GetBalance("bob"));

            command.CommandText = "DROP TRIGGER fail_writes;";
            command.ExecuteNonQuery();
            Assert.Equal(2, store.TryTransfer("viewer", "bob", 4, 0));
            Assert.Equal(2, store.GetBalance("viewer"));
            Assert.Equal(2, store.GetBalance("bob"));
        }
        finally
        {
            store.CloseConnection();
        }

        TokenStore reader = new(databasePath);
        try
        {
            Assert.Equal(2, reader.GetBalance("viewer"));
            Assert.Equal(2, reader.GetBalance("bob"));
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
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(
                FollowRewardResult.Rewarded,
                store.TryRewardFollower("123456", "FirstName", DateTimeOffset.Parse("2026-08-27T01:02:03Z", System.Globalization.CultureInfo.InvariantCulture), 50, out _));
            Assert.Equal(
                FollowRewardResult.AlreadyRewarded,
                store.TryRewardFollower("123456", "RenamedUser", DateTimeOffset.Parse("2026-08-27T02:03:04Z", System.Globalization.CultureInfo.InvariantCulture), 50, out _));

            Assert.Equal(50, store.GetBalance("firstname"));
            Assert.Equal(0, store.GetBalance("renameduser"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowReward_PersistsExactlyOnceAndCanRetryFailedWrites(bool failFirstWrite)
    {
        DateTimeOffset followedAt = new(2026, 8, 27, 1, 2, 3, TimeSpan.Zero);
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenStore writer = new(databasePath);

        try
        {
            if (failFirstWrite)
            {
                Assert.Equal(0, writer.GetBalance("viewer"));
                using SqliteConnection connection = new($"Data Source={databasePath}");
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER fail_reward BEFORE INSERT ON TokenBalances BEGIN SELECT RAISE(ABORT, 'test'); END;";
                command.ExecuteNonQuery();
                Assert.Equal(FollowRewardResult.Failed,
                    writer.TryRewardFollower("987654", "viewer", followedAt, 50, out int awarded));
                Assert.Equal(0, awarded);
                Assert.Equal(0, writer.GetBalance("viewer"));
                command.CommandText = "SELECT COUNT(*) FROM RewardedFollows;";
                Assert.Equal(0L, command.ExecuteScalar());
                command.CommandText = "DROP TRIGGER fail_reward;";
                command.ExecuteNonQuery();
            }
            Assert.Equal(
                FollowRewardResult.Rewarded,
                writer.TryRewardFollower("987654", "viewer", followedAt, 50, out _));
        }
        finally
        {
            writer.CloseConnection();
        }

        TokenStore reader = new(databasePath);
        try
        {
            Assert.Equal(
                FollowRewardResult.AlreadyRewarded,
                reader.TryRewardFollower("987654", "viewer", followedAt, 50, out _));
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
        DateTimeOffset followedAt = new(2026, 8, 27, 1, 2, 3, TimeSpan.Zero);
        using TemporaryDirectory directory = new();
        TokenStore store = new(Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("not-a-user-id", "viewer", followedAt, 50, out _));
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("123", "", followedAt, 50, out _));
            Assert.Equal(FollowRewardResult.Failed, store.TryRewardFollower("123", "viewer", followedAt, 0, out _));
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
        TokenStore store = new(databasePath);

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

        TokenStore backup = new(backupPath);
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
        TokenStore store = new(databasePath);

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
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EconomyDatabaseCollection
{
    public const string Name = "SQLite database tests";
}
