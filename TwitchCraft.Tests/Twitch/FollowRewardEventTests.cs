using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class FollowRewardEventTests
{
    [Fact]
    public async Task FollowRewardAmount_DefaultsTo100AndAppliesConfiguredValue()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(100, runtime.FollowRewardAmount);
            TwitchCraftConfig config = new();
            config.Settings.FollowRewardAmount = 250;
            await runtime.ApplySettingsAsync(config);
            Assert.Equal(250, runtime.FollowRewardAmount);
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public void ParsesChannelFollowV2Notification()
    {
        JObject message = JObject.Parse("""
            {
              "metadata": {
                "message_type": "notification",
                "subscription_type": "channel.follow"
              },
              "payload": {
                "subscription": { "type": "channel.follow" },
                "event": {
                  "user_id": "123456",
                  "user_login": "RandomDudeReincarnatedX3",
                  "followed_at": "2026-08-27T01:02:03.456Z"
                }
              }
            }
            """);

        Assert.True(MainHandler.TryParseFollow(message, out MainHandler.FollowNotification notification));
        Assert.Equal("123456", notification.UserID);
        Assert.Equal("randomdudereincarnatedx3", notification.UserLogin);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T01:02:03.456Z", System.Globalization.CultureInfo.InvariantCulture), notification.FollowedAt);
    }

    [Theory]
    [InlineData("channel.subscribe")]
    [InlineData("")]
    public void RejectsOtherSubscriptionTypes(string subscriptionType)
    {
        JObject message = CreateFollowMessage();
        message["metadata"]!["subscription_type"] = subscriptionType;

        Assert.False(MainHandler.TryParseFollow(message, out _));
    }

    [Theory]
    [InlineData("user_id", "")]
    [InlineData("user_id", null)]
    [InlineData("user_login", " ")]
    [InlineData("followed_at", "bad")]
    [InlineData("followed_at", null)]
    public void RejectsFollowWhenAnyRequiredFieldIsMissingOrInvalid(string field, string? value)
    {
        JObject message = CreateFollowMessage();
        message["payload"]!["event"]![field] = value;

        Assert.False(MainHandler.TryParseFollow(message, out MainHandler.FollowNotification notification));
        Assert.Equal(default, notification);
    }

    [Fact]
    public void ParsesSubscriptionFallbackAndNormalizesStringTimestampToUtc()
    {
        JObject message = CreateFollowMessage();
        message.Remove("metadata");
        message["payload"]!["subscription"] = new JObject { ["type"] = "channel.follow" };

        Assert.True(MainHandler.TryParseFollow(message, out MainHandler.FollowNotification notification));
        Assert.Equal("123", notification.UserID);
        Assert.Equal("viewer", notification.UserLogin);
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 1, 2, 3, TimeSpan.Zero), notification.FollowedAt);
        Assert.Equal(TimeSpan.Zero, notification.FollowedAt.Offset);
    }

    private static JObject CreateFollowMessage() => new()
    {
        ["metadata"] = new JObject { ["subscription_type"] = "channel.follow" },
        ["payload"] = new JObject
        {
            ["event"] = new JObject
            {
                ["user_id"] = " 123 ",
                ["user_login"] = " Viewer ",
                ["followed_at"] = "2026-08-27T03:02:03+02:00"
            }
        }
    };
}
