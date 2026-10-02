using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class RCONRequestValidationTests
{
    [Fact]
    public async Task Queries_RecoverAfterOneInvalidResponseAndPreserveResultOrder()
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeout.Token;
        const string password = "batch-query-validation-password";
        await using FakeRCONServer RCON = new(password, wrongTypeResponseCommand: "broken",
            responseFactory: command => "reply:" + command);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            var responses = await MinecraftRCONClient.ExecuteQueriesAsync(
                "127.0.0.1", RCON.Port, password, ["first", "broken", "last"], cancellationToken);

            Assert.Equal(new string?[] { "reply:first", null, "reply:last" }, responses);
            Assert.Equal("reply:after", await MinecraftRCONClient.ExecuteQueryAsync(
                "127.0.0.1", RCON.Port, password, "after", cancellationToken));
            Assert.Equal(["first", "broken", "last", "after"], RCON.Commands);
        }
        finally
        {
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            await MinecraftRCONClient.DisconnectAsync(cleanup.Token);
        }
    }

    [Fact]
    public async Task Query_RejectsWrongResponseType()
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken cancellationToken = timeout.Token;
        const string password = "query-validation-password";
        await using FakeRCONServer RCON = new(password, wrongTypeResponseCommand: "list");

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => MinecraftRCONClient.ExecuteQueryAsync(
                "127.0.0.1", RCON.Port, password, "list", cancellationToken));
            Assert.True(await MinecraftRCONClient.ExecuteCommandAsync(
                "127.0.0.1", RCON.Port, password, "say recovered", cancellationToken));
            Assert.Equal(["list", "say recovered"], RCON.Commands);
        }
        finally
        {
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            await MinecraftRCONClient.DisconnectAsync(cleanup.Token);
        }
    }

    [Fact]
    public async Task PublicOperations_RejectInvalidRequests()
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
