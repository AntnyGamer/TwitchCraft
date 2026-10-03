using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

[Collection(PersistenceDataCollection.Name)]
public sealed class PersistenceIntegrationTests : IDisposable
{
    public PersistenceIntegrationTests()
    {
        Assert.Equal(TestApplicationData.Path, ConfigurationStore.WorkingDirectory);
        StatisticsStore.CloseConnection();
        SqliteConnection.ClearAllPools();
        foreach (string name in new[] { "statistics.db", "statistics.db-wal", "statistics.db-shm", "config.json", "config.json.tmp" })
            File.Delete(Path.Combine(TestApplicationData.Path, name));
    }

    [Fact]
    public void Statistics_PersistAcrossRestartAndExportCommandAndDeathAccounting()
    {
        List<string> deathRefreshes = [];
        List<string> respawnRefreshes = [];
        StatisticsService service = CreateStatistics(deathRefreshes, respawnRefreshes);
        service.ResetForSession();
        service.RecordSession();
        service.RecordPlayerJoin("Steve");
        service.RecordCommand("!HEAL", " Viewer ", 12);
        service.RecordCommand("fire", "troublemaker", 20);
        service.RecordCommand("heal", "streamer", 5);
        service.RecordCommand("tokens", "viewer", 99);
        service.RecordEffects(2, streamerReceivedEffect: true);
        service.RecordLine("[Server thread/INFO]: Steve was slain by Zombie", hasDeathScoreObjective: false);
        Assert.Equal(["Steve"], deathRefreshes);

        service.RecordDeathScore("Steve", 1);
        service.RecordDeathScore("Steve", 1);
        service.RecordGamemode("Steve", 3);
        service.RecordRespawn("Steve");
        Assert.False(service.NeedsRespawnRefresh("Steve"));
        service.RecordGamemode("Steve", 0);
        Assert.Equal(["Steve"], respawnRefreshes);
        Assert.True(service.NeedsRespawnRefresh("Steve"));
        service.RecordRespawn("Steve");
        Assert.False(service.NeedsRespawnRefresh("Steve"));
        service.RecordPlayerLeave("Steve");

        StatisticsSnapshot session = service.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(3, session.SessionGameCommandsRun);
        Assert.Equal(2, session.SessionNiceCommandsRun);
        Assert.Equal(1, session.SessionDangerousCommandsRun);
        Assert.Equal(37, session.SessionTokensSpent);
        Assert.Equal(2, session.SessionEffectsGiven);
        Assert.Equal(1, session.SessionDeaths);
        Assert.Equal("!heal", session.SessionMostUsedCommand);
        Assert.Equal("viewer", session.SessionNicestViewer);
        Assert.Equal("troublemaker", session.SessionMostDangerousViewer);

        StatisticsStore.CloseConnection();
        StatisticsService restarted = CreateStatistics([], []);
        StatisticsSnapshot persisted = restarted.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(0, persisted.SessionGameCommandsRun);
        Assert.Equal(3, persisted.TotalGameCommandsRun);
        Assert.Equal(37, persisted.TotalTokensSpent);
        Assert.Equal(2, persisted.TotalEffectsGiven);
        Assert.Equal(1, persisted.TotalDeaths);
        Assert.Equal(1, persisted.SessionsStarted);
        Assert.Equal("!heal", persisted.TotalMostUsedCommand);
        Assert.Equal("viewer", persisted.TotalNicestViewer);
        Assert.Equal("troublemaker", persisted.TotalMostDangerousViewer);

        Assert.True(StatisticsStore.TryExportJson());
        using JsonDocument exported = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestApplicationData.Path, "exports", "statistics.json")));
        JsonElement global = exported.RootElement.GetProperty("Global");
        Assert.Equal(3, global.GetProperty("GameCommandsRun").GetInt64());
        Assert.Equal(37, global.GetProperty("TokensSpent").GetInt64());
        Assert.Equal(1, global.GetProperty("Deaths").GetInt64());
        Assert.Equal(2, exported.RootElement.GetProperty("CommandUseCounts").GetProperty("!heal").GetInt64());
        using JsonDocument viewers = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestApplicationData.Path, "exports", "statistics_viewers.json")));
        JsonElement scores = viewers.RootElement.GetProperty("ViewerStatistics");
        Assert.Equal(1, scores.GetProperty("viewer").GetProperty("NiceScore").GetInt64());
        Assert.Equal(1, scores.GetProperty("troublemaker").GetProperty("DangerousScore").GetInt64());
        Assert.False(scores.TryGetProperty("streamer", out _));
    }

    [Fact]
    public void CommandStatistics_FailedViewerWriteRollsBackTotalsAndCanRetry()
    {
        StatisticsService service = CreateStatistics([], []);
        service.RecordCommand("heal", "viewer", 10);
        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TestApplicationData.Path, "statistics.db")
        }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            // Fail the last write, after the total and command-use rows have been updated.
            command.CommandText = "CREATE TRIGGER fail_viewer_score BEFORE INSERT ON ViewerScores WHEN NEW.Username = 'other' BEGIN SELECT RAISE(ABORT, 'blocked viewer score'); END;";
            command.ExecuteNonQuery();

            service.RecordCommand("fire", "other", 20);

            StatisticsSnapshot failed = service.GetSnapshot(TestContext.Current.CancellationToken);
            Assert.Equal(1, failed.SessionGameCommandsRun);
            Assert.Equal(1, failed.TotalGameCommandsRun);
            Assert.Equal(10, failed.SessionTokensSpent);
            Assert.Equal(10, failed.TotalTokensSpent);
            Assert.Equal(0, failed.SessionDangerousCommandsRun);
            Assert.Empty(failed.TotalMostDangerousViewer);
            Assert.Equal("!heal", failed.TotalMostUsedCommand);
            LifetimeStatistics persisted = Assert.IsType<LifetimeStatistics>(StatisticsStore.LoadGlobal());
            Assert.Equal(1, persisted.GameCommandsRun);
            Assert.Equal(10, persisted.TokensSpent);
            Assert.Equal(1, persisted.CommandUseCounts["heal"]);
            Assert.False(persisted.CommandUseCounts.ContainsKey("fire"));
            Assert.Equal((string.Empty, "viewer"), StatisticsStore.GetTopViewers("streamer"));

            command.CommandText = "DROP TRIGGER fail_viewer_score;";
            command.ExecuteNonQuery();
        }

        service.RecordCommand("fire", "other", 20);
        StatisticsSnapshot retried = service.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(2, retried.SessionGameCommandsRun);
        Assert.Equal(2, retried.TotalGameCommandsRun);
        Assert.Equal(30, retried.TotalTokensSpent);
        Assert.Equal(1, retried.SessionDangerousCommandsRun);
        Assert.Equal("other", retried.SessionMostDangerousViewer);
        Assert.Equal("other", retried.TotalMostDangerousViewer);

        StatisticsStore.CloseConnection();
        StatisticsSnapshot restarted = CreateStatistics([], []).GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(2, restarted.TotalGameCommandsRun);
        Assert.Equal(30, restarted.TotalTokensSpent);
        Assert.Equal("!fire", restarted.TotalMostUsedCommand);
        Assert.Equal("other", restarted.TotalMostDangerousViewer);
        Assert.Equal("viewer", restarted.TotalNicestViewer);
    }

    [Fact]
    public async Task StatisticsReset_RollsBackFailureThenClearsWithoutRecountingDeaths()
    {
        Assert.True(StatisticsStore.ApplyDeathScore(3, 90, out long deaths));
        Assert.Equal(3, deaths);
        List<string> refreshes = [];
        StatisticsService service = CreateStatistics([], [], refreshes);
        service.RecordSession();
        service.ResetForSession();
        service.RecordPlayerJoin("Steve");
        service.RecordCommand("heal", "viewer", 25);
        service.RecordEffects(2, streamerReceivedEffect: true);
        StatisticsSnapshot before = service.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal("viewer", before.TotalNicestViewer);
        Assert.Equal(TimeSpan.FromSeconds(90), before.LongestTimeSurvived);
        Assert.Equal(3, before.TotalDeaths);

        using (SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(TestApplicationData.Path, "statistics.db")
        }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            // Abort after command counts and viewer scores have been deleted in the transaction.
            command.CommandText = "CREATE TRIGGER fail_reset BEFORE UPDATE ON GlobalStats BEGIN SELECT RAISE(ABORT, 'blocked reset'); END;";
            command.ExecuteNonQuery();
            await Assert.ThrowsAsync<IOException>(() => service.ResetAllAsync());

            StatisticsSnapshot failed = service.GetSnapshot(TestContext.Current.CancellationToken);
            Assert.Equal(1, failed.SessionGameCommandsRun);
            Assert.Equal(1, failed.TotalGameCommandsRun);
            Assert.Equal(25, failed.TotalTokensSpent);
            Assert.Equal(2, failed.TotalEffectsGiven);
            Assert.Equal(3, failed.TotalDeaths);
            Assert.Equal("!heal", failed.TotalMostUsedCommand);
            Assert.Equal("viewer", failed.TotalNicestViewer);
            Assert.Equal(1, StatisticsStore.LoadGlobal()!.CommandUseCounts["heal"]);
            Assert.Equal((string.Empty, "viewer"), StatisticsStore.GetTopViewers("streamer"));
            Assert.Empty(refreshes);

            command.CommandText = "DROP TRIGGER fail_reset;";
            command.ExecuteNonQuery();
        }

        await service.ResetAllAsync();

        StatisticsSnapshot cleared = service.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(0, cleared.SessionGameCommandsRun);
        Assert.Equal(0, cleared.SessionTokensSpent);
        Assert.Equal(0, cleared.SessionEffectsGiven);
        Assert.Equal(0, cleared.TotalGameCommandsRun);
        Assert.Equal(0, cleared.TotalTokensSpent);
        Assert.Equal(0, cleared.TotalEffectsGiven);
        Assert.Equal(0, cleared.TotalDeaths);
        Assert.Equal(0, cleared.SessionsStarted);
        Assert.Empty(cleared.SessionMostUsedCommand);
        Assert.Empty(cleared.SessionNicestViewer);
        Assert.Empty(cleared.TotalMostUsedCommand);
        Assert.Empty(cleared.TotalNicestViewer);
        Assert.Null(cleared.LongestTimeSurvived);
        Assert.Null(cleared.ShortestTimeSurvived);
        Assert.NotNull(cleared.SessionTimeSurvived);
        Assert.False(service.NeedsRespawnRefresh("Steve"));
        Assert.Equal(["snapshot", "gamemode", "death-scores"], refreshes);

        StatisticsStore.CloseConnection();
        LifetimeStatistics persisted = Assert.IsType<LifetimeStatistics>(StatisticsStore.LoadGlobal());
        Assert.Equal(0, persisted.GameCommandsRun);
        Assert.Equal(0, persisted.TokensSpent);
        Assert.Equal(0, persisted.EffectsGiven);
        Assert.Equal(0, persisted.Deaths);
        Assert.Equal(0, persisted.SessionsStarted);
        Assert.Equal(3, persisted.LastDeathScore);
        Assert.Empty(persisted.CommandUseCounts);
        Assert.Equal((string.Empty, string.Empty), StatisticsStore.GetTopViewers("streamer"));

        StatisticsService restarted = CreateStatistics([], []);
        restarted.Load();
        restarted.ResetForSession();
        restarted.RecordDeathScore("Steve", 3);
        Assert.Equal(0, restarted.GetSnapshot(TestContext.Current.CancellationToken).TotalDeaths);
        restarted.RecordDeathScore("Steve", 4);
        StatisticsSnapshot nextDeath = restarted.GetSnapshot(TestContext.Current.CancellationToken);
        Assert.Equal(1, nextDeath.SessionDeaths);
        Assert.Equal(1, nextDeath.TotalDeaths);
        Assert.Equal(1, StatisticsStore.LoadGlobal()!.Deaths);
    }

    [Fact]
    public void ShutdownBackup_PreservesRestorablePointInTimeData()
    {
        TwitchCraftConfig config = new() { Twitch = { BotName = "savedbot" } };
        config.Settings.AutomaticBackupsEnabled = true;
        ConfigurationStore.Save(config);
        TokenService tokens = new(ConfigurationStore.ViewerTokensPath, () => 0);
        try
        {
            tokens.Award("viewer", 100);
            Assert.True(tokens.TrySpend("viewer", 25));
            CreateStatistics([], []).RecordCommand("heal", "viewer", 25);
            DataMaintenance maintenance = new(() => config, config.Settings, tokens,
                (_, _) => Task.FromResult(string.Empty), (_, _) => { }, (_, _) => Task.FromResult(false));

            string[] priorBackups = Directory.Exists(ConfigurationStore.BackupsDirectory)
                ? Directory.GetDirectories(ConfigurationStore.BackupsDirectory) : [];
            maintenance.BackupOnShutdown();
            string backup = Assert.Single(Array.FindAll(Directory.GetDirectories(ConfigurationStore.BackupsDirectory), path => !Array.Exists(priorBackups, previous => previous == path)));

            ConfigurationStore.Update(saved => saved.Twitch.BotName = "changedbot");
            tokens.Award("viewer", 50);
            Assert.True(StatisticsStore.ApplySessionDelta());

            using JsonDocument savedConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "config.json")));
            Assert.Equal("savedbot", savedConfig.RootElement.GetProperty("Twitch").GetProperty("BotName").GetString());
            Assert.Equal("changedbot", ConfigurationStore.Load().Twitch.BotName);
            Assert.Equal(125, tokens.GetBalance("viewer"));
            TokenStore restoredTokens = new(Path.Combine(backup, "viewer_tokens.db"));
            try { Assert.Equal(75, restoredTokens.GetBalance("viewer")); }
            finally { restoredTokens.CloseConnection(); }

            using SqliteConnection restoredStats = new(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(backup, "statistics.db"),
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            restoredStats.Open();
            using SqliteCommand command = restoredStats.CreateCommand();
            command.CommandText = "SELECT GameCommandsRun, TokensSpent, SessionsStarted FROM GlobalStats WHERE ID = 1";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal(25, reader.GetInt64(1));
            Assert.Equal(0, reader.GetInt64(2));
            Assert.Equal(1, StatisticsStore.LoadGlobal()!.SessionsStarted);
        }
        finally { tokens.Close(); }
    }

    [Fact]
    public void ConfigurationUpdate_PreservesUnreadablePrimaryAndPendingFiles()
    {
        const string primary = "{interrupted configuration";
        const string pending = "null";
        File.WriteAllText(ConfigurationStore.ConfigPath, primary);
        File.WriteAllText(ConfigurationStore.ConfigPath + ".tmp", pending);
        bool updateCalled = false;

        Assert.Throws<InvalidDataException>(() => ConfigurationStore.Load());
        Assert.Throws<InvalidDataException>(() => ConfigurationStore.Update(saved =>
        {
            updateCalled = true;
            saved.Twitch.BotName = "replacement";
        }));

        Assert.False(updateCalled);
        Assert.Equal(primary, File.ReadAllText(ConfigurationStore.ConfigPath));
        Assert.Equal(pending, File.ReadAllText(ConfigurationStore.ConfigPath + ".tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationRecovery_PromotesPendingWriteAndPreservesNextUpdate(bool malformedPrimary)
    {
        TwitchCraftConfig config = new()
        {
            Twitch = { BotName = "savedbot", BotToken = "test-token" },
            Settings = { MultiplayerEnabled = true, RemoteControlEnabled = true, RequireOnlineMode = false, FollowRewardAmount = 125 }
        };
        config.Settings.CommandCustomizations["heal"] = new() { Enabled = false, CooldownSeconds = 17 };
        ConfigurationStore.Save(config);
        Assert.True(config.Settings.MultiplayerEnabled);
        Assert.True(config.Settings.RemoteControlEnabled);
        Assert.False(config.Settings.RequireOnlineMode);
        if (malformedPrimary)
        {
            File.Copy(ConfigurationStore.ConfigPath, ConfigurationStore.ConfigPath + ".tmp");
            File.WriteAllText(ConfigurationStore.ConfigPath, "{interrupted configuration");
        }
        else
        {
            File.Move(ConfigurationStore.ConfigPath, ConfigurationStore.ConfigPath + ".tmp");
        }

        Assert.True(ConfigurationStore.HasConfig());
        TwitchCraftConfig recovered = ConfigurationStore.Load();

        Assert.True(File.Exists(ConfigurationStore.ConfigPath));
        Assert.False(File.Exists(ConfigurationStore.ConfigPath + ".tmp"));
        Assert.Equal("savedbot", recovered.Twitch.BotName);
        Assert.Equal("test-token", recovered.Twitch.BotToken);
        Assert.Equal(125, recovered.Settings.FollowRewardAmount);
        Assert.False(recovered.Settings.MultiplayerEnabled);
        Assert.False(recovered.Settings.RemoteControlEnabled);
        Assert.True(recovered.Settings.RequireOnlineMode);
        Assert.False(recovered.Settings.CommandCustomizations["HEAL"].Enabled);
        Assert.Equal(17, recovered.Settings.CommandCustomizations["heal"].CooldownSeconds);

        ConfigurationStore.Update(saved => saved.Settings.FollowRewardAmount = 250);
        TwitchCraftConfig updated = ConfigurationStore.Load();
        Assert.Equal(250, updated.Settings.FollowRewardAmount);
        Assert.Equal("test-token", updated.Twitch.BotToken);
        Assert.False(updated.Settings.CommandCustomizations["heal"].Enabled);
        Assert.False(File.Exists(ConfigurationStore.ConfigPath + ".tmp"));
    }

    private static StatisticsService CreateStatistics(List<string> deathRefreshes, List<string> respawnRefreshes, List<string>? refreshes = null)
    {
        StatisticsService service = new(new(
            command => command switch
            {
                "heal" => ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice,
                "fire" => ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous,
                _ => ChatCommandStatisticFlags.None
            }, _ => true, _ => false,
            () => refreshes?.Add("snapshot"), () => refreshes?.Add("gamemode"), () => refreshes?.Add("death-scores"),
            deathRefreshes.Add, respawnRefreshes.Add));
        service.SetContext(true, "streamer", "Steve", "!");
        return service;
    }

    public void Dispose()
    {
        StatisticsStore.CloseConnection();
        SqliteConnection.ClearAllPools();
        File.Delete(ConfigurationStore.ConfigPath);
        File.Delete(ConfigurationStore.ConfigPath + ".tmp");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PersistenceDataCollection
{
    public const string Name = "Isolated application persistence";
}
