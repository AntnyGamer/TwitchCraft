using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

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

                    if (!IRCMessage.TryParse(line, out IRCMessage? message) || message is null)
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
                    else if (_activeConfig?.Settings.NonCommandChatTellrawsEnabled != false)
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
                "[IRC] Reconnecting in " +
                (reconnectDelayMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) +
                " second(s)...");

            try
            {
                await Task.Delay(reconnectDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            reconnectDelayMs = Math.Min(reconnectDelayMs * 2, 15000);
        }
    }

    private async Task NotifyIRCCommandQueueOverloadAsync(CancellationToken cancellationToken)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long previousTicks = Volatile.Read(ref _lastIRCCommandOverflowNoticeTicks);
        if (previousTicks != 0 && nowTicks - previousTicks < IRCCommandOverflowNoticeIntervalTicks)
            return;

        if (Interlocked.CompareExchange(ref _lastIRCCommandOverflowNoticeTicks, nowTicks, previousTicks) != previousTicks)
            return;

        _shellWindow?.AddChatLogLine("[IRC] Command queue overloaded; skipped commands temporarily.");
        await SendToChannelAsync("The bot is backed up, so commands are being skipped for a moment. Try again in a few seconds.", cancellationToken).ConfigureAwait(false);
    }

    private bool QueueIRCWorkCore(
        IRCWorkQueueState state,
        Func<CancellationToken, Task> work,
        string context,
        bool quick,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        Queue<IRCQueuedWork> queueToRun;
        bool startProcessor;
        int generation;

        lock (state.Gate)
        {
            generation = Volatile.Read(ref _IRCQueueGeneration);
            if (cancellationToken.IsCancellationRequested)
                return false;

            int depth = Interlocked.Increment(ref state.Depth);
            if (depth > state.MaxDepth)
            {
                Interlocked.Decrement(ref state.Depth);
                return false;
            }

            state.Queue.Enqueue(new IRCQueuedWork(work, context, generation, cancellationToken));
            queueToRun = state.Queue;
            startProcessor = state.Active == 0;
            if (startProcessor)
                state.Active = 1;
        }

        if (startProcessor)
            TrackSessionBackgroundTask(Task.Run(() => ProcessIRCWorkQueueAsync(state, queueToRun, quick), CancellationToken.None));

        return true;
    }

    private async Task ProcessIRCWorkQueueAsync(IRCWorkQueueState state, Queue<IRCQueuedWork> queue, bool quick)
    {
        try
        {
            while (true)
            {
                IRCQueuedWork item;
                lock (state.Gate)
                {
                    if (!ReferenceEquals(queue, state.Queue) || queue.Count == 0)
                        return;

                    item = queue.Dequeue();
                }

                await ExecuteQueuedIRCWorkAsync(state, item, quick).ConfigureAwait(false);
            }
        }
        finally
        {
            bool restart = false;
            lock (state.Gate)
            {
                if (ReferenceEquals(queue, state.Queue))
                {
                    state.Active = 0;
                    if (queue.Count > 0)
                    {
                        state.Active = 1;
                        restart = true;
                    }
                }
            }

            if (restart)
                TrackSessionBackgroundTask(Task.Run(() => ProcessIRCWorkQueueAsync(state, queue, quick), CancellationToken.None));
        }
    }

    private async Task ExecuteQueuedIRCWorkAsync(IRCWorkQueueState state, IRCQueuedWork item, bool quick)
    {
        CancellationToken cancellationToken = item.CancellationToken;
        try
        {
            if (item.Generation == Volatile.Read(ref _IRCQueueGeneration) && !cancellationToken.IsCancellationRequested)
                await item.Work(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            string prefix = quick ? "Quick IRC " : "Queued IRC ";
            string context = quick ? item.Context : BuildCommandQueueContext(item.Context);
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage(prefix + context + " failed", ex));
        }
        finally
        {
            if (item.Generation == Volatile.Read(ref _IRCQueueGeneration))
                Interlocked.Decrement(ref state.Depth);
        }
    }

    private void ResetIRCQueues()
    {
        lock (_IRCCommandQueue.Gate)
            lock (_IRCQuickQueue.Gate)
            {
                Interlocked.Increment(ref _IRCQueueGeneration);
                ResetIRCQueueStateNoLock(_IRCCommandQueue);
                ResetIRCQueueStateNoLock(_IRCQuickQueue);
            }
    }

    private static void ResetIRCQueueStateNoLock(IRCWorkQueueState state)
    {
        state.Queue = new Queue<IRCQueuedWork>();
        Volatile.Write(ref state.Depth, 0);
        state.Active = 0;
    }

    private static bool IsIgnoredIRCUser(string sender, string botName, bool separateBotAccount)
    {
        if (separateBotAccount &&
            string.Equals(sender, botName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int i = 0; i < IgnoredIRCUsers.Length; i++)
        {
            if (string.Equals(sender, IgnoredIRCUsers[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string StripIRCTagsForLog(string line)
    {
        if (line.Length == 0 || line[0] != '@')
            return line;

        int firstSpace = line.IndexOf(' ');
        return firstSpace > 0 && firstSpace + 1 < line.Length ? line[(firstSpace + 1)..] : line;
    }

    private static string BuildCommandQueueContext(string payload)
    {
        const string Prefix = "command ";
        int commandEnd = payload.IndexOf(' ');
        return commandEnd > 0
            ? string.Concat(Prefix.AsSpan(), payload.AsSpan(0, commandEnd))
            : Prefix + payload;
    }

    private async Task DispatchCommandAsync(string payload, string sender, bool isModerator, CancellationToken cancellationToken)
    {
        ParsedCommand parsed = ParsedCommand.Parse(payload);
        if (parsed.Name.Length == 0)
            return;

        if (!_commandRegistry.TryResolve(parsed.Name, out ChatCommandHandler handler))
            return;

        SetCurrentCommandSenderModeratorState(isModerator);
        SetCurrentStatisticCommandName(parsed.Name);
        try
        {
            await handler(parsed.ArgumentArray, sender, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Command error in !" + parsed.Name, ex));
        }
        finally
        {
            SetCurrentStatisticCommandName(null);
            SetCurrentCommandSenderModeratorState(false);
        }
    }

    private async Task<string> ResolveAndPersistBotNameAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeTwitchToken(token);
        string resolvedBotName;

        lock (_botIdentityCacheGate)
        {
            resolvedBotName = string.Equals(_cachedBotToken, normalizedToken, StringComparison.Ordinal)
                ? _cachedBotName
                : string.Empty;
        }

        if (resolvedBotName.Length == 0)
        {
            await _botIdentityResolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_botIdentityCacheGate)
                {
                    resolvedBotName = string.Equals(_cachedBotToken, normalizedToken, StringComparison.Ordinal)
                        ? _cachedBotName
                        : string.Empty;
                }

                if (resolvedBotName.Length == 0)
                {
                    resolvedBotName = await GetValidatedBotNameAsync(normalizedToken, cancellationToken).ConfigureAwait(false);

                    if (resolvedBotName.Length > 0)
                    {
                        lock (_botIdentityCacheGate)
                        {
                            _cachedBotToken = normalizedToken;
                            _cachedBotName = resolvedBotName;
                        }
                    }
                }
            }
            finally
            {
                _botIdentityResolveGate.Release();
            }
        }

        if (resolvedBotName.Length == 0)
        {
            return resolvedBotName;
        }

        bool configAlreadyCurrent = _activeConfig?.Twitch != null
            && string.Equals(_currentBotName, resolvedBotName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeTwitchToken(_activeConfig.Twitch.BotToken), normalizedToken, StringComparison.Ordinal);

        if (!configAlreadyCurrent)
        {
            PersistResolvedBotIdentity(normalizedToken, resolvedBotName);
        }

        return resolvedBotName;
    }

    private void PersistResolvedBotIdentity(string normalizedToken, string resolvedBotName)
    {
        try
        {
            lock (_configPersistenceGate)
            {
                BotConfig config = ConfigurationStore.Update(configToUpdate =>
                {
                    configToUpdate.Twitch ??= new BotSetup.TwitchConfig();

                    if (string.Equals(NormalizeUser(configToUpdate.Twitch.BotName), resolvedBotName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(NormalizeTwitchToken(configToUpdate.Twitch.BotToken), normalizedToken, StringComparison.Ordinal))
                    {
                        return;
                    }

                    configToUpdate.Twitch.BotName = resolvedBotName;
                    configToUpdate.Twitch.BotToken = normalizedToken;
                });

                if (_activeConfig != null)
                {
                    BotConfig activeConfig = CloneConfig(_activeConfig);
                    activeConfig.Twitch.ClientID = config.Twitch.ClientID;
                    activeConfig.Twitch.BotName = config.Twitch.BotName;
                    activeConfig.Twitch.BotToken = config.Twitch.BotToken;
                    activeConfig.Twitch.StreamerName = config.Twitch.StreamerName;
                    SetActiveConfig(activeConfig);
                }
                else
                {
                    SetActiveConfig(config);
                }
            }
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Failed to save bot token identity", ex));
        }
    }

    private static async Task<string> GetValidatedBotNameAsync(string token, CancellationToken cancellationToken)
    {
        string normalizedToken = NormalizeTwitchToken(token);

        using HttpRequestMessage request = new(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildValidateHeader(normalizedToken));

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string JSON = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JObject payload = JObject.Parse(JSON);
        return CommandUserHelper.NormalizeUsername(payload["login"]?.ToString());
    }

    private void SafeCloseIRCSocket(TcpClient? socketToClose = null)
    {
        TcpClient? socket = socketToClose ?? _IRCSocket;
        if (socket == null)
            return;

        if (ReferenceEquals(socket, _IRCSocket))
            _IRCSocket = null;

        try
        {
            socket.Dispose();
        }
        catch
        {
        }
    }

    // ===== Twitch chat output =====

    private static string NormalizeOutgoingChannelMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        int start = 0;
        int end = message.Length - 1;
        while (start <= end && char.IsWhiteSpace(message[start]))
            start++;
        while (end >= start && char.IsWhiteSpace(message[end]))
            end--;

        if (start > end)
            return string.Empty;

        bool hasLineBreak = false;
        for (int i = start; i <= end; i++)
        {
            char c = message[i];
            if (c == '\r' || c == '\n')
            {
                hasLineBreak = true;
                break;
            }
        }

        int length = end - start + 1;
        if (!hasLineBreak)
            return start == 0 && length == message.Length ? message : message.Substring(start, length);

        return string.Create(length, (Message: message, Start: start), static (destination, state) =>
        {
            for (int i = 0; i < destination.Length; i++)
            {
                char c = state.Message[state.Start + i];
                destination[i] = c is '\r' or '\n' ? ' ' : c;
            }
        });
    }

    private static string TruncateUtf8ToByteCount(string message, int maxBytes)
    {
        if (maxBytes <= 0 || message.Length == 0)
            return string.Empty;

        if (message.Length <= maxBytes)
        {
            bool asciiOnly = true;
            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] > 0x7F)
                {
                    asciiOnly = false;
                    break;
                }
            }

            if (asciiOnly)
                return message;
        }

        if (IRCUtf8NoBom.GetByteCount(message) <= maxBytes)
            return message;

        int usedBytes = 0;
        int length = 0;
        while (length < message.Length)
        {
            int charCount = char.IsHighSurrogate(message[length]) && length + 1 < message.Length && char.IsLowSurrogate(message[length + 1]) ? 2 : 1;
            int nextBytes = IRCUtf8NoBom.GetByteCount(message.AsSpan(length, charCount));
            if (usedBytes + nextBytes > maxBytes)
                break;

            usedBytes += nextBytes;
            length += charCount;
        }

        return message[..length];
    }

    public async Task SendToChannelAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StreamWriter? writer = _IRCWriter;
        if (writer == null)
        {
            return;
        }

        string channelPrefix = _ircChannelPrefix;
        int maxMessageBytes = _ircChannelMessageMaxBytes;
        if (channelPrefix.Length == 0 || maxMessageBytes <= 0)
        {
            return;
        }

        string safeMessage = NormalizeOutgoingChannelMessage(message);
        safeMessage = TruncateUtf8ToByteCount(safeMessage, maxMessageBytes);

        if (safeMessage.Length == 0)
        {
            return;
        }

        try
        {
            await SendIRCLineAsync(writer, string.Concat(channelPrefix, safeMessage), cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("IRC write failed", ex));
        }
    }

}
