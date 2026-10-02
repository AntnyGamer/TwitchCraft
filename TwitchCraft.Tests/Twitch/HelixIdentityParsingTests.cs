using System.Collections.Generic;
using System.Text.Json;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

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

        string? cursor = MainHandler.ParseRosterPage(json, viewers);

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

        string? cursor = MainHandler.ParseRosterPage(json, viewers);

        Assert.Null(cursor);
        Assert.Equal(["charlie"], viewers);
    }

    [Fact]
    public void ParseUserIDs_MatchesUsersByLoginRegardlessOfResponseOrder()
    {
        const string json = """
            {
              "data": [
                { "id": "streamer-id", "login": "Streamer" },
                { "id": "bot-id", "login": "BotAccount" }
              ]
            }
            """;

        string[] IDs = MainHandler.ParseUserIDs(json, "botaccount", "streamer");

        Assert.Equal(["bot-id", "streamer-id"], IDs);
    }

    [Theory]
    [InlineData("""{"data":[{"id":"bot-id","login":"botaccount"}]}""")]
    [InlineData("""{"data":[{"id":"streamer-id","login":"streamer"}]}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"unrelated":true}""")]
    public void ParseUserIDs_ReturnsEmptyWhenEitherUserIsMissing(string json)
    {
        Assert.Empty(MainHandler.ParseUserIDs(json, "botaccount", "streamer"));
    }

    [Theory]
    [InlineData("""{"client_id":"client123","login":" BotAccount ","user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", true, "botaccount")]
    [InlineData("""{"client_id":"client123","login":null,"user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", false, "")]
    [InlineData("""{"client_id":"client123","user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""", false, "")]
    public void TryReadIdentity_NormalizesLoginOrRejectsMissingLogin(string json, bool expectedAccepted, string expectedLogin)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        bool accepted = TwitchOAuthAuthorizer.TryReadIdentity(
            document.RootElement,
            "client123",
            out string login,
            out _);

        Assert.Equal(expectedAccepted, accepted);
        Assert.Equal(expectedLogin, login);
    }
}
