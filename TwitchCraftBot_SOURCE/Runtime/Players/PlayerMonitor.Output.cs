using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void HandleRemoteQueryResponse(string response)
    {
        if (!response.Contains('\n') && !response.Contains('\r'))
        {
            HandleRemoteQueryResponseLine(response);
            return;
        }

        using StringReader reader = new(response);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            HandleRemoteQueryResponseLine(line);
        }
    }

    private void HandleRemoteQueryResponseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        ServerLogLineFlags flags = new(line);
        if (flags.HasEntityData)
            HandleEntityDataLine(line);
        else if (flags.HasGameMode && TryHandlePlayerGamemodeLine(line, out string playerName, out int gameType))
            HandlePlayerGamemodeResult(playerName, gameType);

        RecordServerLineForStatistics(line, flags.HasTcDeaths);
    }

    private async Task ReadServerOutputAsync(CancellationToken cancellationToken)
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

                if (TryHandleServerProbeMarkerLine(line))
                    continue;

                ServerLogLineFlags flags = new(line);
                if (flags.HasProbeMarkerStorage)
                    continue;

                if (flags.HasEntityData)
                {
                    HandleEntityDataLine(line);
                }
                else if (flags.HasGameMode)
                {
                    if (TryHandlePlayerGamemodeLine(line, out string playerName, out int gameType))
                        HandlePlayerGamemodeResult(playerName, gameType);
                }

                bool mightContainCommandError = line.Contains("command", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("execute", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase);
                bool isCommandParserError = mightContainCommandError && IsCommandParserErrorLine(line);
                bool isUnexpectedCommandError = mightContainCommandError && IsUnexpectedCommandErrorLine(line);
                bool isMinecraftCommandErrorContext = mightContainCommandError && IsMinecraftCommandErrorContextLine(line);
                bool isSidebarObjectiveIssue = IsSidebarObjectiveIssueLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext);

                HandleServerReadyState(line);
                RecoverSidebarInitializationFromServerLine(isSidebarObjectiveIssue);
                RecordServerLineForStatistics(line, flags.HasTcDeaths);

                bool showCommandErrorContext = TryConsumeServerCommandErrorContextLine();
                if (isCommandParserError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 1);
                }
                else if (isUnexpectedCommandError)
                {
                    Interlocked.Exchange(ref _serverCommandErrorContextLines, 8);
                }

                bool suppressServerLogLine = ShouldSuppressServerLogLine(
                    line,
                    flags,
                    isCommandParserError,
                    isUnexpectedCommandError,
                    isMinecraftCommandErrorContext,
                    isSidebarObjectiveIssue);
                bool suppressOnlinePlayersLogLine = !suppressServerLogLine && ShouldSuppressOnlinePlayersLogLine(line);
                bool shouldShowLogLine = showCommandErrorContext ||
                    (!suppressServerLogLine && !suppressOnlinePlayersLogLine);

                if (isUnexpectedCommandError && shouldShowLogLine)
                {
                    ShowSuppressedServerLogContextLines();
                }

                if (shouldShowLogLine)
                {
                    _shellWindow?.AddServerLogLine(line);
                }
                else if (suppressServerLogLine && !flags.HasEntityData)
                {
                    RememberSuppressedServerLogContextLine(line);
                }

                CaptureOnlinePlayers(line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Server output reader failed", ex));
        }
    }

    private async Task ReadServerErrorAsync(CancellationToken cancellationToken)
    {
        Process? process = _javaServerProcess;
        if (process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                _shellWindow?.AddServerLogLine("[stderr] " + line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddServerLogLine(ErrorHandling.FormatLogMessage("Server error reader failed", ex));
        }
    }

    private async Task RunPlayerRosterLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (MultiTargetingEnabled)
                    QueueOnlinePlayerSnapshotRefresh();

                if (MultiplayerEnabled)
                    await RefreshPlayerSidebarAsync(cancellationToken).ConfigureAwait(false);

                QueueTrackedPlayerGamemodeRefreshForStatistics();
                QueueTrackedPlayerDeathScoreRefreshForStatistics();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                RecordPlayerSidebarRefreshFailure(ex);
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
            int index = FindSortedPlayerIndex(_knownPlayers, normalizedPlayer);
            if (index >= 0)
                return false;

            List<string> players = [.. _knownPlayers];
            players.Insert(~index, normalizedPlayer);
            _knownPlayers = players;
        }

        if (MultiplayerEnabled)
            QueuePlayerSidebarRefresh();

        return true;
    }

    private bool RemoveKnownPlayer(string playerName)
    {
        if (!MinecraftNameHelper.TryNormalizePlayerName(playerName, out string normalizedPlayer))
            return false;

        lock (_playerGate)
        {
            int index = FindSortedPlayerIndex(_knownPlayers, normalizedPlayer);
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
            QueuePlayerSidebarRefresh();

        return true;
    }

    private void CaptureOnlinePlayers(string line)
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
            string joinedPlayer = ExtractPlayerEventName(line, joinedMarker);
            if (joinedPlayer.Length > 0)
            {
                if (!AddKnownPlayer(joinedPlayer) && MultiplayerEnabled)
                    QueuePlayerSidebarRefresh();

                RemoveSpectatorPlayer(joinedPlayer);

                RecordPlayerJoinForStatistics(joinedPlayer);
                QueueTrackedPlayerGamemodeRefreshForStatistics(joinedPlayer);
                QueueOnlinePlayerSnapshotRefresh();
            }

            return;
        }

        int leftMarker = line.IndexOf(" left the game", StringComparison.OrdinalIgnoreCase);
        if (leftMarker >= 0)
        {
            string leftPlayer = ExtractPlayerEventName(line, leftMarker);
            if (leftPlayer.Length > 0)
            {
                if (!RemoveKnownPlayer(leftPlayer) && MultiplayerEnabled)
                    QueuePlayerSidebarRefresh();

                RemoveSpectatorPlayer(leftPlayer);

                RecordPlayerLeaveForStatistics(leftPlayer);
                QueueOnlinePlayerSnapshotRefresh();
            }

            return;
        }

        if (!TryParsePlayerListResponse(line, false, out List<string> players))
            return;

        ApplyOnlinePlayerSnapshot(players);
        CompleteOnlinePlayerSnapshotRequest(true);
    }

}
