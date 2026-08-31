using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    public async Task<bool> RunMinecraftCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (HasLineBreak(TrimCommand(command.AsSpan())))
        {
            _shellWindow?.AddChatLogLine("Manual command was not sent because it must be a single line.");
            return false;
        }

        try
        {
            using CancellationTokenSource timeoutCts = new(RemoteControlEnabled ? RCONTimeout : ManualCommandTimeout);
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
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Failed to send manual command", ex));
        }

        return false;
    }

    public async Task<bool> SendServerCommandAsync(string command, CancellationToken cancellationToken, bool applyRemoteTimeout = true)
    {
        string commandText = CleanServerCommand(command);
        if (commandText.Length == 0)
            return false;

        BotConfig? activeConfig = _activeConfig;
        if (activeConfig?.Settings.RemoteControlEnabled == true)
            return await SendRconCommandAsync(activeConfig, commandText, cancellationToken, applyRemoteTimeout).ConfigureAwait(false);

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

            return await WriteCommandNoLockAsync(process, commandText, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Minecraft command write failed", ex));
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

        if (TryGetSingleCommand(commands, out string singleCommand))
        {
            return await SendServerCommandAsync(singleCommand, cancellationToken).ConfigureAwait(false);
        }

        List<string> snapshot = SnapshotCommands(commands);
        if (snapshot.Count == 0)
            return false;

        if (snapshot.Count == 1)
            return await SendServerCommandAsync(snapshot[0], cancellationToken).ConfigureAwait(false);

        BotConfig? activeConfig = _activeConfig;
        if (activeConfig?.Settings.RemoteControlEnabled == true)
            return await SendRconCommandsAsync(activeConfig, snapshot, cancellationToken).ConfigureAwait(false);

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

            return await WriteCommandsNoLockAsync(process, snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Minecraft command write failed", ex));
            return false;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<bool> SendRconCommandAsync(BotConfig config, string command, CancellationToken cancellationToken, bool applyTimeout = true)
    {
        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? timeoutCts = null;
        CancellationToken commandToken = cancellationToken;
        if (applyTimeout)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RCONTimeout);
            commandToken = timeoutCts.Token;
        }

        try
        {
            return await MinecraftRCONClient.ExecuteCommandAsync(
                GetRconHost(config),
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
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("RCON command failed", ex));
            return false;
        }
        finally
        {
            timeoutCts?.Dispose();
            _serverWriteGate.Release();
        }
    }

    private async Task<bool> SendRconCommandsAsync(BotConfig config, List<string> commands, CancellationToken cancellationToken)
    {
        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetRconTimeout(commands.Count));
            return await MinecraftRCONClient.ExecuteCommandsAsync(
                GetRconHost(config),
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
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("RCON command failed", ex));
            return false;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<string?> ExecuteRconQueryAsync(string command, CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Settings.RemoteControlEnabled != true)
            return null;

        string commandText = CleanServerCommand(command);
        if (commandText.Length == 0)
            return null;

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(RCONTimeout);
            return await MinecraftRCONClient.ExecuteQueryAsync(
                GetRconHost(config),
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
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("RCON query failed", ex));
            return null;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private async Task<List<string?>?> ExecuteRconQueriesAsync(List<string> commands, CancellationToken cancellationToken)
    {
        BotConfig? config = _activeConfig;
        if (config?.Settings.RemoteControlEnabled != true || commands.Count == 0)
            return null;

        List<string> commandTexts = new(commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            string commandText = CleanServerCommand(commands[i]);
            if (commandText.Length > 0)
                commandTexts.Add(commandText);
        }

        if (commandTexts.Count == 0)
            return null;

        await _serverWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetRconTimeout(commandTexts.Count));
            return await MinecraftRCONClient.ExecuteQueriesAsync(
                GetRconHost(config),
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
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("RCON query failed", ex));
            return null;
        }
        finally
        {
            _serverWriteGate.Release();
        }
    }

    private TimeSpan GetRconTimeout(int commandCount)
        => GetRconTimeout(commandCount, RCONTimeout);

    internal static TimeSpan GetRconTimeout(int commandCount, TimeSpan baseTimeout)
    {
        if (commandCount <= 1)
            return baseTimeout;

        double milliseconds = baseTimeout.TotalMilliseconds + Math.Min(commandCount - 1, 50) * 200.0;
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, baseTimeout.TotalMilliseconds + 10000.0));
    }

    private static bool TryGetSingleCommand(IEnumerable<string> commands, out string command)
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

    internal static string CleanServerCommand(string command)
    {
        ReadOnlySpan<char> trimmed = TrimCommand(command.AsSpan());
        if (trimmed.IsEmpty || HasLineBreak(trimmed))
            return string.Empty;

        return trimmed.Length == command.Length ? command : trimmed.ToString();
    }

    private static ReadOnlySpan<char> TrimCommand(ReadOnlySpan<char> command)
    {
        int start = 0;
        int end = command.Length - 1;
        while (start <= end && IsCommandBoundary(command[start]))
            start++;

        while (end >= start && IsCommandBoundary(command[end]))
            end--;

        return start > end ? [] : command.Slice(start, end - start + 1);
    }

    private static bool IsCommandBoundary(char value)
        => char.IsWhiteSpace(value) || value == '\uFEFF';

    private static bool HasLineBreak(ReadOnlySpan<char> command)
        => command.Contains('\r') || command.Contains('\n');

    private Task<bool> WriteCommandNoLockAsync(Process process, string normalizedCommand, CancellationToken cancellationToken)
    {
        ReadOnlySpan<char> commandSpan = normalizedCommand.AsSpan();
        int byteCount = ServerCommandEncoding.GetByteCount(commandSpan) + ServerCommandNewLineBytes.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        int written = ServerCommandEncoding.GetBytes(commandSpan, rented);
        ServerCommandNewLineBytes.CopyTo(rented.AsSpan(written));
        written += ServerCommandNewLineBytes.Length;
        return WriteBytesNoLockAsync(process.StandardInput.BaseStream, rented, written, cancellationToken);
    }

    internal static List<string> SnapshotCommands(IEnumerable<string> commands)
    {
        int capacity = System.Linq.Enumerable.TryGetNonEnumeratedCount(commands, out int count) ? count : 0;

        List<string> snapshot = new(capacity);
        foreach (string command in commands)
        {
            string raw = command ?? string.Empty;
            ReadOnlySpan<char> trimmed = TrimCommand(raw.AsSpan());
            if (!trimmed.IsEmpty && !HasLineBreak(trimmed))
                snapshot.Add(trimmed.Length == raw.Length ? raw : trimmed.ToString());
        }

        return snapshot;
    }

    private Task<bool> WriteCommandsNoLockAsync(Process process, List<string> commands, CancellationToken cancellationToken)
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

        return WriteBytesNoLockAsync(process.StandardInput.BaseStream, rented, written, cancellationToken);
    }

    private async Task<bool> WriteBytesNoLockAsync(Stream baseStream, byte[] rented, int written, CancellationToken cancellationToken)
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
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Minecraft command flush failed after the command data was written", ex));
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

}
