using System;
using System.Collections.Generic;
using System.IO;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class WorldReplacementSafetyTests
{
    [Fact]
    public void IsWorldFolder_RequiresLevelDat()
    {
        using TemporaryDirectory directory = new();

        Assert.False(MinecraftWorldImporter.IsWorldFolder(null!));
        Assert.False(MinecraftWorldImporter.IsWorldFolder(""));
        Assert.False(MinecraftWorldImporter.IsWorldFolder(directory.Path));
        File.WriteAllText(Path.Combine(directory.Path, "level.dat"), "world");
        Assert.True(MinecraftWorldImporter.IsWorldFolder(directory.Path));
    }

    [Fact]
    public void CreateImportPlan_UsesTheServerWorldAndUniqueSiblingPaths()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        TwitchCraftConfig config = new()
        {
            Server = new ServerConfig { ServerDirectory = directory.Path }
        };

        MinecraftWorldImportPlan plan = MinecraftWorldImporter.CreateImportPlan(config, source);
        MinecraftWorldImportPlan nextPlan = MinecraftWorldImporter.CreateImportPlan(config, source);

        Assert.Equal(source, plan.SourceWorldPath);
        Assert.Equal("world", plan.LevelName);
        Assert.Equal(Path.Combine(directory.Path, "world"), plan.DestinationWorldPath);
        Assert.StartsWith(
            Path.Combine(directory.Path, "world.importing-"),
            plan.StagingWorldPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            Path.Combine(directory.Path, "world.backup-"),
            plan.BackupWorldPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(plan.StagingWorldPath, plan.BackupWorldPath);
        Assert.Equal(plan.DestinationWorldPath, nextPlan.DestinationWorldPath);
        Assert.NotEqual(plan.StagingWorldPath, nextPlan.StagingWorldPath);
        Assert.NotEqual(plan.BackupWorldPath, nextPlan.BackupWorldPath);
    }

    [Fact]
    public void ReplaceWorld_WhenNoPreviousWorldExists_InstallsTheImportedWorld()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "level.dat"), "new");

        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);

        MinecraftWorldImporter.ReplaceWorld(plan);

        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "level.dat")));
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(plan.StagingWorldPath));
        Assert.False(Directory.Exists(plan.BackupWorldPath));
    }

    [Fact]
    public void ReplaceWorld_OptionalDatapackFailureCommitsWorldAndWarns()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(Path.Combine(source, "new.txt"), "new data");
        File.WriteAllText(Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(Path.Combine(destination, "old.txt"), "old data");
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        List<(string Context, Exception Exception)> warnings = [];

        MinecraftWorldImporter.ReplaceWorld(plan, () =>
        {
            string blockedDatapacksPath = Path.Combine(destination, "datapacks");
            File.WriteAllText(blockedDatapacksPath, "blocked");
            bool installed = DatapackInstaller.SyncLocateDatapack(
                directory.Path,
                "1.21.11",
                "world",
                (context, exception) => warnings.Add((context, exception)));
            Assert.False(installed);
            File.Delete(blockedDatapacksPath);
        });

        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "level.dat")));
        Assert.Equal("new data", File.ReadAllText(Path.Combine(destination, "new.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "old.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(source, "level.dat")));
        Assert.False(Directory.Exists(plan.StagingWorldPath));
        Assert.False(Directory.Exists(plan.BackupWorldPath));
        Assert.IsType<IOException>(Assert.Single(warnings).Exception);
    }

    [Fact]
    public void SourceIsCurrentWorld_DistinguishesEquivalentAndDifferentPaths()
    {
        using TemporaryDirectory directory = new();
        string destination = Path.Combine(directory.Path, "world");
        string equivalentSource = Path.Combine(
            directory.Path.ToUpperInvariant(),
            ".",
            "world");

        Assert.True(CreatePlan(directory.Path, equivalentSource, destination).SourceIsCurrentWorld);
        string source = Path.Combine(directory.Path, "source");
        Assert.False(CreatePlan(directory.Path, source, destination).SourceIsCurrentWorld);
    }

    [Fact]
    public void ReplaceWorld_IncompleteStagePreservesExistingProgress()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "region.dat"), "incomplete imported data");
        File.WriteAllText(Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(Path.Combine(destination, "player-data.dat"), "saved progress");
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        bool finishCalled = false;

        IOException exception = Assert.Throws<IOException>(() =>
            MinecraftWorldImporter.ReplaceWorld(plan, () => finishCalled = true));

        Assert.Multiple(
            () => Assert.IsType<InvalidDataException>(exception.InnerException),
            () => Assert.False(finishCalled),
            () => Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "level.dat"))),
            () => Assert.Equal("saved progress", File.ReadAllText(Path.Combine(destination, "player-data.dat"))),
            () => Assert.False(File.Exists(Path.Combine(destination, "region.dat"))),
            () => Assert.Equal("incomplete imported data", File.ReadAllText(Path.Combine(source, "region.dat"))),
            () => Assert.False(Directory.Exists(plan.StagingWorldPath)),
            () => Assert.False(Directory.Exists(plan.BackupWorldPath)));
    }

    [Fact]
    public void ReplaceWorld_WhenBackupPathIsBlocked_PreservesExistingWorld()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(plan.BackupWorldPath, "blocked");

        IOException exception = Assert.Throws<IOException>(() =>
            MinecraftWorldImporter.ReplaceWorld(plan));

        Assert.Multiple(
            () => Assert.Contains("before replacing", exception.Message, StringComparison.OrdinalIgnoreCase),
            () => Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "level.dat"))),
            () => Assert.False(Directory.Exists(plan.StagingWorldPath)),
            () => Assert.Equal("blocked", File.ReadAllText(plan.BackupWorldPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaceWorld_WhenFinishingFails_RollsBackInstalledWorld(bool hasPreviousWorld)
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "level.dat"), "new");
        if (hasPreviousWorld)
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "level.dat"), "old");
            File.WriteAllText(Path.Combine(destination, "player-data.dat"), "saved progress");
        }
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        IOException failure = new("Finish import failed after installation");

        IOException exception = Assert.Throws<IOException>(() => MinecraftWorldImporter.ReplaceWorld(plan, () =>
        {
            Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "level.dat")));
            Assert.Equal(hasPreviousWorld, Directory.Exists(plan.BackupWorldPath));
            File.WriteAllText(Path.Combine(destination, "partial-datapack.txt"), "incomplete");
            throw failure;
        }));

        Assert.Same(failure, exception.InnerException);
        Assert.Equal(hasPreviousWorld, plan.DestinationExists);
        if (hasPreviousWorld)
        {
            Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "level.dat")));
            Assert.Equal("saved progress", File.ReadAllText(Path.Combine(destination, "player-data.dat")));
        }
        Assert.False(File.Exists(Path.Combine(destination, "partial-datapack.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(source, "level.dat")));
        Assert.False(Directory.Exists(plan.StagingWorldPath));
        Assert.False(Directory.Exists(plan.BackupWorldPath));
    }

    [Fact]
    public void ReplaceWorld_FailedRestorePreservesBackupForRecovery()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source");
        string destination = Path.Combine(directory.Path, "world");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "level.dat"), "new");
        File.WriteAllText(Path.Combine(source, "new.txt"), "imported data");
        File.WriteAllText(Path.Combine(destination, "level.dat"), "old");
        File.WriteAllText(Path.Combine(destination, "player-data.dat"), "saved progress");
        MinecraftWorldImportPlan plan = CreatePlan(directory.Path, source, destination);
        IOException finishFailure = new("Finish import failed after installation");

        IOException exception = Assert.Throws<IOException>(() => MinecraftWorldImporter.ReplaceWorld(plan, () =>
        {
            Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "level.dat")));
            Assert.True(Directory.Exists(plan.BackupWorldPath));
            Assert.StartsWith(
                Path.GetFullPath(directory.Path) + Path.DirectorySeparatorChar,
                Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase);
            Directory.Delete(destination, recursive: true);
            File.WriteAllText(destination, "blocks automatic restoration");
            throw finishFailure;
        }));

        Assert.Multiple(
            () => Assert.Contains("previous world could not be restored automatically", exception.Message, StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("Backup folder: " + plan.BackupWorldPath, exception.Message, StringComparison.Ordinal),
            () => Assert.IsType<IOException>(exception.InnerException),
            () => Assert.NotSame(finishFailure, exception.InnerException),
            () => Assert.Equal("old", File.ReadAllText(Path.Combine(plan.BackupWorldPath, "level.dat"))),
            () => Assert.Equal("saved progress", File.ReadAllText(Path.Combine(plan.BackupWorldPath, "player-data.dat"))),
            () => Assert.False(File.Exists(Path.Combine(plan.BackupWorldPath, "new.txt"))),
            () => Assert.Equal("new", File.ReadAllText(Path.Combine(source, "level.dat"))),
            () => Assert.Equal("imported data", File.ReadAllText(Path.Combine(source, "new.txt"))),
            () => Assert.False(Directory.Exists(plan.StagingWorldPath)),
            () => Assert.Equal("blocks automatic restoration", File.ReadAllText(destination)));
    }

    private static MinecraftWorldImportPlan CreatePlan(
        string serverDirectory,
        string source,
        string destination)
        => new()
        {
            SourceWorldPath = source,
            DestinationWorldPath = destination,
            StagingWorldPath = Path.Combine(serverDirectory, "world.importing-test"),
            BackupWorldPath = Path.Combine(serverDirectory, "world.backup-test"),
            ServerDirectory = serverDirectory,
            LevelName = "world"
        };
}
