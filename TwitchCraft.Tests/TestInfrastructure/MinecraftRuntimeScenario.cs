using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;

namespace TwitchCraft.Tests.TestInfrastructure;

internal sealed class MinecraftRuntimeScenario : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly CancellationTokenSource _cts;
    private long _barrierID;

    private MinecraftRuntimeScenario(
        TemporaryDirectory directory,
        CancellationTokenSource cts,
        TwitchCraftConfig config,
        MainHandler runtime)
    {
        _directory = directory;
        _cts = cts;
        Config = config;
        Runtime = runtime;
    }

    internal TwitchCraftConfig Config { get; }
    internal MainHandler Runtime { get; }
    internal string JarPath => Config.Server.JarPath;
    internal CancellationToken Token => _cts.Token;

    internal static async Task<MinecraftRuntimeScenario> StartAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<string>? players = null,
        IReadOnlyList<string>? spectators = null,
        bool multiplayer = false,
        double maxHealth = 20,
        string? selectedItem = null,
        string? attributes = null,
        bool statistics = false,
        string version = "26.1.0")
    {
        TemporaryDirectory directory = new();
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready-responsive");
        IReadOnlyList<string> playerList = players ?? ["PlayerOne"];
        config.Identity.StreamerMinecraftName = playerList.Count > 0 ? playerList[0] : "PlayerOne";
        config.Server.MinecraftVersion = version;
        config.Settings.MultiplayerEnabled = multiplayer;
        config.Settings.StatisticsEnabled = statistics;
        FakeJavaServer.ConfigureResponsiveServer(
            config.Server.JarPath,
            playerList,
            spectators,
            maxHealth,
            selectedItem,
            attributes);

        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        MinecraftRuntimeScenario scenario = new(directory, cts, config, runtime);
        try
        {
            await runtime.ApplySettingsAsync(config);
            await runtime.StartServerAsync(config, cts.Token);
            _ = runtime.ReadOutputAsync(cts.Token);
            await runtime.StartServerIfNeededAsync(cts.Token);
            await FakeJavaServer.WaitForReadyAsync(runtime, cts.Token);
            await FakeJavaServer.WaitUntilAsync(
                () => runtime.HasOnlinePlayerSnapshot,
                "Responsive fake server did not publish its initial player snapshot.",
                cts.Token);
            _ = await runtime.GetPlayersAsync(cts.Token);
            return scenario;
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
    }

    internal Task DispatchAsync(string payload, string sender = "viewer", bool isModerator = false)
        => Runtime.DispatchAsync(payload, "!", sender, isModerator, Token);

    internal int CaptureCommandCursor() => FakeJavaServer.ReadAllLinesShared(JarPath + ".stdin").Count;

    internal void SetProbeDelay(int milliseconds)
        => File.WriteAllText(JarPath + ".probe-delay", Math.Max(0, milliseconds).ToString(CultureInfo.InvariantCulture));

    internal async Task<List<string>> DrainCommandsAsync(int cursor)
    {
        string barrier = "say tc_test_barrier_" + Interlocked.Increment(ref _barrierID).ToString(CultureInfo.InvariantCulture);
        if (!await Runtime.SendServerCommandAsync(barrier, Token))
            throw new InvalidOperationException("Could not send the test command barrier.");

        await FakeJavaServer.WaitUntilAsync(
            () =>
            {
                List<string> lines = FakeJavaServer.ReadAllLinesShared(JarPath + ".stdin");
                return lines.Count > cursor && lines.FindIndex(cursor, line => string.Equals(line, barrier, StringComparison.Ordinal)) >= 0;
            },
            "Fake Minecraft process did not receive the test command barrier.",
            Token);

        List<string> all = FakeJavaServer.ReadAllLinesShared(JarPath + ".stdin");
        int barrierIndex = all.FindIndex(cursor, line => string.Equals(line, barrier, StringComparison.Ordinal));
        return barrierIndex <= cursor ? [] : all.GetRange(cursor, barrierIndex - cursor);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await FakeJavaServer.StopRuntimeAndProcessAsync(Runtime, JarPath);
        _cts.Dispose();
        _directory.Dispose();
    }
}
