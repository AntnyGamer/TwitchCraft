using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class ServerDiagnosticFilteringTests
{
    private static readonly string[] JomlWarning =
    [
        "WARNING: A terminally deprecated method in sun.misc.Unsafe has been called",
        "WARNING: sun.misc.Unsafe::objectFieldOffset has been called by org.joml.MemUtil$MemUtilUnsafe (file:/C:/server/libraries/org/joml/joml/1.10.8/joml-1.10.8.jar)",
        "WARNING: Please consider reporting this to the maintainers of class org.joml.MemUtil$MemUtilUnsafe",
        "WARNING: sun.misc.Unsafe::objectFieldOffset will be removed in a future release"
    ];

    [Fact]
    public void CompleteJomlUnsafeWarningIsHidden()
    {
        List<string> shownLines = Filter(JomlWarning);

        Assert.Empty(shownLines);
    }

    [Fact]
    public void DisplayPrefixesAndDifferentJomlVersionsAreRecognized()
    {
        string[] lines =
        [
            "[stderr] " + JomlWarning[0],
            "[stderr] WARNING: sun.misc.Unsafe::objectFieldOffset has been called by org.joml.MemUtil$MemUtilUnsafe (file:/server/joml-9.9.9.jar)",
            "[stderr] " + JomlWarning[2],
            "[stderr] " + JomlWarning[3]
        ];

        Assert.Empty(Filter(lines));
    }

    [Fact]
    public void SimilarWarningFromAnotherLibraryRemainsVisible()
    {
        string[] lines =
        [
            JomlWarning[0],
            "WARNING: sun.misc.Unsafe::objectFieldOffset has been called by example.OtherLibrary",
            "A later stderr message"
        ];

        Assert.Equal(lines, Filter(lines));
    }

    [Fact]
    public void IncompleteJomlWarningRemainsVisibleWhenStreamEnds()
    {
        string[] lines = JomlWarning[..3];

        Assert.Equal(lines, Filter(lines));
    }

    [Fact]
    public void OrdinaryStderrIsShownImmediately()
    {
        MinecraftSTDERRFilter filter = new();
        List<string> shownLines = [];

        filter.ProcessLine("ERROR: Failed to bind server port", shownLines.Add);

        Assert.Equal(["ERROR: Failed to bind server port"], shownLines);
    }

    private static List<string> Filter(IEnumerable<string> lines)
    {
        MinecraftSTDERRFilter filter = new();
        List<string> shownLines = [];
        foreach (string line in lines)
            filter.ProcessLine(line, shownLines.Add);

        filter.Flush(shownLines.Add);
        return shownLines;
    }
}
