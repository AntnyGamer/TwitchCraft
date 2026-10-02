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
        string first = "{\"event\":\"first-boundary-event\"}";
        string second = "{\"event\":\"second-boundary-event\"}";

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
        for (int index = 0; index <= 3; index++)
        {
            string path = index == 0 ? logPath : logPath + ".old" + index;
            Assert.Equal("{\"event\":" + (29 - index) + ",\"value\":\"abcdefghij\"}",
                Assert.Single(File.ReadAllLines(path)));
        }
    }

    [Theory]
    [InlineData("{\"event\":2}", true)]
    [InlineData("{\"event\":2}\r", true)]
    [InlineData("{\"event\":", false)]
    [InlineData("{\"event\":\"é", false)]
    public void TryWriteLine_RepairsUnterminatedFinalRecordBeforeAppending(string tail, bool completeRecord)
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraft.log");
        const string first = "{\"event\":1}";
        const string next = "{\"event\":3}";
        File.WriteAllText(logPath, first + System.Environment.NewLine + tail, UTF8NoBOM);

        using (RollingJsonLogWriter writer = new(logPath, 1024, 3, UTF8NoBOM))
            Assert.True(writer.TryWriteLine(next));

        Assert.Equal(completeRecord ? new[] { first, "{\"event\":2}", next } : new[] { first, next },
            File.ReadAllLines(logPath));
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
