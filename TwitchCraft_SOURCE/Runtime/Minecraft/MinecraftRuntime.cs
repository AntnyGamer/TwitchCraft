using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly UTF8Encoding ServerCommandEncoding = new(false);
    private static readonly byte[] ServerCommandNewLineBytes = ServerCommandEncoding.GetBytes(Environment.NewLine);
    private static readonly string[] ClearPlayerSidebarCommands = ["scoreboard objectives remove tc_playerlist", "scoreboard objectives remove tc_health"];
    private static readonly TimeSpan ServerLogUnlockWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopCommandTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ManualCommandTimeout = TimeSpan.FromSeconds(5);
    private int _initialPlayerSnapshotQueued;
    private int _suppressedOnlinePlayersLogLines;

    private async Task TryStopServerAsync()
    {
        using CancellationTokenSource timeoutCts = new(StopCommandTimeout);
        try
        {
            await SendServerCommandAsync("stop", timeoutCts.Token).ConfigureAwait(false);
            if (_minecraftSession.Process is { HasExited: false })
            {
                await Task.Delay(500, timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    internal async Task EnsureRCONAsync(TwitchCraftConfig config, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RCONTimeout);
        string host = GetRCONHost(config);
        _ = await MinecraftRCONClient.ExecuteQueryAsync(
            host,
            config.Server.RCON.Port,
            config.Server.RCON.Password,
            "list",
            timeoutCts.Token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Remote controller could not authenticate with RCON. Check the host, RCON port, and RCON password.");
        _minecraftSession.ServerReady = true;
        _minecraftSession.RCONHealthy = true;
        await RecoverSlaughterGameRuleAsync(cancellationToken).ConfigureAwait(false);
        if (!MultiplayerEnabled)
        {
            await ClearSidebarAsync(cancellationToken).ConfigureAwait(false);
        }

        _shellWindow?.AddServerLogLine("Remote controller connected to " + host + ":" + config.Server.RCON.Port.ToString(CultureInfo.InvariantCulture) + ".");
        QueueFirstSnapshot();
        QueueSnapshot();
        QueueGamemode();
        QueueDeathScore();
    }

    private async Task RecoverSlaughterGameRuleAsync(CancellationToken cancellationToken)
    {
        if (!await SendServerCommandAsync(GameplayCommands.SlaughterRestoreCommand(MobLootGameRuleName), cancellationToken).ConfigureAwait(false))
        {
            AddServerLogLine("Mob loot gamerule recovery could not be confirmed.");
        }
    }

    internal async Task StartServerAsync(TwitchCraftConfig config, CancellationToken cancellationToken)
    {
        if (_minecraftSession.Process is { HasExited: false })
        {
            throw new InvalidOperationException("The previous Minecraft server process is still running.");
        }

        string jarPath = string.IsNullOrWhiteSpace(config.Server.JarPath)
            ? Path.Combine(config.Server.ServerDirectory, "server.jar")
            : config.Server.JarPath;
        if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException("Minecraft server jar was not found.", jarPath);
        }

        Directory.CreateDirectory(config.Server.ServerDirectory);
        ServerPropertyEditor.CleanupServerJars(config.Server.ServerDirectory, jarPath);
        CopyServerIcon(config.Server.ServerDirectory);
        await UnlockLogAsync(config, cancellationToken).ConfigureAwait(false);
        Process process = new();
        process.StartInfo.FileName = config.Server.Java.ExecutablePath;
        AddJavaArgs(process.StartInfo, config, jarPath);
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
            _minecraftSession.Process = process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private async Task WatchServerAsync(CancellationToken cancellationToken)
    {
        Process? process = _minecraftSession.Process;
        if (process == null)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || _minecraftSession.ServerExitExpected)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (cancellationToken.IsCancellationRequested
                || _minecraftSession.ServerExitExpected
                || !ReferenceEquals(process, _minecraftSession.Process)
                || _runtimeState != RuntimeState.Running)
            {
                return;
            }

            _minecraftSession.ServerReady = false;
            Statistics.PauseSurvival();
            _runtimeState = RuntimeState.Stopped;

            CancellationTokenSource? sessionCts = _sessionCts;
            _sessionCts = null;
            _backgroundTaskTracker.Clear();
            ResetQueues();
            MinigameManager.StopLoops(this, true);

            try
            {
                sessionCts?.Cancel();
            }
            catch
            {
            }
            try
            {
                if (Commands.ResetHeartEffectsAsync != null)
                {
                    await Commands.ResetHeartEffectsAsync(null, true, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Failed to clear heart effects after server exit", ex);
            }

            try
            {
                sessionCts?.Dispose();
            }
            catch
            {
            }

            _twitchSession.CloseSocket();
            Tokens.TryExportJson();
            StatisticsService.FlushForShutdown();
            _minecraftSession.Process = null;
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

    private static void AddJavaArgs(ProcessStartInfo startInfo, TwitchCraftConfig config, string jarPath)
    {
        bool enableNativeAccess = MinecraftVersionSupport.GetVersion(config.Server.MinecraftVersion).RequiredJDK >= 25;
        startInfo.ArgumentList.Add("-Xmx" + config.Server.MemoryMaxGB.ToString(CultureInfo.InvariantCulture) + "G");
        startInfo.ArgumentList.Add("-Xms" + config.Server.MemoryMinGB.ToString(CultureInfo.InvariantCulture) + "G");

        if (enableNativeAccess)
        {
            startInfo.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");
        }

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(jarPath);
        startInfo.ArgumentList.Add("nogui");
    }

    private static void CopyServerIcon(string serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return;
        }

        try
        {
            using Stream? source = typeof(MainHandler).Assembly.GetManifestResourceStream("TwitchCraft.server-icon.png");
            if (source == null)
            {
                return;
            }

            Directory.CreateDirectory(serverDirectory);
            string destinationPath = Path.Combine(serverDirectory, "server-icon.png");
            if (File.Exists(destinationPath) && FilesMatch(source, destinationPath))
            {
                return;
            }

            source.Position = 0;
            using FileStream destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }
        catch
        {
        }
    }

    private static bool FilesMatch(Stream firstStream, string secondPath)
    {
        FileInfo secondInfo = new(secondPath);
        if (firstStream.Length != secondInfo.Length)
        {
            return false;
        }

        byte[] firstBuffer = ArrayPool<byte>.Shared.Rent(8192);
        byte[] secondBuffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using FileStream secondStream = new(secondPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
            while (true)
            {
                int firstRead = firstStream.Read(firstBuffer, 0, 8192);
                int secondRead = secondStream.ReadAtLeast(secondBuffer.AsSpan(0, firstRead), firstRead, throwOnEndOfStream: false);
                if (firstRead == 0)
                {
                    return secondStream.ReadByte() == -1;
                }

                if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                {
                    return false;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(firstBuffer);
            ArrayPool<byte>.Shared.Return(secondBuffer);
        }
    }
}
