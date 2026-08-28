using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class MinecraftRuntimeCommandTests
{
    [Theory]
    [InlineData("  say hi  ", "say hi")]
    [InlineData("\uFEFFstop\uFEFF", "stop")]
    [InlineData("say hi\r\nstop", "")]
    public void NormalizeSingleServerCommand_TrimsBoundariesAndRejectsMultipleLines(
        string command,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.NormalizeSingleServerCommand(command));
    }

    [Theory]
    [InlineData(1, 5000)]
    [InlineData(100, 15000)]
    public void GetRemoteCommandTimeout_UsesTheBaseAndMaximumLimits(
        int commandCount,
        int expectedMilliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            BotMainHandler.GetRemoteCommandTimeout(commandCount));
    }

    [Fact]
    public void CalculateRemoteCommandTimeout_UsesConfiguredBaseTimeout()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(17),
            BotMainHandler.CalculateRemoteCommandTimeout(11, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void SnapshotServerCommands_NormalizesValidCommandsAndRejectsUnsafeEntries()
    {
        List<string> result = BotMainHandler.SnapshotServerCommands(
            [" say hi ", "", "stop\nnow", "\uFEFFsave-all"]);

        Assert.Equal(["say hi", "save-all"], result);
    }
}
