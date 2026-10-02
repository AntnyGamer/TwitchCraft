using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Diagnostics;

public sealed class RollingLogPersistenceTests
{
    private static readonly UTF8Encoding UTF8NoBOM = new(false);

    [Fact]
    public void TryWriteLine_RotatesDuringOneSessionWithoutLosingThePendingEvent()
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraft.log");
        string first = "{\"event\":\"éééééééééé\"}";
        string second = "{\"event\":\"øøøøøøøøøø\"}";

        using (RollingJsonLogWriter writer = new(logPath, 48, 3, UTF8NoBOM))
        {
            Assert.True(writer.TryWriteLine(first));
            Assert.True(writer.TryWriteLine(second));
        }

        string[] lines = ReadAllLogLines(logPath);
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, lines.Count(line => line == first));
        Assert.Equal(1, lines.Count(line => line == second));
        Assert.True(File.Exists(logPath + ".old1"));
        Assert.Equal(second, Assert.Single(File.ReadAllLines(logPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryWriteLine_ReopeningAfterAnInterruptedAppendPreservesCompleteJson(bool completeTail)
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraft.log");
        const string retained = "{\"event\":\"retained\"}";
        const string pending = "{\"event\":\"pending\"}";
        string tail = completeTail ? "{\"event\":\"complete-tail\"}" : "{\"event\":";
        File.WriteAllText(logPath, retained + System.Environment.NewLine + tail, UTF8NoBOM);

        using (RollingJsonLogWriter writer = new(logPath, 1024, 3, UTF8NoBOM))
            Assert.True(writer.TryWriteLine(pending));

        string[] expected = completeTail ? [retained, tail, pending] : [retained, pending];
        string[] lines = File.ReadAllLines(logPath);
        Assert.Equal(expected, lines);
        foreach (string line in lines)
        {
            using JsonDocument json = JsonDocument.Parse(line);
            Assert.NotNull(json.RootElement.GetProperty("event").GetString());
        }
    }

    [Fact]
    public void TryWriteLine_RetainsOnlyTheConfiguredNumberOfRotatedFiles()
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraft.log");

        using (RollingJsonLogWriter writer = new(logPath, 40, 3, UTF8NoBOM))
        {
            for (int index = 0; index < 30; index++)
                Assert.True(writer.TryWriteLine("{\"event\":" + index + ",\"value\":\"abcdefghij\"}"));
        }

        string[] files = Directory.GetFiles(directory.Path, "TwitchCraft.log*");
        Assert.Equal(4, files.Length);
        Assert.DoesNotContain(logPath + ".old4", files);
        for (int retained = 0; retained <= 3; retained++)
        {
            string retainedPath = retained == 0 ? logPath : logPath + ".old" + retained;
            string expected = "{\"event\":" + (29 - retained) + ",\"value\":\"abcdefghij\"}";
            Assert.Equal(expected, Assert.Single(File.ReadAllLines(retainedPath)));
        }
    }

    [Fact]
    public void TryWriteLine_SerializesConcurrentWritesAndRotationsIntoValidJsonLines()
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraft.log");

        using (RollingJsonLogWriter writer = new(logPath, 160, 64, UTF8NoBOM))
        {
            Parallel.For(0, 200, index =>
                Assert.True(writer.TryWriteLine("{\"event\":" + index + "}"), $"Event {index} could not be written."));
        }

        string[] lines = ReadAllLogLines(logPath);
        Assert.Equal(200, lines.Length);
        HashSet<int> eventIDs = [];
        foreach (string line in lines)
        {
            using JsonDocument json = JsonDocument.Parse(line);
            eventIDs.Add(json.RootElement.GetProperty("event").GetInt32());
        }

        Assert.Equal(Enumerable.Range(0, 200), eventIDs.Order());
    }

    private static string[] ReadAllLogLines(string logPath)
    {
        string directory = Path.GetDirectoryName(logPath)!;
        return Directory.GetFiles(directory, Path.GetFileName(logPath) + "*")
            .SelectMany(File.ReadAllLines)
            .ToArray();
    }
}
