using System;
using System.Collections.Generic;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

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
        Assert.Equal(expected, MainHandler.CleanServerCommand(command));
    }

    [Theory]
    [InlineData(1, 5, 5)]
    [InlineData(100, 5, 15)]
    public void GetRCONTimeout_UsesTheBaseAndMaximumLimits(
        int commandCount,
        int baseSeconds,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            MainHandler.GetRCONTimeout(commandCount, TimeSpan.FromSeconds(baseSeconds)));
    }

    [Fact]
    public void GetRCONTimeout_UsesConfiguredBaseTimeout()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(17),
            MainHandler.GetRCONTimeout(11, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void SnapshotCommands_NormalizesValidCommandsAndRejectsUnsafeEntries()
    {
        List<string> result = MainHandler.SnapshotCommands(
            [" say hi ", "", "stop\nnow", "\uFEFFsave-all"]);

        Assert.Equal(["say hi", "save-all"], result);
    }
}
