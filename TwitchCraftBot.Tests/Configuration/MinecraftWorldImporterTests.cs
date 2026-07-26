using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Configuration;

public sealed class MinecraftWorldImporterTests
{
    [Fact]
    public void IsMinecraftWorldFolder_RejectsBlankPaths()
    {
        Assert.False(MinecraftWorldImporter.IsMinecraftWorldFolder(null!));
        Assert.False(MinecraftWorldImporter.IsMinecraftWorldFolder(""));
    }

    [Fact]
    public void IsMinecraftWorldFolder_RejectsDirectoryWithoutLevelDat()
    {
        using TemporaryDirectory directory = new();

        Assert.False(MinecraftWorldImporter.IsMinecraftWorldFolder(directory.Path));
    }

    [Fact]
    public void IsMinecraftWorldFolder_AcceptsDirectoryWithLevelDat()
    {
        using TemporaryDirectory directory = new();
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "level.dat"), "world");

        Assert.True(MinecraftWorldImporter.IsMinecraftWorldFolder(directory.Path));
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
    public void ReplaceWorldSafely_ReplacesExistingWorldAndRemovesTemporaryFolders()
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

        MinecraftWorldImporter.ReplaceWorldSafely(new MinecraftWorldImportPlan
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
    public void ReplaceWorldSafely_WhenBackupPathIsBlocked_PreservesExistingWorld()
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
            MinecraftWorldImporter.ReplaceWorldSafely(new MinecraftWorldImportPlan
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
}
