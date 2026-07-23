using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
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

        if (!ConfigurationStore.TryNormalizeRconPassword(config.Server.RCON.Password, out string normalizedRCONPassword))
            throw new InvalidOperationException("RCON password is missing or invalid.");

        config.Server.RCON.Password = normalizedRCONPassword;

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

    private void ResetSessionState()
    {
        ResetIRCQueues();

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

    private void SafeStopProcess()
    {
        Process? process = _javaServerProcess;
        _javaServerProcess = null;
        if (process == null)
            return;

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
        SafeStopProcess();

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

}
