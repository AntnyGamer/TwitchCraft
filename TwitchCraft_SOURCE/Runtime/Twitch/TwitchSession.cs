using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace TwitchCraft_V1;

internal sealed class TwitchSession
{
    private TcpClient? _socket;
    private StreamWriter? _writer;

    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal SemaphoreSlim ChatRateGate { get; } = new(1, 1);
    internal SemaphoreSlim BotIdentityResolveGate { get; } = new(1, 1);
    internal SemaphoreSlim TokenRefreshGate { get; } = new(1, 1);
    internal HashSet<string> MessageIDs { get; } = new(StringComparer.Ordinal);
    internal Queue<string> MessageIDOrder { get; } = new();
    internal Queue<long> ChatSendTimes { get; } = new(100);

    internal TcpClient? Socket
    {
        get => _socket;
        set => _socket = value;
    }

    internal StreamWriter? Writer
    {
        get => _writer;
        set => _writer = value;
    }

    internal bool TryClearWriter(StreamWriter? writer)
        => ReferenceEquals(Interlocked.CompareExchange(ref _writer, null, writer), writer);

    internal void CloseSocket(TcpClient? socketToClose = null)
    {
        TcpClient? socket = socketToClose ?? _socket;
        if (socket == null)
            return;

        Interlocked.CompareExchange(ref _socket, null, socket);

        try
        {
            socket.Dispose();
        }
        catch
        {
        }
    }
}
