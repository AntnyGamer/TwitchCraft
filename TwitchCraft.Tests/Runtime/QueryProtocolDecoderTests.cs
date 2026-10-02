using System;
using System.Buffers.Binary;
using System.Text;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class QueryProtocolDecoderTests
{
    [Theory]
    [InlineData("5678", 5678)]
    [InlineData("-5678", -5678)]
    [InlineData("+5678", 5678)]
    [InlineData("0", 0)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void ParseChallenge_ReturnsValidatedNumericToken(string text, int expected)
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes(text + "\0"));

        Assert.Equal(expected, MinecraftQueryClient.ParseChallenge(packet, 1234));
    }

    [Fact]
    public void ParseChallenge_RejectsMismatchedSession()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 4321));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1.5")]
    [InlineData("１２")]
    public void ParseChallenge_RejectsInvalidOrOverflowingToken(string text)
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.UTF8.GetBytes(text + "\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 1234));
    }

    [Fact]
    public void ParseChallenge_RejectsUnexpectedPacketType()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(BuildPacket(0x00, 1234, [0]), 1234));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ParseChallenge_RejectsTruncatedHeaderOrMissingPayload(int packetLength)
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet[..packetLength], 1234));
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
    public void ParsePlayers_RejectsTruncatedPlayerList()
    {
        byte[] payload = Encoding.ASCII.GetBytes("hostname\0server\0\0player_\0\0Steve\0");
        byte[] packet = BuildPacket(0x00, 1234, payload);

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(packet, 1234));
    }

    [Fact]
    public void ParsePlayers_RejectsMissingPlayerSection()
    {
        byte[] packet = BuildPacket(0x00, 1234, Encoding.ASCII.GetBytes("hostname\0server\0\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(packet, 1234));
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
