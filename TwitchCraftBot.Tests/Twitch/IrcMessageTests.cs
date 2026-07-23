using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Twitch;

public sealed class IrcMessageTests
{
    [Fact]
    public void TryParse_ExtractsModeratorBitsSenderAndMessage()
    {
        const string line = "@badges=moderator/1;color=#fff;mod=1;bits=250 :SomeUser!someuser@host PRIVMSG #channel :!Heal Player";

        bool result = IRCMessage.TryParse(line, out IRCMessage? message);

        Assert.True(result);
        Assert.NotNull(message);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("someuser", message.SenderLogin);
        Assert.Equal("!Heal Player", message.Trailing);
        Assert.Equal(250, message.Bits);
        Assert.True(message.IsModerator);
    }

    [Fact]
    public void TryParse_HandlesPingWithoutSender()
    {
        bool result = IRCMessage.TryParse("PING :tmi.twitch.tv", out IRCMessage? message);

        Assert.True(result);
        Assert.NotNull(message);
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
        Assert.False(IRCMessage.TryParse(line, out _));
    }
}
