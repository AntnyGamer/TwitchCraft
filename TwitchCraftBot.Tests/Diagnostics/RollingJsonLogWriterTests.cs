using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Diagnostics;

public sealed class RollingJsonLogWriterTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    [Fact]
    public void TryWriteLine_RotatesDuringOneSessionWithoutLosingThePendingEvent()
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraftBot.log");
        string first = "{\"event\":\"first-boundary-event\"}";
        string second = "{\"event\":\"second-boundary-event\"}";

        using (RollingJsonLogWriter writer = new(logPath, 48, 3, Utf8NoBom))
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
        string logPath = Path.Combine(directory.Path, "TwitchCraftBot.log");

        using (RollingJsonLogWriter writer = new(logPath, 40, 3, Utf8NoBom))
        {
            for (int index = 0; index < 30; index++)
                Assert.True(writer.TryWriteLine("{\"event\":" + index + ",\"value\":\"abcdefghij\"}"));
        }

        string[] files = Directory.GetFiles(directory.Path, "TwitchCraftBot.log*");
        Assert.InRange(files.Length, 1, 4);
        Assert.DoesNotContain(logPath + ".old4", files);
        Assert.Contains("{\"event\":29,\"value\":\"abcdefghij\"}", ReadAllLogLines(logPath));
    }

    [Fact]
    public void TryWriteLine_SerializesConcurrentWritesAndRotationsIntoValidJsonLines()
    {
        using TemporaryDirectory directory = new();
        string logPath = Path.Combine(directory.Path, "TwitchCraftBot.log");
        ConcurrentBag<bool> results = [];

        using (RollingJsonLogWriter writer = new(logPath, 160, 64, Utf8NoBom))
        {
            Parallel.For(0, 200, index =>
            {
                results.Add(writer.TryWriteLine("{\"event\":" + index + "}"));
            });
        }

        Assert.Equal(200, results.Count);
        Assert.All(results, result => Assert.True(result));

        string[] lines = ReadAllLogLines(logPath);
        Assert.Equal(200, lines.Length);
        HashSet<int> eventIds = [];
        foreach (string line in lines)
        {
            using JsonDocument json = JsonDocument.Parse(line);
            eventIds.Add(json.RootElement.GetProperty("event").GetInt32());
        }

        Assert.Equal(200, eventIds.Count);
    }

    private static string[] ReadAllLogLines(string logPath)
    {
        string directory = Path.GetDirectoryName(logPath)!;
        return Directory.GetFiles(directory, Path.GetFileName(logPath) + "*")
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Length > 0)
            .ToArray();
    }

}
