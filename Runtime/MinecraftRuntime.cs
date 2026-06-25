using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static readonly UTF8Encoding ServerCommandEncoding = new(false);
    private static readonly byte[] ServerCommandNewLineBytes = ServerCommandEncoding.GetBytes(Environment.NewLine);
    private static readonly string[] ClearPlayerSidebarCommands = ["scoreboard objectives remove tc_playerlist", "scoreboard objectives remove tc_health"];
    private static readonly TimeSpan ServerLogUnlockWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopCommandTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ManualCommandTimeout = TimeSpan.FromSeconds(5);
    private volatile bool _minecraftServerReady;
    private int _initialPlayerSnapshotQueued;
    private int _suppressedOnlinePlayersLogLines;
    private int _serverCommandErrorContextLines;
    private readonly Lock _suppressedServerLogContextGate = new();
    private readonly Queue<string> _suppressedServerLogContextLines = new();

    private async Task TrySendStopCommandAsync()
    {
        using CancellationTokenSource timeoutCts = new(StopCommandTimeout);
        try
        {
            await SendServerCommandAsync("stop", timeoutCts.Token).ConfigureAwait(false);
            if (_javaServerProcess is { HasExited: false })
                await Task.Delay(500, timeoutCts.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task EnsureRemoteControllerConnectedAsync(BotConfig config, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ManualCommandTimeout);

        string host = GetRemoteControllerHost(config);
        _ = await MinecraftRCONClient.ExecuteQueryAsync(
            host,
            config.Server.RCON.Port,
            config.Server.RCON.Password,
            "list",
            timeoutCts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Remote controller could not authenticate with RCON. Check the host, RCON port, and RCON password.");

        _minecraftServerReady = true;
        _shellWindow?.AddServerLogLine("Remote controller connected to " + host + ":" + config.Server.RCON.Port.ToString(CultureInfo.InvariantCulture) + ".");
        QueueInitialPlayerSnapshot();
        QueueOnlinePlayerSnapshotRefresh();
        QueueTrackedPlayerGamemodeRefreshForStatistics();
        QueueTrackedPlayerDeathScoreRefreshForStatistics();
    }

    private async Task StartJavaServerAsync(BotConfig config, CancellationToken cancellationToken)
    {
        string jarPath = string.IsNullOrWhiteSpace(config.Server.JarPath)
            ? Path.Combine(config.Server.ServerDirectory, "server.jar")
            : config.Server.JarPath;

        if (!File.Exists(jarPath))
            throw new FileNotFoundException("Minecraft server jar was not found.", jarPath);

        Directory.CreateDirectory(config.Server.ServerDirectory);
        ServerPropertyEditor.CleanupUnusedServerJars(config.Server.ServerDirectory, jarPath);
        TryCopyServerIcon(config.Server.ServerDirectory);
        await EnsureServerLogIsNotLockedAsync(config, cancellationToken).ConfigureAwait(false);

        Process process = new();
        process.StartInfo.FileName = config.Server.Java.ExecutablePath;
        AddJavaArguments(process.StartInfo, config, jarPath);
        process.StartInfo.WorkingDirectory = config.Server.ServerDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardInputEncoding = ServerCommandEncoding;
        process.StartInfo.StandardOutputEncoding = ServerCommandEncoding;
        process.StartInfo.StandardErrorEncoding = ServerCommandEncoding;

        try
        {
            process.Start();
            _javaServerProcess = process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private async Task WatchServerProcessExitAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || Interlocked.CompareExchange(ref _serverExitExpected, 0, 0) != 0)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (cancellationToken.IsCancellationRequested
                || Interlocked.CompareExchange(ref _serverExitExpected, 0, 0) != 0
                || !ReferenceEquals(process, _javaServerProcess)
                || _runtimeState != RuntimeState.Running)
            {
                return;
            }

            _minecraftServerReady = false;
            PauseCurrentSurvivalForStatistics();
            _runtimeState = RuntimeState.Stopped;

            CancellationTokenSource? sessionCts = _sessionCts;
            _sessionCts = null;
            lock (_backgroundTasksGate)
                _backgroundTasks = [];

            ResetIRCQueues();
            MinigameManager.StopMinigameLoops(this, true);

            try
            {
                sessionCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                sessionCts?.Dispose();
            }
            catch
            {
            }

            SafeCloseIRCSocket();
            _tokenStore.TryExportReadableJson();
            FlushStatisticsForShutdown();
            _javaServerProcess = null;
            try
            {
                process.Dispose();
            }
            catch
            {
            }

            _shellWindow?.AddServerLogLine("Minecraft server process exited unexpectedly. TwitchCraft was stopped.");
            UIThread.BeginInvoke(() => _shellModel.Navigate(ShellPage.Start));
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static void AddJavaArguments(ProcessStartInfo startInfo, BotConfig config, string jarPath)
    {
        bool enableNativeAccess =
            MinecraftVersionSupport.TryGetVersion(config.Server.MinecraftVersion, out MinecraftVersionSupport.MinecraftVersionInfo version)
            && version.RequiredJDK >= 25;

        startInfo.ArgumentList.Add("-Xmx" + config.Server.MemoryMaxGB.ToString(CultureInfo.InvariantCulture) + "G");
        startInfo.ArgumentList.Add("-Xms" + config.Server.MemoryMinGB.ToString(CultureInfo.InvariantCulture) + "G");

        if (enableNativeAccess)
            startInfo.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jarPath);
        startInfo.ArgumentList.Add("nogui");
    }

    private static void TryCopyServerIcon(string serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
            return;

        try
        {
            string sourcePath = Path.Combine(AppContext.BaseDirectory, "Assets", "server-icon.png");
            if (!File.Exists(sourcePath))
                sourcePath = Path.Combine(AppHelpers.GetExecutableDirectory(), "Assets", "server-icon.png");

            if (!File.Exists(sourcePath))
                return;

            Directory.CreateDirectory(serverDirectory);
            string destinationPath = Path.Combine(serverDirectory, "server-icon.png");
            if (File.Exists(destinationPath) && FilesHaveSameContent(sourcePath, destinationPath))
                return;

            File.Copy(sourcePath, destinationPath, true);
        }
        catch
        {
        }
    }

    private static bool FilesHaveSameContent(string firstPath, string secondPath)
    {
        FileInfo firstInfo = new(firstPath);
        FileInfo secondInfo = new(secondPath);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        byte[] firstBuffer = ArrayPool<byte>.Shared.Rent(8192);
        byte[] secondBuffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using FileStream firstStream = new(firstPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
            using FileStream secondStream = new(secondPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
            while (true)
            {
                int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
                int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
                if (firstRead != secondRead)
                    return false;

                if (firstRead == 0)
                    return true;

                if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                    return false;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(firstBuffer);
            ArrayPool<byte>.Shared.Return(secondBuffer);
        }
    }

    private async Task EnsureServerLogIsNotLockedAsync(BotConfig config, CancellationToken cancellationToken)
    {
        string latestLogPath = Path.Combine(config.Server.ServerDirectory, "logs", "latest.log");
        if (!IsFileLocked(latestLogPath))
            return;

        if (!ErrorHandling.ConfirmCloseRunningJavaAndRetry(_shellWindow))
            return;

        CloseLockingJavaProcesses(latestLogPath);
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
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void CloseLockingJavaProcesses(string lockedFilePath)
    {
        foreach (int processId in GetLockingProcessIds(lockedFilePath))
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

    private static unsafe IEnumerable<int> GetLockingProcessIds(string path)
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
                    if (IsMatchingLiveProcess(uniqueProcess))
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

    private static bool IsMatchingLiveProcess(RM_UNIQUE_PROCESS uniqueProcess)
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

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmStartSession")]
    private static unsafe partial int RMStartSession(out uint sessionHandle, int sessionFlags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmRegisterResources")]
    private static unsafe partial int RMRegisterResources(
        uint sessionHandle,
        uint fileCount,
        char** fileNames,
        uint applicationCount,
        RM_UNIQUE_PROCESS* applications,
        uint serviceCount,
        char** serviceNames);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmGetList")]
    private static unsafe partial int RMGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        RM_PROCESS_INFO* processInfo,
        ref uint rebootReasons);

    [LibraryImport("rstrtmgr.dll", EntryPoint = "RmEndSession")]
    private static partial int RMEndSession(uint sessionHandle);

    private async Task ClearPlayerSidebarAsync(CancellationToken cancellationToken)
    {
        if (!_minecraftServerReady)
        {
            lock (_playerGate)
            {
                _lastSidebarPlayers = [];
                _playerSidebarInitialized = false;
            }

            return;
        }

        await SendServerCommandsAsync(ClearPlayerSidebarCommands, cancellationToken).ConfigureAwait(false);

        lock (_playerGate)
        {
            _lastSidebarPlayers = [];
            _playerSidebarInitialized = false;
        }
    }

    private static bool SamePlayers(List<string> players, List<string> previousPlayers)
    {
        int count = players.Count;
        if (count != previousPlayers.Count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(players[i], previousPlayers[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task RefreshPlayerSidebarAsync(CancellationToken cancellationToken)
    {
        if (!MultiplayerEnabled || !_minecraftServerReady)
            return;

        List<string> players;
        List<string> previousPlayers;
        bool needsInitialization;

        lock (_playerGate)
        {
            needsInitialization = !_playerSidebarInitialized;
            if (_knownPlayers.Count == 0 && _lastSidebarPlayers.Count == 0)
                return;

            if (!needsInitialization && SamePlayers(_knownPlayers, _lastSidebarPlayers))
                return;

            players = _knownPlayers.Count == 0 ? [] : [.. _knownPlayers];
            previousPlayers = _lastSidebarPlayers.Count == 0 ? [] : [.. _lastSidebarPlayers];
        }

        if (players.Count == 0)
        {
            await ClearPlayerSidebarAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        List<string> commands = BuildPlayerSidebarCommands(players, previousPlayers, needsInitialization, UsesInlineTextComponentSyntax);
        if (commands.Count == 0)
            return;

        if (!await SendServerCommandsAsync(commands, cancellationToken).ConfigureAwait(false))
            return;

        lock (_playerGate)
        {
            _playerSidebarInitialized = true;
            _lastSidebarPlayers = [.. players];
        }
    }

    private static List<string> BuildPlayerSidebarCommands(List<string> players, List<string> previousPlayers, bool needsInitialization, bool usesInlineTextComponents)
    {
        const string objective = "tc_playerlist";
        const string healthObjective = "tc_health";

        List<string> commands = new((needsInitialization ? 9 : 0) + previousPlayers.Count + players.Count);
        if (needsInitialization)
        {
            string playerListDisplay = BuildScoreboardDisplayComponent("Player List:", usesInlineTextComponents);
            string healthDisplay = BuildScoreboardDisplayComponent("Health", usesInlineTextComponents);

            commands.Add("scoreboard objectives remove " + objective);
            commands.Add("scoreboard objectives remove " + healthObjective);
            commands.Add("scoreboard objectives add " + objective + " dummy " + playerListDisplay);
            commands.Add("scoreboard objectives add " + healthObjective + " health " + healthDisplay);
            commands.Add("scoreboard objectives modify " + objective + " displayname " + playerListDisplay);
            commands.Add("scoreboard objectives modify " + healthObjective + " displayname " + healthDisplay);
            commands.Add("scoreboard objectives modify " + healthObjective + " rendertype hearts");
            commands.Add("scoreboard objectives setdisplay sidebar " + objective);
            commands.Add("scoreboard objectives setdisplay list " + healthObjective);
        }

        int playerIndex = 0;
        foreach (string oldName in previousPlayers)
        {
            while (playerIndex < players.Count && PlayerNameComparer.Compare(players[playerIndex], oldName) < 0)
                playerIndex++;

            if (playerIndex >= players.Count || !PlayerNameComparer.Equals(players[playerIndex], oldName))
                commands.Add("scoreboard players reset " + oldName + " " + objective);
        }

        int previousIndex = 0;
        int score = players.Count;
        foreach (string player in players)
        {
            while (previousIndex < previousPlayers.Count && PlayerNameComparer.Compare(previousPlayers[previousIndex], player) < 0)
                previousIndex++;

            bool scoreChanged = needsInitialization ||
                previousIndex >= previousPlayers.Count ||
                !PlayerNameComparer.Equals(previousPlayers[previousIndex], player) ||
                previousPlayers.Count - previousIndex != score;

            if (scoreChanged)
                commands.Add("scoreboard players set " + player + " " + objective + " " + score.ToString(CultureInfo.InvariantCulture));

            score--;
        }

        return commands;
    }

    private static string BuildScoreboardDisplayComponent(string text, bool usesInlineTextComponents)
    {
        return usesInlineTextComponents
            ? "{text:'" + MinecraftCommandBuilder.EscapeSnbtString(text) + "'}"
            : "{\"text\":\"" + MinecraftCommandBuilder.EscapeJson(text) + "\"}";
    }

    private void QueueInitialPlayerSnapshot()
    {
        if (!TryGetQueuedSessionToken(requireMultiplayer: true, out CancellationToken token) ||
            Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            RefreshOnlinePlayerSnapshotNowAsync,
            () => Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 0),
            token: token);
    }

    private void QueuePlayerSidebarRefresh()
    {
        if (!TryGetQueuedSessionToken(requireMultiplayer: true, out CancellationToken token))
            return;

        int previous = Interlocked.CompareExchange(ref _playerSidebarRefreshQueued, 1, 0);
        if (previous != 0)
        {
            Interlocked.Exchange(ref _playerSidebarRefreshQueued, 2);
            return;
        }

        RunCoalescedQueuedSessionWork(
            RefreshPlayerSidebarAsync,
            () => Interlocked.CompareExchange(ref _playerSidebarRefreshQueued, 0, 1) == 1,
            () => Interlocked.Exchange(ref _playerSidebarRefreshQueued, 1),
            () => Interlocked.Exchange(ref _playerSidebarRefreshQueued, 0),
            onError: RecordPlayerSidebarRefreshFailure,
            token: token);
    }

    private static void ValidateConfig(BotConfig config)
    {
        if (config == null)
            throw new InvalidOperationException("Config is missing.");

        config.Server ??= new ServerConfig();
        config.Server.Java ??= new JavaConfig();
        config.Server.RCON ??= new RCONConfig();
        config.Twitch ??= new TwitchConfig();
        config.Settings ??= new StartingProfile();

        bool remoteController = config.Settings.RemoteControlEnabled;

        if (remoteController)
        {
            if (!ConfigurationStore.IsValidRemoteHost(config.Server.RemoteHost))
                throw new InvalidOperationException("Remote controller host is missing or invalid.");
        }
        else
        {
            string javaExecutablePath = (config.Server.Java.ExecutablePath ?? string.Empty).Trim();
            if (javaExecutablePath.Length == 0)
                throw new InvalidOperationException("Java executable path is missing.");

            if (!File.Exists(javaExecutablePath))
                throw new InvalidOperationException("Java executable path does not exist: " + javaExecutablePath);

            string serverDirectory = (config.Server.ServerDirectory ?? string.Empty).Trim();
            if (serverDirectory.Length == 0)
                throw new InvalidOperationException("Minecraft server directory is missing.");

            if (!Directory.Exists(serverDirectory))
                throw new InvalidOperationException("Minecraft server directory does not exist: " + serverDirectory);

            string jarPath = string.IsNullOrWhiteSpace(config.Server.JarPath)
                ? Path.Combine(serverDirectory, "server.jar")
                : config.Server.JarPath.Trim();
            if (!File.Exists(jarPath))
                throw new InvalidOperationException("Minecraft server jar path does not exist: " + jarPath);

            if (config.Server.MemoryMinGB <= 0 || config.Server.MemoryMaxGB <= 0 || config.Server.MemoryMinGB > config.Server.MemoryMaxGB || config.Server.MemoryMaxGB > 256)
                throw new InvalidOperationException("Minecraft server RAM must be between 1 and 256 GB, and minimum RAM less than or equal to maximum RAM.");

            if (!ConfigurationStore.IsValidBindIP(config.Server.BindIP))
                throw new InvalidOperationException("Minecraft server address is invalid.");
        }

        if (!IsValidPort(config.Server.Port))
            throw new InvalidOperationException("Minecraft server port must be between 1 and 65535.");

        if (!IsValidPort(config.Server.RCON.Port))
            throw new InvalidOperationException("RCON port must be between 1 and 65535.");

        if (!remoteController && config.Server.Port == config.Server.RCON.Port)
            throw new InvalidOperationException("Minecraft server port and RCON port cannot be the same.");

        string RCONPassword = config.Server.RCON.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(RCONPassword) || RCONPassword.Contains('\r') || RCONPassword.Contains('\n'))
            throw new InvalidOperationException("RCON password is missing or invalid.");

        if (string.IsNullOrWhiteSpace(config.Twitch.BotToken))
            throw new InvalidOperationException("Twitch bot token is missing.");

        if (string.IsNullOrWhiteSpace(config.Twitch.StreamerName))
            throw new InvalidOperationException("Twitch channel name is missing.");
    }

    private static bool IsValidPort(int port)
    {
        return port is >= 1 and <= 65535;
    }

    private static string GetRemoteControllerHost(BotConfig config)
    {
        string host = (config.Server.RemoteHost ?? string.Empty).Trim();
        return host.Length == 0 ? "127.0.0.1" : host;
    }

    private void ResetSessionState(bool resetStatistics)
    {
        ResetIRCQueues();
        if (resetStatistics)
        {
            ResetStatisticsForNewSession();
        }

        lock (_viewerGate)
        {
            _knownChatters = [];
            _viewerRewardSchedule = new(PlayerNameComparer);
        }

        lock (_playerGate)
        {
            _knownPlayers = [];
            _lastSidebarPlayers = [];
            _playerSidebarInitialized = false;
        }

        lock (_spectatorProbeGate)
        {
            _pendingGameTypeRequests.Clear();
            _spectatorPlayers.Clear();
            _lastSpectatorRefreshUtc = DateTime.MinValue;
            _spectatorSnapshotInitialized = false;
        }

        lock (_selectedItemProbeGate)
        {
            _pendingSelectedItemRequests.Clear();
        }

        lock (_respawnPositionProbeGate)
        {
            _pendingRespawnPositionRequests.Clear();
        }

        CompleteOnlinePlayerSnapshotRequest(false);
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers.Clear();
            Volatile.Write(ref _pendingServerProbeMarkerCount, 0);
        }

        ClearLightningCooldown();
        ClearGlobalGameCommandCooldown();

        Interlocked.Exchange(ref _playerSidebarRefreshQueued, 0);
        Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 0);
        Interlocked.Exchange(ref _onlinePlayerSnapshotQueued, 0);
        Interlocked.Exchange(ref _suppressedOnlinePlayersLogLines, 0);
        Interlocked.Exchange(ref _trackedPlayerGamemodeRefreshQueued, 0);
        Interlocked.Exchange(ref _trackedPlayerRespawnPositionRefreshQueued, 0);
        Interlocked.Exchange(ref _deathScoreObjectiveQueued, 0);
        Interlocked.Exchange(ref _deathScoreObjectiveReady, 0);
        Interlocked.Exchange(ref _trackedPlayerDeathScoreRefreshQueued, 0);
        Volatile.Write(ref _minecraftQueryUnavailableUntilTicks, 0);
        _minecraftServerReady = false;

        _shellWindow?.ClearServerLogView();
        _shellWindow?.ClearChatLogView();
        _shellWindow?.DisplayNormalizedViewerList([]);
    }

    private void SafeStopProcess(bool waitBriefly)
    {
        Process? process = _javaServerProcess;
        _javaServerProcess = null;
        if (process == null)
            return;

        try
        {
            if (waitBriefly && !process.HasExited)
                process.WaitForExit(5000);
        }
        catch
        {
        }

        try
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }

        SafeDisposeProcess(process);
    }

    private async Task SafeStopProcessAsync(bool waitBriefly)
    {
        Process? process = _javaServerProcess;
        _javaServerProcess = null;
        if (process == null)
            return;

        try
        {
            if (waitBriefly)
                await WaitForProcessExitAsync(process, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
                await WaitForProcessExitAsync(process, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        SafeDisposeProcess(process);
    }

    private static async Task WaitForProcessExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            if (process.HasExited)
                return;

            Task exitTask = process.WaitForExitAsync();
            Task completed = await Task.WhenAny(exitTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (ReferenceEquals(completed, exitTask))
            {
                await exitTask.ConfigureAwait(false);
                return;
            }
        }
        catch
        {
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            process.Kill();
        }
    }

    private static void SafeDisposeProcess(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    private void SafeSynchronousCleanup()
    {
        PauseCurrentSurvivalForStatistics();

        try
        {
            MinigameManager.StopMinigameLoops(this);
        }
        catch
        {
        }

        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
        }

        SafeCloseIRCSocket();
        SafeStopProcess(false);

        _tokenStore.TryExportReadableJson();
        FlushStatisticsForShutdown();
        CloseDataStoreConnections();
    }

    private static string NormalizeUser(string? user) => CommandUserHelper.NormalizeUsername(user);

    private List<string> GetKnownPlayersList()
    {
        lock (_playerGate)
        {
            return [.. _knownPlayers];
        }
    }

    // ===== Minecraft command I/O and version helpers =====
    public async Task<bool> ExecuteMinecraftCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (HasEmbeddedServerCommandLineBreak(TrimServerCommand(command.AsSpan())))
        {
            _shellWindow?.AddChatLogLine("Manual command was not sent because it must be a single line.");
            return false;
        }

        try
        {
            using CancellationTokenSource timeoutCts = new(ManualCommandTimeout);
            bool sent = await SendServerCommandAsync(
                command,
                timeoutCts.Token,
                applyRemoteTimeout: false).ConfigureAwait(false);
            if (!sent)
            {
                _shellWindow?.AddChatLogLine("Manual command could not be sent because the Minecraft server is not running.");
            }

            return sent;
        }
        catch (OperationCanceledException)
        {
            _shellWindow?.AddChatLogLine("Manual command send timed out.");
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Failed to send manual command", ex));
        }

        return false;
    }

    public async Task<bool> SendServerCommandAsync(string command, CancellationToken cancellationToken, bool applyRemoteTimeout = true)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string commandText = NormalizeSingleServerCommand(command);
        if (commandText.Length == 0)
            return false;

        BotConfig? activeConfig = _activeConfig;
        if (activeConfig?.Settings.RemoteControlEnabled == true)
            return await SendRemoteServerCommandAsync(activeConfig, commandText, cancellationToken, applyRemoteTimeout).ConfigureAwait(false);

        Process? process = _javaServerProcess;
        if (process == null)
        {
            return false;
        }

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(process, _javaServerProcess) || process.HasExited)
            {
                return false;
            }

            return await WriteSingleServerCommandNoLockAsync(process, commandText, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Minecraft command write failed", ex));
            return false;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    public async Task<bool> SendServerCommandsAsync(IEnumerable<string> commands, CancellationToken cancellationToken)
    {
        if (commands == null)
        {
            return false;
        }

        if ((commands is ICollection<string> collection && collection.Count == 0) ||
            (commands is IReadOnlyCollection<string> readOnlyCollection && readOnlyCollection.Count == 0))
        {
            return false;
        }

        if (TryGetSingleServerCommand(commands, out string singleCommand))
        {
            return await SendServerCommandAsync(singleCommand, cancellationToken).ConfigureAwait(false);
        }

        List<string> snapshot = SnapshotServerCommands(commands);
        if (snapshot.Count == 0)
            return false;

        if (snapshot.Count == 1)
            return await SendServerCommandAsync(snapshot[0], cancellationToken).ConfigureAwait(false);

        BotConfig? activeConfig = _activeConfig;
        if (activeConfig?.Settings.RemoteControlEnabled == true)
            return await SendRemoteServerCommandsAsync(activeConfig, snapshot, cancellationToken).ConfigureAwait(false);

        Process? process = _javaServerProcess;
        if (process == null)
        {
            return false;
        }

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(process, _javaServerProcess) || process.HasExited)
            {
                return false;
            }

            return await WriteTrimmedServerCommandListNoLockAsync(process, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Minecraft command write failed", ex));
            return false;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<bool> SendRemoteServerCommandAsync(BotConfig config, string command, CancellationToken cancellationToken, bool applyTimeout = true)
    {
        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? timeoutCts = null;
        CancellationToken commandToken = cancellationToken;
        if (applyTimeout)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ManualCommandTimeout);
            commandToken = timeoutCts.Token;
        }

        try
        {
            return await MinecraftRCONClient.ExecuteCommandAsync(
                GetRemoteControllerHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                command,
                commandToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _shellWindow?.AddServerLogLine("RCON command timed out.");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("RCON command failed", ex));
            return false;
        }
        finally
        {
            timeoutCts?.Dispose();
            _serverWriteGate.Release();
        }
    }

    private async Task<bool> SendRemoteServerCommandsAsync(BotConfig config, List<string> commands, CancellationToken cancellationToken)
    {
        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetRemoteCommandTimeout(commands.Count));
            return await MinecraftRCONClient.ExecuteCommandsAsync(
                GetRemoteControllerHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                commands,
                timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _shellWindow?.AddServerLogLine("RCON command timed out.");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("RCON command failed", ex));
            return false;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<string?> ExecuteRemoteServerQueryAsync(string command, CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Settings.RemoteControlEnabled != true)
            return null;

        string commandText = NormalizeSingleServerCommand(command);
        if (commandText.Length == 0)
            return null;

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ManualCommandTimeout);
            return await MinecraftRCONClient.ExecuteQueryAsync(
                GetRemoteControllerHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                commandText,
                timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _shellWindow?.AddServerLogLine("RCON query timed out.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("RCON query failed", ex));
            return null;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<List<string?>?> ExecuteRemoteServerQueriesAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Settings.RemoteControlEnabled != true || commands.Count == 0)
            return null;

        List<string> commandTexts = new(commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            string commandText = NormalizeSingleServerCommand(commands[i]);
            if (commandText.Length > 0)
                commandTexts.Add(commandText);
        }

        if (commandTexts.Count == 0)
            return null;

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetRemoteCommandTimeout(commandTexts.Count));
            return await MinecraftRCONClient.ExecuteQueriesAsync(
                GetRemoteControllerHost(config),
                config.Server.RCON.Port,
                config.Server.RCON.Password,
                commandTexts,
                timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _shellWindow?.AddServerLogLine("RCON query timed out.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("RCON query failed", ex));
            return null;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private static TimeSpan GetRemoteCommandTimeout(int commandCount)
    {
        if (commandCount <= 1)
            return ManualCommandTimeout;

        double milliseconds = ManualCommandTimeout.TotalMilliseconds + Math.Min(commandCount - 1, 50) * 200.0;
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, 15000.0));
    }

    private static bool TryGetSingleServerCommand(IEnumerable<string> commands, out string command)
    {
        if (commands is IList<string> list && list.Count == 1)
        {
            command = list[0];
            return true;
        }

        if (commands is IReadOnlyList<string> readOnlyList && readOnlyList.Count == 1)
        {
            command = readOnlyList[0];
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static string NormalizeSingleServerCommand(string command)
    {
        ReadOnlySpan<char> trimmed = TrimServerCommand(command.AsSpan());
        if (trimmed.IsEmpty || HasEmbeddedServerCommandLineBreak(trimmed))
            return string.Empty;

        return trimmed.Length == command.Length ? command : trimmed.ToString();
    }

    private static ReadOnlySpan<char> TrimServerCommand(ReadOnlySpan<char> command)
    {
        int start = 0;
        int end = command.Length - 1;
        while (start <= end && IsServerCommandBoundaryChar(command[start]))
            start++;

        while (end >= start && IsServerCommandBoundaryChar(command[end]))
            end--;

        return start > end ? [] : command.Slice(start, end - start + 1);
    }

    private static bool IsServerCommandBoundaryChar(char value)
    {
        return char.IsWhiteSpace(value) || value == '\uFEFF';
    }

    private static bool HasEmbeddedServerCommandLineBreak(ReadOnlySpan<char> command)
        => command.Contains('\r') || command.Contains('\n');

    private Task<bool> WriteSingleServerCommandNoLockAsync(Process process, string command, CancellationToken cancellationToken)
    {
        ReadOnlySpan<char> trimmedCommand = TrimServerCommand(command.AsSpan());
        if (trimmedCommand.IsEmpty || HasEmbeddedServerCommandLineBreak(trimmedCommand))
            return Task.FromResult(false);

        int byteCount = ServerCommandEncoding.GetByteCount(trimmedCommand) + ServerCommandNewLineBytes.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        int written = ServerCommandEncoding.GetBytes(trimmedCommand, rented);
        ServerCommandNewLineBytes.CopyTo(rented.AsSpan(written));
        written += ServerCommandNewLineBytes.Length;
        return WriteEncodedServerCommandPayloadNoLockAsync(process.StandardInput.BaseStream, rented, written, cancellationToken);
    }

    private static List<string> SnapshotServerCommands(IEnumerable<string> commands)
    {
        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(commands, out int count) ? count : 0;

        List<string> snapshot = new(capacity);
        foreach (string command in commands)
        {
            string raw = command ?? string.Empty;
            ReadOnlySpan<char> trimmed = TrimServerCommand(raw.AsSpan());
            if (!trimmed.IsEmpty && !HasEmbeddedServerCommandLineBreak(trimmed))
                snapshot.Add(trimmed.Length == raw.Length ? raw : trimmed.ToString());
        }

        return snapshot;
    }

    private Task<bool> WriteTrimmedServerCommandListNoLockAsync(Process process, List<string> commands, CancellationToken cancellationToken)
    {
        int count = commands.Count;
        int byteCount = 0;
        for (int i = 0; i < count; i++)
            byteCount += ServerCommandEncoding.GetByteCount(commands[i]) + ServerCommandNewLineBytes.Length;

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            string command = commands[i];
            written += ServerCommandEncoding.GetBytes(command.AsSpan(), rented.AsSpan(written));
            ServerCommandNewLineBytes.CopyTo(rented.AsSpan(written));
            written += ServerCommandNewLineBytes.Length;
        }

        return WriteEncodedServerCommandPayloadNoLockAsync(process.StandardInput.BaseStream, rented, written, cancellationToken);
    }

    private async Task<bool> WriteEncodedServerCommandPayloadNoLockAsync(Stream baseStream, byte[] rented, int written, CancellationToken cancellationToken)
    {
        bool writeCompleted = false;

        try
        {
            await baseStream.WriteAsync(rented.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
            writeCompleted = true;
            await baseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException ex) when (writeCompleted)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Minecraft command flush failed after the command data was written", ex));
            return true;
        }
        catch (Exception ex) when (writeCompleted && (ex is ObjectDisposedException or InvalidOperationException))
        {
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async Task SendTellrawAsync(string selector, string message, string color, bool bold, CancellationToken cancellationToken)
    {
        await SendServerCommandAsync(
            MinecraftCommandBuilder.Tellraw(string.IsNullOrWhiteSpace(selector) ? "@a" : selector, message, color, bold, UsesInlineTextComponentSyntax),
            cancellationToken).ConfigureAwait(false);
    }

    public bool HasOtherKnownPlayer(string excludedPlayerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(excludedPlayerName, out string excludedName))
            return false;

        lock (_playerGate)
        {
            foreach (string player in _knownPlayers)
            {
                if (!string.Equals(player, excludedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public async Task SendTellrawToOthersAsync(ResolvedTarget target, string message, string color, bool bold, CancellationToken cancellationToken)
    {
        if (!MultiTargetingEnabled || target == null || string.IsNullOrWhiteSpace(message) || target.PlayerCount != 1)
        {
            return;
        }

        if (!MinecraftNameHelper.TryNormalizePlayerName(target.MinecraftName, out string excludedName))
        {
            return;
        }

        if (!HasOtherKnownPlayer(excludedName))
        {
            return;
        }

        string selector = MinecraftCommandBuilder.AllExceptPlayerSelector(excludedName);
        await SendTellrawAsync(selector, message, color, bold, cancellationToken).ConfigureAwait(false);
    }

    public EffectDefinition GetRandomEffect()
    {
        string version = CurrentMinecraftVersion;
        List<EffectDefinition> availableEffects;
        lock (_effectCacheGate)
        {
            if (!string.Equals(_cachedSupportedEffectsVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                List<EffectDefinition> effects = new(_effectList.Count);
                foreach (EffectDefinition effect in _effectList)
                {
                    if (MinecraftVersionSupport.SupportsStatusEffect(version, effect.ID))
                    {
                        effects.Add(effect);
                    }
                }

                _cachedSupportedEffects = effects.Count == 0 ? _effectList : effects;
                _cachedSupportedEffectsVersion = version;
            }

            availableEffects = _cachedSupportedEffects;
        }

        return availableEffects[Random.Shared.Next(availableEffects.Count)];
    }

    public string GetRandomLootTable() => _lootList[Random.Shared.Next(_lootList.Count)];

    public string GetRandomMob() => _mobList[Random.Shared.Next(_mobList.Count)];

    public string CurrentMinecraftVersion => _currentMinecraftVersion;

    private bool TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo versionInfo)
    {
        string version = CurrentMinecraftVersion;
        if (!string.Equals(_cachedMinecraftFeatureVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            _cachedMinecraftFeatureInfo = MinecraftVersionSupport.TryGetVersion(version, out MinecraftVersionSupport.MinecraftVersionInfo resolved)
                ? resolved
                : null;
            _cachedMinecraftFeatureVersion = version;
        }

        if (_cachedMinecraftFeatureInfo != null)
        {
            versionInfo = _cachedMinecraftFeatureInfo;
            return true;
        }

        versionInfo = null!;
        return false;
    }

    public bool UsesItemComponentsSyntax
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesItemComponents;
        }
    }

    public bool UsesInlineTextComponentSyntax
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesInlineTextComponents;
        }
    }

    public bool UsesModernEntityAttributeNbt
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.DataPackFormatMajor >= 48;
        }
    }

    public bool UsesNamespacedGameRules
    {
        get
        {
            return TryGetCurrentMinecraftVersionInfo(out MinecraftVersionSupport.MinecraftVersionInfo version)
                && version.UsesNamespacedGameRules;
        }
    }

    public string MobLootGameRuleName => UsesNamespacedGameRules ? "minecraft:mob_drops" : "doMobLoot";

    public bool MinecraftServerReady => _minecraftServerReady;

}
