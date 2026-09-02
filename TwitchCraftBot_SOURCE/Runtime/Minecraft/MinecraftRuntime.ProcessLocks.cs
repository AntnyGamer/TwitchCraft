using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task UnlockLogAsync(BotConfig config, CancellationToken cancellationToken)
    {
        string latestLogPath = Path.Combine(config.Server.ServerDirectory, "logs", "latest.log");
        if (!IsFileLocked(latestLogPath))
            return;

        if (!ErrorHandling.ConfirmCloseJava(_shellWindow))
            return;

        CloseLockingApps(latestLogPath);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ServerLogUnlockWaitTimeout)
        {
            if (!IsFileLocked(latestLogPath))
                break;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        if (IsFileLocked(latestLogPath))
            throw new IOException("The Minecraft server log is still locked after attempting to close the locking Java process. Please close it manually and try again.");
    }

    private static bool IsFileLocked(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using FileStream _ = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void CloseLockingApps(string lockedFilePath)
    {
        foreach (int processId in GetLockingPids(lockedFilePath))
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase))
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static unsafe IEnumerable<int> GetLockingPids(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return [];

        Span<char> sessionKeyBuffer = stackalloc char[CchRmSessionKey + 1];
        fixed (char* sessionKey = sessionKeyBuffer)
        {
            if (RMStartSession(out uint sessionHandle, 0, sessionKey) != 0)
                return [];

            try
            {
                string pathWithNull = path + '\0';
                fixed (char* fileName = pathWithNull)
                {
                    char** fileNames = stackalloc char*[1];
                    fileNames[0] = fileName;

                    if (RMRegisterResources(sessionHandle, 1, fileNames, 0, null, 0, null) != 0)
                        return [];
                }

                uint processInfoCount = 0;
                uint rebootReasons = 0;

                int firstGetListResult = RMGetList(sessionHandle, out uint processInfoNeeded, ref processInfoCount, null, ref rebootReasons);
                if (firstGetListResult != ErrorMoreData || processInfoNeeded == 0)
                    return [];

                if (processInfoNeeded > int.MaxValue)
                    return [];

                RM_PROCESS_INFO[] processes = new RM_PROCESS_INFO[(int)processInfoNeeded];
                processInfoCount = processInfoNeeded;

                fixed (RM_PROCESS_INFO* processInfo = processes)
                {
                    int secondGetListResult = RMGetList(sessionHandle, out _, ref processInfoCount, processInfo, ref rebootReasons);
                    if (secondGetListResult != 0 || processInfoCount == 0)
                        return [];
                }

                int actualProcessCount = (int)Math.Min(processInfoCount, (uint)processes.Length);
                HashSet<int> processIDs = [];
                for (int i = 0; i < actualProcessCount; i++)
                {
                    RM_UNIQUE_PROCESS uniqueProcess = processes[i].Process;
                    if (MatchesProcess(uniqueProcess))
                        processIDs.Add(uniqueProcess.dwProcessId);
                }

                return [.. processIDs];
            }
            finally
            {
                _ = RMEndSession(sessionHandle);
            }
        }
    }

    private static bool MatchesProcess(RM_UNIQUE_PROCESS uniqueProcess)
    {
        if (uniqueProcess.dwProcessId <= 0)
            return false;

        try
        {
            using Process process = Process.GetProcessById(uniqueProcess.dwProcessId);
            if (process.HasExited)
                return false;

            long fileTimeValue = ((long)uniqueProcess.ProcessStartTime.dwHighDateTime << 32) | uniqueProcess.ProcessStartTime.dwLowDateTime;
            DateTime processStartTimeUtc = process.StartTime.ToUniversalTime();
            DateTime rmStartTimeUtc = fileTimeValue <= 0 ? DateTime.MinValue : DateTime.FromFileTimeUtc(fileTimeValue);
            return Math.Abs((processStartTimeUtc - rmStartTimeUtc).TotalSeconds) < 1;
        }
        catch
        {
            return false;
        }
    }

    private const int CchRmSessionKey = 32;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME_NATIVE
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public FILETIME_NATIVE ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char strAppName[256];
        public fixed char strServiceShortName[64];
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int bRestartable;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmStartSession")]
    private static unsafe partial int RMStartSession(out uint sessionHandle, int sessionFlags, char* sessionKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmRegisterResources")]
    private static unsafe partial int RMRegisterResources(
        uint sessionHandle,
        uint fileCount,
        char** fileNames,
        uint applicationCount,
        RM_UNIQUE_PROCESS* applications,
        uint serviceCount,
        char** serviceNames);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmGetList")]
    private static unsafe partial int RMGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        RM_PROCESS_INFO* processInfo,
        ref uint rebootReasons);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmEndSession")]
    private static partial int RMEndSession(uint sessionHandle);

}
