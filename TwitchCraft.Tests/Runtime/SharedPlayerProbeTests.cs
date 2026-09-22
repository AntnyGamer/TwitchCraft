using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Runtime;

public sealed class SharedPlayerProbeTests
{
    [Fact]
    public async Task QueryMaxHealth_CancelingOneCallerDoesNotCancelTheSharedServerProbe()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready-probes");
        FakeJavaServer.SetProbeDelay(config, 250);
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        using CancellationTokenSource firstCaller = new();

        try
        {
            await FakeJavaServer.StartReadyRuntimeAsync(runtime, config, serverCts.Token);

            Task<double?> canceledTask = runtime.QueryMaxHealthAsync("streamer", firstCaller.Token);
            await FakeJavaServer.WaitUntilAsync(
                () => FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin")
                    .Any(static line => line.StartsWith("attribute ", StringComparison.Ordinal) &&
                                        line.Contains("max_health", StringComparison.OrdinalIgnoreCase)),
                "The health probe was not sent.",
                testCancellation);

            Task<double?> survivingTask = runtime.QueryMaxHealthAsync("streamer", serverCts.Token);
            firstCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTask);

            Assert.Equal(20d, await survivingTask.WaitAsync(TimeSpan.FromSeconds(10), testCancellation));
            List<string> commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin");
            Assert.Equal(
                1,
                commands.Count(static line =>
                    line.StartsWith("attribute ", StringComparison.Ordinal) &&
                    line.Contains("max_health", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public void HealthProbes_ParseVanillaFeedback()
    {
        Assert.True(MainHandler.TryParseMaxHealthResponse("Value of attribute Max Health for entity PlayerOne is 20.0", "PlayerOne", out double health));
        Assert.Equal(20, health);
        Assert.True(MainHandler.TryParseMaxHealthResponse("The value of attribute Max Health for entity [VIP] PlayerOne is 30.0", "PlayerOne", out health));
        Assert.Equal(30, health);
        Assert.True(MainHandler.TryParseMaxHealthResponse("Value of attribute minecraft:max_health for entity PlayerOne is 40", "PlayerOne", out health));
        Assert.Equal(40, health);
        Assert.False(MainHandler.TryParseMaxHealthResponse("Value of attribute Max Health for entity PlayerOne2 is 20", "PlayerOne", out _));
        Assert.Equal(["twitchcraft:heart_0123456789abcdef0123456789abcdef"], MainHandler.ParseHeartModifierIDs("[{id:\"minecraft:max_health\",modifiers:[{id:\"twitchcraft:heart_0123456789abcdef0123456789abcdef\",amount:-4.0d}]}]", true));
        Assert.Equal(["01234567-89ab-cdef-fedc-ba9876543210"], MainHandler.ParseHeartModifierIDs("[{Name:\"minecraft:generic.max_health\",Modifiers:[{UUID:[I;19088743,-1985229329,-19088744,1985229328],Name:\"twitchcraft_health\",Amount:2.0d}]}]", false));
    }
}
