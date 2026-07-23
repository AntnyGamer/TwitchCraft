using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public void NormalizeForRuntime_RepairsInvalidValuesWithoutChangingValidIdentityData()
    {
        BotConfig config = new()
        {
            Server = new ServerConfig
            {
                BindIP = "not an ip",
                Port = 0,
                MaxPlayers = 0,
                MemoryMinGB = 16,
                MemoryMaxGB = 4,
                RemoteHost = "server.example:25580",
                RCON = new RCONConfig { Port = 70000, Password = "  secret  " }
            },
            Twitch = new TwitchConfig
            {
                StreamerName = "  Streamer  ",
                BotToken = " OAuth: token-value "
            },
            Identity = new BotIdentityConfig { StreamerMinecraftName = "  Player  " },
            Settings = new StartingProfile
            {
                Difficulty = "unsupported",
                MinigameCooldown = 1,
                GlobalGameCommandCooldownSeconds = 0
            }
        };

        ConfigurationStore.NormalizeForRuntime(config);

        Assert.Equal("127.0.0.1", config.Server.BindIP);
        Assert.Equal(25565, config.Server.Port);
        Assert.Equal(1, config.Server.MaxPlayers);
        Assert.Equal(4, config.Server.MemoryMinGB);
        Assert.Equal(4, config.Server.MemoryMaxGB);
        Assert.Equal("server.example", config.Server.RemoteHost);
        Assert.Equal(25580, config.Server.RCON.Port);
        Assert.Equal("secret", config.Server.RCON.Password);
        Assert.Equal("Streamer", config.Twitch.StreamerName);
        Assert.Equal("token-value", config.Twitch.BotToken);
        Assert.Equal("Player", config.Identity.StreamerMinecraftName);
        Assert.Equal("Medium", config.Settings.Difficulty);
        Assert.Equal(15, config.Settings.MinigameCooldown);
        Assert.Equal(10.0, config.Settings.GlobalGameCommandCooldownSeconds);
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1")]
    [InlineData(" 127.0.0.1 ", "127.0.0.1")]
    [InlineData("::1", "::1")]
    public void NormalizeBindIp_NormalizesKnownValidForms(string value, string expected)
    {
        Assert.Equal(expected, ConfigurationStore.NormalizeBindIP(value));
        Assert.True(ConfigurationStore.IsValidBindIP(value));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com:25575")]
    [InlineData("[2001:db8::1]:25575")]
    [InlineData("127.0.0.1")]
    public void IsValidRemoteHost_AcceptsHostsAndOptionalPorts(string value)
    {
        Assert.True(ConfigurationStore.IsValidRemoteHost(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("host name")]
    [InlineData("host/path")]
    [InlineData("host\\path")]
    [InlineData("example.com:70000")]
    public void IsValidRemoteHost_RejectsUnsafeOrInvalidValues(string value)
    {
        Assert.False(ConfigurationStore.IsValidRemoteHost(value));
    }

    [Fact]
    public void TryNormalizeRconPassword_RejectsNewlines()
    {
        bool result = ConfigurationStore.TryNormalizeRconPassword(" first\nsecond ", out string password);

        Assert.False(result);
        Assert.Equal("first\nsecond", password);
    }
}
