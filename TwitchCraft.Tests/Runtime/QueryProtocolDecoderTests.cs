using System;
using System.Buffers.Binary;
using System.Text;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class QueryProtocolDecoderTests
{
    [Fact]
    public void ParseChallenge_ReturnsValidatedNumericToken()
    {
        (string Text, int Token)[] tokens =
        [
            ("5678", 5678),
            ("-42", -42),
            ("+7", 7),
            ("-2147483648", int.MinValue),
            ("2147483647", int.MaxValue)
        ];
        foreach ((string text, int token) in tokens)
        {
            byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes(text + "\0"));
            Assert.Equal(token, MinecraftQueryClient.ParseChallenge(packet, 1234));
        }
    }

    [Fact]
    public void ParseChallenge_RejectsMismatchedSession()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 4321));
    }

    [Fact]
    public void ParseChallenge_RejectsInvalidOrOutOfRangeToken()
    {
        foreach (string token in new[] { "invalid", "", "2147483648", "-2147483649" })
        {
            byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes(token + "\0"));
            Assert.Throws<InvalidOperationException>(() =>
                MinecraftQueryClient.ParseChallenge(packet, 1234));
        }
    }

    [Fact]
    public void ParseChallenge_RejectsUnexpectedPacketType()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(BuildPacket(0x00, 1234, [0]), 1234));
    }

    [Fact]
    public void ParseChallenge_RejectsTruncatedHeaderOrMissingPayload()
    {
        byte[] header = [0x09, 0, 0, 0, 1];
        for (int length = 0; length <= header.Length; length++)
        {
            byte[] packet = header.AsSpan(0, length).ToArray();
            Assert.Throws<InvalidOperationException>(() =>
                MinecraftQueryClient.ParseChallenge(packet, 1));
        }
    }

    [Fact]
    public void ParseChallenge_RejectsUnterminatedToken()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 1234));
    }

    [Fact]
    public void ParsePlayers_FiltersNormalizesSortsAndDeduplicatesNames()
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            "hostname\0server\0\0player_\0\0Steve\0Alex\0Steve\0bad-name\0\0");
        byte[] packet = BuildPacket(0x00, 1234, payload);

        Assert.Equal(["Alex", "Steve"], MinecraftQueryClient.ParsePlayers(packet, 1234));
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(BuildPacket(0x09, 1234, payload), 1234));
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(packet, 4321));
    }

    [Fact]
    public void ParsePlayers_AcceptsAnEmptyPlayerList()
    {
        byte[] payload = Encoding.ASCII.GetBytes("hostname\0server\0\0player_\0\0\0");
        byte[] packet = BuildPacket(0x00, 1234, payload);

        Assert.Empty(MinecraftQueryClient.ParsePlayers(packet, 1234));
    }

    [Fact]
    public void ParsePlayers_RejectsMissingOrTruncatedPlayerList()
    {
        string[] payloads =
        [
            "hostname\0server\0\0",
            "hostname\0server\0\0player_\0\0",
            "hostname\0server\0\0player_\0\0Steve\0"
        ];
        foreach (string payload in payloads)
        {
            byte[] packet = BuildPacket(0x00, 1234, Encoding.ASCII.GetBytes(payload));
            Assert.Throws<InvalidOperationException>(() =>
                MinecraftQueryClient.ParsePlayers(packet, 1234));
        }
    }

    private static byte[] BuildPacket(byte type, int sessionID, byte[] payload)
    {
        byte[] packet = new byte[5 + payload.Length];
        packet[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(1, 4), sessionID);
        payload.CopyTo(packet, 5);
        return packet;
    }
}
