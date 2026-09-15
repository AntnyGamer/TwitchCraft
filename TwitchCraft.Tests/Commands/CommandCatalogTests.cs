using System.Reflection;
using TwitchCraft.Tests.TestInfrastructure;
using TwitchCraft.Tests.Economy;
using TwitchCraft_V1;
using TwitchCraft_V1.Setup;

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

            ChatCommandRegistry registry = ChatCommandRegistry.CreateDefault(runtime);
            Assert.True(registry.TryResolve("givetokens", out ChatCommandHandler handler));

            await handler(["all", "25"], "streamer", CancellationToken.None);

            Assert.All(knownViewers, viewer => Assert.Equal(25, runtime.Tokens.GetBalance(viewer)));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
