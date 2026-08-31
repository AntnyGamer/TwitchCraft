using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Configuration;

public sealed class WorldReplacementSafetyTests
{
    [Fact]
    public void IsWorldFolder_RejectsBlankPaths()
    {
        Assert.False(MinecraftWorldImporter.IsWorldFolder(null!));
        Assert.False(MinecraftWorldImporter.IsWorldFolder(""));
    }

    [Fact]
    public void IsWorldFolder_RequiresLevelDat()
    {
        using TemporaryDirectory directory = new();

        Assert.False(MinecraftWorldImporter.IsWorldFolder(directory.Path));
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "level.dat"), "world");
        Assert.True(MinecraftWorldImporter.IsWorldFolder(directory.Path));
    }

    [Fact]
    public void CreateImportPlan_UsesTheServerWorldAndUniqueSiblingPaths()
    {
        using TemporaryDirectory directory = new();
        string source = System.IO.Path.Combine(directory.Path, "source");
        BotConfig config = new()
        {
            Server = new ServerConfig { ServerDirectory = directory.Path }
        };

        MinecraftWorldImportPlan plan = MinecraftWorldImporter.CreateImportPlan(config, source);

        Assert.Equal(source, plan.SourceWorldPath);
        Assert.Equal("world", plan.LevelName);
        Assert.Equal(System.IO.Path.Combine(directory.Path, "world"), plan.DestinationWorldPath);
        Assert.StartsWith(
            System.IO.Path.Combine(directory.Path, "world.importing-"),
            plan.StagingWorldPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            System.IO.Path.Combine(directory.Path, "world.backup-"),
            plan.BackupWorldPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(plan.StagingWorldPath, plan.BackupWorldPath);
    }

    [Fact]
    public void ReplaceWorld_ReplacesExistingWorldAndRemovesTemporaryFolders()
    {
        using TemporaryDirectory directory = new();
        string source = System.IO.Path.Combine(directory.Path, "source");
        string destination = System.IO.Path.Combine(directory.Path, "world");
        string staging = System.IO.Path.Combine(directory.Path, "world.importing-test");
        string backup = System.IO.Path.Combine(directory.Path, "world.backup-test");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(System.IO.Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(System.IO.Path.Combine(source, "new.txt"), "new data");
        File.WriteAllText(System.IO.Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(System.IO.Path.Combine(destination, "old.txt"), "old data");

        MinecraftWorldImporter.ReplaceWorld(new MinecraftWorldImportPlan
        {
            SourceWorldPath = source,
            DestinationWorldPath = destination,
            StagingWorldPath = staging,
            BackupWorldPath = backup,
            ServerDirectory = directory.Path,
            LevelName = "world"
        });

        Assert.Equal("new", File.ReadAllText(System.IO.Path.Combine(destination, "level.dat")));
        Assert.True(File.Exists(System.IO.Path.Combine(destination, "new.txt")));
        Assert.False(File.Exists(System.IO.Path.Combine(destination, "old.txt")));
        Assert.False(Directory.Exists(staging));
        Assert.False(Directory.Exists(backup));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public void ReplaceWorld_WhenNoPreviousWorldExists_InstallsTheImportedWorld()
    {
        using TemporaryDirectory directory = new();
        string source = System.IO.Path.Combine(directory.Path, "source");
        string destination = System.IO.Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        File.WriteAllText(System.IO.Path.Combine(source, "level.dat"), "new");

        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);

        MinecraftWorldImporter.ReplaceWorld(plan);

        Assert.Equal("new", File.ReadAllText(System.IO.Path.Combine(destination, "level.dat")));
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(plan.StagingWorldPath));
        Assert.False(Directory.Exists(plan.BackupWorldPath));
    }

    [Fact]
    public void ReplaceWorld_WhenOptionalDatapackIsMissing_CommitsTheWorldAndReportsWarning()
    {
        using TemporaryDirectory directory = new();
        string source = System.IO.Path.Combine(directory.Path, "source");
        string destination = System.IO.Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(System.IO.Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(System.IO.Path.Combine(destination, "level.dat"), "old");
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        List<(string Context, Exception Exception)> warnings = [];

        MinecraftWorldImporter.ReplaceWorld(plan, () =>
        {
            bool installed = DatapackInstaller.TrySyncDatapack(
                System.IO.Path.Combine(directory.Path, "missing-datapack-source"),
                System.IO.Path.Combine(destination, "datapacks", "locateplayers"),
                "1.21.11",
                (context, exception) => warnings.Add((context, exception)));
            Assert.False(installed);
        });

        Assert.Equal("new", File.ReadAllText(System.IO.Path.Combine(destination, "level.dat")));
        Assert.False(Directory.Exists(plan.StagingWorldPath));
        Assert.False(Directory.Exists(plan.BackupWorldPath));
        Assert.IsType<DirectoryNotFoundException>(Assert.Single(warnings).Exception);
    }

    [Fact]
    public void SourceIsCurrentWorld_DistinguishesEquivalentAndDifferentPaths()
    {
        using TemporaryDirectory directory = new();
        string destination = System.IO.Path.Combine(directory.Path, "world");
        string equivalentSource = System.IO.Path.Combine(
            directory.Path.ToUpperInvariant(),
            ".",
            "world");

        Assert.True(CreatePlan(directory.Path, equivalentSource, destination).SourceIsCurrentWorld);
        string source = System.IO.Path.Combine(directory.Path, "source");
        Assert.False(CreatePlan(directory.Path, source, destination).SourceIsCurrentWorld);
    }

    [Fact]
    public void ReplaceWorld_WhenBackupPathIsBlocked_PreservesExistingWorld()
    {
        using TemporaryDirectory directory = new();
        string source = System.IO.Path.Combine(directory.Path, "source");
        string destination = System.IO.Path.Combine(directory.Path, "world");
        string staging = System.IO.Path.Combine(directory.Path, "world.importing-test");
        string backup = System.IO.Path.Combine(directory.Path, "world.backup-test");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(System.IO.Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(System.IO.Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(backup, "blocked");

        IOException exception = Assert.Throws<IOException>(() =>
            MinecraftWorldImporter.ReplaceWorld(new MinecraftWorldImportPlan
            {
                SourceWorldPath = source,
                DestinationWorldPath = destination,
                StagingWorldPath = staging,
                BackupWorldPath = backup,
                ServerDirectory = directory.Path,
                LevelName = "world"
            }));

        Assert.Contains("before replacing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old", File.ReadAllText(System.IO.Path.Combine(destination, "level.dat")));
        Assert.False(Directory.Exists(staging));
        Assert.True(File.Exists(backup));
    }

    private static MinecraftWorldImportPlan CreatePlan(
        string serverDirectory,
        string source,
        string destination)
        => new()
        {
            SourceWorldPath = source,
            DestinationWorldPath = destination,
            StagingWorldPath = System.IO.Path.Combine(serverDirectory, "world.importing-test"),
            BackupWorldPath = System.IO.Path.Combine(serverDirectory, "world.backup-test"),
            ServerDirectory = serverDirectory,
            LevelName = "world"
        };
}
