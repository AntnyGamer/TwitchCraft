using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private void HandleReadyState(string line, CancellationToken cancellationToken)
    {
        if (_minecraftSession.ServerReady || string.IsNullOrEmpty(line))
            return;

        if (!line.Contains("Done (", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("For help, type \"help\"", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _minecraftSession.ServerReady = true;

        try
        {
            if (_activeConfig is { } activeConfig && activeConfig.Settings?.RemoteControlEnabled != true && ServerPropsChanged(activeConfig))
                ApplyProfile(activeConfig);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Failed to reformat server.properties after Minecraft startup", ex);
        }

        TrackTask(ApplyPvPGameRuleAsync());
        if (!MultiplayerEnabled) TrackTask(ClearSidebarAsync(cancellationToken));
        QueueDeathSetup();
        QueueFirstSnapshot();
        QueueSidebarRefresh();
        QueueGamemode();
        QueueDeathScore();
    }

    private Task ApplyPvPGameRuleAsync()
    {
        TwitchCraftConfig? config = _activeConfig;
        if (config == null || config.Settings.RemoteControlEnabled || !config.Settings.MultiplayerEnabled)
            return Task.CompletedTask;

        MinecraftVersionSupport.MinecraftVersionInfo version = MinecraftVersionSupport.GetVersion(config.Server.MinecraftVersion);
        if (!version.UsesServerSettingGameRules || !TryGetSessionToken(requireMultiplayer: false, out CancellationToken token))
            return Task.CompletedTask;

        string pvp = (version.UsesNamespacedGameRules ? "gamerule minecraft:pvp " : "gamerule pvp ") + (config.Settings.MultiplayerPvPEnabled ? "true" : "false");
        return SendServerCommandAsync(pvp, token);
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
                string? response = await ExecuteRCONQueryAsync(command, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(response))
                    HandleRCONResponse(response);

                return response != null;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        string marker = AddProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                (MainHandler handler, string marker) = ((MainHandler Handler, string Marker))state!;
                handler.CompleteProbe(marker);
            },
            (this, marker));

        try
        {
            if (await SendServerCommandsAsync(
                    [command, "data get storage " + ProbeMarkerNamespace + marker],
                    cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, cancellationToken);
                return true;
            }

            CompleteProbe(marker);

            return false;
        }
        catch
        {
            CompleteProbe(marker);

            throw;
        }
    }

    private async Task<bool> SendProbesAsync(string[] commands, Action onProbeCompleted, CancellationToken cancellationToken)
    {
        if (commands.Length == 0)
            return false;

        if (RemoteControlEnabled)
        {
            try
            {
                List<string?>? responses = await ExecuteRCONQueriesAsync(commands, cancellationToken).ConfigureAwait(false);
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
                        HandleRCONResponse(response);
                }

                return delivered;
            }
            finally
            {
                onProbeCompleted();
            }
        }

        List<string> probeCommands = new(commands.Length + 1);
        for (int i = 0; i < commands.Length; i++)
            if (!string.IsNullOrWhiteSpace(commands[i]))
                probeCommands.Add(commands[i]);

        if (probeCommands.Count == 0)
        {
            onProbeCompleted();
            return false;
        }

        string marker = AddProbeMarker(onProbeCompleted);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                (MainHandler handler, string marker) = ((MainHandler Handler, string Marker))state!;
                handler.CompleteProbe(marker);
            },
            (this, marker));

        try
        {
            probeCommands.Add("data get storage " + ProbeMarkerNamespace + marker);
            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, cancellationToken);
                return true;
            }

            CompleteProbe(marker);

            return false;
        }
        catch
        {
            CompleteProbe(marker);

            throw;
        }
    }

    private string AddProbeMarker(Action onProbeCompleted)
    {
        string marker = string.Create(
            CultureInfo.InvariantCulture,
            $"{_serverProbeMarkerSessionPrefix}{Interlocked.Increment(ref _serverProbeMarkerCounter)}");
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers[marker] = onProbeCompleted;
        }

        return marker;
    }

    private void QueueProbeFallback(string marker, CancellationToken cancellationToken)
    {
        _ = CompleteLaterAsync();

        async Task CompleteLaterAsync()
        {
            try
            {
                await Task.Delay(ServerProbeMarkerFallbackTimeout, cancellationToken).ConfigureAwait(false);
                CompleteProbe(marker);
            }
            catch (OperationCanceledException)
            {
                CompleteProbe(marker);
            }
            catch (Exception ex)
            {
                ErrorHandling.LogNonFatal("Server probe marker fallback failed", ex);
            }
        }
    }

    private void CompleteProbe(string marker)
    {
        Action? onCompleted = null;
        lock (_serverProbeMarkerGate)
        {
            _pendingServerProbeMarkers.Remove(marker, out onCompleted);
        }

        onCompleted?.Invoke();
    }

    private bool TryHandleProbe(string line)
    {
        string marker = GetProbeMarker(line);
        if (!marker.StartsWith(_serverProbeMarkerSessionPrefix, StringComparison.Ordinal))
            return false;

        CompleteProbe(marker);
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

    private static bool ShouldHideLogLine(
        string line,
        in ServerLogLineFlags flags,
        bool isCommandParserError,
        bool isUnexpectedCommandError,
        bool isMinecraftCommandErrorContext,
        bool isSidebarObjectiveIssue)
    {
        if (string.IsNullOrEmpty(line))
            return false;

        if (isUnexpectedCommandError || isCommandParserError || isMinecraftCommandErrorContext)
            return false;

        if (line.Contains("pvp is now set to", StringComparison.OrdinalIgnoreCase) || line.Contains("difficulty has been set to", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set game difficulty to", StringComparison.OrdinalIgnoreCase) || isSidebarObjectiveIssue)
            return true;

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
            line.Contains("Reset [Player List:] for ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_playerlist]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Created new objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot sidebar to show objective Player List:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot list to show objective Health", StringComparison.OrdinalIgnoreCase) ||
            (flags.HasTcPlayerList && flags.AlreadyExists) ||
            (flags.HasTcHealth && flags.AlreadyExists) ||
            flags.HasTcDeaths ||
            line.Contains("Created new objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Changed render type of [Health]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParserError(string line)
        => !string.IsNullOrEmpty(line) &&
           line.Contains("Unknown or incomplete command", StringComparison.OrdinalIgnoreCase) &&
           line.Contains("See below for error", StringComparison.OrdinalIgnoreCase);

}
