using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class DatapackInstallerTests
{
    private const string LegacyRunTellraw = "tellraw @s {\"text\":\"Players online:\",\"color\":\"yellow\",\"bold\":true}";
    private const string InlineRunTellraw = "tellraw @s {text:'Players online:',color:'yellow',bold:true}";

    [Fact]
    public void TrySyncLocatePlayersDatapack_MissingSourceReportsWarningAndContinues()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "missing");
        string destination = Path.Combine(directory.Path, "world", "datapacks", "locateplayers");
        List<(string Context, Exception Exception)> warnings = [];

        bool installed = DatapackInstaller.TrySyncLocatePlayersDatapack(
            source,
            destination,
            "1.21.11",
            (context, exception) => warnings.Add((context, exception)));

        Assert.False(installed);
        (string context, Exception exception) = Assert.Single(warnings);
        Assert.Contains("will continue", context, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<DirectoryNotFoundException>(exception);
        Assert.Contains(source, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TrySyncLocatePlayersDatapack_IncompleteSourceReportsWarningAndContinues(
        bool includeFunctions,
        bool includeTags)
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world", "datapacks", "locateplayers");
        Directory.CreateDirectory(source);
        if (includeFunctions)
            Directory.CreateDirectory(Path.Combine(source, "data", "locateplayers", "functions"));
        if (includeTags)
            Directory.CreateDirectory(Path.Combine(source, "data", "minecraft", "tags", "functions"));
        List<(string Context, Exception Exception)> warnings = [];

        bool installed = DatapackInstaller.TrySyncLocatePlayersDatapack(
            source,
            destination,
            "1.21.11",
            (context, exception) => warnings.Add((context, exception)));

        Assert.False(installed);
        (string context, Exception exception) = Assert.Single(warnings);
        Assert.Contains("will continue", context, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidDataException>(exception);
        Assert.Contains(source, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData("1.20.5", true, false, false)]
    [InlineData("1.21.11", false, true, true)]
    public void TrySyncLocatePlayersDatapack_InstallsTheVersionAppropriateLayout(
        string minecraftVersion,
        bool expectsPluralLayout,
        bool expectsSingularLayout,
        bool expectsInlineText)
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world", "datapacks", "locateplayers");
        CreateCompleteSource(source);
        List<(string Context, Exception Exception)> warnings = [];

        bool installed = DatapackInstaller.TrySyncLocatePlayersDatapack(
            source,
            destination,
            minecraftVersion,
            (context, exception) => warnings.Add((context, exception)));

        Assert.True(installed);
        Assert.Empty(warnings);
        string pluralRun = Path.Combine(destination, "data", "locateplayers", "functions", "run.mcfunction");
        string singularRun = Path.Combine(destination, "data", "locateplayers", "function", "run.mcfunction");
        Assert.Equal(expectsPluralLayout, File.Exists(pluralRun));
        Assert.Equal(expectsSingularLayout, File.Exists(singularRun));
        string installedRun = File.ReadAllText(expectsSingularLayout ? singularRun : pluralRun);
        Assert.Contains(expectsInlineText ? InlineRunTellraw : LegacyRunTellraw, installedRun, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "pack.mcmeta")));
    }

    private static void CreateCompleteSource(string source)
    {
        string functions = Path.Combine(source, "data", "locateplayers", "functions");
        string tags = Path.Combine(source, "data", "minecraft", "tags", "functions");
        Directory.CreateDirectory(functions);
        Directory.CreateDirectory(tags);
        File.WriteAllText(Path.Combine(functions, "run.mcfunction"), LegacyRunTellraw);
        File.WriteAllText(Path.Combine(tags, "load.json"), "{}");
    }
}
