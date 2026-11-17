using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

internal sealed class TwitchSession
{
    private const int MaxQueuedCommands = 75;
    private const int MaxQueuedQuickWork = 500;
    internal static readonly UTF8Encoding UTF8NoBOM = new(false);
    internal static readonly TimeSpan ShutdownPartTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);
    internal const long CommandOverflowNoticeIntervalMilliseconds = 30_000;

    private readonly Lock _relayGate = new();
    private readonly Queue<long> _relayMessageTimestamps = new();
    private TcpClient? _socket;
    private StreamWriter? _writer;

    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    internal SemaphoreSlim ChatRateGate { get; } = new(1, 1);
    internal SemaphoreSlim BotIdentityResolveGate { get; } = new(1, 1);
    internal SemaphoreSlim TokenRefreshGate { get; } = new(1, 1);
    internal HashSet<string> MessageIDs { get; } = new(StringComparer.Ordinal);
    internal Queue<string> MessageIDOrder { get; } = new();
    internal Queue<long> ChatSendTimes { get; } = new(100);
    internal WorkQueueState CommandQueue { get; } = new(MaxQueuedCommands);
    internal WorkQueueState QuickQueue { get; } = new(MaxQueuedQuickWork);
    internal CancellationTokenSource? FollowRewardsCts;
    internal Task? FollowRewardsTask;
    internal int QueueGeneration;
    internal long LastCommandOverflowNoticeMilliseconds;
    internal volatile bool ChatConnected;
    internal string ChannelPrefix { get; private set; } = string.Empty;
    internal int ChannelMessageMaxBytes { get; private set; }

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

    internal void SetChannel(string streamerName)
    {
        ChannelPrefix = streamerName.Length == 0 ? string.Empty : "PRIVMSG #" + streamerName + " :";
        ChannelMessageMaxBytes = ChannelPrefix.Length == 0 ? 0 : 510 - UTF8NoBOM.GetByteCount(ChannelPrefix);
    }

    internal bool TryUseRelaySlot(int configuredLimit, bool lowResourceMode, long? nowMilliseconds = null)
    {
        int limit = lowResourceMode
            ? configuredLimit <= 0 ? 5 : Math.Min(configuredLimit, 5)
            : configuredLimit;
        if (limit <= 0)
        {
            return true;
        }

        long now = nowMilliseconds ?? Environment.TickCount64;
        lock (_relayGate)
        {
            long cutoff = now - 1000;
            while (_relayMessageTimestamps.TryPeek(out long timestamp) && timestamp <= cutoff)
            {
                _relayMessageTimestamps.Dequeue();
            }

            if (_relayMessageTimestamps.Count >= limit)
            {
                return false;
            }

            _relayMessageTimestamps.Enqueue(now);
            return true;
        }
    }

    internal void ResetRelayRateLimit()
    {
        lock (_relayGate)
        {
            _relayMessageTimestamps.Clear();
        }
    }

    internal static string FormatRelay(string sender, string payload, bool includeTimestamp, DateTime localTime)
        => includeTimestamp
            ? string.Create(CultureInfo.InvariantCulture, $"[{localTime:HH:mm}] {sender}: {payload}")
            : string.Concat(sender, ": ", payload);

    internal bool TryClearWriter(StreamWriter? writer)
        => ReferenceEquals(Interlocked.CompareExchange(ref _writer, null, writer), writer);

    internal void CloseSocket(TcpClient? socketToClose = null)
    {
        TcpClient? socket = socketToClose ?? _socket;
        if (socket == null)
        {
            return;
        }

        Interlocked.CompareExchange(ref _socket, null, socket);

        try
        {
            socket.Dispose();
        }
        catch
        {
        }
    }

    internal readonly struct QueuedWork(
        Func<CancellationToken, Task> work,
        string context,
        int generation,
        CancellationToken cancellationToken)
    {
        internal Func<CancellationToken, Task> Work { get; } = work;
        internal string Context { get; } = context;
        internal int Generation { get; } = generation;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
    }

    internal sealed class WorkQueueState(int maxDepth)
    {
        internal Lock Gate { get; } = new();
        internal Queue<QueuedWork> Queue { get; set; } = new();
        internal int Depth;
        internal int Active;
        internal int MaxDepth { get; } = maxDepth;
    }
}
