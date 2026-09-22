using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static string NormalizeToken(string? token) => TwitchTokenHelper.NormalizeAccessToken(token);

    private async Task SendIRCLineAsync(StreamWriter writer, string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line) || !ReferenceEquals(writer, _twitchSession.Writer))
            return;

        bool rateLimited = line.StartsWith("PRIVMSG ", StringComparison.Ordinal);
        if (rateLimited) await _twitchSession.ChatRateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long sendDelay = 0;
            if (rateLimited)
            {
                long now = Environment.TickCount64;
                if (_twitchSession.ChatSendTimes.Count >= 100)
                    sendDelay = Math.Max(0, _twitchSession.ChatSendTimes.Peek() + 30_000 - now);
            }

            if (sendDelay > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(sendDelay), cancellationToken).ConfigureAwait(false);
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TwitchSession.OperationTimeout);
            CancellationToken writeToken = timeoutCts.Token;
            await _twitchSession.WriteGate.WaitAsync(writeToken).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(writer, _twitchSession.Writer))
                    return;

                await writer.WriteLineAsync(line.AsMemory(), writeToken).ConfigureAwait(false);
                await writer.FlushAsync(writeToken).ConfigureAwait(false);
                if (rateLimited)
                {
                    if (_twitchSession.ChatSendTimes.Count >= 100) _twitchSession.ChatSendTimes.Dequeue();
                    _twitchSession.ChatSendTimes.Enqueue(Environment.TickCount64);
                }
            }
            finally
            {
                _twitchSession.WriteGate.Release();
            }
        }
        finally
        {
            if (rateLimited) _twitchSession.ChatRateGate.Release();
        }
    }

    private async Task SendIRCLinesAsync(StreamWriter writer, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            return;

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TwitchSession.OperationTimeout);
        CancellationToken writeToken = timeoutCts.Token;
        await _twitchSession.WriteGate.WaitAsync(writeToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(writer, _twitchSession.Writer))
                return;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (!string.IsNullOrWhiteSpace(line))
                    await writer.WriteLineAsync(line.AsMemory(), writeToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(writeToken).ConfigureAwait(false);
        }
        finally
        {
            _twitchSession.WriteGate.Release();
        }
    }

    private async Task LeaveIRCAsync(CancellationToken cancellationToken)
    {
        StreamWriter? writer = _twitchSession.Writer;
        if (writer == null || !_twitchSession.WriteGate.Wait(0, CancellationToken.None))
            return;

        try
        {
            if (!ReferenceEquals(writer, _twitchSession.Writer))
                return;

            string channel = StreamerName;
            if (channel.Length == 0)
                return;

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TwitchSession.ShutdownPartTimeout);
            await writer.WriteLineAsync(("PART #" + channel).AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await writer.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is ObjectDisposedException or IOException or InvalidOperationException or SocketException ||
            ex is OperationCanceledException)
        {
        }
        finally
        {
            _twitchSession.WriteGate.Release();
        }
    }

    private async Task RunIRCAsync(CancellationToken cancellationToken)
    {
        var twitch = _activeConfig?.Twitch;
        if (twitch == null ||
            string.IsNullOrWhiteSpace(twitch.BotToken) ||
            string.IsNullOrWhiteSpace(twitch.StreamerName))
        {
            return;
        }

        string channelLogin = _streamerName;
        int reconnectDelayMs = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            string botToken = NormalizeToken(_activeConfig?.Twitch.BotToken);
            if (botToken.Length == 0)
                return;

            const string IRCHost = "IRC.chat.twitch.tv";
            const int IRCPort = 6697;

            StreamReader? reader = null;
            StreamWriter? writer = null;
            SslStream? stream = null;
            TcpClient? socket = null;
            CancellationTokenRegistration tokenRegistration = default;

            try
            {
                string botName = await ResolveBotAsync(botToken, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(botName))
                {
                    _shellWindow?.AddChatLogLine("Unable to resolve bot login from the Twitch token.");
                    return;
                }

                socket = new() { SendTimeout = 30000, NoDelay = true };
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                socket.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                _twitchSession.Socket = socket;

                tokenRegistration = cancellationToken.Register(() => _twitchSession.CloseSocket(socket));

                using CancellationTokenSource connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeoutCts.CancelAfter(TwitchSession.OperationTimeout);
                await socket.ConnectAsync(IRCHost, IRCPort, connectTimeoutCts.Token).ConfigureAwait(false);

                stream = new(socket.GetStream(), false);
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = IRCHost
                    },
                    connectTimeoutCts.Token).ConfigureAwait(false);

                reader = new(stream, TwitchSession.UTF8NoBOM, leaveOpen: true);
                writer = new(stream, TwitchSession.UTF8NoBOM, leaveOpen: true)
                {
                    NewLine = "\r\n",
                    AutoFlush = false
                };
                _twitchSession.Writer = writer;

                await SendIRCLinesAsync(
                    writer,
                    [
                        "PASS " + TwitchTokenHelper.BuildIRCPassword(botToken),
                        "NICK " + botName,
                        "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership",
                        "JOIN #" + channelLogin
                    ],
                    cancellationToken).ConfigureAwait(false);

                bool separateBotAccount = !string.Equals(botName, channelLogin, StringComparison.OrdinalIgnoreCase);
                IRCMessage message = new();

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (IOException ex)
                    {
                        _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC read error", ex));
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (line == null)
                    {
                        _shellWindow?.AddChatLogLine("[IRC] Connection closed by Twitch.");
                        break;
                    }

                    if (!message.TryParse(line))
                    {
                        _shellWindow?.AddChatLogLine(IRCMessage.StripIRCTags(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "PING", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine(IRCMessage.StripIRCTags(line));

                        string pingPayload = string.IsNullOrWhiteSpace(message.Trailing) ? "tmi.twitch.tv" : message.Trailing;
                        try
                        {
                            await SendIRCLineAsync(writer, "PONG :" + pingPayload, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC write error", ex));
                            break;
                        }

                        continue;
                    }

                    if (string.Equals(message.Command, "RECONNECT", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine("[IRC] Twitch requested reconnect.");
                        break;
                    }

                    if (string.Equals(message.Command, "CAP", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine(IRCMessage.StripIRCTags(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "NOTICE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(message.Trailing))
                            _shellWindow?.AddChatLogLine("[NOTICE] " + message.Trailing);
                        else
                            _shellWindow?.AddChatLogLine(IRCMessage.StripIRCTags(line));
                        if (message.Trailing.Contains("authentication failed", StringComparison.OrdinalIgnoreCase))
                        {
                            SaveBot(botToken, await ValidateBotAsync(botToken, twitch.ClientID, cancellationToken).ConfigureAwait(false));
                            break;
                        }
                        continue;
                    }

                    if (!string.Equals(message.Command, "PRIVMSG", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(message.Command, "JOIN", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(message.SenderLogin, botName, StringComparison.OrdinalIgnoreCase))
                        {
                            _twitchSession.ChatConnected = true;
                            reconnectDelayMs = 1000;
                            _shellWindow?.AddChatLogLine("[IRC] Connected to #" + channelLogin + ".");
                        }
                        else
                            _shellWindow?.AddChatLogLine(IRCMessage.StripIRCTags(line));
                        continue;
                    }

                    string sender = message.SenderLogin;
                    string payload = message.Trailing;
                    if (message.ID.Length > 0)
                    {
                        if (!_twitchSession.MessageIDs.Add(message.ID))
                            continue;

                        _twitchSession.MessageIDOrder.Enqueue(message.ID);
                        if (_twitchSession.MessageIDOrder.Count > 4096)
                            _twitchSession.MessageIDs.Remove(_twitchSession.MessageIDOrder.Dequeue());
                    }

                    bool hasChatMessage = sender.Length > 0 && payload.Length > 0;
                    _shellWindow?.AddChatLogLine(
                        hasChatMessage
                            ? "<" + sender + "> " + payload
                            : IRCMessage.StripIRCTags(line));

                    if (!hasChatMessage || IsIgnoredUser(sender, botName, separateBotAccount))
                        continue;

                    RecordChatActivity(sender, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    if (message.Bits > 0)
                    {
                        int bitReward = GetBitReward(
                            CurrentSettings.AutomaticBitRewardsEnabled,
                            message.Bits);
                        int awardedBits = bitReward > 0 ? Tokens.Award(sender, bitReward) : 0;
                        string bitsText = message.Bits.ToString(CultureInfo.InvariantCulture);
                        string rewardResult = bitReward > 0
                            ? " and received " + awardedBits.ToString(CultureInfo.InvariantCulture) + " " + (awardedBits == 1 ? "token" : "tokens") + "."
                            : "; automatic Bit rewards are disabled.";
                        _shellWindow?.AddChatLogLine("[Bits] " + sender + " cheered " + bitsText + " " + (message.Bits == 1 ? "Bit" : "Bits") + rewardResult);
                    }

                    if (ParsedCommand.TryMatchPrefix(payload, CommandPrefix, SecondaryCommandPrefix, out string matchedPrefix))
                    {
                        bool isModerator = message.IsModerator;

                        if (!QueueCommand(
                            ct => DispatchAsync(payload, matchedPrefix, sender, isModerator, ct),
                            payload,
                            cancellationToken))
                            TrackTask(WarnQueueOverloadAsync(cancellationToken));
                    }
                    else
                    {
                        var settings = CurrentSettings;
                        if (!settings.NonCommandChatRelayEnabled ||
                            !_twitchSession.TryUseRelaySlot(settings.MinecraftRelayMessagesPerSecond, settings.LowResourceModeEnabled))
                            continue;
                        bool includeTimestamp = settings.IncludeRelayTimestamps;
                        string relayColor = settings.MinecraftRelayTextColor;
                        string relayMessage = TwitchSession.FormatRelay(
                            sender,
                            payload,
                            includeTimestamp,
                            includeTimestamp ? DateTime.Now : default);
                        _ = QueueIRCWork(
                            _twitchSession.QuickQueue,
                            ct => SendTellrawAsync("@a", relayMessage, relayColor, false, ct),
                            "chat relay",
                            cancellationToken);
                    }
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!await TryRefreshAuthAsync(botToken, cancellationToken).ConfigureAwait(false))
                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC authorization failed", ex));
            }
            catch (SocketException ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC socket error", ex));
            }
            catch (IOException ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC I/O error", ex));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC error", ex));
            }
            finally
            {
                if (_twitchSession.TryClearWriter(writer))
                    _twitchSession.ChatConnected = false;

                try
                {
                    await tokenRegistration.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    reader?.Dispose();
                }
                catch
                {
                }

                try
                {
                    if (stream != null) await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                _twitchSession.CloseSocket(socket);
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            _shellWindow?.AddChatLogLine(
                string.Create(CultureInfo.InvariantCulture, $"[IRC] Reconnecting in {reconnectDelayMs / 1000.0:0.#} second(s)..."));

            try
            {
                await Task.Delay(reconnectDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            reconnectDelayMs = GetReconnectDelayMs(reconnectDelayMs);
        }
    }

    internal static int GetBitReward(bool enabled, int bits)
        => enabled && bits > 0 ? bits : 0;

    internal static int GetReconnectDelayMs(int currentDelayMilliseconds)
        => Math.Min(currentDelayMilliseconds * 2, 15000);
}

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
            return true;

        long now = nowMilliseconds ?? Environment.TickCount64;
        lock (_relayGate)
        {
            long cutoff = now - 1000;
            while (_relayMessageTimestamps.TryPeek(out long timestamp) && timestamp <= cutoff)
                _relayMessageTimestamps.Dequeue();
            if (_relayMessageTimestamps.Count >= limit)
                return false;
            _relayMessageTimestamps.Enqueue(now);
            return true;
        }
    }

    internal void ResetRelayRateLimit()
    {
        lock (_relayGate)
            _relayMessageTimestamps.Clear();
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
