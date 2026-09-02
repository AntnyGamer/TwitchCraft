using System.Buffers.Binary;
using System.Text;
using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

public sealed class QueryProtocolDecoderTests
{
    [Fact]
    public void ParseChallenge_ReturnsValidatedNumericToken()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678\0"));

        Assert.Equal(5678, MinecraftQueryClient.ParseChallenge(packet, 1234));
    }

    [Fact]
    public void ParseChallenge_RejectsMismatchedSession()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("5678\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 4321));
    }

    [Fact]
    public void ParseChallenge_RejectsMalformedResponses()
    {
        byte[] packet = BuildPacket(0x09, 1234, Encoding.ASCII.GetBytes("invalid\0"));

        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(packet, 1234));
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge(BuildPacket(0x00, 1234, [0]), 1234));
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParseChallenge([0x09, 0, 0, 0, 1], 1));
    }

    [Fact]
    public void ParsePlayers_FiltersNormalizesSortsAndDeduplicatesNames()
    {
        byte[] payload = Encoding.UTF8.GetBytes(
            "hostname\0server\0\0player_\0\0Steve\0Alex\0Steve\0bad-name\0\0");
        byte[] packet = BuildPacket(0x00, 1234, payload);

        List<string> players = MinecraftQueryClient.ParsePlayers(packet, 1234);

        Assert.Equal(["Alex", "Steve"], players);
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(BuildPacket(0x09, 1234, payload), 1234));
        Assert.Throws<InvalidOperationException>(() =>
            MinecraftQueryClient.ParsePlayers(packet, 4321));
    }

    private static byte[] BuildPacket(byte type, int sessionId, byte[] payload)
    {
        byte[] packet = new byte[5 + payload.Length];
        packet[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(1, 4), sessionId);
        payload.CopyTo(packet, 5);
        return packet;
    }
}
