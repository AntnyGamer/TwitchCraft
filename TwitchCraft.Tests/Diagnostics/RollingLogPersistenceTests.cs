using System.Text;
using System.Text.Json;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;

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
        Assert.InRange(files.Length, 1, 4);
        Assert.DoesNotContain(logPath + ".old4", files);
        Assert.Contains("{\"event\":29,\"value\":\"abcdefghij\"}", ReadAllLogLines(logPath));
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
