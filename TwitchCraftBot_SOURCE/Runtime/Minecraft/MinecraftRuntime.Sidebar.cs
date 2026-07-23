using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task ClearPlayerSidebarAsync(CancellationToken cancellationToken)
    {
        if (!_minecraftServerReady)
        {
            lock (_playerGate)
            {
                _lastSidebarPlayers = [];
                _playerSidebarInitialized = false;
            }

            return;
        }

        await SendServerCommandsAsync(ClearPlayerSidebarCommands, cancellationToken).ConfigureAwait(false);

        lock (_playerGate)
        {
            _lastSidebarPlayers = [];
            _playerSidebarInitialized = false;
        }
    }

    private static bool SamePlayers(List<string> players, List<string> previousPlayers)
    {
        int count = players.Count;
        if (count != previousPlayers.Count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(players[i], previousPlayers[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task RefreshPlayerSidebarAsync(CancellationToken cancellationToken)
    {
        if (!MultiplayerEnabled || !_minecraftServerReady)
            return;

        List<string> players;
        List<string> previousPlayers;
        bool needsInitialization;

        lock (_playerGate)
        {
            needsInitialization = !_playerSidebarInitialized;
            if (_knownPlayers.Count == 0 && _lastSidebarPlayers.Count == 0)
                return;

            if (!needsInitialization && SamePlayers(_knownPlayers, _lastSidebarPlayers))
                return;

            players = _knownPlayers.Count == 0 ? [] : [.. _knownPlayers];
            previousPlayers = _lastSidebarPlayers.Count == 0 ? [] : [.. _lastSidebarPlayers];
        }

        if (players.Count == 0)
        {
            await ClearPlayerSidebarAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        List<string> commands = BuildPlayerSidebarCommands(players, previousPlayers, needsInitialization, UsesInlineTextComponentSyntax);
        if (commands.Count == 0)
            return;

        if (!await SendServerCommandsAsync(commands, cancellationToken).ConfigureAwait(false))
            return;

        lock (_playerGate)
        {
            _playerSidebarInitialized = true;
            _lastSidebarPlayers = players;
        }
    }

    private static List<string> BuildPlayerSidebarCommands(List<string> players, List<string> previousPlayers, bool needsInitialization, bool usesInlineTextComponents)
    {
        const string objective = "tc_playerlist";
        const string healthObjective = "tc_health";

        List<string> commands = new((needsInitialization ? 9 : 0) + previousPlayers.Count + players.Count);
        if (needsInitialization)
        {
            string playerListDisplay = BuildScoreboardDisplayComponent("Player List:", usesInlineTextComponents);
            string healthDisplay = BuildScoreboardDisplayComponent("Health", usesInlineTextComponents);

            commands.Add("scoreboard objectives remove " + objective);
            commands.Add("scoreboard objectives remove " + healthObjective);
            commands.Add("scoreboard objectives add " + objective + " dummy " + playerListDisplay);
            commands.Add("scoreboard objectives add " + healthObjective + " health " + healthDisplay);
            commands.Add("scoreboard objectives modify " + objective + " displayname " + playerListDisplay);
            commands.Add("scoreboard objectives modify " + healthObjective + " displayname " + healthDisplay);
            commands.Add("scoreboard objectives modify " + healthObjective + " rendertype hearts");
            commands.Add("scoreboard objectives setdisplay sidebar " + objective);
            commands.Add("scoreboard objectives setdisplay list " + healthObjective);
        }

        int playerIndex = 0;
        foreach (string oldName in previousPlayers)
        {
            while (playerIndex < players.Count && PlayerNameComparer.Compare(players[playerIndex], oldName) < 0)
                playerIndex++;

            if (playerIndex >= players.Count || !PlayerNameComparer.Equals(players[playerIndex], oldName))
                commands.Add("scoreboard players reset " + oldName + " " + objective);
        }

        int previousIndex = 0;
        int score = players.Count;
        foreach (string player in players)
        {
            while (previousIndex < previousPlayers.Count && PlayerNameComparer.Compare(previousPlayers[previousIndex], player) < 0)
                previousIndex++;

            bool scoreChanged = needsInitialization ||
                previousIndex >= previousPlayers.Count ||
                !PlayerNameComparer.Equals(previousPlayers[previousIndex], player) ||
                previousPlayers.Count - previousIndex != score;

            if (scoreChanged)
                commands.Add("scoreboard players set " + player + " " + objective + " " + score.ToString(CultureInfo.InvariantCulture));

            score--;
        }

        return commands;
    }

    private static string BuildScoreboardDisplayComponent(string text, bool usesInlineTextComponents)
    {
        return usesInlineTextComponents
            ? "{text:'" + MinecraftCommandBuilder.EscapeSnbtString(text) + "'}"
            : "{\"text\":\"" + MinecraftCommandBuilder.EscapeJson(text) + "\"}";
    }

    private void QueueInitialPlayerSnapshot()
    {
        if (!TryGetQueuedSessionToken(requireMultiplayer: true, out CancellationToken token) ||
            Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 1) != 0)
        {
            return;
        }

        RunQueuedSessionWork(
            RefreshOnlinePlayerSnapshotNowAsync,
            () => Interlocked.Exchange(ref _initialPlayerSnapshotQueued, 0),
            token: token);
    }

    private void QueuePlayerSidebarRefresh()
    {
        if (!TryGetQueuedSessionToken(requireMultiplayer: true, out CancellationToken token))
            return;

        int previous = Interlocked.CompareExchange(ref _playerSidebarRefreshQueued, 1, 0);
        if (previous != 0)
        {
            Interlocked.Exchange(ref _playerSidebarRefreshQueued, 2);
            return;
        }

        RunCoalescedQueuedSessionWork(
            RefreshPlayerSidebarAsync,
            () => Interlocked.CompareExchange(ref _playerSidebarRefreshQueued, 0, 1) == 1,
            () => Interlocked.Exchange(ref _playerSidebarRefreshQueued, 1),
            () => Interlocked.Exchange(ref _playerSidebarRefreshQueued, 0),
            onError: RecordPlayerSidebarRefreshFailure,
            token: token);
    }

}
