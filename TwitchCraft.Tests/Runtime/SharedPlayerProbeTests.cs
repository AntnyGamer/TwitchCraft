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
    private const string ProbeMarkerPrefix = "data get storage twitchcraft:tc_probe_";

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
        int probeIndex = commands.FindIndex(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal));
        int markerIndex = commands.FindIndex(command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal));
        Assert.True(probeIndex >= 0);
        Assert.Equal(probeIndex + 1, markerIndex);
        Assert.Equal(1, commands.Count(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal)));
        Assert.DoesNotContain(commands, command => command.StartsWith("data modify storage twitchcraft:", StringComparison.Ordinal));
        Assert.Single(commands, command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal));
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

    [Fact]
    public async Task PlayerQueries_ConcurrentRequestsUseDistinctReadOnlyMarkers()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string attributes = "[{id:'minecraft:max_health',modifiers:[{id:'twitchcraft:heart_0123456789abcdef0123456789abcdef',amount:2.0d}]}]";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            maxHealth: 36,
            selectedItem: selectedItem,
            attributes: attributes);
        int cursor = scenario.CaptureCommandCursor();

        Task<double?> healthTask = scenario.Runtime.QueryMaxHealthAsync("PlayerOne", scenario.Token);
        Task<string?> itemTask = scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token);
        Task<string?> heartTask = scenario.Runtime.QueryHeartModifiersAsync("PlayerOne", scenario.Token);

        Assert.Equal(36, await healthTask);
        Assert.Equal(selectedItem, await itemTask);
        Assert.Equal(attributes, await heartTask);

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<string> markers = commands
            .Where(command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, markers.Count);
        Assert.Equal(markers.Count, markers.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(commands, command => command.StartsWith("data modify storage twitchcraft:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryItems_NormalizesPlayersAndUsesOneCompletionMarker()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players: ["PlayerOne", "PlayerTwo"],
            selectedItem: selectedItem);
        int cursor = scenario.CaptureCommandCursor();

        Dictionary<string, string?> items = await scenario.Runtime.QueryItemsAsync(
            ["PlayerTwo", "playerone", "PlayerOne"],
            scenario.Token);

        Assert.Equal(2, items.Count);
        Assert.Equal(selectedItem, items["PlayerOne"]);
        Assert.Equal(selectedItem, items["PlayerTwo"]);

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<int> itemProbeIndexes = commands
            .Select((command, index) => (command, index))
            .Where(entry => entry.command.EndsWith(" SelectedItem", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToList();
        int markerIndex = commands.FindIndex(command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal));

        Assert.Equal(2, itemProbeIndexes.Count);
        Assert.Single(commands, command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal));
        Assert.True(markerIndex > itemProbeIndexes[^1]);
        Assert.Contains(commands, command => command.Contains("name=\"PlayerOne\"", StringComparison.Ordinal) && command.EndsWith(" SelectedItem", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("name=\"PlayerTwo\"", StringComparison.Ordinal) && command.EndsWith(" SelectedItem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryItem_MissingProbeResponseCompletesAtReadOnlyMarker()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken);
        scenario.DropNextProbeResponses();
        int cursor = scenario.CaptureCommandCursor();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));

        Assert.Null(await scenario.Runtime.QueryItemAsync("PlayerOne", timeout.Token));

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        Assert.Single(commands, command => command.EndsWith(" SelectedItem", StringComparison.Ordinal));
        Assert.Single(commands, command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryItem_MissingProbeAndMarkerResponsesFallsBackAndNextProbeRecovers()
    {
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            selectedItem: selectedItem);
        scenario.DropNextProbeResponses();
        scenario.DropNextProbeMarkerResponses();
        int cursor = scenario.CaptureCommandCursor();

        Assert.Null(await scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token));
        Assert.Equal(selectedItem, await scenario.Runtime.QueryItemAsync("PlayerOne", scenario.Token));

        List<string> commands = await scenario.DrainCommandsAsync(cursor);
        List<string> markers = commands
            .Where(command => command.StartsWith(ProbeMarkerPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, markers.Count);
        Assert.Equal(2, markers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, commands.Count(command => command.EndsWith(" SelectedItem", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StartupPlayerProbes_NeverMutateProbeStorage()
    {
        await using MinecraftRuntimeScenario scenario = await MinecraftRuntimeScenario.StartAsync(
            TestContext.Current.CancellationToken,
            players: ["PlayerOne", "PlayerTwo"],
            spectators: ["PlayerTwo"]);

        List<string> commands = FakeJavaServer.ReadAllLinesShared(scenario.JarPath + ".stdin");
        List<string> markerCommands = commands
            .Where(command => command.Contains("tc_probe_", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(markerCommands);
        Assert.All(markerCommands, command => Assert.StartsWith(ProbeMarkerPrefix, command, StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => command.StartsWith("data modify storage twitchcraft:", StringComparison.Ordinal));
    }
}
