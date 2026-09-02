using Newtonsoft.Json.Linq;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class FollowRewardEventTests
{
    [Fact]
    public void FollowRewardIsOneHundredTokens()
    {
        Assert.Equal(100, BotMainHandler.DefaultFollowRewardAmount);
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

        Assert.True(BotMainHandler.TryParseFollow(message, out BotMainHandler.FollowNotification notification));
        Assert.Equal("123456", notification.UserId);
        Assert.Equal("randomdudereincarnatedx3", notification.UserLogin);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T01:02:03.456Z", System.Globalization.CultureInfo.InvariantCulture), notification.FollowedAt);
    }

    [Fact]
    public void RejectsWrongSubscriptionOrMalformedFollow()
    {
        JObject wrongType = JObject.Parse("""
            { "metadata": { "subscription_type": "channel.subscribe" }, "payload": { "event": {} } }
            """);
        JObject missingIdentity = JObject.Parse("""
            {
              "metadata": { "subscription_type": "channel.follow" },
              "payload": { "event": { "user_id": "", "user_login": "viewer", "followed_at": "bad" } }
            }
            """);

        Assert.False(BotMainHandler.TryParseFollow(wrongType, out _));
        Assert.False(BotMainHandler.TryParseFollow(missingIdentity, out _));
    }
}
