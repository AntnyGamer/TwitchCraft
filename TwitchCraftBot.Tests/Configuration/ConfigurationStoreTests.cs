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
                BotToken = " OAuth: token-value ",
                RefreshToken = " refresh-value "
            },
            Identity = new BotIdentityConfig { StreamerMinecraftName = "  Player  " },
            Settings = new StartingProfile
            {
                Difficulty = "unsupported",
                MinigameCooldown = 1,
                GlobalGameCommandCooldownSeconds = 0,
                FollowRewardAmount = 0,
                CommandCostMultiplier = double.NaN,
                BotResponseVerbosity = "unsupported",
                CommandPrefix = "   ",
                SecondaryCommandPrefix = "!",
                PassiveTokensPerPayout = 0,
                PassiveTokenPayoutMinimumSeconds = 1,
                PassiveTokenPayoutMaximumSeconds = 901,
                MaximumTokenBalance = -1,
                ChannelCommandLimitPerMinute = 1001,
                ViewerCommandLimitPerMinute = -1,
                PassiveRecentChatWindowMinutes = 500,
                AutomaticBackupIntervalHours = 2,
                AutomaticBackupRetentionCount = 2,
                MaxVisibleTwitchLogLines = 1,
                MaxVisibleMinecraftLogLines = 99999,
                ViewerRosterRefreshIntervalSeconds = 31,
                MaxGameplayCommandQueue = 1,
                RCONTimeoutSeconds = 0,
                GracefulShutdownTimeoutSeconds = 2,
                SQLiteOptimizeIntervalHours = 2,
                ViewDistance = 100,
                SimulationDistance = 1,
                EntityBroadcastRangePercentage = 0,
                NetworkCompressionThreshold = 5000,
                EmptyServerShutdownDelayMinutes = 7,
                MinecraftRelayTextColor = "ultraviolet",
                CommandCustomizations = new Dictionary<string, CommandCustomization>(StringComparer.OrdinalIgnoreCase)
                {
                    [" Heal "] = new() { Enabled = false, CooldownSeconds = 5 },
                    ["bad command"] = new() { Enabled = false },
                    ["lightning"] = new() { CooldownSeconds = 100000 }
                }
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
        Assert.Equal("refresh-value", config.Twitch.RefreshToken);
        Assert.Equal("Player", config.Identity.StreamerMinecraftName);
        Assert.Equal("Medium", config.Settings.Difficulty);
        Assert.Equal(15, config.Settings.MinigameCooldown);
        Assert.Equal(10.0, config.Settings.GlobalGameCommandCooldownSeconds);
        Assert.Equal(100, config.Settings.FollowRewardAmount);
        Assert.Equal(1.0, config.Settings.CommandCostMultiplier);
        Assert.Equal("Normal", config.Settings.BotResponseVerbosity);
        Assert.Equal("!", config.Settings.CommandPrefix);
        Assert.Equal(string.Empty, config.Settings.SecondaryCommandPrefix);
        Assert.Equal(1, config.Settings.PassiveTokensPerPayout);
        Assert.Equal(30, config.Settings.PassiveTokenPayoutMinimumSeconds);
        Assert.Equal(60, config.Settings.PassiveTokenPayoutMaximumSeconds);
        Assert.Equal(0, config.Settings.MaximumTokenBalance);
        Assert.Equal(0, config.Settings.ChannelCommandLimitPerMinute);
        Assert.Equal(0, config.Settings.ViewerCommandLimitPerMinute);
        Assert.Equal(120, config.Settings.PassiveRecentChatWindowMinutes);
        Assert.Equal(24, config.Settings.AutomaticBackupIntervalHours);
        Assert.Equal(StartingProfile.DefaultAutomaticBackupRetentionCount, config.Settings.AutomaticBackupRetentionCount);
        Assert.Equal(250, config.Settings.MaxVisibleTwitchLogLines);
        Assert.Equal(250, config.Settings.MaxVisibleMinecraftLogLines);
        Assert.Equal(30, config.Settings.ViewerRosterRefreshIntervalSeconds);
        Assert.Equal(75, config.Settings.MaxGameplayCommandQueue);
        Assert.Equal(5, config.Settings.RCONTimeoutSeconds);
        Assert.Equal(5, config.Settings.GracefulShutdownTimeoutSeconds);
        Assert.Equal(0, config.Settings.SQLiteOptimizeIntervalHours);
        Assert.Equal(12, config.Settings.ViewDistance);
        Assert.Equal(10, config.Settings.SimulationDistance);
        Assert.Equal(100, config.Settings.EntityBroadcastRangePercentage);
        Assert.Equal(256, config.Settings.NetworkCompressionThreshold);
        Assert.Equal(0, config.Settings.EmptyServerShutdownDelayMinutes);
        Assert.Equal("white", config.Settings.MinecraftRelayTextColor);
        CommandCustomization heal = Assert.Single(config.Settings.CommandCustomizations).Value;
        Assert.False(heal.Enabled);
        Assert.Equal(5, heal.CooldownSeconds);
        Assert.True(config.Settings.CommandCustomizations.ContainsKey("heal"));
    }

    [Fact]
    public void NormalizeForRuntime_OrdersAndPreservesCustomPassivePayoutRange()
    {
        BotConfig config = new();
        config.Settings.PassiveTokenPayoutMinimumSeconds = 487;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 123;

        ConfigurationStore.NormalizeForRuntime(config);

        Assert.Equal(123, config.Settings.PassiveTokenPayoutMinimumSeconds);
        Assert.Equal(487, config.Settings.PassiveTokenPayoutMaximumSeconds);
    }

    [Fact]
    public void NormalizeForRuntime_PreservesValidCustomEconomyCommandAndMaintenanceValues()
    {
        BotConfig config = new();
        config.Settings.PassiveTokensPerPayout = 654_321;
        config.Settings.FollowRewardAmount = 123_456;
        config.Settings.MaximumTokenBalance = 987_654_321;
        config.Settings.ViewerCommandLimitPerMinute = 777;
        config.Settings.ChannelCommandLimitPerMinute = 888;
        config.Settings.CommandCostMultiplier = 0.0;
        config.Settings.AutomaticBackupRetentionCount = 20;
        config.Settings.GracefulShutdownTimeoutSeconds = 60;

        ConfigurationStore.NormalizeForRuntime(config);

        Assert.Equal(654_321, config.Settings.PassiveTokensPerPayout);
        Assert.Equal(123_456, config.Settings.FollowRewardAmount);
        Assert.Equal(987_654_321, config.Settings.MaximumTokenBalance);
        Assert.Equal(777, config.Settings.ViewerCommandLimitPerMinute);
        Assert.Equal(888, config.Settings.ChannelCommandLimitPerMinute);
        Assert.Equal(0.0, config.Settings.CommandCostMultiplier);
        Assert.Equal(20, config.Settings.AutomaticBackupRetentionCount);
        Assert.Equal(60, config.Settings.GracefulShutdownTimeoutSeconds);
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
