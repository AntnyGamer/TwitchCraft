using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class SharedPlayerProbeTests
{
    [Fact]
    public async Task QueryItem_CancelingOneCallerDoesNotCancelAnotherCallerForSamePlayer()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            selectedItem: selectedItem);
        scenario.SetProbeDelay(250);
        using CancellationTokenSource firstCaller = new();

        Task<string?> canceledTask = scenario.Runtime.QueryItemAsync("PlayerOne", firstCaller.Token);
        Task<string?> survivingTask = scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token);

        await FakeJavaServer.WaitUntilAsync(
            () => FakeJavaServer.ReadAllLinesShared(scenario.JarPath + ".stdin")
                .Any(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal)),
            "Shared item query was not sent.",
            scenario.Token);

        firstCaller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);
        Assert.Equal(selectedItem, await survivingTask.WaitAsync(TimeSpan.FromSeconds(5), scenario.Token));
    }

    [Fact]
    public async Task PlayerQueries_ConcurrentRequestsReturnIndependentResults()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string attributes = "[{id:'minecraft:max_health',modifiers:[{id:'twitchcraft:heart_0123456789abcdef0123456789abcdef',amount:2.0d}]}]";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            maxHealth: 36,
            selectedItem: selectedItem,
            attributes: attributes);

        Task<double?> healthTask = scenario.Runtime.QueryMaxHealthAsync("PlayerOne", scenario.Token);
        Task<string?> itemTask = scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token);
        Task<string?> heartTask = scenario.Runtime.QueryHeartModifiersAsync("PlayerOne", scenario.Token);

        Assert.Equal(36, await healthTask);
        Assert.Equal(selectedItem, await itemTask);
        Assert.Equal(attributes, await heartTask);
    }

    [Fact]
    public async Task QueryItems_ReturnsEachPlayersItemAndDeduplicatesNames()
    {
        const string playerOneItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string playerTwoItem = "{id:'minecraft:bow',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players: ["PlayerOne", "PlayerTwo"]);
        scenario.SetSelectedItem("PlayerOne", playerOneItem);
        scenario.SetSelectedItem("PlayerTwo", playerTwoItem);

        Dictionary<string, string?> items = await scenario.Runtime.QueryItemsAsync(
            ["PlayerTwo", "playerone", "PlayerOne"],
            scenario.Token);

        Assert.Equal(2, items.Count);
        Assert.Equal(playerOneItem, items["PlayerOne"]);
        Assert.Equal(playerTwoItem, items["PlayerTwo"]);
    }

    [Fact]
    public async Task QueryItem_MissingResponseReturnsNullWithoutHanging()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken);
        scenario.DropNextServerResponses();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));

        Assert.Null(await scenario.Runtime.QueryItemAsync("PlayerOne", timeout.Token));
    }

    [Fact]
    public async Task QueryItem_AfterFullResponseLossRecoversForTheNextRequest()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            selectedItem: selectedItem);

        scenario.DropNextServerResponses(2);
        Assert.Null(await scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token));

        scenario.DropNextServerResponses();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        Assert.Null(await scenario.Runtime.QueryItemAsync("PlayerOne", timeout.Token));

        Assert.Equal(selectedItem, await scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token));
    }

    [Fact]
    public async Task QueryItems_ConcurrentSingleAndBatchRequestsReturnConsistentResults()
    {
        const string playerOneItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string playerTwoItem = "{id:'minecraft:bow',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players: ["PlayerOne", "PlayerTwo"]);
        scenario.SetSelectedItem("PlayerOne", playerOneItem);
        scenario.SetSelectedItem("PlayerTwo", playerTwoItem);
        scenario.SetProbeDelay(100);

        Task<Dictionary<string, string?>> batchTask = scenario.Runtime.QueryItemsAsync(
            ["PlayerOne", "PlayerTwo"],
            scenario.Token);
        Task<string?> singleTask = scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token);

        Dictionary<string, string?> batch = await batchTask;
        Assert.Equal(playerOneItem, await singleTask);
        Assert.Equal(playerOneItem, batch["PlayerOne"]);
        Assert.Equal(playerTwoItem, batch["PlayerTwo"]);
    }
}
