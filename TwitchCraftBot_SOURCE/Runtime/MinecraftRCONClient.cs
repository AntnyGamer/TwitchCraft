using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

internal static class MinecraftRCONClient
{
    private const int AuthPacketType = 3;
    private const int CommandPacketType = 2;
    private const int AuthResponsePacketType = 2;
    private const int ResponseValuePacketType = 0;
    private const int MaxPacketLength = 1_048_576;
    private static readonly UTF8Encoding RCONEncoding = new(false);
    private static readonly SemaphoreSlim ConnectionGate = new(1, 1);
    private static TcpClient? _client;
    private static NetworkStream? _stream;
    private static byte[]? _packetLengthBuffer;
    private static string _host = string.Empty;
    private static string _password = string.Empty;
    private static int _port;
    private static int _nextRequestId = Environment.TickCount & 0x3FFFFFFF;

    public static Task<bool> ExecuteCommandAsync(string host, int port, string password, string command, CancellationToken cancellationToken)
        => InvalidRequest(host, port, password) || string.IsNullOrWhiteSpace(command)
            ? Task.FromResult(false)
            : WithConnectionAsync(host.Trim(), port, password, async token =>
            {
                await WriteCommandAsync(command, token).ConfigureAwait(false);
                return true;
            }, false, cancellationToken);

    public static Task<bool> ExecuteCommandsAsync(string host, int port, string password, IReadOnlyList<string> commands, CancellationToken cancellationToken)
        => InvalidRequest(host, port, password) || commands.Count == 0
            ? Task.FromResult(false)
            : WithConnectionAsync(host.Trim(), port, password, async token =>
            {
                await WriteCommandsAsync(commands, token).ConfigureAwait(false);
                return true;
            }, false, cancellationToken);

    public static Task<string?> ExecuteQueryAsync(string host, int port, string password, string command, CancellationToken cancellationToken)
        => InvalidRequest(host, port, password) || string.IsNullOrWhiteSpace(command)
            ? Task.FromResult<string?>(null)
            : WithConnectionAsync<string?>(host.Trim(), port, password, token => QueryAsync(command, token), null, cancellationToken);

    public static async Task<List<string?>?> ExecuteQueriesAsync(string host, int port, string password, IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        if (InvalidRequest(host, port, password) || commands.Count == 0)
            return null;

        string normalizedHost = host.Trim();
        await ConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<string?> responses = new(commands.Count);
            bool needsConnection = true;
            foreach (string command in commands)
            {
                if (string.IsNullOrWhiteSpace(command))
                {
                    responses.Add(null);
                    continue;
                }

                try
                {
                    if (needsConnection && !await EnsureConnectedAsync(normalizedHost, port, password, cancellationToken).ConfigureAwait(false))
                    {
                        return null;
                    }

                    needsConnection = false;
                    responses.Add(await QueryAsync(command, cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    DisposeConnection();
                    throw;
                }
                catch
                {
                    DisposeConnection();
                    needsConnection = true;
                    responses.Add(null);
                }
            }

            return responses;
        }
        finally
        {
            ConnectionGate.Release();
        }
    }

    public static async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await ConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposeConnection();
        }
        finally
        {
            ConnectionGate.Release();
        }
    }

    private static async Task<T> WithConnectionAsync<T>(string host, int port, string password, Func<CancellationToken, Task<T>> action, T connectFailedResult, CancellationToken cancellationToken)
    {
        await ConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (!await EnsureConnectedAsync(host, port, password, cancellationToken).ConfigureAwait(false))
                        return connectFailedResult;

                    return await action(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    DisposeConnection();
                    throw;
                }
                catch when (attempt == 0)
                {
                    DisposeConnection();
                }
            }
        }
        finally
        {
            ConnectionGate.Release();
        }
    }

    private static bool InvalidRequest(string host, int port, string password)
        => string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 || string.IsNullOrEmpty(password);

    private static async Task<bool> EnsureConnectedAsync(string host, int port, string password, CancellationToken cancellationToken)
    {
        if (_client?.Connected == true
            && _stream != null
            && _packetLengthBuffer != null
            && _port == port
            && string.Equals(_host, host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_password, password, StringComparison.Ordinal))
        {
            return true;
        }

        DisposeConnection();
        TcpClient client = new() { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            byte[] packetLengthBuffer = new byte[4];

            if (!await AuthenticateAsync(stream, packetLengthBuffer, NextRequestId(), password, cancellationToken).ConfigureAwait(false))
            {
                client.Dispose();
                return false;
            }

            _client = client;
            _stream = stream;
            _packetLengthBuffer = packetLengthBuffer;
            _host = host;
            _port = port;
            _password = password;
            return true;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task WriteCommandAsync(string command, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("RCON is not connected.");
        byte[] packetLengthBuffer = _packetLengthBuffer ?? throw new IOException("RCON is not connected.");
        int requestId = NextRequestId();

        await WritePacketAsync(stream, requestId, CommandPacketType, command, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
            if (packet.ID == requestId)
                return;
        }
    }

    private static async Task WriteCommandsAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("RCON is not connected.");
        byte[] packetLengthBuffer = _packetLengthBuffer ?? throw new IOException("RCON is not connected.");
        HashSet<int> pendingIDs = new(commands.Count);

        foreach (string command in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                int requestId = NextRequestId();
                pendingIDs.Add(requestId);
                await WritePacketAsync(stream, requestId, CommandPacketType, command, cancellationToken).ConfigureAwait(false);
            }
        }

        while (pendingIDs.Count > 0)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
            pendingIDs.Remove(packet.ID);
        }
    }

    private static async Task<string?> QueryAsync(string command, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("RCON is not connected.");
        byte[] packetLengthBuffer = _packetLengthBuffer ?? throw new IOException("RCON is not connected.");
        int commandId = NextRequestId();
        int sentinelId = NextRequestId();

        await WritePacketAsync(stream, commandId, CommandPacketType, command, cancellationToken).ConfigureAwait(false);
        await WritePacketAsync(stream, sentinelId, CommandPacketType, string.Empty, cancellationToken).ConfigureAwait(false);

        StringBuilder? response = null;
        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken).ConfigureAwait(false);
            if (packet.ID == sentinelId)
                break;

            if (packet.ID == commandId && packet.Payload.Length > 0)
                (response ??= new()).Append(packet.Payload);
        }

        return response?.ToString() ?? string.Empty;
    }

    private static async Task<bool> AuthenticateAsync(Stream stream, byte[] packetLengthBuffer, int requestId, string password, CancellationToken cancellationToken)
    {
        await WritePacketAsync(stream, requestId, AuthPacketType, password, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
            if (packet.ID == -1)
                return false;

            if (packet.ID == requestId && packet.Type == AuthResponsePacketType)
                return true;

            if (packet.ID != requestId || packet.Type != ResponseValuePacketType)
                throw new InvalidDataException("RCON returned an unexpected authentication packet.");
        }
    }

    private static async Task WritePacketAsync(Stream stream, int ID, int type, string payload, CancellationToken cancellationToken)
    {
        int payloadLength = RCONEncoding.GetByteCount(payload);
        int length = 4 + 4 + payloadLength + 2;
        if (length > MaxPacketLength)
            throw new InvalidDataException("RCON command payload is too large.");

        int packetSize = 4 + length;
        byte[] packet = ArrayPool<byte>.Shared.Rent(packetSize);
        try
        {
            Memory<byte> packetMemory = packet.AsMemory(0, packetSize);
            BinaryPrimitives.WriteInt32LittleEndian(packetMemory.Span[..4], length);
            BinaryPrimitives.WriteInt32LittleEndian(packetMemory.Span.Slice(4, 4), ID);
            BinaryPrimitives.WriteInt32LittleEndian(packetMemory.Span.Slice(8, 4), type);
            RCONEncoding.GetBytes(payload.AsSpan(), packetMemory.Span.Slice(12, payloadLength));
            packetMemory.Span.Slice(12 + payloadLength, 2).Clear();

            await stream.WriteAsync(packetMemory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            packet.AsSpan(0, packetSize).Clear();
            ArrayPool<byte>.Shared.Return(packet);
        }
    }

    private static async Task<RCONPacket> ReadPacketAsync(Stream stream, byte[] packetLengthBuffer, CancellationToken cancellationToken, bool decodePayload = true)
    {
        await stream.ReadExactlyAsync(packetLengthBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(packetLengthBuffer);
        if (length < 10 || length > MaxPacketLength)
            throw new InvalidDataException("RCON returned an invalid packet length.");

        byte[] payload = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await stream.ReadExactlyAsync(payload.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            string response = decodePayload && length > 10
                ? RCONEncoding.GetString(payload, 8, length - 10)
                : string.Empty;

            return new RCONPacket(
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
                response);
        }
        finally
        {
            payload.AsSpan(0, length).Clear();
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private static int NextRequestId()
    {
        _nextRequestId = unchecked((_nextRequestId + 1) & 0x3FFFFFFF);
        if (_nextRequestId == 0)
            _nextRequestId = 1;

        return _nextRequestId;
    }

    private static void DisposeConnection()
    {
        TryDispose(_stream);
        TryDispose(_client);
        _stream = null;
        _client = null;
        _packetLengthBuffer = null;
        _host = string.Empty;
        _port = 0;
        _password = string.Empty;
    }

    private static void TryDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
        }
    }

    private readonly struct RCONPacket(int ID, int type, string payload)
    {
        public int ID { get; } = ID;
        public int Type { get; } = type;
        public string Payload { get; } = payload;
    }
}
