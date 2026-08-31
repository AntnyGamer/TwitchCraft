using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class IrcFrameParserTests
{
    [Fact]
    public void TryParse_ExtractsModeratorBitsSenderAndMessage()
    {
        const string line = "@badges=moderator/1;color=#fff;mod=1;bits=250 :SomeUser!someuser@host PRIVMSG #channel :!Heal Player";

        IRCMessage message = new();
        bool result = message.TryParse(line);

        Assert.True(result);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("someuser", message.SenderLogin);
        Assert.Equal("!Heal Player", message.Trailing);
        Assert.Equal(250, message.Bits);
        Assert.True(message.IsModerator);
    }

    [Fact]
    public void TryParse_HandlesPingWithoutSender()
    {
        IRCMessage message = new();
        bool result = message.TryParse("PING :tmi.twitch.tv");

        Assert.True(result);
        Assert.Equal("PING", message.Command);
        Assert.Equal(string.Empty, message.SenderLogin);
        Assert.Equal("tmi.twitch.tv", message.Trailing);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" PRIVMSG #channel :message")]
    [InlineData("@badges=moderator/1")]
    [InlineData(":missing-command-prefix")]
    public void TryParse_RejectsMalformedLines(string line)
    {
        IRCMessage message = new();

        Assert.False(message.TryParse(line));
    }
}
