using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Economy;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class MinigameBetDurabilityTests
{
    [Fact]
    public void InterruptedGame_RefundsExactlyOnceOnNextLoad()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenService first = new(path, static () => 0);
        try
        {
            first.Load(0);
            first.Award("viewer", 100);
            Assert.True(first.TrySpendForMinigameBet("viewer", 25));
            Assert.True(first.TrySpendForMinigameBet("@VIEWER", 10));
            Assert.Equal(65, first.GetBalance("viewer"));
        }
        finally { first.Close(); }

        TokenService recovered = new(path, static () => 0);
        try
        {
            recovered.Load(0);
            Assert.Equal(100, recovered.GetBalance("viewer"));
            Assert.False(recovered.TryCompleteMinigameBets([new("viewer", 35)]));
        }
        finally { recovered.Close(); }

        TokenService reopened = new(path, static () => 0);
        try
        {
            reopened.Load(0);
            Assert.Equal(100, reopened.GetBalance("viewer"));
        }
        finally { reopened.Close(); }
    }

    [Fact]
    public void Settlement_CreditsOnlyWinnersAndCannotRunTwice()
    {
        using TemporaryDirectory directory = new();
        TokenService tokens = new(Path.Combine(directory.Path, "viewer_tokens.db"), static () => 0);
        try
        {
            tokens.Load(0);
            tokens.Award(["alice", "bob"], 100);
            Assert.True(tokens.TrySpendForMinigameBet("alice", 25));
            Assert.True(tokens.TrySpendForMinigameBet("bob", 30));
            Assert.True(tokens.TryCompleteMinigameBets([new("alice", 50)]));
            Assert.Equal(125, tokens.GetBalance("alice"));
            Assert.Equal(70, tokens.GetBalance("bob"));
            Assert.False(tokens.TryCompleteMinigameBets([new("alice", 50)]));
            Assert.Equal(125, tokens.GetBalance("alice"));
        }
        finally { tokens.Close(); }
    }

    [Fact]
    public void FailedStakeOrSettlement_RollsBackAndCanRetry()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenService tokens = new(path, static () => 0);
        try
        {
            tokens.Load(0);
            tokens.Award("viewer", 100);
            using SqliteConnection connection = new($"Data Source={path}");
            connection.Open();
            using SqliteCommand sql = connection.CreateCommand();
            sql.CommandText = "CREATE TRIGGER fail_stake BEFORE INSERT ON PendingMinigameBets BEGIN SELECT RAISE(ABORT, 'test'); END;";
            sql.ExecuteNonQuery();
            Assert.False(tokens.TrySpendForMinigameBet("viewer", 25));
            Assert.Equal(100, tokens.GetBalance("viewer"));
            sql.CommandText = "DROP TRIGGER fail_stake;";
            sql.ExecuteNonQuery();

            Assert.True(tokens.TrySpendForMinigameBet("viewer", 25));
            Assert.Equal(75, tokens.GetBalance("viewer"));
            sql.CommandText = "CREATE TRIGGER fail_settle BEFORE UPDATE ON TokenBalances BEGIN SELECT RAISE(ABORT, 'test'); END;";
            sql.ExecuteNonQuery();
            Assert.False(tokens.TryCompleteMinigameBets([new("viewer", 50)]));
            Assert.Equal(75, tokens.GetBalance("viewer"));
            sql.CommandText = "DROP TRIGGER fail_settle;";
            sql.ExecuteNonQuery();
            Assert.True(tokens.TryCompleteMinigameBets([new("viewer", 50)]));
            Assert.Equal(125, tokens.GetBalance("viewer"));
        }
        finally { tokens.Close(); }
    }

    [Fact]
    public void ConcurrentSession_CannotRecoverAnActiveSessionsBets()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenService first = new(path, static () => 0);
        TokenService second = new(path, static () => 0);
        try
        {
            first.Load(0);
            first.Award("viewer", 100);
            Assert.True(first.TrySpendForMinigameBet("viewer", 25));
            Assert.Throws<IOException>(() => second.Load(0));
            Assert.Equal(75, first.GetBalance("viewer"));
        }
        finally
        {
            second.Close();
            first.Close();
        }

        TokenService recovered = new(path, static () => 0);
        try
        {
            recovered.Load(0);
            Assert.Equal(100, recovered.GetBalance("viewer"));
        }
        finally { recovered.Close(); }
    }
}
