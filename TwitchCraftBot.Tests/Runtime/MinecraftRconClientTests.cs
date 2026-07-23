using TwitchCraftBot_V1;

namespace TwitchCraftBot.Tests.Runtime;

public sealed class MinecraftRconClientTests
{
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
