using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Twitch;

public sealed class ChatCommandParserTests
{
    [Fact]
    public void Parse_NormalizesCommandNameAndSplitsArguments()
    {
        ParsedCommand command = ParsedCommand.Parse("!HeAl   Player  5 ");

        Assert.Equal("heal", command.Name);
        Assert.Equal(["Player", "5"], command.ArgumentArray);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t!heal")]
    [InlineData("?heal")]
    [InlineData("hello")]
    [InlineData("!")]
    [InlineData("!   ")]
    public void Parse_ReturnsEmptyCommandForNonCommands(string payload)
    {
        ParsedCommand command = ParsedCommand.Parse(payload);

        Assert.Equal(string.Empty, command.Name);
        Assert.Empty(command.ArgumentArray);
    }

    [Fact]
    public void Parse_AllowsUnicodeArgumentsWithoutChangingThem()
    {
        ParsedCommand command = ParsedCommand.Parse("!title こんにちは 世界");

        Assert.Equal("title", command.Name);
        Assert.Equal(["こんにちは", "世界"], command.ArgumentArray);
    }

    [Fact]
    public void Parse_AcceptsConfiguredMultiCharacterPrefix()
    {
        ParsedCommand command = ParsedCommand.Parse("tc:HeAl Player", "tc:");

        Assert.Equal("heal", command.Name);
        Assert.Equal(["Player"], command.ArgumentArray);
    }
}
