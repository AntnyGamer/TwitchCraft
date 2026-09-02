using System.Reflection;
using TwitchCraftBot.Tests.TestInfrastructure;
using TwitchCraftBot.Tests.Revamped.Economy;
using TwitchCraftBot_V1;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot.Tests.Revamped.Commands;

[Collection(EconomyDatabaseCollection.Name)]
public sealed class CommandCatalogBehaviorTests
{
    [Fact]
    public void DefaultRegistry_HasExpectedCommandsFlagsAndStableSortedNames()
    {
        using TemporaryDirectory directory = new();
        BotMainHandler runtime = new(
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
            Assert.Same(commandNames, registry.CommandNames);
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
        BotMainHandler runtime = new(
            new AppShellViewModel(),
            Path.Combine(directory.Path, "viewer_tokens.db"));

        try
        {
            BotConfig config = new();
            config.Twitch.StreamerName = "streamer";
            await runtime.ApplySettingsAsync(config);

            FieldInfo knownChattersField = typeof(BotMainHandler).GetField(
                "_knownChatters",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(knownChattersField);
            List<string> knownChatters = Assert.IsType<List<string>>(knownChattersField.GetValue(runtime));
            knownChatters.AddRange(["viewer_one", "randomdudereincarnatedx3", "viewer_two"]);

            ChatCommandRegistry registry = ChatCommandRegistry.CreateDefault(runtime);
            Assert.True(registry.TryResolve("givetokens", out ChatCommandHandler handler));

            await handler(["all", "25"], "streamer", CancellationToken.None);

            Assert.All(knownChatters, viewer => Assert.Equal(25, runtime.Tokens.GetBalance(viewer)));
        }
        finally
        {
            runtime.Tokens.Close();
        }
    }
}
