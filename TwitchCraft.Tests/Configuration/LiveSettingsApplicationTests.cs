using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class LiveSettingsApplicationTests
{
    [Fact]
    public async Task ApplySettings_ClonesNestedSettings()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        TwitchCraftConfig config = new();
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

            Assert.Equal(100, runtime.Tokens.MaximumBalance);
            Assert.True(runtime.Commands.HasPerUserCooldownOverride("heal"));
            Assert.True(runtime.Commands.HasGlobalCooldownOverride("heal"));
            Assert.False(runtime.Commands.HasPerUserCooldownOverride("lightning"));
            Assert.False(runtime.Commands.HasGlobalCooldownOverride("lightning"));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task ApplySettings_UpdatesLive()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        TwitchCraftConfig config = new();
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

            Assert.Equal(100, runtime.Tokens.MaximumBalance);
            Assert.False(runtime.Commands.AllowAllPlayerTarget);
            Assert.False(runtime.Commands.AllowRandomPlayerTarget);
            Assert.Equal(5, runtime.PassiveTokensPerPayout);
            Assert.Equal(60, runtime.GetPassivePayoutDelay());
            Assert.True(runtime.Commands.TryUseCommandSlots(string.Empty, out _, 100));
            Assert.True(runtime.Commands.TryUseCommandSlots(string.Empty, out _, 101));
            Assert.False(runtime.Commands.TryUseCommandSlots(string.Empty, out _, 102));

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
    public async Task GetPassivePayoutDelay_UsesConfiguredRange()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        TwitchCraftConfig config = new();
        config.Settings.PassiveTokenPayoutMinimumSeconds = 37;
        config.Settings.PassiveTokenPayoutMaximumSeconds = 41;

        try
        {
            await runtime.ApplySettingsAsync(config);

            for (int i = 0; i < 100; i++)
                Assert.InRange(runtime.GetPassivePayoutDelay(), 37, 41);
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
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        TwitchCraftConfig config = new();
        config.Settings.ViewerCommandLimitPerMinute = 2;
        config.Settings.PassiveRewardsRequireActivity = true;
        config.Settings.PassiveActivityWindowMinutes = 2;
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

            const long now = 10_000;
            Assert.True(runtime.Commands.TryUseCommandSlots("viewer", out _, now));
            Assert.True(runtime.Commands.TryUseCommandSlots("viewer", out _, now + 1));
            Assert.False(runtime.Commands.TryUseCommandSlots("viewer", out _, now + 2));
            Assert.True(runtime.Commands.TryUseCommandSlots("differentviewer", out _, now + 2));
            Assert.True(runtime.Commands.HasPerUserCooldownOverride("heal"));
            Assert.False(runtime.Commands.HasPerUserCooldownOverride("lightning"));
            Assert.True(runtime.Commands.HasGlobalCooldownOverride("tiny"));
            Assert.False(runtime.Commands.HasGlobalCooldownOverride("heal"));
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

    [Fact]
    public async Task ApplySettings_DifficultyChangesReachRunningLocalServer()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken);
        int cursor = scenario.CaptureCommandCursor();

        scenario.Config.Settings.Difficulty = "Hard";
        await scenario.Runtime.ApplySettingsAsync(scenario.Config);
        List<string> hardCommands = await scenario.DrainCommandsAsync(cursor);

        Assert.Contains("difficulty hard", hardCommands);

        cursor = scenario.CaptureCommandCursor();
        scenario.Config.Settings.Difficulty = "Medium";
        await scenario.Runtime.ApplySettingsAsync(scenario.Config);
        List<string> normalCommands = await scenario.DrainCommandsAsync(cursor);

        Assert.Contains("difficulty normal", normalCommands);
    }

    [Fact]
    public async Task ApplySettings_PvpChangesReachSupportedRunningMultiplayerServer()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            multiplayer: true);
        int cursor = scenario.CaptureCommandCursor();

        scenario.Config.Settings.MultiplayerPVPEnabled = true;
        await scenario.Runtime.ApplySettingsAsync(scenario.Config);
        List<string> enabledCommands = await scenario.DrainCommandsAsync(cursor);

        Assert.Contains(enabledCommands, command => command.Contains("pvp true", StringComparison.Ordinal));

        cursor = scenario.CaptureCommandCursor();
        scenario.Config.Settings.MultiplayerPVPEnabled = false;
        await scenario.Runtime.ApplySettingsAsync(scenario.Config);
        List<string> disabledCommands = await scenario.DrainCommandsAsync(cursor);

        Assert.Contains(disabledCommands, command => command.Contains("pvp false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplySettings_PreservesRunningSessionModeSettings()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            multiplayer: true);

        TwitchCraftConfig edited = ConfigurationStore.Clone(scenario.Config);
        edited.Settings.MultiplayerEnabled = false;
        edited.Settings.RemoteControlEnabled = true;
        edited.Settings.RequireOnlineMode = false;
        edited.Settings.AllowRandomPlayerTarget = false;
        await scenario.Runtime.ApplySettingsAsync(edited);

        Assert.True(scenario.Runtime.MultiplayerEnabled);
        Assert.False(scenario.Runtime.RemoteControlEnabled);
        Assert.True(scenario.Runtime.RequireOnlineMode);
        Assert.False(scenario.Runtime.Commands.AllowRandomPlayerTarget);
        Assert.True(scenario.Runtime.MinecraftServerReady);
    }
}
