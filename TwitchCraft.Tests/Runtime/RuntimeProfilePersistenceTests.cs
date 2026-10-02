using System;
using System.IO;
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
    public async Task ProfileTransitions_PreserveLocalCredentialsAndRestoreTheLocalBind()
    {
        Assert.Equal(TestApplicationData.Path, ConfigurationStore.WorkingDirectory);
        using TemporaryDirectory directory = new();
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
            Assert.Contains("server-ip=\\:\\:", multiplayerProperties, StringComparison.Ordinal);
            Assert.Contains("max-players=5", multiplayerProperties, StringComparison.Ordinal);
            Assert.Contains("online-mode=false", multiplayerProperties, StringComparison.Ordinal);
            File.SetLastWriteTimeUtc(propertiesPath, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            DateTime localPropertiesWriteTime = File.GetLastWriteTimeUtc(propertiesPath);

            runtime.ApplyProfile(multiplayerEnabled: true, requireOnlineMode: false, streamerMinecraftName: "PlayerOne",
                remoteControlEnabled: true, remoteHost: " remote.example ", RCONPort: 25600, RCONPassword: " remote-credential ");

            Assert.True(runtime.MultiplayerEnabled);
            Assert.True(runtime.RemoteControlEnabled);
            Assert.False(runtime.RequireOnlineMode);
            Assert.False(runtime.MinecraftProcessRunning);
            TwitchCraftConfig remote = ConfigurationStore.Load();
            Assert.Equal("remote.example", remote.Server.RemoteHost);
            Assert.Equal("::", remote.Server.BindIP);
            Assert.Equal("::1", remote.Server.PreviousBindIP);
            AssertPersistedSettings(remote);
            Assert.Equal(multiplayerProperties, File.ReadAllText(propertiesPath));
            Assert.Equal(localPropertiesWriteTime, File.GetLastWriteTimeUtc(propertiesPath));

            runtime.ApplyProfile(multiplayerEnabled: false, requireOnlineMode: false, streamerMinecraftName: "PlayerOne");

            Assert.False(runtime.MultiplayerEnabled);
            Assert.False(runtime.RemoteControlEnabled);
            Assert.True(runtime.RequireOnlineMode);
            TwitchCraftConfig singlePlayer = ConfigurationStore.Load();
            Assert.Equal("::1", singlePlayer.Server.BindIP);
            Assert.Equal(1, singlePlayer.Server.MaxPlayers);
            AssertPersistedSettings(singlePlayer);
            string singlePlayerProperties = File.ReadAllText(propertiesPath);
            Assert.Contains("server-ip=\\:\\:1", singlePlayerProperties, StringComparison.Ordinal);
            Assert.Contains("max-players=1", singlePlayerProperties, StringComparison.Ordinal);
            Assert.Contains("online-mode=true", singlePlayerProperties, StringComparison.Ordinal);
            Assert.Contains("rcon.port=25580", singlePlayerProperties, StringComparison.Ordinal);
            Assert.Contains("rcon.password=local-credential", singlePlayerProperties, StringComparison.Ordinal);
            Assert.DoesNotContain("remote-credential", singlePlayerProperties, StringComparison.Ordinal);

            Assert.True(await runtime.ShutdownAsync());
            Assert.True(await runtime.ShutdownAsync());
            Assert.False(runtime.MinecraftProcessRunning);
            Assert.Equal(singlePlayerProperties, File.ReadAllText(propertiesPath));
            AssertPersistedSettings(ConfigurationStore.Load());
        }
        finally
        {
            runtime.Tokens.Close();
            StatisticsStore.CloseConnection();
        }
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
