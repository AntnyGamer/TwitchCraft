using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class StartupAndEligibilityTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Eligibility_UnrecordedViewerUsesActivityRequirement(bool requireActivity, bool expectedEligible)
    {
        using RuntimeTestScope scope = await RuntimeTestScope.CreateAsync(settings => settings.PassiveRewardsRequireActivity = requireActivity);

        Assert.Equal(expectedEligible, scope.Runtime.IsRewardEligibleNoLock("quietviewer", 1_000));
    }

    [Fact]
    public async Task RecordedActivity_UsesNormalizedTwitchIdentity()
    {
        using RuntimeTestScope scope = await RuntimeTestScope.CreateAsync(settings =>
        {
            settings.PassiveRewardsRequireActivity = true;
            settings.PassiveActivityWindowMinutes = 1;
        });

        scope.Runtime.RecordChatActivity("  @Viewer_Name  ", 1_000);

        Assert.True(scope.Runtime.IsRewardEligibleNoLock("viewer_name", 1_060));
    }

    [Fact]
    public async Task LaterChatActivity_ExtendsEligibilityWindow()
    {
        using RuntimeTestScope scope = await RuntimeTestScope.CreateAsync(settings =>
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

}
