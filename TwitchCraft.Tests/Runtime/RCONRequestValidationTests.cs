using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;

namespace TwitchCraft.Tests.Runtime;

public sealed class RCONRequestValidationTests
{
    [Fact]
    public async Task Query_RejectsWrongResponseType()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string password = "query-validation-password";
        await using FakeRCONServer RCON = new(password, wrongTypeResponseCommand: "list");

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => MinecraftRCONClient.ExecuteQueryAsync(
                "127.0.0.1", RCON.Port, password, "list", cancellationToken));
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task PublicOperations_RejectInvalidRequestsWithoutOpeningSocket()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Assert.False(await MinecraftRCONClient.ExecuteCommandAsync(
            string.Empty,
            25575,
            "password",
            "list",
            cancellationToken));
        Assert.False(await MinecraftRCONClient.ExecuteCommandAsync(
            "localhost",
            0,
            "password",
            "list",
            cancellationToken));
        Assert.False(await MinecraftRCONClient.ExecuteCommandsAsync(
            "localhost",
            25575,
            string.Empty,
            ["list"],
            cancellationToken));
        Assert.Null(await MinecraftRCONClient.ExecuteQueryAsync(
            "localhost",
            70000,
            "password",
            "list",
            cancellationToken));
        Assert.Null(await MinecraftRCONClient.ExecuteQueriesAsync(
            "localhost",
            25575,
            "password",
            [],
            cancellationToken));
    }
}
