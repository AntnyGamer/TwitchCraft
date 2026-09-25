using System;
using System.Collections.Generic;
using System.IO;
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
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            ChatCommandRegistry registry = ChatCommandRegistry.CreateDefault(runtime);
            IReadOnlyList<string> commandNames = registry.CommandNames;
            string[] newCommands =
            [
                "addheart",
                "removeheart",
                "turnaround",
                "chargedcreeper",
                "kick",
                "whitelistadd",
                "whitelistremove",
                "tokenleaderboard",
                "followreward",
                "commandstats",
                "tokenrank",
                "tiny",
                "giant"
            ];

            Assert.NotEmpty(commandNames);
            Assert.Equal(
                commandNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase),
                commandNames);
            Assert.All(newCommands, command => Assert.True(registry.TryResolve(command, out _), command));

            ChatCommandStatisticFlags dangerous = ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Dangerous;
            Assert.Equal(ChatCommandStatisticFlags.GameAffecting | ChatCommandStatisticFlags.Nice, registry.GetStatisticFlags("addheart"));
            Assert.Equal(dangerous, registry.GetStatisticFlags("removeheart"));
            Assert.Equal(dangerous, registry.GetStatisticFlags("turnaround"));
            Assert.Equal(dangerous, registry.GetStatisticFlags("chargedcreeper"));
            Assert.Equal(dangerous, registry.GetStatisticFlags("tiny"));
            Assert.Equal(dangerous, registry.GetStatisticFlags("giant"));
            Assert.All(
                new[] { "kick", "whitelistadd", "whitelistremove", "tokenleaderboard", "followreward", "commandstats", "tokenrank" },
                command => Assert.Equal(ChatCommandStatisticFlags.None, registry.GetStatisticFlags(command)));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task GiveTokensAll_UsesCompleteLiveRosterIncludingLongUsernames()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            TwitchCraftConfig config = new();
            config.Twitch.StreamerName = "streamer";
            config.Twitch.BotName = "twitchcraft";
            await runtime.ApplySettingsAsync(config);

            runtime.ApplyViewerRoster(["viewer_one", "randomdudereincarnatedx3", "twitchcraft", "viewer_two"]);

            await runtime.DispatchAsync(
                "!givetokens all 25",
                "!",
                "streamer",
                isModerator: false,
                TestContext.Current.CancellationToken);

            List<string> viewers = runtime.GetViewerRosterSnapshot();
            Assert.Equal(3, viewers.Count);
            Assert.Contains("randomdudereincarnatedx3", viewers);
            Assert.DoesNotContain("twitchcraft", viewers);
            Assert.Equal(0, runtime.Tokens.GetBalance("twitchcraft"));
            Assert.All(viewers, viewer => Assert.Equal(25, runtime.Tokens.GetBalance(viewer)));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task GiveTokens_RejectsUnauthorizedViewerAndAllowsStreamer()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));
        TwitchCraftConfig config = new();
        config.Twitch.StreamerName = "streamer";

        try
        {
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
        finally
        {
            runtime.Tokens.Close();
        }
    }

    [Fact]
    public async Task TradeTokens_ChargesSenderAndCreditsHalfToRecipient()
    {
        using TemporaryDirectory directory = new();
        MainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
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
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
