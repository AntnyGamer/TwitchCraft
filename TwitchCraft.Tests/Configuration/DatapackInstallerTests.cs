using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class DatapackInstallerTests
{
    private const string LegacyRunTellraw = "tellraw @s {\"text\":\"Players online:\",\"color\":\"yellow\",\"bold\":true}";
    private const string InlineRunTellraw = "tellraw @s {text:'Players online:',color:'yellow',bold:true}";

    [Fact]
    public void SyncLocateDatapack_UnsupportedVersionReportsWarningAndContinues()
    {
        using TemporaryDirectory directory = new();
        List<(string Context, Exception Exception)> warnings = [];

        bool installed = DatapackInstaller.SyncLocateDatapack(
            directory.Path,
            "unsupported",
            "world",
            (context, exception) => warnings.Add((context, exception)));

        Assert.False(installed);
        (string context, Exception exception) = Assert.Single(warnings);
        Assert.Contains("will continue", context, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<NotSupportedException>(exception);
        Assert.False(File.Exists(Path.Combine(directory.Path, "world", "datapacks", "locateplayers", "pack.mcmeta")));
    }

    [Theory]
    [InlineData("1.20.5", "1.21.11", "functions", "function")]
    [InlineData("1.21.11", "1.20.5", "function", "functions")]
    public void SyncLocateDatapack_ReplacesObsoleteFunctionLayout(
        string firstVersion,
        string secondVersion,
        string firstLayout,
        string secondLayout)
    {
        using TemporaryDirectory directory = new();
        string datapack = Path.Combine(directory.Path, "world", "datapacks", "locateplayers");

        Assert.True(DatapackInstaller.SyncLocateDatapack(directory.Path, firstVersion));
        Assert.True(Directory.Exists(Path.Combine(datapack, "data", "locateplayers", firstLayout)));

        Assert.True(DatapackInstaller.SyncLocateDatapack(directory.Path, secondVersion));
        Assert.True(Directory.Exists(Path.Combine(datapack, "data", "locateplayers", secondLayout)));
        Assert.False(Directory.Exists(Path.Combine(datapack, "data", "locateplayers", firstLayout)));
    }

    [Theory]
    [InlineData("1.20.5", true, false, false)]
    [InlineData("1.21.11", false, true, true)]
    public void SyncLocateDatapack_InstallsTheVersionAppropriateLayout(
        string minecraftVersion,
        bool expectsPluralLayout,
        bool expectsSingularLayout,
        bool expectsInlineText)
    {
        using TemporaryDirectory directory = new();
        string destination = Path.Combine(directory.Path, "world", "datapacks", "locateplayers");

        Assert.True(DatapackInstaller.SyncLocateDatapack(directory.Path, minecraftVersion));

        string pluralRun = Path.Combine(destination, "data", "locateplayers", "functions", "run.mcfunction");
        string singularRun = Path.Combine(destination, "data", "locateplayers", "function", "run.mcfunction");
        Assert.Equal(expectsPluralLayout, File.Exists(pluralRun));
        Assert.Equal(expectsSingularLayout, File.Exists(singularRun));
        string installedRun = File.ReadAllText(expectsSingularLayout ? singularRun : pluralRun);
        Assert.Contains(expectsInlineText ? InlineRunTellraw : LegacyRunTellraw, installedRun, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "pack.mcmeta")));
    }

    [Theory]
    [InlineData("1.20.5", false, 41, 0)]
    [InlineData("1.21.11", true, 94, 1)]
    [InlineData("26.1.0", true, 101, 1)]
    [InlineData("26.3.0", true, 121, 0)]
    public void BuildPackMetadata_UsesTheVersionAppropriateSchema(
        string version,
        bool modern,
        int formatMajor,
        int formatMinor)
    {
        using JsonDocument document = JsonDocument.Parse(
            DatapackInstaller.BuildPackMetadata(version));
        JsonElement pack = document.RootElement.GetProperty("pack");

        Assert.Equal(
            "Locate players command for TwitchCraft",
            pack.GetProperty("description").GetString());

        if (modern)
        {
            JsonElement minFormat = pack.GetProperty("min_format");
            JsonElement maxFormat = pack.GetProperty("max_format");
            Assert.Equal(formatMajor, minFormat[0].GetInt32());
            Assert.Equal(formatMinor, minFormat[1].GetInt32());
            Assert.Equal(formatMajor, maxFormat[0].GetInt32());
            Assert.Equal(formatMinor, maxFormat[1].GetInt32());
        }
        else
        {
            Assert.Equal(formatMajor, pack.GetProperty("pack_format").GetInt32());
        }

        Assert.False(pack.TryGetProperty("supported_formats", out _));
    }
}
