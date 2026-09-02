using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot.Tests.Revamped.Economy;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Runtime;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class CommandRuntimeIntegrationTests
{
    [Fact]
    public async Task RunningBot_LiveCommandEnableDisableControlsChargeAndMinecraftDelivery()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            Enabled = false
        };
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("viewer", 100);

            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
            Assert.Empty(FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin"));

            config.Settings.CommandCustomizations["night"].Enabled = true;
            await runtime.ApplySettingsAsync(config);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 2, cancellationToken);
            List<string> commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin");

            Assert.Equal(85, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal("time set night", commands[0]);
            Assert.StartsWith("tellraw @a ", commands[1], StringComparison.Ordinal);
            Assert.Contains("viewer made it night", commands[1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task RunningBot_PerUserAndGlobalCooldownsEnforceCorrectScope()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            CooldownSeconds = 60
        };
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("alice", 100);
            runtime.Tokens.Award("bob", 100);

            await QueueCommandAndWaitAsync(runtime, "!night", "alice", cancellationToken);
            await QueueCommandAndWaitAsync(runtime, "!night", "alice", cancellationToken);
            await QueueCommandAndWaitAsync(runtime, "!night", "bob", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 4, cancellationToken);
            List<string> commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin");

            Assert.Equal(4, commands.Count);
            Assert.Equal(85, runtime.Tokens.GetBalance("alice"));
            Assert.Equal(85, runtime.Tokens.GetBalance("bob"));
            Assert.Equal(2, commands.Count(command => string.Equals(command, "time set night", StringComparison.Ordinal)));

            config.Settings.CommandCustomizations["night"] = new CommandCustomization
            {
                GlobalCooldownSeconds = 60
            };
            await runtime.ApplySettingsAsync(config);
            runtime.Tokens.Award("carol", 100);
            runtime.Tokens.Award("dave", 100);

            await QueueCommandAndWaitAsync(runtime, "!night", "carol", cancellationToken);
            await QueueCommandAndWaitAsync(runtime, "!night", "dave", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 6, cancellationToken);
            commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin");

            Assert.Equal(6, commands.Count);
            Assert.Equal(85, runtime.Tokens.GetBalance("carol"));
            Assert.Equal(100, runtime.Tokens.GetBalance("dave"));
            Assert.Equal(3, commands.Count(command => string.Equals(command, "time set night", StringComparison.Ordinal)));
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    [Fact]
    public async Task RunningBot_RemoteControllerSendsCommandOverRealRconConnection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "integration-password";
        await using FakeRconServer rcon = new(password);
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = rcon.Port;
        config.Server.RCON.Password = password;
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            Assert.True(await runtime.RunMinecraftCommandAsync("say remote-integration"));
            await rcon.WaitForCommandCountAsync(1, cancellationToken);

            Assert.Equal(["say remote-integration"], rcon.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RunningBot_RemoteControllerRecoversAfterAuthenticationFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "correct-integration-password";
        await using FakeRconServer rcon = new(password);
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = rcon.Port;
        config.Server.RCON.Password = "wrong-integration-password";
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            Assert.False(await runtime.RunMinecraftCommandAsync("say should-not-run"));
            Assert.Empty(rcon.Commands);

            config.Server.RCON.Password = password;
            await runtime.ApplySettingsAsync(config);

            Assert.True(await runtime.RunMinecraftCommandAsync("say recovered"));
            await rcon.WaitForCommandCountAsync(1, cancellationToken);
            Assert.Equal(["say recovered"], rcon.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RunningBot_RemoteControllerSendsCommandBatchOverOneRealConnection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "batch-integration-password";
        await using FakeRconServer rcon = new(password);
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = rcon.Port;
        config.Server.RCON.Password = password;
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            string[] commands = ["say first", "say second", "say third"];
            Assert.True(await runtime.SendServerCommandsAsync(commands, cancellationToken));
            await rcon.WaitForCommandCountAsync(commands.Length, cancellationToken);

            Assert.Equal(commands, rcon.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RunningBot_FailedMinecraftSendRefundsAndDoesNotConsumeCustomCooldown()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        BotConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            CooldownSeconds = 60
        };
        BotMainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("viewer", 100);

            await runtime.StopProcessSafeAsync(waitBriefly: false);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
            Assert.Empty(FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin"));

            await runtime.StartServerAsync(config, serverCts.Token);
            _ = runtime.ReadOutputAsync(serverCts.Token);
            await runtime.StartServerIfNeededAsync(serverCts.Token);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 2, cancellationToken);
            Assert.Equal(85, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    private static async Task StartReadyRuntimeAsync(
        BotMainHandler runtime,
        BotConfig config,
        CancellationToken cancellationToken)
    {
        await runtime.ApplySettingsAsync(config);
        await runtime.StartServerAsync(config, cancellationToken);
        _ = runtime.ReadOutputAsync(cancellationToken);
        await runtime.StartServerIfNeededAsync(cancellationToken);
        await FakeJavaServer.WaitForReadyAsync(runtime, cancellationToken);
    }

    private static async Task QueueCommandAndWaitAsync(
        BotMainHandler runtime,
        string payload,
        string sender,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(runtime.QueueCommand(
            ct => runtime.DispatchAsync(payload, "!", sender, isModerator: false, ct),
            payload,
            cancellationToken));
        Assert.True(runtime.QueueCommand(
            _ =>
            {
                completed.TrySetResult(true);
                return Task.CompletedTask;
            },
            "test-barrier",
            cancellationToken));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
}
