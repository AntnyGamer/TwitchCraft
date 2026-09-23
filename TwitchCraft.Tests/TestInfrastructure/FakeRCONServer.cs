using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft.Tests.TestInfrastructure;

internal sealed class FakeRCONServer : IAsyncDisposable
{
    private static readonly Encoding UTF8 = new UTF8Encoding(false);
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;
    private readonly string _password;
    private readonly string? _malformedResponseCommand;
    private readonly string? _wrongTypeResponseCommand;
    private readonly Func<string, string>? _responseFactory;
    private readonly Lock _gate = new();
    private readonly List<string> _commands = [];
    private int _emptyCommandCount;

    internal FakeRCONServer(
        string password,
        string? malformedResponseCommand = null,
        string? wrongTypeResponseCommand = null,
        Func<string, string>? responseFactory = null)
    {
        _password = password;
        _malformedResponseCommand = malformedResponseCommand;
        _wrongTypeResponseCommand = wrongTypeResponseCommand;
        _responseFactory = responseFactory;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serverTask = RunAsync(_cts.Token);
    }

    internal int Port { get; }

    internal IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
                return [.. _commands];
        }
    }

    internal int EmptyCommandCount => Volatile.Read(ref _emptyCommandCount);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();
        bool authenticated = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            RCONPacket packet;
            try
            {
                packet = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }

            if (packet.Type == 3)
            {
                authenticated = string.Equals(packet.Payload, _password, StringComparison.Ordinal);
                await WritePacketAsync(
                    stream,
                    authenticated ? packet.ID : -1,
                    2,
                    string.Empty,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (packet.Type != 2 || !authenticated)
                continue;

            if (packet.Payload.Length > 0)
            {
                lock (_gate)
                    _commands.Add(packet.Payload);
            }
            else
            {
                Interlocked.Increment(ref _emptyCommandCount);
            }

            if (string.Equals(packet.Payload, _malformedResponseCommand, StringComparison.Ordinal))
            {
                byte[] invalidLength = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(invalidLength, 9);
                await stream.WriteAsync(invalidLength, cancellationToken).ConfigureAwait(false);
                return;
            }

            int responseType = string.Equals(packet.Payload, _wrongTypeResponseCommand, StringComparison.Ordinal) ? 2 : 0;
            string response = _responseFactory?.Invoke(packet.Payload) ?? "OK";
            await WritePacketAsync(stream, packet.ID, responseType, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<RCONPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 10 || length > 1_048_576)
            throw new InvalidDataException("Invalid test RCON packet length.");

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        int ID = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4));
        int type = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4, 4));
        string payload = length > 10 ? UTF8.GetString(body, 8, length - 10) : string.Empty;
        return new RCONPacket(ID, type, payload);
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        int ID,
        int type,
        string payload,
        CancellationToken cancellationToken)
    {
        int payloadLength = UTF8.GetByteCount(payload);
        int length = 4 + 4 + payloadLength + 2;
        byte[] packet = new byte[4 + length];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), ID);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        UTF8.GetBytes(payload.AsSpan(), packet.AsSpan(12, payloadLength));
        await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct RCONPacket(int ID, int Type, string Payload);
}
