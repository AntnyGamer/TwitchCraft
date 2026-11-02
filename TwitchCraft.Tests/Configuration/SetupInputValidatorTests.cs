using System;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Configuration;

public sealed class SetupInputValidatorTests
{
    [Fact]
    public void CanStart_AcceptsValidFieldsAndAuthorization()
    {
        Assert.True(SetupInputValidator.CanStart(
            "1.21.8", "127.0.0.1", "client-id", "client-id", "access-token", "streamer_name", "bot_name"));
    }

    [Theory]
    [InlineData("", "127.0.0.1", "client-id", "client-id", "access-token", "streamer", "bot")]
    [InlineData("1.19.4", "127.0.0.1", "client-id", "client-id", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "not-an-ip", "client-id", "client-id", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "", "", "", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "new-client", "old-client", "access-token", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "", "streamer", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "access-token", "bad channel", "bot")]
    [InlineData("1.21.8", "127.0.0.1", "client-id", "client-id", "access-token", "streamer", "")]
    public void CanStart_RejectsMissingOrInvalidRequiredValues(
        string version,
        string bindIP,
        string clientID,
        string authorizedClientID,
        string token,
        string channel,
        string botName)
    {
        Assert.False(SetupInputValidator.CanStart(
            version, bindIP, clientID, authorizedClientID, token, channel, botName));
    }

    [Fact]
    public void ValidateRuntimeConfig_RejectsInvalidRemoteSettings()
    {
        (string Version, string Host, int Port, int RconPort, string Password, string Token, string Channel, string Reason)[] invalid =
        [
            ("unsupported", "localhost", 25565, 25575, "secret", "token", "streamer", "Minecraft version"),
            ("1.21.8", "bad host", 25565, 25575, "secret", "token", "streamer", "Remote controller host"),
            ("1.21.8", "[]", 25565, 25575, "secret", "token", "streamer", "Remote controller host"),
            ("1.21.8", ".", 25565, 25575, "secret", "token", "streamer", "Remote controller host"),
            ("1.21.8", "bad..host", 25565, 25575, "secret", "token", "streamer", "Remote controller host"),
            ("1.21.8", "[::1]junk", 25565, 25575, "secret", "token", "streamer", "Remote controller host"),
            ("1.21.8", "localhost", 0, 25575, "secret", "token", "streamer", "Minecraft server port"),
            ("1.21.8", "localhost", 25565, 0, "secret", "token", "streamer", "RCON port"),
            ("1.21.8", "localhost", 25565, 25575, "", "token", "streamer", "RCON password"),
            ("1.21.8", "localhost", 25565, 25575, "secret", "", "streamer", "Twitch bot token"),
            ("1.21.8", "localhost", 25565, 25575, "secret", "token", "", "Twitch channel name")
        ];

        Assert.All(invalid, item =>
        {
            TwitchCraftConfig config = new()
            {
                Server = { MinecraftVersion = item.Version, RemoteHost = item.Host, Port = item.Port, RCON = { Port = item.RconPort, Password = item.Password } },
                Twitch = { BotToken = item.Token, StreamerName = item.Channel },
                Settings = { RemoteControlEnabled = true }
            };
            Assert.Contains(item.Reason, Assert.Throws<InvalidOperationException>(
                () => SetupInputValidator.ValidateRuntimeConfig(config)).Message);
        });
    }
}
