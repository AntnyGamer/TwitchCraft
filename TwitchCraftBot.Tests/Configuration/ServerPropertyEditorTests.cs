using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class ServerPropertyEditorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../outside")]
    public void NormalizeLevelName_ReplacesUnsafeOrEmptyValues(string? value)
    {
        Assert.Equal("world", ServerPropertyEditor.NormalizeLevelName(value));
    }

    [Fact]
    public void NormalizeLevelName_PreservesSafeWorldName()
    {
        Assert.Equal("Streamer World", ServerPropertyEditor.NormalizeLevelName("  Streamer World  "));
    }

    [Fact]
    public void ApplyStartProfile_PreservesUnmanagedPropertiesAndUpdatesManagedValues()
    {
        using TemporaryDirectory directory = new();
        string propertiesPath = Path.Combine(directory.Path, "server.properties");
        File.WriteAllText(
            propertiesPath,
            """
            custom-setting=keep=this
            view-distance=6
            level-name=Streamer World
            server-port=12345
            online-mode=true
            """);

        BotConfig config = new()
        {
            Server = new ServerConfig
            {
                ServerDirectory = directory.Path,
                MinecraftVersion = "1.21.11",
                BindIP = "127.0.0.1",
                Port = 25570,
                MaxPlayers = 7,
                RCON = new RCONConfig
                {
                    Port = 25580,
                    Password = " secret:password "
                }
            },
            Settings = new StartingProfile
            {
                MultiplayerEnabled = true,
                MultiplayerPVPEnabled = true,
                RequireOnlineMode = false,
                HardcoreEnabled = false,
                Difficulty = "Hard"
            }
        };

        string content = ServerPropertyEditor.ApplyStartProfile(config);

        Assert.Contains(@"custom-setting=keep\=this", content);
        Assert.Contains("view-distance=6", content);
        Assert.Contains("level-name=Streamer World", content);
        Assert.Contains("server-port=25570", content);
        Assert.Contains("rcon.port=25580", content);
        Assert.Contains(@"rcon.password=secret\:password", content);
        Assert.Contains("max-players=7", content);
        Assert.Contains("online-mode=false", content);
        Assert.Contains("pvp=true", content);
        Assert.Contains("difficulty=hard", content);
        Assert.Contains("hardcore=false", content);
        Assert.DoesNotContain("server-port=12345", content);
        Assert.Equal(content, File.ReadAllText(propertiesPath));
        Assert.Equal("Streamer World", ServerPropertyEditor.GetLevelName(config));
    }

}
