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
    public async Task FollowRewardAmount_DefaultsTo50AndAppliesConfiguredValue()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            Assert.Equal(StartingProfile.DefaultFollowRewardAmount, runtime.FollowRewardAmount);
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

    [Fact]
    public void RejectsWrongSubscriptionOrMalformedFollow()
    {
        JObject wrongType = JObject.Parse("""
            {
              "metadata": { "subscription_type": "channel.subscribe" },
              "payload": { "event": { "user_id": "123456", "user_login": "viewer", "followed_at": "2026-08-27T01:02:03Z" } }
            }
            """);
        JObject malformedFollow = JObject.Parse("""
            {
              "metadata": { "subscription_type": "channel.follow" },
              "payload": { "event": { "user_id": "", "user_login": "viewer", "followed_at": "2026-08-27T01:02:03Z" } }
            }
            """);

        Assert.False(MainHandler.TryParseFollow(wrongType, out _));
        Assert.False(MainHandler.TryParseFollow(malformedFollow, out _));

        malformedFollow["payload"]!["event"]!["user_id"] = "123456";
        malformedFollow["payload"]!["event"]!["followed_at"] = "bad";
        Assert.False(MainHandler.TryParseFollow(malformedFollow, out _));
    }
}
