using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class StartupAndEligibilityRegressionTests
{
    [Fact]
    public async Task Eligibility_WhenRecentChatIsNotRequiredAcceptsRosterViewerWithoutActivity()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings => settings.PassiveRewardsRequireRecentChat = false);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("quietviewer", 1_000_000));
    }

    [Fact]
    public async Task Eligibility_WhenRecentChatIsRequiredRejectsViewerWithoutActivity()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings => settings.PassiveRewardsRequireRecentChat = true);

        Assert.False(scope.Runtime.IsRewardEligibleNoLock("quietviewer", 1_000));
    }

    [Fact]
    public async Task RecordedActivity_UsesNormalizedTwitchIdentity()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireRecentChat = true;
            settings.PassiveRecentChatWindowMinutes = 1;
        });

        scope.Runtime.RecordChatActivity("  @Viewer_Name  ", 1_000);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer_name", 1_060));
    }

    [Fact]
    public async Task Eligibility_IncludesExactConfiguredActivityWindowBoundary()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireRecentChat = true;
            settings.PassiveRecentChatWindowMinutes = 2;
        });
        scope.Runtime.RecordChatActivity("viewer", 1_000);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_120));
    }

    [Fact]
    public async Task LaterChatActivitySupersedesOldTimestampAndExtendsEligibility()
    {
        using RuntimeScope scope = await RuntimeScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireRecentChat = true;
            settings.PassiveRecentChatWindowMinutes = 1;
        });
        scope.Runtime.RecordChatActivity("viewer", 1_000);
        scope.Runtime.RecordChatActivity("VIEWER", 1_100);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_160));
        Assert.False(scope.Runtime.IsRewardEligibleNoLock("viewer", 1_161));
    }

    [Fact]
    public async Task StartSession_MissingTokenFailsWithoutOpeningAuthorizationAndReleasesLifecycleGate()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Twitch.ClientID = TwitchOAuthAuthorizer.TwitchCraftClientId;
        config.Twitch.BotToken = string.Empty;
        config.Twitch.RefreshToken = string.Empty;
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

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

        private RuntimeScope(TemporaryDirectory directory, BotMainHandler runtime)
        {
            _directory = directory;
            Runtime = runtime;
        }

        internal BotMainHandler Runtime { get; }

        internal static async Task<RuntimeScope> CreateAsync(Action<StartingProfile> configure)
        {
            TemporaryDirectory directory = new();
            BotMainHandler runtime = new(
                new AppShellViewModel(),
                Path.Combine(directory.Path, "viewer_tokens.db"));
            BotConfig config = new();
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
