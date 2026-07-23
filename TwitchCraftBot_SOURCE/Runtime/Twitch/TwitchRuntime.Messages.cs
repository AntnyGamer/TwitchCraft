using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
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
