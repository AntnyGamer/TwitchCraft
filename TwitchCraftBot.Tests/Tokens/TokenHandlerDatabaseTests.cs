using Microsoft.Data.Sqlite;
using System.Text.Json;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Tokens;

[Collection(SqliteDatabaseTestCollection.Name)]
public sealed class TokenHandlerDatabaseTests
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
            store.AdjustBalances(["Alice", " alice ", "@BOB", "bob"], 3);

            Assert.Equal(6, store.GetBalance("alice"));
            Assert.Equal(6, store.GetBalance("BOB"));
        }
        finally
        {
            store.CloseConnection();
        }
    }

    [Fact]
    public void TrySpendNow_PersistsOnlySuccessfulSpends()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler store = new(databasePath);

        try
        {
            store.AdjustBalance("viewer", 10);

            Assert.True(store.TrySpendNow("viewer", 4));
            Assert.False(store.TrySpendNow("viewer", 7));
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
    public void ExportReadableJson_ContainsSortedPositiveBalances()
    {
        using TemporaryDirectory directory = new();
        string databasePath = Path.Combine(directory.Path, "viewer_tokens.db");
        TokenHandler store = new(databasePath);

        try
        {
            store.AdjustBalance("Bob", 7);
            store.AdjustBalance("alice", 3);
            Assert.True(store.TryExportReadableJson());
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
public sealed class SqliteDatabaseTestCollection
{
    public const string Name = "SQLite database tests";
}
