using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        timeoutCts.CancelAfter(RCONTimeout);

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

    internal async Task StartJavaServerAsync(BotConfig config, CancellationToken cancellationToken)
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
            {
                _backgroundTasks.Clear();
            }

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
        bool enableNativeAccess = MinecraftVersionSupport.GetVersion(config.Server.MinecraftVersion).RequiredJDK >= 25;

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

}
