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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PVPSetting_AppliesToRunningMultiplayerServer(bool enabled)
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            multiplayer: true);

        scenario.Config.Settings.MultiplayerPVPEnabled = enabled;
        await scenario.Runtime.ApplySettingsAsync(scenario.Config);
        int cursor = scenario.CaptureCommandCursor();

        await scenario.Runtime.ApplyPVPGameRuleAsync();

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        Assert.Contains("gamerule minecraft:pvp " + enabled.ToString().ToLowerInvariant(), commands);
    }

    [Fact]
    public async Task QueryItem_CancelingOneCallerDoesNotCancelTheSharedServerProbe()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            selectedItem: selectedItem);
        scenario.SetProbeDelay(250);
        int cursor = scenario.CaptureCommandCursor();
        using CancellationTokenSource firstCaller = new();

        Task<string?> canceledTask = scenario.Runtime.QueryItemAsync("PlayerOne", firstCaller.Token);
        Task<string?> survivingTask = scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token);

        await FakeJavaServer.WaitUntilAsync(
            () => FakeJavaServer.ReadAllLinesShared(scenario.JarPath + ".stdin")
                .Skip(cursor)
                .Any(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal)),
            "Shared SelectedItem probe was not sent.",
            scenario.Token);

        firstCaller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);
        Assert.Equal(selectedItem, await survivingTask.WaitAsync(TimeSpan.FromSeconds(5), scenario.Token));

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        Assert.Equal(1, commands.Count(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlayerQueries_ReadHealthItemsAndHeartAttributes()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string attributes = "[{id:'minecraft:max_health',modifiers:[{id:'twitchcraft:heart_0123456789abcdef0123456789abcdef',amount:2.0d}]}]";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players: ["PlayerOne", "PlayerTwo"],
            maxHealth: 36,
            selectedItem: selectedItem,
            attributes: attributes);

        Assert.Equal(36, await scenario.Runtime.QueryMaxHealthAsync("PlayerOne", scenario.Token));
        Assert.Equal(selectedItem, await scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token));
        Assert.Equal(attributes, await scenario.Runtime.QueryHeartModifiersAsync("PlayerOne", scenario.Token));

        Dictionary<string, string?> items = await scenario.Runtime.QueryItemsAsync(
            ["PlayerTwo", "PlayerOne", "playerone"],
            scenario.Token);
        Assert.Equal(2, items.Count);
        Assert.Equal(selectedItem, items["PlayerOne"]);
        Assert.Equal(selectedItem, items["PlayerTwo"]);
    }
}
