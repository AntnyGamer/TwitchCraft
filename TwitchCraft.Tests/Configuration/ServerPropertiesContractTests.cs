using System;
using System.IO;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

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

        TwitchCraftConfig config = new()
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
                MultiplayerPvPEnabled = true,
                RequireOnlineMode = false,
                HardcoreEnabled = false,
                Difficulty = "Hard",
                ViewDistance = 6,
                SimulationDistance = 8,
                EntityBroadcastRangePercentage = 75,
                NetworkCompressionThreshold = 128,
                WhitelistEnabled = true
            }
        };

        ServerPropertyEditor.WriteInitialFiles(config);
        string firstWrite = File.ReadAllText(propertiesPath);
        ServerPropertyEditor.WriteInitialFiles(config);
        string content = File.ReadAllText(propertiesPath);
        Assert.Equal(firstWrite, content);
        string[] propertyLines = File.ReadAllLines(propertiesPath);

        Assert.All([
            "a-custom=first", @"custom-setting=keep\=this",
            @"escaped-value=hello\=world\\path", "z-custom=last",
            "view-distance=6", "simulation-distance=8",
            "entity-broadcast-range-percentage=75",
            "network-compression-threshold=128",
            "white-list=true", "enforce-whitelist=true",
            "level-name=Streamer World", "server-port=25570",
            "rcon.port=25580", @"rcon.password=secret\:password",
            "max-players=7", "online-mode=false"
        ], property => Assert.Contains(property, propertyLines));
        Assert.DoesNotContain("\npvp=", "\n" + content, StringComparison.Ordinal);
        Assert.Contains("difficulty=hard", propertyLines);
        Assert.Contains("hardcore=false", propertyLines);
        Assert.DoesNotContain("server-port=12345", content, StringComparison.Ordinal);
        Assert.All(["allow-nether", "spawn-monsters", "enable-command-block"], key =>
            Assert.DoesNotContain("\n" + key + "=", "\n" + content, StringComparison.Ordinal));
        Assert.Contains("# THESE SETTINGS CAN BE CHANGED DIRECTLY IN THIS FILE.", content, StringComparison.Ordinal);
        Assert.Contains("# TWITCHCRAFT WILL PRESERVE YOUR CHANGES.", content, StringComparison.Ordinal);
        Assert.Contains("# THE SETTINGS BELOW ARE MANAGED BY TWITCHCRAFT.", content, StringComparison.Ordinal);
        Assert.Contains("# IT IS RECOMMENDED TO CHANGE THEM IN TWITCHCRAFT AS DIRECT EDITS HERE MAY BE REPLACED.", content, StringComparison.Ordinal);
        Assert.Contains("# IF LOCALLY HOSTING, CHANGE IT IN TWITCHCRAFT: SETTINGS > DANGEROUS", content, StringComparison.Ordinal);
        int editableHeader = content.IndexOf("# THESE SETTINGS CAN BE CHANGED DIRECTLY IN THIS FILE.", StringComparison.Ordinal);
        int managedHeader = content.IndexOf("# THE SETTINGS BELOW ARE MANAGED BY TWITCHCRAFT.", StringComparison.Ordinal);
        int RCONPasswordHeader = content.IndexOf("# RCON PASSWORD - MANAGED BY TWITCHCRAFT - KEEP SECURE", StringComparison.Ordinal);

        Assert.True(editableHeader >= 0 && editableHeader < managedHeader && managedHeader < RCONPasswordHeader);

        Assert.All(["a-custom", "custom-setting", "escaped-value", "gamemode", "level-name", "z-custom"], key =>
            AssertPropertyInSection(content, key, editableHeader, managedHeader));

        Assert.All([
                "difficulty", "hardcore", "view-distance", "simulation-distance", "entity-broadcast-range-percentage", "network-compression-threshold",
                "white-list", "enforce-whitelist", "max-players", "motd", "online-mode", "server-ip", "server-port", "enable-query", "query.port", "enable-rcon", "rcon.port"
            ], key => AssertPropertyInSection(content, key, managedHeader, RCONPasswordHeader));

        AssertPropertyInSection(content, "rcon.password", RCONPasswordHeader, content.Length);

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
