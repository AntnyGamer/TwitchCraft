using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class LiveSettingsApplicationTests
{
    [Fact]
    public async Task ApplySavedConfig_ClonesNestedSettingsInsteadOfAliasingCallerState()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        BotConfig config = new();
        config.Settings.MaximumTokenBalance = 100;
        config.Settings.CommandCustomizations["heal"] = new CommandCustomization
        {
            Enabled = false,
            CooldownSeconds = 5,
            GlobalCooldownSeconds = 2.5
        };

        try
        {
            await runtime.ApplySettingsAsync(config);

            config.Settings.MaximumTokenBalance = 999;
            config.Settings.CommandCustomizations["heal"].Enabled = true;
            config.Settings.CommandCustomizations["heal"].CooldownSeconds = null;
            config.Settings.CommandCustomizations["heal"].GlobalCooldownSeconds = null;
            config.Settings.CommandCustomizations["lightning"] = new CommandCustomization { Enabled = false };

            Assert.Equal(100, runtime.MaximumTokenBalance);
            Assert.True(runtime.HasPerUserCooldownOverride("heal"));
            Assert.True(runtime.HasGlobalCooldownOverride("heal"));
            Assert.False(runtime.HasPerUserCooldownOverride("lightning"));
            Assert.False(runtime.HasGlobalCooldownOverride("lightning"));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task SelectedSettings_ApplyLiveWithoutRestart()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        BotConfig config = new();
        config.Twitch.StreamerName = "streamer";
        config.Settings.MaximumTokenBalance = 100;
        config.Settings.AllowAllPlayerTarget = false;
        config.Settings.AllowRandomPlayerTarget = false;
        config.Settings.ChannelCommandLimitPerMinute = 2;
        config.Settings.PassiveTokensPerPayout = 5;
        config.Settings.PassiveTokenPayoutMinimumSeconds = 60;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 60;
        config.Settings.PassiveRewardsRequireActivity = true;

        try
        {
            await runtime.ApplySettingsAsync(config);

            Assert.Equal(100, runtime.MaximumTokenBalance);
            Assert.False(runtime.AllowAllPlayerTarget);
            Assert.False(runtime.AllowRandomPlayerTarget);
            Assert.Equal(5, runtime.PassiveTokensPerPayout);
            Assert.Equal(60, runtime.GetPayoutDelay());
            Assert.True(runtime.TryUseCommandSlots(string.Empty, out _, 100));
            Assert.True(runtime.TryUseCommandSlots(string.Empty, out _, 101));
            Assert.False(runtime.TryUseCommandSlots(string.Empty, out _, 102));

            runtime.RecordChatActivity("viewer", 1000);
            Assert.True(runtime.IsRewardEligibleNoLock("viewer", 1599));
            Assert.False(runtime.IsRewardEligibleNoLock("viewer", 1601));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task PassivePayoutRange_AcceptsCustomSecondsAndChoosesEachDelayInsideTheRange()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        BotConfig config = new();
        config.Settings.PassiveTokenPayoutMinimumSeconds = 37;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 41;

        try
        {
            await runtime.ApplySettingsAsync(config);

            HashSet<int> observed = [];
            for (int i = 0; i < 100; i++)
            {
                int delay = runtime.GetPayoutDelay();
                Assert.InRange(delay, 37, 41);
                observed.Add(delay);
            }
            Assert.True(observed.Count > 1);
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task PerformanceAndCommandSettings_ApplyLiveAndRemainPerViewer()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        BotConfig config = new();
        config.Settings.ViewerCommandLimitPerMinute = 2;
        config.Settings.PassiveRewardsRequireActivity = true;
        config.Settings.PassiveActivityWindowMinutes = 2;
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
        config.Settings.CommandCustomizations["tiny"] = new CommandCustomization { GlobalCooldownSeconds = 2.5 };

        try
        {
            await runtime.ApplySettingsAsync(config);

            long now = DateTime.UtcNow.Ticks;
            Assert.True(runtime.TryUseCommandSlots("viewer", out _, now));
            Assert.True(runtime.TryUseCommandSlots("viewer", out _, now + 1));
            Assert.False(runtime.TryUseCommandSlots("viewer", out _, now + 2));
            Assert.True(runtime.TryUseCommandSlots("differentviewer", out _, now + 2));
            Assert.True(runtime.TryUseRelaySlot(now));
            Assert.True(runtime.TryUseRelaySlot(now + 1));
            Assert.False(runtime.TryUseRelaySlot(now + 2));
            Assert.True(runtime.HasPerUserCooldownOverride("heal"));
            Assert.False(runtime.HasPerUserCooldownOverride("lightning"));
            Assert.True(runtime.HasGlobalCooldownOverride("tiny"));
            Assert.False(runtime.HasGlobalCooldownOverride("heal"));
            Assert.Equal(100, runtime.MaxVisibleTwitchLogLines);
            Assert.Equal(100, runtime.MaxVisibleMinecraftLogLines);
            Assert.Equal(60, runtime.ViewerRosterRefreshIntervalSeconds);
            Assert.Equal(35, runtime.MaxGameplayCommandQueue);
            Assert.Equal(TimeSpan.FromSeconds(15), runtime.RCONTimeout);
            Assert.Equal(TimeSpan.FromSeconds(30), runtime.GracefulShutdownTimeout);
            Assert.Contains("tiny", runtime.RegisteredCommandNames);

            runtime.RecordChatActivity("recent", 1000);
            Assert.True(runtime.IsRewardEligibleNoLock("recent", 1120));
            Assert.False(runtime.IsRewardEligibleNoLock("recent", 1121));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
