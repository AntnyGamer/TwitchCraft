using System;
using System.IO;

namespace TwitchCraft_V1.Setup;

internal sealed class MinecraftWorldImportPlan
{
    public required string SourceWorldPath { get; init; }
    public required string DestinationWorldPath { get; init; }
    public required string StagingWorldPath { get; init; }
    public required string BackupWorldPath { get; init; }
    public required string ServerDirectory { get; init; }
    public required string LevelName { get; init; }

    public bool DestinationExists => Directory.Exists(DestinationWorldPath);
    public bool SourceIsCurrentWorld => string.Equals(
        Path.GetFullPath(SourceWorldPath),
        Path.GetFullPath(DestinationWorldPath),
        StringComparison.OrdinalIgnoreCase);
}

internal static class MinecraftWorldImporter
{
    public static bool IsWorldFolder(string path)
        => !string.IsNullOrWhiteSpace(path)
            && Directory.Exists(path)
            && File.Exists(Path.Combine(path, "level.dat"));

    public static MinecraftWorldImportPlan CreateImportPlan(TwitchCraftConfig config, string sourceWorldPath)
    {
        ArgumentNullException.ThrowIfNull(config);

        string serverDirectory = config.Server.ServerDirectory;
        string levelName = ServerPropertyEditor.GetLevelName(config);
        string destinationWorldPath = ServerPropertyEditor.GetWorldDirectory(config);
        string importID = Guid.NewGuid().ToString("N");

        return new MinecraftWorldImportPlan
        {
            SourceWorldPath = sourceWorldPath,
            DestinationWorldPath = destinationWorldPath,
            StagingWorldPath = Path.Combine(serverDirectory, levelName + ".importing-" + importID),
            BackupWorldPath = Path.Combine(serverDirectory, levelName + ".backup-" + importID),
            ServerDirectory = serverDirectory,
            LevelName = levelName
        };
    }

    public static void ReplaceWorld(MinecraftWorldImportPlan plan, Action? finishImport = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Directory.CreateDirectory(plan.ServerDirectory);
        TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
        TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.BackupWorldPath);

        string sourcePrefix = Path.GetFullPath(plan.SourceWorldPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(plan.StagingWorldPath).StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source world cannot contain TwitchCraft's import staging folder.");

        bool backupCreated = false;
        bool destinationExisted = Directory.Exists(plan.DestinationWorldPath);
        try
        {
            TwitchCraft_V1.FileSystemHelper.CopyDirectory(plan.SourceWorldPath, plan.StagingWorldPath, skipReparsePoints: true);
            if (!IsWorldFolder(plan.StagingWorldPath)) throw new InvalidDataException("The staged world is incomplete.");
            if (destinationExisted)
            {
                Directory.Move(plan.DestinationWorldPath, plan.BackupWorldPath);
                backupCreated = true;
            }

            Directory.Move(plan.StagingWorldPath, plan.DestinationWorldPath);
            finishImport?.Invoke();
            TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.BackupWorldPath);
        }
        catch (Exception ex)
        {
            try
            {
                if (!destinationExisted || backupCreated)
                    TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.DestinationWorldPath);

                if (backupCreated && Directory.Exists(plan.BackupWorldPath))
                {
                    Directory.Move(plan.BackupWorldPath, plan.DestinationWorldPath);
                }
            }
            catch (Exception restoreEx)
            {
                TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
                throw new IOException(
                    "World import failed, and the previous world could not be restored automatically. Backup folder: " + plan.BackupWorldPath,
                    restoreEx);
            }

            TwitchCraft_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
            throw new IOException(
                backupCreated
                    ? "World import failed. The previous world was restored from backup."
                    : "World import failed before replacing the existing world.",
                ex);
        }
    }
}
