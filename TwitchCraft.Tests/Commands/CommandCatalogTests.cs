using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchCraft.Tests.Economy;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class CommandCatalogTests
{
    [Fact]
    public void DefaultRegistry_HasExpectedCommandsFlagsAndSortedNames()
    {
        using RuntimeTestScope scope = new();
        MainHandler runtime = scope.Runtime;

        ChatCommandRegistry registry = ChatCommandRegistry.CreateDefault(runtime);
        IReadOnlyList<string> commandNames = registry.CommandNames;
        Assert.NotEmpty(commandNames);
        Assert.Equal(
            commandNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase),
            commandNames);
        string[] requiredCommands =
        [
            "addheart", "removeheart", "turnaround", "chargedcreeper",
            "johnny", "switchmilk", "swarm", "fireworks",
            "kick", "whitelistadd", "whitelistremove", "tokenleaderboard",
            "followreward", "commandstats", "tokenrank", "tiny", "giant"
        ];
        Assert.All(requiredCommands, command => Assert.True(registry.TryResolve(command, out _), command));

        ChatCommandStatisticFlags dangerous = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous;
        Assert.All(["addheart", "loot"], command =>
            Assert.Equal(ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice,
                registry.GetStatisticFlags(command)));
        Assert.All(["removeheart", "turnaround", "chargedcreeper", "johnny", "switchmilk", "swarm", "tiny", "giant"], command =>
            Assert.Equal(dangerous, registry.GetStatisticFlags(command)));
        Assert.Equal(ChatCommandStatisticFlags.GameAffecting, registry.GetStatisticFlags("fireworks"));
        Assert.All(
            ["kick", "whitelistadd", "whitelistremove", "tokenleaderboard", "followreward", "commandstats", "tokenrank"],
            command => Assert.Equal(ChatCommandStatisticFlags.None, registry.GetStatisticFlags(command)));
    }

    [Fact]
    public async Task GiveTokensAll_UsesFullRosterIncludingLongUsernames()
    {
        using RuntimeTestScope scope = new();
        MainHandler runtime = scope.Runtime;

        TwitchCraftConfig config = new();
        config.Twitch.StreamerName = "streamer";
        config.Twitch.BotName = "twitchcraft";
        await runtime.ApplySettingsAsync(config);

        runtime.ApplyViewerRoster(["viewer_one", "randomdudereincarnatedx3", "twitchcraft", "viewer_two", "TwitchCraft"]);

        await runtime.DispatchAsync(
            "!givetokens all 25",
            "!",
            "streamer",
            isModerator: false,
            TestContext.Current.CancellationToken);

        List<string> viewers = runtime.GetViewerRosterSnapshot();
        Assert.Equal(4, viewers.Count);
        Assert.All(["viewer_one", "randomdudereincarnatedx3", "twitchcraft", "viewer_two"],
            name => Assert.Contains(viewers, actual => string.Equals(name, actual, StringComparison.OrdinalIgnoreCase)));
        Assert.All(viewers, viewer => Assert.Equal(25, runtime.Tokens.GetBalance(viewer)));
    }

    [Fact]
    public async Task GiveTokens_RejectsUnauthorizedViewerAndAllowsStreamer()
    {
        using RuntimeTestScope scope = new();
        MainHandler runtime = scope.Runtime;
        TwitchCraftConfig config = new();
        config.Twitch.StreamerName = "streamer";

        await runtime.ApplySettingsAsync(config);

        await runtime.DispatchAsync(
            "!givetokens bob 25",
            "!",
            "viewer",
            isModerator: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, runtime.Tokens.GetBalance("bob"));

        await runtime.DispatchAsync(
            "!givetokens bob 25",
            "!",
            "streamer",
            isModerator: false,
            TestContext.Current.CancellationToken);
        Assert.Equal(25, runtime.Tokens.GetBalance("bob"));
    }

    [Fact]
    public async Task TradeTokens_RejectsOneTokenWithoutChangingBalances()
    {
        using RuntimeTestScope scope = new();
        MainHandler runtime = scope.Runtime;

        await runtime.ApplySettingsAsync(new TwitchCraftConfig());
        runtime.Tokens.Award("alice", 10);

        await runtime.DispatchAsync(
            "!tradetokens bob 1",
            "!",
            "alice",
            isModerator: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(10, runtime.Tokens.GetBalance("alice"));
        Assert.Equal(0, runtime.Tokens.GetBalance("bob"));
    }

    [Fact]
    public async Task TradeTokens_ChargesSenderAndCreditsHalfToRecipient()
    {
        using RuntimeTestScope scope = new();
        MainHandler runtime = scope.Runtime;

        await runtime.ApplySettingsAsync(new TwitchCraftConfig());
        runtime.Tokens.Award("alice", 50);

        await runtime.DispatchAsync(
            "!tradetokens bob 10",
            "!",
            "alice",
            isModerator: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(40, runtime.Tokens.GetBalance("alice"));
        Assert.Equal(5, runtime.Tokens.GetBalance("bob"));
    }
}
