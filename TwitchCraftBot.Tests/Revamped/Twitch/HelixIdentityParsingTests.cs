using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class HelixIdentityParsingTests
{
    [Fact]
    public void ParseRosterPage_ReadsNormalizedLoginsAndCursor()
    {
        const string json = """
            {
              "data": [
                { "user_id": "1", "user_login": "Alice", "user_name": "Alice" },
                { "user_id": "2", "user_login": " BOB ", "user_name": "Bob" }
              ],
              "pagination": { "cursor": "next-page" }
            }
            """;
        List<string> viewers = [];

        string? cursor = BotMainHandler.ParseRosterPage(json, viewers);

        Assert.Equal("next-page", cursor);
        Assert.Equal(["alice", "bob"], viewers);
    }

    [Fact]
    public void ParseRosterPage_IgnoresUnknownNestedDataAndEmptyLogins()
    {
        const string json = """
            {
              "metadata": { "nested": [{ "user_login": "not-a-viewer" }] },
              "data": [
                { "user_login": "" },
                { "unrelated": { "user_login": "also-not-a-viewer" } },
                { "user_login": "Charlie" }
              ],
              "pagination": {}
            }
            """;
        List<string> viewers = [];

        string? cursor = BotMainHandler.ParseRosterPage(json, viewers);

        Assert.Null(cursor);
        Assert.Equal(["charlie"], viewers);
    }

    [Fact]
    public void ParseUserIds_MatchesUsersByLoginRegardlessOfResponseOrder()
    {
        const string json = """
            {
              "data": [
                { "id": "streamer-id", "login": "Streamer" },
                { "id": "bot-id", "login": "BotAccount" }
              ]
            }
            """;

        string[] ids = BotMainHandler.ParseUserIds(json, "botaccount", "streamer");

        Assert.Equal(["bot-id", "streamer-id"], ids);
    }

    [Theory]
    [InlineData("""{"data":[{"id":"bot-id","login":"botaccount"}]}""")]
    [InlineData("""{"data":[{"id":"streamer-id","login":"streamer"}]}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"unrelated":true}""")]
    public void ParseUserIds_ReturnsEmptyWhenEitherUserIsMissing(string json)
    {
        Assert.Empty(BotMainHandler.ParseUserIds(json, "botaccount", "streamer"));
    }

    [Theory]
    [InlineData("""{"client_id":"client","login":" BotAccount ","user_id":"42"}""", "botaccount")]
    [InlineData("""{"login":null}""", "")]
    [InlineData("""{"user_id":"42"}""", "")]
    public void ParseLogin_ReturnsNormalizedLoginOrEmpty(string json, string expected)
    {
        Assert.Equal(expected, BotMainHandler.ParseLogin(json));
    }
}
