using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Configuration;

public sealed class ServerPropertiesContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("folder/name")]
    [InlineData("folder\\name")]
    [InlineData("name:stream")]
    [InlineData("C:\\world")]
    [InlineData("/world")]
    [InlineData("world*")]
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
    public void WriteInitialFiles_PreservesUnmanagedPropertiesAndUpdatesManagedValues()
    {
        using TemporaryDirectory directory = new();
        string propertiesPath = Path.Combine(directory.Path, "server.properties");
        File.WriteAllText(
            propertiesPath,
            """
            z-custom=last
            custom-setting=keep=this
            view-distance=6
            level-name=Streamer World
            server-port=12345
            online-mode=true
            escaped-value=hello\=world\\path
            a-custom=first
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
                Difficulty = "Hard",
                ViewDistance = 6,
                SimulationDistance = 8,
                EntityBroadcastRangePercentage = 75,
                NetworkCompressionThreshold = 128
            }
        };

        ServerPropertyEditor.WriteInitialFiles(config);
        string content = File.ReadAllText(propertiesPath);

        Assert.Contains("a-custom=first", content, StringComparison.Ordinal);
        Assert.Contains(@"custom-setting=keep\=this", content, StringComparison.Ordinal);
        Assert.Contains(@"escaped-value=hello\=world\\path", content, StringComparison.Ordinal);
        Assert.Contains("z-custom=last", content, StringComparison.Ordinal);
        Assert.Contains("view-distance=6", content, StringComparison.Ordinal);
        Assert.Contains("simulation-distance=8", content, StringComparison.Ordinal);
        Assert.Contains("entity-broadcast-range-percentage=75", content, StringComparison.Ordinal);
        Assert.Contains("network-compression-threshold=128", content, StringComparison.Ordinal);
        Assert.Contains("level-name=Streamer World", content, StringComparison.Ordinal);
        Assert.Contains("server-port=25570", content, StringComparison.Ordinal);
        Assert.Contains("rcon.port=25580", content, StringComparison.Ordinal);
        Assert.Contains(@"rcon.password=secret\:password", content, StringComparison.Ordinal);
        Assert.Contains("max-players=7", content, StringComparison.Ordinal);
        Assert.Contains("online-mode=false", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\npvp=", "\n" + content, StringComparison.Ordinal);
        Assert.Contains("difficulty=hard", content, StringComparison.Ordinal);
        Assert.Contains("hardcore=false", content, StringComparison.Ordinal);
        Assert.DoesNotContain("server-port=12345", content, StringComparison.Ordinal);
        foreach (string key in new[] { "allow-nether", "spawn-monsters", "enable-command-block" })
            Assert.DoesNotContain("\n" + key + "=", "\n" + content, StringComparison.Ordinal);
        int editableHeader = content.IndexOf("# THE SETTINGS BELOW ARE NOT MANAGED BY TWITCHCRAFT", StringComparison.Ordinal);
        int managedHeader = content.IndexOf("# THE SETTINGS BELOW ARE MANAGED BY TWITCHCRAFT", StringComparison.Ordinal);
        int gameplayHeader = content.IndexOf("# GAMEPLAY SETTINGS", StringComparison.Ordinal);
        int minecraftServerHeader = content.IndexOf("# MINECRAFT SERVER SETTINGS", StringComparison.Ordinal);
        int startupHeader = content.IndexOf("# SERVER STARTUP & CONNECTION", StringComparison.Ordinal);

        Assert.True(editableHeader >= 0);
        Assert.True(editableHeader < managedHeader);
        Assert.True(managedHeader < gameplayHeader);
        Assert.True(gameplayHeader < minecraftServerHeader);
        Assert.True(minecraftServerHeader < startupHeader);

        foreach (string key in new[] { "a-custom", "custom-setting", "escaped-value", "gamemode", "level-name", "z-custom" })
            AssertPropertyInSection(content, key, editableHeader, managedHeader);

        foreach (string key in new[] { "difficulty", "hardcore" })
            AssertPropertyInSection(content, key, gameplayHeader, minecraftServerHeader);

        foreach (string key in new[] { "view-distance", "simulation-distance", "entity-broadcast-range-percentage", "network-compression-threshold" })
            AssertPropertyInSection(content, key, minecraftServerHeader, startupHeader);

        foreach (string key in new[] { "max-players", "motd", "online-mode", "server-ip", "server-port", "enable-query", "query.port", "enable-rcon", "rcon.port", "rcon.password" })
            AssertPropertyInSection(content, key, startupHeader, content.Length);

        Assert.True(content.IndexOf("a-custom=first", StringComparison.Ordinal) < content.IndexOf("z-custom=last", StringComparison.Ordinal));
        Assert.True(content.IndexOf("enable-query=", StringComparison.Ordinal) < content.IndexOf("rcon.port=", StringComparison.Ordinal));
        Assert.Equal("eula=true", File.ReadAllText(Path.Combine(directory.Path, "eula.txt")));
        Assert.Equal("Streamer World", ServerPropertyEditor.GetLevelName(config));
    }

    private static void AssertPropertyInSection(string content, string key, int sectionStart, int sectionEnd)
    {
        string propertyPrefix = "\n" + key + "=";
        int index = content.IndexOf(propertyPrefix, StringComparison.Ordinal);
        Assert.True(index > sectionStart && index < sectionEnd, $"Expected '{key}' in the requested server.properties section.");
        Assert.Equal(index, content.LastIndexOf(propertyPrefix, StringComparison.Ordinal));
    }

}
