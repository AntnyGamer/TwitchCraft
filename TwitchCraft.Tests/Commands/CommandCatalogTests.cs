using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System;
using TwitchCraft.Tests.Economy;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft_V1.Setup;
using TwitchCraft_V1;
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
            await runtime.ApplySettingsAsync(config);

            FieldInfo knownViewersField = typeof(MainHandler).GetField(
                "_knownViewers",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(knownViewersField);
            List<string> knownViewers = Assert.IsType<List<string>>(knownViewersField.GetValue(runtime));
            knownViewers.AddRange(["viewer_one", "randomdudereincarnatedx3", "viewer_two"]);

            await runtime.DispatchAsync(
                "!givetokens all 25",
                "!",
                "streamer",
                isModerator: false,
                CancellationToken.None);

            Assert.All(
                runtime.GetViewerRosterSnapshot(),
                viewer => Assert.Equal(25, runtime.Tokens.GetBalance(viewer)));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
