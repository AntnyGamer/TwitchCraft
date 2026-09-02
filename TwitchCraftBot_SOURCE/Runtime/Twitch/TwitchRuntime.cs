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

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private readonly struct IRCQueuedWork(
        Func<CancellationToken, Task> work,
        string context,
        int generation,
        CancellationToken cancellationToken)
    {
        public Func<CancellationToken, Task> Work { get; } = work;
        public string Context { get; } = context;
        public int Generation { get; } = generation;
        public CancellationToken CancellationToken { get; } = cancellationToken;
    }

    private sealed class IRCWorkQueueState(int maxDepth)
    {
        public Lock Gate { get; } = new();
        public Queue<IRCQueuedWork> Queue { get; set; } = new();
        public int Depth;
        public int Active;
        public int MaxDepth { get; } = maxDepth;
    }

    private static readonly UTF8Encoding IRCUtf8NoBom = new(false);
    private static readonly TimeSpan IRCShutdownPartTimeout = TimeSpan.FromSeconds(1);
    private static readonly long IRCCommandOverflowNoticeIntervalTicks = TimeSpan.FromSeconds(30).Ticks;
    private readonly HashSet<string> _IRCMessageIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _IRCMessageIdOrder = new();
    private readonly Queue<long> _IRCChatSendTimes = new(100);

    private static string NormalizeToken(string? token) => TwitchTokenHelper.NormalizeAccessToken(token);

    private async Task SendIrcLineAsync(StreamWriter writer, string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line) || !ReferenceEquals(writer, _IRCWriter))
            return;

        long sendDelay = 0;
        if (line.StartsWith("PRIVMSG ", StringComparison.Ordinal))
        {
            lock (_IRCChatSendTimes)
            {
                long now = Environment.TickCount64;
                long sendAt = _IRCChatSendTimes.Count < 100
                    ? now
                    : Math.Max(now, _IRCChatSendTimes.Dequeue() + 30_000);
                _IRCChatSendTimes.Enqueue(sendAt);
                sendDelay = sendAt - now;
            }
        }

        if (sendDelay > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(sendDelay), cancellationToken).ConfigureAwait(false);
        await _IRCWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(writer, _IRCWriter))
                return;

            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _IRCWriteGate.Release();
        }
    }

    private async Task SendIrcLinesAsync(StreamWriter writer, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            return;

        await _IRCWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(writer, _IRCWriter))
                return;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (!string.IsNullOrWhiteSpace(line))
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _IRCWriteGate.Release();
        }
    }

    private async Task LeaveIrcAsync(CancellationToken cancellationToken)
    {
        StreamWriter? writer = _IRCWriter;
        if (writer == null || !_IRCWriteGate.Wait(0, CancellationToken.None))
            return;

        try
        {
            if (!ReferenceEquals(writer, _IRCWriter))
                return;

            string channel = StreamerName;
            if (channel.Length == 0)
                return;

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(IRCShutdownPartTimeout);
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
            _IRCWriteGate.Release();
        }
    }

    private async Task RunIrcAsync(CancellationToken cancellationToken)
    {
        var twitch = _activeConfig?.Twitch;
        if (twitch == null ||
            string.IsNullOrWhiteSpace(twitch.BotToken) ||
            string.IsNullOrWhiteSpace(twitch.StreamerName))
        {
            return;
        }

        string channelLogin = _currentStreamerName;
        int reconnectDelayMs = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            string botToken = NormalizeToken(_activeConfig?.Twitch.BotToken);
            if (botToken.Length == 0)
                return;

            const string ircHost = "IRC.chat.twitch.tv";
            const int ircPort = 6697;

            StreamReader? reader = null;
            StreamWriter? writer = null;
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
                _IRCSocket = socket;

                tokenRegistration = cancellationToken.Register(() => CloseIrcSocket(socket));

                await socket.ConnectAsync(ircHost, ircPort, cancellationToken).ConfigureAwait(false);

                SslStream stream = new(socket.GetStream(), false);
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = ircHost
                    },
                    cancellationToken).ConfigureAwait(false);

                reader = new(stream, IRCUtf8NoBom);
                writer = new(stream, IRCUtf8NoBom)
                {
                    NewLine = "\r\n",
                    AutoFlush = false
                };
                _IRCWriter = writer;

                await SendIrcLinesAsync(
                    writer,
                    [
                        "PASS " + TwitchTokenHelper.BuildIrcPassword(botToken),
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
                        _shellWindow?.AddChatLogLine(StripIrcTags(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "PING", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine(StripIrcTags(line));

                        string pingPayload = string.IsNullOrWhiteSpace(message.Trailing) ? "tmi.twitch.tv" : message.Trailing;
                        try
                        {
                            await SendIrcLineAsync(writer, "PONG :" + pingPayload, cancellationToken).ConfigureAwait(false);
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
                        _shellWindow?.AddChatLogLine(StripIrcTags(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "NOTICE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(message.Trailing))
                            _shellWindow?.AddChatLogLine("[NOTICE] " + message.Trailing);
                        else
                            _shellWindow?.AddChatLogLine(StripIrcTags(line));
                        if (message.Trailing.Contains("authentication failed", StringComparison.OrdinalIgnoreCase))
                        {
                            SaveBot(botToken, await ValidateBotAsync(botToken, cancellationToken).ConfigureAwait(false));
                            break;
                        }
                        continue;
                    }

                    if (!string.Equals(message.Command, "PRIVMSG", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(message.Command, "JOIN", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(message.SenderLogin, botName, StringComparison.OrdinalIgnoreCase))
                        {
                            SetChatConnected(true);
                            reconnectDelayMs = 1000;
                            _shellWindow?.AddChatLogLine("[IRC] Connected to #" + channelLogin + ".");
                        }
                        else
                            _shellWindow?.AddChatLogLine(StripIrcTags(line));
                        continue;
                    }

                    string sender = message.SenderLogin;
                    string payload = message.Trailing;
                    if (message.Id.Length > 0)
                    {
                        if (!_IRCMessageIds.Add(message.Id))
                            continue;

                        _IRCMessageIdOrder.Enqueue(message.Id);
                        if (_IRCMessageIdOrder.Count > 4096)
                            _IRCMessageIds.Remove(_IRCMessageIdOrder.Dequeue());
                    }

                    _shellWindow?.AddChatLogLine(
                        sender.Length == 0 || payload.Length == 0
                            ? StripIrcTags(line)
                            : "<" + sender + "> " + payload);

                    if (sender.Length == 0 || IsIgnoredUser(sender, botName, separateBotAccount) || payload.Length == 0)
                        continue;

                    RecordChatActivity(sender, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    if (message.Bits > 0)
                    {
                        int bitReward = GetBitReward(
                            _activeConfig?.Settings.AutomaticBitRewardsEnabled ?? true,
                            message.Bits);
                        int awardedBits = bitReward > 0 ? Tokens.Award(sender, bitReward) : 0;
                        string bitsText = message.Bits.ToString(CultureInfo.InvariantCulture);
                        string rewardResult = bitReward > 0
                            ? " and received " + awardedBits.ToString(CultureInfo.InvariantCulture) + " " + (awardedBits == 1 ? "token" : "tokens") + "."
                            : "; automatic Bit rewards are disabled.";
                        _shellWindow?.AddChatLogLine("[Bits] " + sender + " cheered " + bitsText + " " + (message.Bits == 1 ? "Bit" : "Bits") + rewardResult);
                    }

                    if (TryMatchPrefix(payload, CommandPrefix, SecondaryCommandPrefix, out string matchedPrefix))
                    {
                        bool isModerator = message.IsModerator;

                        if (!QueueCommand(
                            ct => DispatchAsync(payload, matchedPrefix, sender, isModerator, ct),
                            payload,
                            cancellationToken))
                        {
                            await WarnQueueOverloadAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (_activeConfig?.Settings.NonCommandChatRelayEnabled != false)
                    {
                        if (!TryUseRelaySlot())
                            continue;
                        bool includeTimestamp = _activeConfig?.Settings.IncludeRelayTimestamps == true;
                        string relayColor = MinecraftRelayTextColor;
                        string relayMessage = FormatRelay(
                            sender,
                            payload,
                            includeTimestamp,
                            includeTimestamp ? DateTime.Now : default);
                        _ = QueueIrcWork(
                            _IRCQuickQueue,
                            ct => SendTellrawAsync("@a", relayMessage, relayColor, false, ct),
                            "chat relay",
                            quick: true,
                            cancellationToken: cancellationToken);
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
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC error", ex));
            }
            finally
            {
                SetChatConnected(false);
                try
                {
                    await tokenRegistration.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                if (ReferenceEquals(_IRCWriter, writer))
                    _IRCWriter = null;

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

                CloseIrcSocket(socket);
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
