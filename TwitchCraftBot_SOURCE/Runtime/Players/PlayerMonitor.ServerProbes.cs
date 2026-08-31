using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private void HandleReadyState(string line)
    {
        if (_minecraftServerReady || string.IsNullOrEmpty(line))
            return;

        if (!line.Contains("Done (", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("For help, type \"help\"", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _minecraftServerReady = true;

        try
        {
            if (_activeConfig is { } activeConfig && activeConfig.Settings?.RemoteControlEnabled != true && ServerPropsChanged(activeConfig))
                ApplyProfile(activeConfig);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to reformat server.properties after Minecraft startup", ex);
        }

        ApplyServerSettingGameRules();
        QueueDeathSetup();
        QueueFirstSnapshot();
        QueueSidebarRefresh();
        QueueGamemode();
        QueueDeathScore();
    }

    private void ApplyServerSettingGameRules()
    {
        BotConfig? config = _activeConfig;
        if (config == null || config.Settings.RemoteControlEnabled || !config.Settings.MultiplayerEnabled)
            return;

        MinecraftVersionSupport.MinecraftVersionInfo version = MinecraftVersionSupport.GetVersion(config.Server.MinecraftVersion);
        if (!version.UsesServerSettingGameRules || !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token))
            return;

        string pvp = (version.UsesNamespacedGameRules ? "gamerule minecraft:pvp " : "gamerule pvp ") + (config.Settings.MultiplayerPVPEnabled ? "true" : "false");
        TrackTask(SendServerCommandAsync(pvp, token));
    }

    private void RestoreSidebar(bool isSidebarObjectiveIssue)
    {
        if (!isSidebarObjectiveIssue)
            return;

        bool hasOnlinePlayers;
        lock (_playerGate)
        {
            hasOnlinePlayers = _knownPlayers.Count > 0;
            _playerSidebarInitialized = false;
        }

        if (hasOnlinePlayers)
            QueueSidebarRefresh();
    }

    private bool ShouldHidePlayerList(string line)
    {
        if (Volatile.Read(ref _suppressedOnlinePlayersLogLines) <= 0 || string.IsNullOrEmpty(line))
            return false;

        return (line.Contains("players online:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("player online:", StringComparison.OrdinalIgnoreCase))
            && TryReleasePlayerList();
    }

    private bool TryReleasePlayerList()
    {
        while (true)
        {
            int pending = Volatile.Read(ref _suppressedOnlinePlayersLogLines);
            if (pending <= 0)
                return false;

            if (Interlocked.CompareExchange(ref _suppressedOnlinePlayersLogLines, pending - 1, pending) == pending)
                return true;
        }
    }

    private async Task<bool> SendProbeAsync(string command, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            onProbeCompleted();
            return false;
        }

        if (RemoteControlEnabled)
        {
            try
            {
                string? response = await ExecuteRconQueryAsync(command, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(response))
                    HandleRconResponse(response);

                return response != null;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        string marker = AddProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            (BotMainHandler handler, string marker, Action onCompleted) = ((BotMainHandler Handler, string Marker, Action OnCompleted))state!;
            if (handler.TryCancelProbe(marker))
                onCompleted();
        }, (this, marker, onProbeCompleted));

        try
        {
            string escapedMarker = MinecraftCommandBuilder.EscapeJson(marker);
            string[] probeCommands =
            [
                command,
                "data modify storage " + ProbeMarkerStorage + " " + ProbeMarkerPath + " set value \"" + escapedMarker + "\"",
                "data get storage " + ProbeMarkerStorage + " " + ProbeMarkerPath
            ];

            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelProbe(marker))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (TryCancelProbe(marker))
                onProbeCompleted();

            throw;
        }
    }

    private async Task<bool> SendProbesAsync(string[] commands, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        if (commands.Length == 0)
            return false;

        bool remoteControlEnabled = RemoteControlEnabled;
        List<string> probeCommands = new(remoteControlEnabled ? commands.Length : commands.Length + 2);
        for (int i = 0; i < commands.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(commands[i]))
                probeCommands.Add(commands[i]);
        }

        if (probeCommands.Count == 0)
        {
            onProbeCompleted();
            return false;
        }

        if (remoteControlEnabled)
        {
            try
            {
                List<string?>? responses = await ExecuteRconQueriesAsync(probeCommands, cancellationToken).ConfigureAwait(false);
                if (responses == null)
                    return false;

                bool delivered = false;
                for (int i = 0, count = responses.Count; i < count; i++)
                {
                    string? response = responses[i];
                    if (response == null)
                        continue;

                    delivered = true;
                    if (!string.IsNullOrWhiteSpace(response))
                        HandleRconResponse(response);
                }

                return delivered;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        string marker = AddProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
        {
            (BotMainHandler handler, string marker, Action onCompleted) = ((BotMainHandler Handler, string Marker, Action OnCompleted))state!;
            if (handler.TryCancelProbe(marker))
                onCompleted();
        }, (this, marker, onProbeCompleted));

        try
        {
            string escapedMarker = MinecraftCommandBuilder.EscapeJson(marker);
            probeCommands.Add("data modify storage " + ProbeMarkerStorage + " " + ProbeMarkerPath + " set value \"" + escapedMarker + "\"");
            probeCommands.Add("data get storage " + ProbeMarkerStorage + " " + ProbeMarkerPath);
            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelProbe(marker))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (TryCancelProbe(marker))
                onProbeCompleted();

            throw;
        }
    }

    private string AddProbeMarker(Action onProbeCompleted)
    {
        string marker = string.Create(CultureInfo.InvariantCulture, $"{_serverProbeMarkerSessionPrefix}{Interlocked.Increment(ref _serverProbeMarkerCounter)}");
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers[marker] = onProbeCompleted;
            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
        }

        return marker;
    }

    private void QueueProbeFallback(string marker, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        _ = CompleteLaterAsync();

        async Task CompleteLaterAsync()
        {
            try
            {
                await Task.Delay(ServerProbeMarkerFallbackTimeout, cancellationToken).ConfigureAwait(false);
                if (TryCancelProbe(marker))
                    onProbeCompleted();
            }
            catch (OperationCanceledException)
            {
                if (TryCancelProbe(marker))
                    onProbeCompleted();
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Server probe marker fallback failed", ex);
            }
        }
    }

    private bool TryCancelProbe(string marker)
    {
        lock (_serverProbeMarkerGate)
        {
            bool removed = _pendingServerProbeMarkers.Remove(marker);
            if (removed)
                Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);

            return removed;
        }
    }

    private bool TryHandleProbe(string line)
    {
        if (Volatile.Read(ref _pendingServerProbeMarkerCount) <= 0)
            return false;

        string marker = GetProbeMarker(line);
        if (marker.Length == 0)
            return false;

        Action? onCompleted;
        lock (_serverProbeMarkerGate)
        {
            if (!_pendingServerProbeMarkers.Remove(marker, out onCompleted))
                return false;

            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
        }

        onCompleted();
        return true;
    }

    private static string GetProbeMarker(string line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        int markerIndex = line.IndexOf(ProbeMarkerPrefix, StringComparison.Ordinal);
        if (markerIndex < 0)
            return string.Empty;

        int end = markerIndex + ProbeMarkerPrefix.Length;
        while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '_'))
            end++;

        return line[markerIndex..end];
    }

    private static bool IsUnexpectedError(string line)
        => !string.IsNullOrEmpty(line) &&
           (line.Contains("An unexpected error occurred trying to execute that command", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("An unexpected error occurred while trying to execute that command", StringComparison.OrdinalIgnoreCase));

    private static bool IsErrorContext(string line)
        => !string.IsNullOrEmpty(line) &&
           (line.Contains("Command exception:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Failed to execute", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Unable to execute command", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Error trying to execute", StringComparison.OrdinalIgnoreCase));

    private void SaveHiddenContext(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_suppressedServerLogContextGate)
        {
            if (_suppressedServerLogContextLines.Count >= 8)
                _suppressedServerLogContextLines.Dequeue();

            _suppressedServerLogContextLines.Enqueue(line);
        }
    }

    private void ShowHiddenContext()
    {
        string[] lines;
        lock (_suppressedServerLogContextGate)
        {
            if (_suppressedServerLogContextLines.Count == 0)
                return;

            lines = [.. _suppressedServerLogContextLines];
            _suppressedServerLogContextLines.Clear();
        }

        foreach (string contextLine in lines)
        {
            _shellWindow?.AddServerLogLine(contextLine);
        }
    }

    private static bool ShouldHideLogLine(
        string line,
        in ServerLogLineFlags flags,
        bool isCommandParserError,
        bool isUnexpectedCommandError,
        bool isMinecraftCommandErrorContext,
        bool isSidebarObjectiveIssue)
    {
        if (flags.HasEntityData)
            return true;

        if (string.IsNullOrEmpty(line))
            return false;

        if (line.Contains("Gamerule pvp is now set to:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (isUnexpectedCommandError || isCommandParserError || isMinecraftCommandErrorContext)
            return false;

        if (!flags.HasObjective &&
            !flags.HasPlayerList &&
            !flags.HasTcMarker &&
            !flags.HasHealth &&
            !flags.HasDisplaySlot)
        {
            return false;
        }

        return
            line.Contains("An objective already exists by that name", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set [Player List:] for ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_playerlist]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Created new objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot sidebar to show objective Player List:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot list to show objective Health", StringComparison.OrdinalIgnoreCase) ||
            isSidebarObjectiveIssue ||
            (flags.HasTcPlayerList && flags.hasAlreadyExists) ||
            (flags.HasTcHealth && flags.hasAlreadyExists) ||
            flags.HasTcDeaths ||
            line.Contains("Created new objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Changed render type of [Health]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParserError(string line)
        => !string.IsNullOrEmpty(line) &&
           line.Contains("Unknown or incomplete command", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("See below for error", StringComparison.OrdinalIgnoreCase);

    private bool TryConsumeError()
    {
        while (true)
        {
            int pending = Volatile.Read(ref _serverCommandErrorContextLines);
            if (pending <= 0)
                return false;

            if (Interlocked.CompareExchange(ref _serverCommandErrorContextLines, pending - 1, pending) == pending)
                return true;
        }
    }

}
