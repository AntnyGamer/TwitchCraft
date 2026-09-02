using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class ServerCommandTransportTests
{
    [Theory]
    [InlineData("  say hi  ", "say hi")]
    [InlineData("\uFEFFstop\uFEFF", "stop")]
    [InlineData("say hi\r\nstop", "")]
    public void CleanServerCommand_TrimsBoundariesAndRejectsMultipleLines(
        string command,
        string expected)
    {
        Assert.Equal(expected, BotMainHandler.CleanServerCommand(command));
    }

    [Theory]
    [InlineData(1, 5, 5)]
    [InlineData(100, 5, 15)]
    public void GetRconTimeout_UsesTheBaseAndMaximumLimits(
        int commandCount,
        int baseSeconds,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            BotMainHandler.GetRconTimeout(commandCount, TimeSpan.FromSeconds(baseSeconds)));
    }

    [Fact]
    public void GetRconTimeout_UsesConfiguredBaseTimeout()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(17),
            BotMainHandler.GetRconTimeout(11, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void SnapshotCommands_NormalizesValidCommandsAndRejectsUnsafeEntries()
    {
        List<string> result = BotMainHandler.SnapshotCommands(
            [" say hi ", "", "stop\nnow", "\uFEFFsave-all"]);

        Assert.Equal(["say hi", "save-all"], result);
    }
}
