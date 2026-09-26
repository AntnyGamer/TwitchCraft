using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft.Tests.Economy;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class CommandRuntimeIntegrationTests
{
    [Fact]
    public async Task LiveCommandToggle_ControlsChargeAndMinecraftDelivery()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            Enabled = false
        };
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            int commandCursor = await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("viewer", 100);

            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(commandCursor, FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin").Count);

            config.Settings.CommandCustomizations["night"].Enabled = true;
            await runtime.ApplySettingsAsync(config);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", commandCursor + 2, cancellationToken);
            List<string> commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin").Skip(commandCursor).ToList();

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
    public async Task PerUserAndGlobalCooldowns_EnforceCorrectScope()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            CooldownSeconds = 60
        };
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            int commandCursor = await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("alice", 100);
            runtime.Tokens.Award("bob", 100);

            await QueueCommandAndWaitAsync(runtime, "!night", "alice", cancellationToken);
            await QueueCommandAndWaitAsync(runtime, "!night", "alice", cancellationToken);
            await QueueCommandAndWaitAsync(runtime, "!night", "bob", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", commandCursor + 4, cancellationToken);
            List<string> commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin").Skip(commandCursor).ToList();

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

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", commandCursor + 6, cancellationToken);
            commands = FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin").Skip(commandCursor).ToList();

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
    public async Task RemoteControllerStart_ClearsStaleSidebarAndRecoversMobLoot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "startup-integration-password";
        await using FakeRCONServer RCON = new(password);
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = RCON.Port;
        config.Server.RCON.Password = password;
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);
            await runtime.EnsureRCONAsync(config, cancellationToken);

            Assert.Equal(
                [
                    "list",
                    "execute if data storage twitchcraft:runtime {slaughter_mob_loot:1b} run gamerule " + runtime.MobLootGameRuleName + " true",
                    "data remove storage twitchcraft:runtime slaughter_mob_loot",
                    "scoreboard objectives remove tc_playerlist",
                    "scoreboard objectives remove tc_health"
                ], RCON.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RemoteController_QueriesPlayerStateAndRejectsMalformedRCONResponse()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "integration-password";
        const string selectedItem = "{id:'minecraft:diamond_sword',count:1,components:{}}";
        const string attributes = "[{id:'minecraft:max_health',modifiers:[]}]";
        await using FakeRCONServer RCON = new(
            password,
            "say malformed",
            responseFactory: command =>
            {
                if (command.StartsWith("attribute ", StringComparison.Ordinal))
                    return "Value of attribute Max Health for entity PlayerOne is 36";
                if (command.EndsWith(" SelectedItem", StringComparison.Ordinal))
                {
                    string player = command.Contains("PlayerTwo", StringComparison.Ordinal) ? "PlayerTwo" : "PlayerOne";
                    return player + " has the following entity data: " + selectedItem;
                }
                if (command.EndsWith(" attributes", StringComparison.Ordinal) ||
                    command.EndsWith(" Attributes", StringComparison.Ordinal))
                    return "PlayerOne has the following entity data: " + attributes;
                return "OK";
            });
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = RCON.Port;
        config.Server.RCON.Password = password;
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            Assert.Equal(36, await runtime.QueryMaxHealthAsync("PlayerOne", cancellationToken));
            Assert.Equal(selectedItem, await runtime.QueryItemAsync("PlayerOne", cancellationToken));
            Dictionary<string, string?> items = await runtime.QueryItemsAsync(
                ["PlayerTwo", "PlayerOne", "playerone"], cancellationToken);
            Assert.Equal(2, items.Count);
            Assert.Equal(selectedItem, items["PlayerOne"]);
            Assert.Equal(selectedItem, items["PlayerTwo"]);
            Assert.Equal(attributes, await runtime.QueryHeartModifiersAsync("PlayerOne", cancellationToken));

            Assert.True(await runtime.RunMinecraftCommandAsync("say remote-integration"));
            Assert.False(await runtime.RunMinecraftCommandAsync("say malformed"));
            Assert.Equal("say remote-integration", RCON.Commands[RCON.Commands.Count - 2]);
            Assert.Equal("say malformed", RCON.Commands[RCON.Commands.Count - 1]);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RemoteManualCommand_RejectsMultilineInputWithoutSendingToServer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "multiline-integration-password";
        await using FakeRCONServer RCON = new(password);
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = RCON.Port;
        config.Server.RCON.Password = password;
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            Assert.True(await runtime.RunMinecraftCommandAsync("say baseline"));
            Assert.False(await runtime.RunMinecraftCommandAsync("say safe\nstop"));
            Assert.Equal(["say baseline"], RCON.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task RCONClient_RecoversAfterAuthenticationFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string password = "correct-integration-password";
        await using FakeRCONServer RCON = new(password);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            Assert.False(await MinecraftRCONClient.ExecuteCommandAsync(
                "127.0.0.1", RCON.Port, "wrong-integration-password", "say should-not-run", cancellationToken));
            Assert.Empty(RCON.Commands);

            Assert.True(await MinecraftRCONClient.ExecuteCommandAsync(
                "127.0.0.1", RCON.Port, password, "say recovered", cancellationToken));
            Assert.Equal(["say recovered"], RCON.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task RCONPartialBatch_SucceedsAndWrongTypeFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        const string password = "batch-integration-password";
        await using FakeRCONServer RCON = new(password, malformedResponseCommand: "say batch-two", wrongTypeResponseCommand: "say wrong-type");
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path);
        config.Settings.RemoteControlEnabled = true;
        config.Server.RemoteHost = "127.0.0.1";
        config.Server.RCON.Port = RCON.Port;
        config.Server.RCON.Password = password;
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);

        try
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            await runtime.ApplySettingsAsync(config);

            Assert.True(await runtime.RunMinecraftCommandAsync("say confirmed"));
            Assert.True(await runtime.SendServerCommandsAsync(
                ["say batch-one", "say batch-ok"], cancellationToken));
            Assert.True(await runtime.SendServerCommandsAsync(
                ["say batch-one", "say batch-two"], cancellationToken));
            Assert.False(await runtime.RunMinecraftCommandAsync("say wrong-type"));
            Assert.Equal(
                ["say confirmed", "say batch-one", "say batch-ok", "say batch-one", "say batch-two", "say wrong-type"],
                RCON.Commands);
        }
        finally
        {
            await MinecraftRCONClient.DisconnectAsync(cancellationToken);
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task FailedMinecraftSend_RefundsAndDoesNotConsumeCustomCooldown()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TemporaryDirectory directory = new();
        TwitchCraftConfig config = FakeJavaServer.CreateConfig(directory.Path, "ready");
        config.Settings.CommandCustomizations["night"] = new CommandCustomization
        {
            CooldownSeconds = 60
        };
        MainHandler runtime = FakeJavaServer.CreateRuntime(directory.Path);
        using CancellationTokenSource serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            int commandCursor = await StartReadyRuntimeAsync(runtime, config, serverCts.Token);
            runtime.Tokens.Award("viewer", 100);

            await runtime.StopProcessSafeAsync(waitBriefly: false);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            Assert.Equal(100, runtime.Tokens.GetBalance("viewer"));
            Assert.Equal(commandCursor, FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin").Count);

            await runtime.StartServerAsync(config, serverCts.Token);
            _ = runtime.ReadOutputAsync(serverCts.Token);
            await runtime.StartServerIfNeededAsync(serverCts.Token);
            await QueueCommandAndWaitAsync(runtime, "!night", "viewer", cancellationToken);

            await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", commandCursor + 2, cancellationToken);
            Assert.Equal(85, runtime.Tokens.GetBalance("viewer"));
        }
        finally
        {
            serverCts.Cancel();
            await FakeJavaServer.StopRuntimeAndProcessAsync(runtime, config.Server.JarPath);
        }
    }

    private static async Task<int> StartReadyRuntimeAsync(
        MainHandler runtime,
        TwitchCraftConfig config,
        CancellationToken cancellationToken)
    {
        await runtime.ApplySettingsAsync(config);
        await runtime.StartServerAsync(config, cancellationToken);
        _ = runtime.ReadOutputAsync(cancellationToken);
        await runtime.StartServerIfNeededAsync(cancellationToken);
        await FakeJavaServer.WaitForReadyAsync(runtime, cancellationToken);
        await FakeJavaServer.WaitForLineCountAsync(config.Server.JarPath + ".stdin", 4, cancellationToken);
        Assert.Equal(
            [
                "execute if data storage twitchcraft:runtime {slaughter_mob_loot:1b} run gamerule " + runtime.MobLootGameRuleName + " true",
                "data remove storage twitchcraft:runtime slaughter_mob_loot",
                "scoreboard objectives remove tc_playerlist",
                "scoreboard objectives remove tc_health"
            ],
            FakeJavaServer.ReadAllLinesShared(config.Server.JarPath + ".stdin"));
        return 4;
    }

    private static async Task QueueCommandAndWaitAsync(
        MainHandler runtime,
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
