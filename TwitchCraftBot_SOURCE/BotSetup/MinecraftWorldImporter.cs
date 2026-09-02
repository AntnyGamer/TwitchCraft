using System;
using System.IO;

namespace TwitchCraftBot_V1.BotSetup;

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
    {
        return !string.IsNullOrWhiteSpace(path)
            && Directory.Exists(path)
            && File.Exists(Path.Combine(path, "level.dat"));
    }

    public static MinecraftWorldImportPlan CreateImportPlan(BotConfig config, string sourceWorldPath)
    {
        ArgumentNullException.ThrowIfNull(config);

        string serverDirectory = config.Server.ServerDirectory;
        string levelName = ServerPropertyEditor.GetLevelName(config);
        string destinationWorldPath = ServerPropertyEditor.GetWorldDirectory(config);
        string importId = Guid.NewGuid().ToString("N");

        return new MinecraftWorldImportPlan
        {
            SourceWorldPath = sourceWorldPath,
            DestinationWorldPath = destinationWorldPath,
            StagingWorldPath = Path.Combine(serverDirectory, levelName + ".importing-" + importId),
            BackupWorldPath = Path.Combine(serverDirectory, levelName + ".backup-" + importId),
            ServerDirectory = serverDirectory,
            LevelName = levelName
        };
    }

    public static void ReplaceWorld(MinecraftWorldImportPlan plan, Action? finishImport = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Directory.CreateDirectory(plan.ServerDirectory);
        TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
        TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.BackupWorldPath);

        TwitchCraftBot_V1.FileSystemHelper.CopyDirectory(plan.SourceWorldPath, plan.StagingWorldPath, skipReparsePoints: true);

        bool backupCreated = false;
        bool destinationExisted = Directory.Exists(plan.DestinationWorldPath);
        try
        {
            if (destinationExisted)
            {
                Directory.Move(plan.DestinationWorldPath, plan.BackupWorldPath);
                backupCreated = true;
            }

            Directory.Move(plan.StagingWorldPath, plan.DestinationWorldPath);
            finishImport?.Invoke();
            TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.BackupWorldPath);
        }
        catch (Exception ex)
        {
            try
            {
                if (!destinationExisted || backupCreated)
                    TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.DestinationWorldPath);

                if (backupCreated && Directory.Exists(plan.BackupWorldPath))
                {
                    Directory.Move(plan.BackupWorldPath, plan.DestinationWorldPath);
                }
            }
            catch (Exception restoreEx)
            {
                TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
                throw new IOException(
                    "World import failed, and the previous world could not be restored automatically. Backup folder: " + plan.BackupWorldPath,
                    restoreEx);
            }

            TwitchCraftBot_V1.FileSystemHelper.DeleteDirectorySafe(plan.StagingWorldPath);
            throw new IOException(
                backupCreated
                    ? "World import failed. The previous world was restored from backup."
                    : "World import failed before replacing the existing world.",
                ex);
        }
    }

}
