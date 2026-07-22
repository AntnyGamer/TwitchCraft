using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TwitchCraftBot_V1;

internal enum FileReplaceMode
{
    Atomic,
    Fallback
}

internal static class FileSystemHelper
{
    public static string GetUniqueTempPath(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        string fileName = Path.GetFileName(path);
        return Path.Combine(directory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }

    public static void EnsureDirectoryForFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public static FileReplaceMode ReplaceOrMoveWithFallback(string tempPath, string targetPath, string? backupPath, string logMessage)
    {
        try
        {
            if (File.Exists(targetPath))
                File.Replace(tempPath, targetPath, backupPath, true);
            else
                File.Move(tempPath, targetPath);

            return FileReplaceMode.Atomic;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal(logMessage, ex);
            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(targetPath))
            {
                try
                {
                    File.Copy(targetPath, backupPath, true);
                }
                catch (Exception backupEx)
                {
                    ErrorHandling.LogNonFatal("Failed to copy file", backupEx);
                }
            }

            try
            {
                File.Move(tempPath, targetPath, true);
            }
            catch (Exception moveEx)
            {
                ErrorHandling.LogNonFatal("Failed to move file", moveEx);
                File.Copy(tempPath, targetPath, true);
                TryDeleteFile(tempPath);
            }

            return FileReplaceMode.Fallback;
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to delete file", ex);
        }
    }

    public static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool skipReparsePoints)
    {
        DirectoryInfo source = new(sourceDirectory);
        if (skipReparsePoints && (source.Attributes & FileAttributes.ReparsePoint) != 0)
            return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (FileInfo file in source.EnumerateFiles())
        {
            if (!skipReparsePoints || (file.Attributes & FileAttributes.ReparsePoint) == 0)
                file.CopyTo(Path.Combine(destinationDirectory, file.Name), true);
        }

        foreach (DirectoryInfo directory in source.EnumerateDirectories())
        {
            if (!skipReparsePoints || (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                CopyDirectory(directory.FullName, Path.Combine(destinationDirectory, directory.Name), skipReparsePoints);
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to delete directory", ex);
        }
    }
}

