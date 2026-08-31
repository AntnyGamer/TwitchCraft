using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

internal static class MinecraftQueryClient
{
    private const byte MagicOne = 0xFE;
    private const byte MagicTwo = 0xFD;
    private const byte HandshakeType = 0x09;
    private const byte StatType = 0x00;
    private static readonly byte[] PlayerSectionMarker = Encoding.ASCII.GetBytes("player_\0\0");

    public static async Task<List<string>> GetPlayersAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return [];

        int sessionId = Environment.TickCount & 0x7FFFFFFF;
        using UdpClient client = IPAddress.TryParse(host, out IPAddress? IP) ? new(IP.AddressFamily) : new();
        client.Client.SendTimeout = 1000;
        client.Client.ReceiveTimeout = 1000;

        byte[] queryPacket = new byte[15];
        queryPacket[0] = MagicOne;
        queryPacket[1] = MagicTwo;
        queryPacket[2] = HandshakeType;
        WriteInt32BE(queryPacket, 3, sessionId);

        await client.SendAsync(queryPacket, 7, host, port).WaitAsync(cancellationToken).ConfigureAwait(false);
        UdpReceiveResult challengeResponse = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        int challengeToken = ParseChallenge(challengeResponse.Buffer, sessionId);

        queryPacket[2] = StatType;
        WriteInt32BE(queryPacket, 7, challengeToken);

        await client.SendAsync(queryPacket, queryPacket.Length, host, port).WaitAsync(cancellationToken).ConfigureAwait(false);
        UdpReceiveResult statResponse = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return ParsePlayers(statResponse.Buffer, sessionId);
    }

    internal static int ParseChallenge(byte[] buffer, int sessionId)
    {
        if (buffer.Length < 6 || buffer[0] != HandshakeType || ReadInt32BE(buffer) != sessionId)
            throw new InvalidOperationException("Minecraft query handshake returned an invalid response.");

        int end = 5;
        while (end < buffer.Length && buffer[end] != 0)
            end++;

        string text = Encoding.ASCII.GetString(buffer, 5, end - 5).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int token))
            throw new InvalidOperationException("Minecraft query handshake did not include a valid challenge token.");

        return token;
    }

    internal static List<string> ParsePlayers(byte[] buffer, int sessionId)
    {
        if (buffer.Length < 5 || buffer[0] != StatType || ReadInt32BE(buffer) != sessionId)
            throw new InvalidOperationException("Minecraft query stats returned an invalid response.");

        int playerSection = IndexOf(buffer, PlayerSectionMarker, 5);
        if (playerSection < 0)
            throw new InvalidOperationException("Minecraft query stats did not include a player section.");

        List<string> players = [];
        int start = playerSection + PlayerSectionMarker.Length;
        for (int i = start; i <= buffer.Length; i++)
        {
            if (i < buffer.Length && buffer[i] != 0)
                continue;

            if (i == start)
                break;

            string name = Encoding.UTF8.GetString(buffer, start, i - start);
            if (MinecraftNameHelper.TryNormalizePlayerName(name, out string normalizedPlayer))
                players.Add(normalizedPlayer);

            start = i + 1;
            if (start >= buffer.Length || buffer[start] == 0)
                break;
        }

        SortedListHelper.SortAndDeduplicate(players, StringComparer.OrdinalIgnoreCase);
        return players;
    }

    private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
    {
        if (pattern.Length == 0 || source.Length < pattern.Length)
            return -1;

        int offset = Math.Max(0, startIndex);
        if (offset > source.Length - pattern.Length)
            return -1;

        int index = source.AsSpan(offset).IndexOf(pattern);
        return index < 0 ? -1 : offset + index;
    }

    private static int ReadInt32BE(byte[] buffer)
        => BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(1, 4));

    private static void WriteInt32BE(byte[] buffer, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, 4), value);
}
