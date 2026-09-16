using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class StartupAndEligibilityTests
{
    [Fact]
    public async Task Eligibility_AcceptsViewerWhenActivityNotRequired()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings => settings.PassiveRewardsRequireActivity = false);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("quietviewer", 1_000_000));
    }

    [Fact]
    public async Task Eligibility_RejectsInactiveViewerWhenActivityRequired()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings => settings.PassiveRewardsRequireActivity = true);

        Assert.False(scope.Runtime.IsRewardEligibleNoLock("quietviewer", 1_000));
    }

    [Fact]
    public async Task RecordedActivity_UsesNormalizedTwitchIdentity()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireActivity = true;
            settings.PassiveActivityWindowMinutes = 1;
        });

        scope.Runtime.RecordChatActivity("  @Viewer_Name  ", 1_000);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer_name", 1_060));
    }

    [Fact]
    public async Task Eligibility_IncludesExactConfiguredActivityWindowBoundary()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireActivity = true;
            settings.PassiveActivityWindowMinutes = 2;
        });
        scope.Runtime.RecordChatActivity("viewer", 1_000);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_120));
    }

    [Fact]
    public async Task LaterChatActivitySupersedesOldTimestampAndExtendsEligibility()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireActivity = true;
            settings.PassiveActivityWindowMinutes = 1;
        });
        scope.Runtime.RecordChatActivity("viewer", 1_000);
        scope.Runtime.RecordChatActivity("VIEWER", 1_100);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_160));
        Assert.False(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_161));
    }

    [Fact]
    public async Task StartSession_MissingTokenFailsAndReleasesLifecycleGate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Twitch.ClientID = TwitchOAuthAuthorizer.ApplicationClientID;
        config.Twitch.BotToken = string.Empty;
        config.Twitch.RefreshToken = string.Empty;
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await runtime.ApplySettingsAsync(config);

            InvalidOperationException first = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartSessionAsync());
            InvalidOperationException retry = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await runtime.StartSessionAsync().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Contains("token is missing", first.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("token is missing", retry.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    private sealed class RuntimeScope : IDisposable
    {
        private readonly TemporaryDirectory _directory;

        private RuntimeScope(TemporaryDirectory directory, MainHandler runtime)
        {
            _directory = directory;
            Runtime = runtime;
        }

        internal MainHandler Runtime { get; }

        internal static async Task<RuntimeScope> CreateAsync(Action<StartingProfile> configure)
        {
            TemporaryDirectory directory = new();
            MainHandler runtime = new(
                new AppShellViewModel(),
                Path.Combine(directory.Path, "viewer_tokens.db"));
            TwitchCraftConfig config = new();
            configure(config.Settings);
            await runtime.ApplySettingsAsync(config);
            return new RuntimeScope(directory, runtime);
        }

        public void Dispose()
        {
            Runtime.Tokens.Close();
            _directory.Dispose();
        }
    }
}
