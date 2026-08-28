using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class SelectedSettingsRuntimeTests
{
    [Fact]
    public async Task ApplySavedConfig_ClonesNestedSettingsInsteadOfAliasingCallerState()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);
        BotConfig config = new();
        config.Settings.MaximumTokenBalance = 100;
        config.Settings.CommandCustomizations["heal"] = new CommandCustomization
        {
            Enabled = false,
            CooldownSeconds = 5
        };

        try
        {
            await runtime.ApplySavedConfigAsync(config);

            config.Settings.MaximumTokenBalance = 999;
            config.Settings.CommandCustomizations["heal"].Enabled = true;
            config.Settings.CommandCustomizations["heal"].CooldownSeconds = null;
            config.Settings.CommandCustomizations["lightning"] = new CommandCustomization { Enabled = false };

            Assert.Equal(100, runtime.MaximumTokenBalance);
            Assert.False(runtime.IsCommandEnabled("heal"));
            Assert.True(runtime.HasCustomCommandCooldown("heal"));
            Assert.True(runtime.IsCommandEnabled("lightning"));
        }
        finally
        {
            runtime.CloseTokenStoreConnection();
        }
    }

    [Fact]
    public async Task SelectedSettings_ApplyLiveWithoutRestart()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);
        BotConfig config = new();
        config.Twitch.StreamerName = "streamer";
        config.Settings.MaximumTokenBalance = 100;
        config.Settings.AllowAllPlayerTarget = false;
        config.Settings.AllowRandomPlayerTarget = false;
        config.Settings.ChannelCommandLimitPerMinute = 2;
        config.Settings.PassiveTokensPerPayout = 5;
        config.Settings.PassiveTokenPayoutMinimumSeconds = 60;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 60;
        config.Settings.PassiveRewardsRequireRecentChat = true;

        await runtime.ApplySavedConfigAsync(config);

        Assert.Equal(100, runtime.MaximumTokenBalance);
        Assert.False(runtime.AllowAllPlayerTarget);
        Assert.False(runtime.AllowRandomPlayerTarget);
        Assert.Equal(5, runtime.PassiveTokensPerPayout);
        Assert.Equal(60, runtime.GetNextPassivePayoutDelaySeconds());
        Assert.True(runtime.TryConsumeChannelCommandSlot(100));
        Assert.True(runtime.TryConsumeChannelCommandSlot(101));
        Assert.False(runtime.TryConsumeChannelCommandSlot(102));

        runtime.RecordViewerChatActivity("viewer", 1000);
        Assert.True(runtime.IsViewerPassiveRewardEligibleNoLock("viewer", 1599));
        Assert.False(runtime.IsViewerPassiveRewardEligibleNoLock("viewer", 1601));
        runtime.CloseTokenStoreConnection();
    }

    [Fact]
    public async Task PassivePayoutRange_AcceptsCustomSecondsAndChoosesEachDelayInsideTheRange()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);
        BotConfig config = new();
        config.Settings.PassiveTokenPayoutMinimumSeconds = 37;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 41;

        await runtime.ApplySavedConfigAsync(config);

        HashSet<int> observed = [];
        for (int i = 0; i < 100; i++)
        {
            int delay = runtime.GetNextPassivePayoutDelaySeconds();
            Assert.InRange(delay, 37, 41);
            observed.Add(delay);
        }
        Assert.True(observed.Count > 1);
        runtime.CloseTokenStoreConnection();
    }

    [Fact]
    public async Task PerformanceAndCommandSettings_ApplyLiveAndRemainPerViewer()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"),
            initializeApplicationState: false);
        BotConfig config = new();
        config.Settings.ViewerCommandLimitPerMinute = 2;
        config.Settings.PassiveRewardsRequireRecentChat = true;
        config.Settings.PassiveRecentChatWindowMinutes = 2;
        config.Settings.MinecraftRelayMessagesPerSecond = 2;
        config.Settings.LowResourceModeEnabled = true;
        config.Settings.MaxVisibleTwitchLogLines = 500;
        config.Settings.MaxVisibleMinecraftLogLines = 1000;
        config.Settings.ViewerRosterRefreshIntervalSeconds = 30;
        config.Settings.MaxGameplayCommandQueue = 100;
        config.Settings.RCONTimeoutSeconds = 15;
        config.Settings.GracefulShutdownTimeoutSeconds = 30;
        config.Settings.CommandCustomizations["lightning"] = new CommandCustomization { Enabled = false };
        config.Settings.CommandCustomizations["heal"] = new CommandCustomization { CooldownSeconds = 5 };

        await runtime.ApplySavedConfigAsync(config);

        long now = DateTime.UtcNow.Ticks;
        Assert.True(runtime.TryConsumeViewerCommandSlot("viewer", now));
        Assert.True(runtime.TryConsumeViewerCommandSlot("viewer", now + 1));
        Assert.False(runtime.TryConsumeViewerCommandSlot("viewer", now + 2));
        Assert.True(runtime.TryConsumeViewerCommandSlot("differentviewer", now + 2));
        Assert.True(runtime.TryConsumeMinecraftRelaySlot(now));
        Assert.True(runtime.TryConsumeMinecraftRelaySlot(now + 1));
        Assert.False(runtime.TryConsumeMinecraftRelaySlot(now + 2));
        Assert.False(runtime.IsCommandEnabled("lightning"));
        Assert.True(runtime.IsCommandEnabled("heal"));
        Assert.True(runtime.HasCustomCommandCooldown("heal"));
        Assert.False(runtime.HasCustomCommandCooldown("lightning"));
        Assert.Equal(100, runtime.MaxVisibleTwitchLogLines);
        Assert.Equal(100, runtime.MaxVisibleMinecraftLogLines);
        Assert.Equal(60, runtime.ViewerRosterRefreshIntervalSeconds);
        Assert.Equal(35, runtime.MaxGameplayCommandQueue);
        Assert.Equal(TimeSpan.FromSeconds(15), runtime.RCONTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), runtime.GracefulShutdownTimeout);
        Assert.Contains("tiny", runtime.RegisteredCommandNames);

        runtime.RecordViewerChatActivity("recent", 1000);
        Assert.True(runtime.IsViewerPassiveRewardEligibleNoLock("recent", 1120));
        Assert.False(runtime.IsViewerPassiveRewardEligibleNoLock("recent", 1121));
        runtime.CloseTokenStoreConnection();
    }
}
