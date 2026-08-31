using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TwitchCraftBot.Tests.TestInfrastructure;

internal sealed class FakeRconServer : IAsyncDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;
    private readonly string _password;
    private readonly Lock _gate = new();
    private readonly List<string> _commands = [];

    internal FakeRconServer(string password)
    {
        _password = password;
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

    internal async Task WaitForCommandCountAsync(int expectedCount, CancellationToken cancellationToken)
        => await FakeJavaServer.WaitUntilAsync(
            () =>
            {
                lock (_gate)
                    return _commands.Count >= expectedCount;
            },
            $"Expected at least {expectedCount} RCON command(s) within 10 seconds.",
            cancellationToken);

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
            RconPacket packet;
            try
            {
                packet = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return;
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
                    authenticated ? packet.Id : -1,
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

            await WritePacketAsync(
                stream,
                packet.Id,
                0,
                packet.Payload.Length == 0 ? string.Empty : "OK",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 10 || length > 1_048_576)
            throw new InvalidDataException("Invalid test RCON packet length.");

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        int id = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, 4));
        int type = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4, 4));
        string payload = length > 10 ? Utf8.GetString(body, 8, length - 10) : string.Empty;
        return new RconPacket(id, type, payload);
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        int id,
        int type,
        string payload,
        CancellationToken cancellationToken)
    {
        int payloadLength = Utf8.GetByteCount(payload);
        int length = 4 + 4 + payloadLength + 2;
        byte[] packet = new byte[4 + length];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        Utf8.GetBytes(payload.AsSpan(), packet.AsSpan(12, payloadLength));
        await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct RconPacket(int Id, int Type, string Payload);
}
