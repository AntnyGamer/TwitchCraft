using TwitchCraft_V1;
using Xunit;

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

    [Theory]
    [InlineData(":missing-command-prefix", false, "", "")]
    [InlineData("PING :tmi.twitch.tv", true, "PING", "tmi.twitch.tv")]
    public void TryParse_ClearsPreviousMessageMetadata(string nextLine, bool expectedAccepted, string command, string trailing)
    {
        IRCMessage message = new();
        Assert.True(message.TryParse("@mod=1;bits=25;id=message-id :User!user@host PRIVMSG #channel :hello"));

        Assert.Equal(expectedAccepted, message.TryParse(nextLine));

        Assert.Equal(0, message.Bits);
        Assert.Empty(message.ID);
        Assert.False(message.IsModerator);
        Assert.Equal(command, message.Command);
        Assert.Empty(message.SenderLogin);
        Assert.Equal(trailing, message.Trailing);
    }

    [Theory]
    [InlineData("2147483648", 0)]
    [InlineData("-25", 0)]
    [InlineData("+25", 0)]
    [InlineData("25x", 0)]
    [InlineData("2.5", 0)]
    [InlineData("٢٥", 0)]
    [InlineData("2147483647", int.MaxValue)]
    public void TryParse_OnlyAcceptsNonOverflowingAsciiBitsWithoutRejectingChat(string bits, int expectedBits)
    {
        IRCMessage message = new();

        Assert.True(message.TryParse($"@bits={bits};id=message-id :User!user@host PRIVMSG #channel :hello"));
        Assert.Equal(expectedBits, message.Bits);
        Assert.Equal("message-id", message.ID);
        Assert.Equal("hello", message.Trailing);
    }

    [Theory]
    [InlineData("badges=moderator/1", true)]
    [InlineData("mod=1", true)]
    [InlineData("mod=0;badges=moderator/1", true)]
    [InlineData("mod=0;badges=vip/1", false)]
    public void TryParse_RecognizesModeratorStatusFromEitherSupportedTag(string tags, bool expectedModerator)
    {
        IRCMessage message = new();

        Assert.True(message.TryParse($"@{tags} :User!user@host PRIVMSG #channel :hello"));
        Assert.Equal(expectedModerator, message.IsModerator);
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
