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
    public void SessionStatistics_SurviveRestartAndExportTheSameCommandAndDeathAccounting()
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
    public void ShutdownBackup_PreservesAConsistentRestorableSnapshotAfterLiveDataChanges()
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
    public void ConfigurationRecovery_PromotesACompletePendingWriteAndPreservesTheNextUpdate()
    {
        TwitchCraftConfig config = new()
        {
            Twitch = { BotName = "savedbot", BotToken = "test-token" },
            Settings = { MultiplayerEnabled = true, RemoteControlEnabled = true, RequireOnlineMode = false }
        };
        config.Settings.CommandCustomizations["heal"] = new() { Enabled = false, CooldownSeconds = 17 };
        ConfigurationStore.Save(config);
        Assert.True(config.Settings.MultiplayerEnabled);
        Assert.True(config.Settings.RemoteControlEnabled);
        Assert.False(config.Settings.RequireOnlineMode);
        File.Move(ConfigurationStore.ConfigPath, ConfigurationStore.ConfigPath + ".tmp");

        Assert.True(ConfigurationStore.HasConfig());
        TwitchCraftConfig recovered = ConfigurationStore.Load();

        Assert.True(File.Exists(ConfigurationStore.ConfigPath));
        Assert.False(File.Exists(ConfigurationStore.ConfigPath + ".tmp"));
        Assert.Equal("savedbot", recovered.Twitch.BotName);
        Assert.Equal("test-token", recovered.Twitch.BotToken);
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

    private static StatisticsService CreateStatistics(List<string> deathRefreshes, List<string> respawnRefreshes)
    {
        StatisticsService service = new(new(
            command => command switch
            {
                "heal" => ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice,
                "fire" => ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous,
                _ => ChatCommandStatisticFlags.None
            }, _ => true, _ => false, () => { }, () => { }, () => { }, deathRefreshes.Add, respawnRefreshes.Add));
        service.SetContext(true, "streamer", "Steve", "!");
        return service;
    }

    public void Dispose()
    {
        StatisticsStore.CloseConnection();
        SqliteConnection.ClearAllPools();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PersistenceDataCollection
{
    public const string Name = "Isolated application persistence";
}
