using TwitchCraft_V1;

namespace TwitchCraft.Tests.Twitch;

public sealed class IRCFrameParserTests
{
    [Fact]
    public void TryParse_ExtractsModeratorBitsSenderAndMessage()
    {
        const string line = "@badges=moderator/1;color=#fff;mod=1;bits=250;id=abc-123 :SomeUser!someuser@host PRIVMSG #channel :!Heal Player";

        IRCMessage message = new();
        bool result = message.TryParse(line);

        Assert.True(result);
        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("someuser", message.SenderLogin);
        Assert.Equal("!Heal Player", message.Trailing);
        Assert.Equal((250, "abc-123"), (message.Bits, message.ID));
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

    [Fact]
    public void TryParse_ClearsPreviousMessageStateAfterMalformedInput()
    {
        IRCMessage message = new();
        Assert.True(message.TryParse("@mod=1;bits=25;id=message-id :User!user@host PRIVMSG #channel :hello"));

        Assert.False(message.TryParse(":missing-command-prefix"));

        Assert.Equal(0, message.Bits);
        Assert.Empty(message.ID);
        Assert.False(message.IsModerator);
        Assert.Empty(message.Command);
        Assert.Empty(message.SenderLogin);
        Assert.Empty(message.Trailing);
    }

    [Fact]
    public void TryParse_IgnoresOverflowingBitsWithoutRejectingChat()
    {
        IRCMessage message = new();

        Assert.True(message.TryParse("@bits=2147483648;id=message-id :User!user@host PRIVMSG #channel :hello"));
        Assert.Equal(0, message.Bits);
        Assert.Equal("message-id", message.ID);
        Assert.Equal("hello", message.Trailing);
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
