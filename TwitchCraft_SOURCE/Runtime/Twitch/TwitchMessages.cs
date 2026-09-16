using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    internal static string CleanChannelMessage(string message)
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

        int length = end - start + 1;
        bool hasLineBreak = message.AsSpan(start, length).IndexOfAny('\r', '\n') >= 0;
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

    internal static string TruncateUTF8(string message, int maxBytes)
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

        if (TwitchSession.UTF8NoBOM.GetByteCount(message) <= maxBytes)
            return message;

        int usedBytes = 0;
        int length = 0;
        while (length < message.Length)
        {
            int charCount = char.IsHighSurrogate(message[length]) && length + 1 < message.Length && char.IsLowSurrogate(message[length + 1]) ? 2 : 1;
            int nextBytes = TwitchSession.UTF8NoBOM.GetByteCount(message.AsSpan(length, charCount));
            if (usedBytes + nextBytes > maxBytes)
                break;

            usedBytes += nextBytes;
            length += charCount;
        }

        return message[..length];
    }

    public async Task SendChatAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StreamWriter? writer = _twitchSession.Writer;
        if (writer == null)
        {
            return;
        }

        string channelPrefix = _twitchSession.ChannelPrefix;
        int maxMessageBytes = _twitchSession.ChannelMessageMaxBytes;
        if (channelPrefix.Length == 0 || maxMessageBytes <= 0)
        {
            return;
        }

        string safeMessage = CleanChannelMessage(ApplyPrefix(message, CommandPrefix));
        safeMessage = TruncateUTF8(safeMessage, maxMessageBytes);

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
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("IRC write failed", ex));
        }
    }

    internal Task SendReplyAsync(
        string message,
        BotResponseKind kind,
        CancellationToken cancellationToken)
        => BotResponseVerbositySettings.ShouldSend(BotResponseVerbosity, kind)
            ? SendChatAsync(
                FormatReply(
                    message,
                    _currentCommandSender.Value ?? string.Empty,
                    _activeConfig?.Settings.MentionViewersInBotReplies == true),
                cancellationToken)
            : Task.CompletedTask;
}
