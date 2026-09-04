using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void HandleRconResponse(string response)
    {
        if (response.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            HandleRconLine(response);
            return;
        }

        using StringReader reader = new(response);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            HandleRconLine(line);
        }
    }

    private void HandleRconLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        ServerLogLineFlags flags = new(line);
        if (flags.HasEntityData)
            HandleEntity(line);
        else if (flags.HasGameMode && TryHandleGamemode(line, out string playerName, out int gameType))
            HandleGamemode(playerName, gameType);

        Statistics.RecordLine(line, flags.HasTcDeaths);
    }

    internal async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                if (TryHandleProbe(line))
                    continue;

                ServerLogLineFlags flags = new(line);
                if (flags.HasProbeMarkerStorage)
                    continue;

                if (flags.HasEntityData)
                {
                    HandleEntity(line);
                }
                else if (flags.HasGameMode)
                {
                    if (TryHandleGamemode(line, out string playerName, out int gameType))
                        HandleGamemode(playerName, gameType);
                }

                bool mightContainCommandError = line.Contains("command", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("execute", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase);
                bool isCommandParserError = mightContainCommandError && IsParserError(line);
                bool isUnexpectedCommandError = mightContainCommandError && IsUnexpectedError(line);
                bool isMinecraftCommandErrorContext = mightContainCommandError && IsErrorContext(line);
                bool isSidebarObjectiveIssue = IsSidebarErrorLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext);

                HandleReadyState(line);
                RestoreSidebar(isSidebarObjectiveIssue);
                Statistics.RecordLine(line, flags.HasTcDeaths);

                bool showCommandErrorContext = TryConsumeError();
                if (isCommandParserError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 1);
                }
                else if (isUnexpectedCommandError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 8);
                }

                bool suppressServerLogLine = ShouldHideLogLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext,
                    isSidebarObjectiveIssue);
                bool suppressOnlinePlayersLogLine = !suppressServerLogLine && ShouldHidePlayerList(line);
                bool shouldShowLogLine = showCommandErrorContext ||
                    (!suppressServerLogLine && !suppressOnlinePlayersLogLine);

                if (isUnexpectedCommandError && shouldShowLogLine)
                {
                    ShowHiddenContext();
                }

                if (shouldShowLogLine)
                {
                    _shellWindow?.AddServerLogLine(line);
                }
                else if (suppressServerLogLine && !flags.HasEntityData)
                {
                    SaveHiddenContext(line);
                }

                CapturePlayers(line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Server output reader failed", ex));
        }
    }

    private async Task ReadErrorAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        MinecraftSTDERRFilter filter = new();
        Exception? readerFailure = null;

        void ShowStderrLine(string line) => _shellWindow?.AddServerLogLine("[stderr] " + line);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                filter.ProcessLine(line, ShowStderrLine);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            readerFailure = ex;
        }

        filter.Flush(ShowStderrLine);
        if (readerFailure != null)
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLog("Server error reader failed", readerFailure));
    }

    private async Task RunRosterAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (MultiTargetingEnabled)
                    QueueSnapshot();

                if (MultiplayerEnabled)
                    await RefreshSidebarAsync(cancellationToken).ConfigureAwait(false);

                QueueGamemode();
                QueueDeathScore();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSidebarError(ex);
            }

            try
            {
                await Task.Delay(10000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool AddKnownPlayer(string playerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayer))
            return false;

        lock (_playerGate)
        {
            int index = FindPlayerIndex(_knownPlayers, normalizedPlayer);
            if (index >= 0)
                return false;

            List<string> players = [.. _knownPlayers];
            players.Insert(~index, normalizedPlayer);
            _knownPlayers = players;
        }

        if (MultiplayerEnabled)
            QueueSidebarRefresh();

        return true;
    }

    private bool RemoveKnownPlayer(string playerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayer))
            return false;

        lock (_playerGate)
        {
            int index = FindPlayerIndex(_knownPlayers, normalizedPlayer);
            if (index < 0)
                return false;

            if (_knownPlayers.Count == 1)
            {
                _knownPlayers = [];
            }
            else
            {
                List<string> players = [.. _knownPlayers];
                players.RemoveAt(index);
                _knownPlayers = players;
            }
        }

        if (MultiplayerEnabled)
            QueueSidebarRefresh();

        return true;
    }

    private void CapturePlayers(string line)
    {
        if (string.IsNullOrEmpty(line) ||
            (!line.Contains("game", StringComparison.OrdinalIgnoreCase) &&
             !line.Contains("online", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        int joinedMarker = line.IndexOf(" joined the game", StringComparison.OrdinalIgnoreCase);
        if (joinedMarker >= 0)
        {
            string joinedPlayer = GetPlayerName(line, joinedMarker);
            if (joinedPlayer.Length > 0)
            {
                if (!AddKnownPlayer(joinedPlayer) && MultiplayerEnabled)
                    QueueSidebarRefresh();

                RemoveSpectator(joinedPlayer);

                Statistics.RecordPlayerJoin(joinedPlayer);
                QueueGamemode(joinedPlayer);
                QueueSnapshot();
            }

            return;
        }

        int leftMarker = line.IndexOf(" left the game", StringComparison.OrdinalIgnoreCase);
        if (leftMarker >= 0)
        {
            string leftPlayer = GetPlayerName(line, leftMarker);
            if (leftPlayer.Length > 0)
            {
                if (!RemoveKnownPlayer(leftPlayer) && MultiplayerEnabled)
                    QueueSidebarRefresh();

                RemoveSpectator(leftPlayer);

                Statistics.RecordPlayerLeave(leftPlayer);
                QueueSnapshot();
            }

            return;
        }

        if (!TryParseList(line, false, out List<string> players))
            return;

        ApplySnapshot(players);
        CompleteSnapshot(true);
    }
}
