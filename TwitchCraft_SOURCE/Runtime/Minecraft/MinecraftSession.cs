using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal sealed class MinecraftSession
{
    private Process? _process;
    private volatile bool _serverReady;
    private int _rconHealthy;
    private int _serverExitExpected;

    internal SemaphoreSlim WriteGate { get; } = new(1, 1);

    internal Process? Process
    {
        get => _process;
        set => _process = value;
    }

    internal bool ServerReady
    {
        get => _serverReady;
        set => _serverReady = value;
    }

    internal bool RCONHealthy
    {
        get => Volatile.Read(ref _rconHealthy) != 0;
        set => Volatile.Write(ref _rconHealthy, value ? 1 : 0);
    }

    internal bool ServerExitExpected
    {
        get => Volatile.Read(ref _serverExitExpected) != 0;
        set => Interlocked.Exchange(ref _serverExitExpected, value ? 1 : 0);
    }

    internal bool ProcessRunning
        => _process is { } process && TryGetProcessRunning(process, out bool running) && running;

    private void ClearProcessIfCurrent(Process process)
        => Interlocked.CompareExchange(ref _process, null, process);

    internal void StopProcessSafe()
    {
        Process? process = _process;
        if (process == null)
            return;

        try
        {
            if (TryGetProcessRunning(process, out bool running) && running)
            {
                KillProcessTree(process);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }

        if (!TryGetProcessRunning(process, out bool stillRunning) || stillRunning)
            return;

        ClearProcessIfCurrent(process);
        DisposeProcessSafe(process);
    }

    internal async Task StopProcessSafeAsync(bool waitBriefly, TimeSpan gracefulShutdownTimeout)
    {
        Process? process = _process;
        if (process == null)
            return;

        if (waitBriefly)
            await WaitForProcessExitAsync(process, gracefulShutdownTimeout).ConfigureAwait(false);

        try
        {
            if (TryGetProcessRunning(process, out bool running) && running)
            {
                KillProcessTree(process);
                await WaitForProcessExitAsync(process, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        if (!TryGetProcessRunning(process, out bool stillRunning) || stillRunning)
            return;

        ClearProcessIfCurrent(process);
        DisposeProcessSafe(process);
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
                await exitTask.ConfigureAwait(false);
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

    private static bool TryGetProcessRunning(Process process, out bool running)
    {
        try
        {
            running = !process.HasExited;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            running = false;
            return ex is InvalidOperationException;
        }
    }

    private static void DisposeProcessSafe(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }
}
