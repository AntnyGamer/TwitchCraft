using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

[Collection(PersistenceDataCollection.Name)]
public sealed class RuntimeProfilePersistenceTests
{
    [Fact]
    public async Task ProfileTransitions_PreserveLocalCredentialsAndRestoreBind()
    {
        Assert.Equal(TestApplicationData.Path, ConfigurationStore.WorkingDirectory);
        using TemporaryDirectory directory = new();
        await using FakeRCONServer RCON = new("remote-credential");
        TwitchCraftConfig config = new()
        {
            Server =
            {
                ServerDirectory = directory.Path,
                MinecraftVersion = "26.1.0",
                BindIP = "::1",
                RCON = { Port = 25580, Password = "local-credential" }
            },
            Settings =
            {
                AutomaticBackupsEnabled = false,
                StatisticsEnabled = false,
                CommandPrefix = "?",
                FollowRewardAmount = 321
            }
        };
        ConfigurationStore.Save(config);
        MainHandler runtime = new(new AppShellViewModel(), Path.Combine(directory.Path, "viewer_tokens.db"));
        string propertiesPath = Path.Combine(directory.Path, "server.properties");

        try
        {
            runtime.ApplyProfile(multiplayerEnabled: true, requireOnlineMode: false, streamerMinecraftName: " PlayerOne ");

            Assert.True(runtime.ProfileApplied);
            Assert.True(runtime.MultiplayerEnabled);
            Assert.False(runtime.RemoteControlEnabled);
            Assert.False(runtime.RequireOnlineMode);
            TwitchCraftConfig multiplayer = ConfigurationStore.Load();
            Assert.Equal("::", multiplayer.Server.BindIP);
            Assert.Equal("::1", multiplayer.Server.PreviousBindIP);
            Assert.Equal(5, multiplayer.Server.MaxPlayers);
            Assert.Equal("PlayerOne", multiplayer.Identity.StreamerMinecraftName);
            AssertPersistedSettings(multiplayer);
            string multiplayerProperties = File.ReadAllText(propertiesPath);
            AssertProperty(multiplayerProperties, "server-ip", "\\:\\:");
            AssertProperty(multiplayerProperties, "max-players", "5");
            AssertProperty(multiplayerProperties, "online-mode", "false");
            File.SetLastWriteTimeUtc(propertiesPath, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            DateTime localPropertiesWriteTime = File.GetLastWriteTimeUtc(propertiesPath);

            runtime.ApplyProfile(multiplayerEnabled: true, requireOnlineMode: false, streamerMinecraftName: "PlayerOne",
                remoteControlEnabled: true, remoteHost: " 127.0.0.1 ", RCONPort: RCON.Port, RCONPassword: " remote-credential ");

            Assert.True(runtime.MultiplayerEnabled);
            Assert.True(runtime.RemoteControlEnabled);
            Assert.False(runtime.RequireOnlineMode);
            Assert.False(runtime.MinecraftProcessRunning);
            TwitchCraftConfig remote = ConfigurationStore.Load();
            Assert.Equal("127.0.0.1", remote.Server.RemoteHost);
            Assert.Equal("::", remote.Server.BindIP);
            Assert.Equal("::1", remote.Server.PreviousBindIP);
            AssertPersistedSettings(remote);
            Assert.Equal(multiplayerProperties, File.ReadAllText(propertiesPath));
            Assert.Equal(localPropertiesWriteTime, File.GetLastWriteTimeUtc(propertiesPath));
            Assert.True(await runtime.RunMinecraftCommandAsync("say remote-profile"));
            Assert.Equal(["say remote-profile"], RCON.Commands);

            runtime.ApplyProfile(multiplayerEnabled: false, requireOnlineMode: false, streamerMinecraftName: "PlayerOne");

            Assert.False(runtime.MultiplayerEnabled);
            Assert.False(runtime.RemoteControlEnabled);
            Assert.True(runtime.RequireOnlineMode);
            TwitchCraftConfig singlePlayer = ConfigurationStore.Load();
            Assert.Equal("::1", singlePlayer.Server.BindIP);
            Assert.Equal(1, singlePlayer.Server.MaxPlayers);
            AssertPersistedSettings(singlePlayer);
            string singlePlayerProperties = File.ReadAllText(propertiesPath);
            AssertProperty(singlePlayerProperties, "server-ip", "\\:\\:1");
            AssertProperty(singlePlayerProperties, "max-players", "1");
            AssertProperty(singlePlayerProperties, "online-mode", "true");
            AssertProperty(singlePlayerProperties, "rcon.port", "25580");
            AssertProperty(singlePlayerProperties, "rcon.password", "local-credential");
            Assert.DoesNotContain("remote-credential", singlePlayerProperties, StringComparison.Ordinal);

            Assert.True(await runtime.ShutdownAsync());
            Assert.True(await runtime.ShutdownAsync());
            Assert.False(runtime.MinecraftProcessRunning);
            Assert.Equal(singlePlayerProperties, File.ReadAllText(propertiesPath));
            AssertPersistedSettings(ConfigurationStore.Load());
        }
        finally
        {
            // Cleanup must still run when the test's cancellation token is canceled.
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            await MinecraftRCONClient.DisconnectAsync(cleanup.Token);
            runtime.Tokens.Close();
            StatisticsStore.CloseConnection();
            File.Delete(ConfigurationStore.ConfigPath);
            File.Delete(ConfigurationStore.ConfigPath + ".tmp");
        }
    }

    private static void AssertProperty(string content, string key, string value)
    {
        string line = Assert.Single(content.Split(Environment.NewLine),
            line => line.StartsWith(key + "=", StringComparison.Ordinal));
        Assert.Equal(key + "=" + value, line);
    }

    private static void AssertPersistedSettings(TwitchCraftConfig config)
    {
        Assert.Equal(25580, config.Server.RCON.Port);
        Assert.Equal("local-credential", config.Server.RCON.Password);
        Assert.Equal("?", config.Settings.CommandPrefix);
        Assert.Equal(321, config.Settings.FollowRewardAmount);
        Assert.False(config.Settings.MultiplayerEnabled);
        Assert.False(config.Settings.RemoteControlEnabled);
        Assert.True(config.Settings.RequireOnlineMode);
    }
}
