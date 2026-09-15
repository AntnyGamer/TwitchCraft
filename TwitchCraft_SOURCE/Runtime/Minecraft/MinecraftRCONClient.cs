using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal static class MinecraftRCONClient
{
    private const int AuthPacketType = 3;
    private const int CommandPacketType = 2;
    private const int AuthResponsePacketType = 2;
    private const int ResponseValuePacketType = 0;
    private const int MaxPacketLength = 1_048_576;
    private const int MaxResponseLength = 1_048_576;
    private static readonly UTF8Encoding RCONEncoding = new(false);
    private static readonly SemaphoreSlim ConnectionGate = new(1, 1);
    private static TcpClient? _client;
    private static NetworkStream? _stream;
    private static byte[]? _packetLengthBuffer;
    private static string _host = string.Empty;
    private static string _password = string.Empty;
    private static int _port;
    private static int _nextRequestID = Environment.TickCount & 0x3FFFFFFF;

    public static Task<bool> ExecuteCommandAsync(string host, int port, string password, string command, CancellationToken cancellationToken)
        => IsInvalidRequest(host, port, password) || string.IsNullOrWhiteSpace(command)
            ? Task.FromResult(false)
            : UseConnectionAsync(host.Trim(), port, password, async token =>
            {
                await WriteCommandAsync(command, token).ConfigureAwait(false);
                return true;
            }, cancellationToken);

    public static Task<bool> ExecuteCommandsAsync(string host, int port, string password, IReadOnlyList<string> commands, CancellationToken cancellationToken)
        => IsInvalidRequest(host, port, password) || commands.Count == 0
            ? Task.FromResult(false)
            : UseConnectionAsync(host.Trim(), port, password,
                token => WriteCommandsAsync(commands, token), cancellationToken);

    public static Task<string?> ExecuteQueryAsync(string host, int port, string password, string command, CancellationToken cancellationToken)
        => IsInvalidRequest(host, port, password) || string.IsNullOrWhiteSpace(command)
            ? Task.FromResult<string?>(null)
            : UseConnectionAsync<string?>(host.Trim(), port, password,
                token => QueryAsync(command, token)!, cancellationToken);

    public static async Task<List<string?>?> ExecuteQueriesAsync(string host, int port, string password, IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        if (IsInvalidRequest(host, port, password) || commands.Count == 0)
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

    private static async Task<T> UseConnectionAsync<T>(string host, int port, string password, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await ConnectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await EnsureConnectedAsync(host, port, password, cancellationToken).ConfigureAwait(false))
                return default!;

            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            DisposeConnection();
            throw;
        }
        finally
        {
            ConnectionGate.Release();
        }
    }

    private static bool IsInvalidRequest(string host, int port, string password)
        => string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 || string.IsNullOrEmpty(password);

    private static async Task<bool> EnsureConnectedAsync(string host, int port, string password, CancellationToken cancellationToken)
    {
        if (_client?.Connected == true && !(_client.Client.Poll(0, SelectMode.SelectRead) && _client.Available == 0)
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

            if (!await AuthenticateAsync(stream, packetLengthBuffer, NextRequestID(), password, cancellationToken).ConfigureAwait(false))
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
        int requestID = NextRequestID();

        await WritePacketAsync(stream, requestID, CommandPacketType, command, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
            if (packet.ID != requestID)
                continue;
            if (packet.Type != ResponseValuePacketType)
                throw new InvalidDataException("RCON returned an unexpected command response packet.");
            return;
        }
    }

    private static async Task<bool> WriteCommandsAsync(IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("RCON is not connected.");
        byte[] packetLengthBuffer = _packetLengthBuffer ?? throw new IOException("RCON is not connected.");
        HashSet<int> pendingIDs = new(commands.Count);
        bool confirmedAny = false;

        try
        {
            foreach (string command in commands)
            {
                if (!string.IsNullOrWhiteSpace(command))
                {
                    int requestID = NextRequestID();
                    pendingIDs.Add(requestID);
                    await WritePacketAsync(stream, requestID, CommandPacketType, command, cancellationToken).ConfigureAwait(false);
                }
            }

            if (pendingIDs.Count == 0)
                return false;

            while (pendingIDs.Count > 0)
            {
                RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
                if (!pendingIDs.Remove(packet.ID))
                    continue;
                if (packet.Type != ResponseValuePacketType)
                    throw new InvalidDataException("RCON returned an unexpected command response packet.");
                confirmedAny = true;
            }

            return true;
        }
        catch (Exception ex) when (confirmedAny && ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidDataException)
        {
            throw new RCONPartialResponseException(ex);
        }
    }

    private static async Task<string> QueryAsync(string command, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new IOException("RCON is not connected.");
        byte[] packetLengthBuffer = _packetLengthBuffer ?? throw new IOException("RCON is not connected.");
        int commandID = NextRequestID();
        int sentinelID = NextRequestID();
        await WritePacketAsync(stream, commandID, CommandPacketType, command, cancellationToken).ConfigureAwait(false);
        await WritePacketAsync(stream, sentinelID, CommandPacketType, string.Empty, cancellationToken).ConfigureAwait(false);

        StringBuilder response = new();
        Decoder decoder = RCONEncoding.GetDecoder();
        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decoder: decoder).ConfigureAwait(false);
            if (packet.Type != ResponseValuePacketType && (packet.ID == commandID || packet.ID == sentinelID))
                throw new InvalidDataException("RCON returned an unexpected query response packet.");
            if (packet.ID == sentinelID)
                return response.ToString();

            if (packet.ID == commandID && packet.Payload.Length > 0)
                AppendResponse(response, packet.Payload);
        }
    }

    private static void AppendResponse(StringBuilder response, string payload)
    {
        if (response.Length > MaxResponseLength - payload.Length)
            throw new InvalidDataException("RCON response is too large.");
        response.Append(payload);
    }

    private static async Task<bool> AuthenticateAsync(Stream stream, byte[] packetLengthBuffer, int requestID, string password, CancellationToken cancellationToken)
    {
        await WritePacketAsync(stream, requestID, AuthPacketType, password, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            RCONPacket packet = await ReadPacketAsync(stream, packetLengthBuffer, cancellationToken, decodePayload: false).ConfigureAwait(false);
            if (packet.ID == -1)
                return false;

            if (packet.ID == requestID && packet.Type == AuthResponsePacketType)
                return true;

            if (packet.ID != requestID || packet.Type != ResponseValuePacketType)
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

    private static async Task<RCONPacket> ReadPacketAsync(Stream stream, byte[] packetLengthBuffer, CancellationToken cancellationToken, bool decodePayload = true, Decoder? decoder = null)
    {
        await stream.ReadExactlyAsync(packetLengthBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(packetLengthBuffer);
        if (length < 10 || length > MaxPacketLength)
            throw new InvalidDataException("RCON returned an invalid packet length.");

        byte[] payload = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await stream.ReadExactlyAsync(payload.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            if (payload[length - 2] != 0 || payload[length - 1] != 0)
                throw new InvalidDataException("RCON packet is missing its required null terminators.");
            string response = decodePayload && length > 10
                ? DecodePayload(payload.AsSpan(8, length - 10), decoder)
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

    private static string DecodePayload(ReadOnlySpan<byte> payload, Decoder? decoder)
    {
        if (decoder == null) return RCONEncoding.GetString(payload);
        char[] chars = ArrayPool<char>.Shared.Rent(payload.Length);
        try { return new string(chars, 0, decoder.GetChars(payload, chars.AsSpan(), flush: false)); }
        finally { ArrayPool<char>.Shared.Return(chars); }
    }

    private static int NextRequestID()
    {
        _nextRequestID = unchecked((_nextRequestID + 1) & 0x3FFFFFFF);
        if (_nextRequestID == 0)
            _nextRequestID = 1;

        return _nextRequestID;
    }

    private static void DisposeConnection()
    {
        DisposeSafe(_stream);
        DisposeSafe(_client);
        _stream = null;
        _client = null;
        _packetLengthBuffer = null;
        _host = string.Empty;
        _port = 0;
        _password = string.Empty;
    }

    private static void DisposeSafe(IDisposable? disposable)
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

internal sealed class RCONPartialResponseException(Exception innerException)
    : IOException("At least one RCON command response was confirmed before the batch was interrupted.", innerException);
