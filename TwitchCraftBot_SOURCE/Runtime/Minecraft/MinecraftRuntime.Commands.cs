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

    internal static TimeSpan GetRemoteCommandTimeout(int commandCount)
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

    internal static string NormalizeSingleServerCommand(string command)
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

    internal static List<string> SnapshotServerCommands(IEnumerable<string> commands)
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

}
