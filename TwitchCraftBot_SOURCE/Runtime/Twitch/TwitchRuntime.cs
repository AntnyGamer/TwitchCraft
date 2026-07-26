using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    private static readonly string[] IgnoredIRCUsers = ["nightbot", "streamlabs", "streamelements"];
    private static readonly UTF8Encoding IRCUtf8NoBom = new(false);
    private static readonly TimeSpan IRCShutdownPartTimeout = TimeSpan.FromSeconds(1);
    private static readonly long IRCCommandOverflowNoticeIntervalTicks = TimeSpan.FromSeconds(30).Ticks;

    private static string NormalizeTwitchToken(string? token) => TwitchTokenHelper.NormalizeAccessToken(token);

    private async Task SendIRCLineAsync(StreamWriter writer, string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

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

    private async Task SendIRCLinesAsync(StreamWriter writer, IReadOnlyList<string> lines, CancellationToken cancellationToken)
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

    private async Task SendIRCPartForShutdownAsync(CancellationToken cancellationToken)
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

    private async Task RunIRCLoopAsync(CancellationToken cancellationToken)
    {
        if (_activeConfig?.Twitch == null)
            return;

        string botToken = NormalizeTwitchToken(_activeConfig.Twitch.BotToken);
        if (string.IsNullOrWhiteSpace(botToken) ||
            string.IsNullOrWhiteSpace(_activeConfig.Twitch.StreamerName))
        {
            return;
        }

        string channelLogin = _currentStreamerName;
        int reconnectDelayMs = 1000;

        while (!cancellationToken.IsCancellationRequested)
        {
            StreamReader? reader = null;
            StreamWriter? writer = null;
            TcpClient? socket = null;
            CancellationTokenRegistration tokenRegistration = default;

            try
            {
                string botName = await ResolveAndPersistBotNameAsync(botToken, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(botName))
                {
                    _shellWindow?.AddChatLogLine("Unable to resolve bot login from the Twitch token.");
                    return;
                }

                socket = new()
                {
                    ReceiveTimeout = 30000,
                    SendTimeout = 30000,
                    NoDelay = true
                };
                _IRCSocket = socket;

                tokenRegistration = cancellationToken.Register(() => SafeCloseIRCSocket(socket));

                await socket.ConnectAsync("IRC.chat.twitch.tv", 6697, cancellationToken).ConfigureAwait(false);

                SslStream stream = new(socket.GetStream(), false);
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = "IRC.chat.twitch.tv"
                    },
                    cancellationToken).ConfigureAwait(false);

                reader = new(stream, IRCUtf8NoBom);
                writer = new(stream, IRCUtf8NoBom)
                {
                    NewLine = "\r\n",
                    AutoFlush = false
                };
                _IRCWriter = writer;

                await SendIRCLinesAsync(
                    writer,
                    [
                        "PASS " + TwitchTokenHelper.BuildIRCPassword(botToken),
                        "NICK " + botName,
                        "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership",
                        "JOIN #" + channelLogin
                    ],
                    cancellationToken).ConfigureAwait(false);

                _shellWindow?.AddChatLogLine("[IRC] Connected to #" + channelLogin + ".");
                reconnectDelayMs = 1000;
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
                        _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC read error", ex));
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
                        _shellWindow?.AddChatLogLine(StripIRCTagsForLog(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "PING", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine(StripIRCTagsForLog(line));

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
                            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC write error", ex));
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
                        _shellWindow?.AddChatLogLine(StripIRCTagsForLog(line));
                        continue;
                    }

                    if (string.Equals(message.Command, "NOTICE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(message.Trailing))
                            _shellWindow?.AddChatLogLine("[NOTICE] " + message.Trailing);
                        else
                            _shellWindow?.AddChatLogLine(StripIRCTagsForLog(line));

                        continue;
                    }

                    if (!string.Equals(message.Command, "PRIVMSG", StringComparison.OrdinalIgnoreCase))
                    {
                        _shellWindow?.AddChatLogLine(StripIRCTagsForLog(line));
                        continue;
                    }

                    string sender = message.SenderLogin;
                    string payload = message.Trailing;

                    _shellWindow?.AddChatLogLine(
                        sender.Length == 0 || payload.Length == 0
                            ? StripIRCTagsForLog(line)
                            : "<" + sender + "> " + payload);

                    if (sender.Length == 0 || IsIgnoredIRCUser(sender, botName, separateBotAccount) || payload.Length == 0)
                        continue;

                    if (message.Bits > 0)
                    {
                        AdjustTokens(sender, message.Bits);
                        string bitsText = message.Bits.ToString(CultureInfo.InvariantCulture);
                        _shellWindow?.AddChatLogLine(
                            "[Bits] " + sender + " cheered " + bitsText + " " + (message.Bits == 1 ? "Bit" : "Bits") + " and received " + bitsText + " " + (message.Bits == 1 ? "token" : "tokens") + ".");
                    }

                    if (payload[0] == '!')
                    {
                        bool isModerator = message.IsModerator;

                        if (!QueueIRCWorkCore(
                            _IRCCommandQueue,
                            ct => DispatchCommandAsync(payload, sender, isModerator, ct),
                            payload,
                            quick: false,
                            cancellationToken: cancellationToken))
                        {
                            await NotifyIRCCommandQueueOverloadAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (_activeConfig?.Settings.NonCommandChatRelayEnabled != false)
                    {
                        _ = QueueIRCWorkCore(
                            _IRCQuickQueue,
                            ct => SendTellrawAsync("@a", sender + ": " + payload, "white", false, ct),
                            "chat relay",
                            quick: true,
                            cancellationToken: cancellationToken);
                    }
                }
            }
            catch (SocketException ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC socket error", ex));
            }
            catch (IOException ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC I/O error", ex));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC error", ex));
            }
            finally
            {
                try
                {
                    tokenRegistration.Dispose();
                }
                catch
                {
                }

                if (ReferenceEquals(_IRCWriter, writer))
                    _IRCWriter = null;

                try
                {
                    writer?.Dispose();
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

                SafeCloseIRCSocket(socket);
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

            reconnectDelayMs = GetNextIRCReconnectDelayMilliseconds(reconnectDelayMs);
        }
    }

    internal static int GetNextIRCReconnectDelayMilliseconds(int currentDelayMilliseconds)
        => Math.Min(currentDelayMilliseconds * 2, 15000);

}
