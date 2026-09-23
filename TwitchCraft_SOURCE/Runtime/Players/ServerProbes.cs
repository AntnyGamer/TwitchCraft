using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private const string ProbeMarkerCommand = "difficulty";
    private const string ProbeMarkerResponse = "The difficulty is ";

    private void HandleReadyState(string line)
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

        ApplyPVPGameRule();
        QueueDeathSetup();
        QueueFirstSnapshot();
        QueueSidebarRefresh();
        QueueGamemode();
        QueueDeathScore();
    }

    private void ApplyPVPGameRule()
    {
        TwitchCraftConfig? config = _activeConfig;
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

        await _serverProbeSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        LinkedListNode<Action?>? marker = null;
        try
        {
            marker = AddProbeMarker(onProbeCompleted);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
            {
                (MainHandler handler, LinkedListNode<Action?> marker, Action onCompleted) =
                    ((MainHandler Handler, LinkedListNode<Action?> Marker, Action OnCompleted))state!;
                if (handler.TryCancelProbe(marker))
                    onCompleted();
            }, (this, marker, onProbeCompleted));

            if (await SendServerCommandsAsync([command, ProbeMarkerCommand], cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelProbe(marker, removeMarker: true))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (marker != null && TryCancelProbe(marker))
                onProbeCompleted();

            throw;
        }
        finally
        {
            _serverProbeSendGate.Release();
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

        await _serverProbeSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        LinkedListNode<Action?>? marker = null;
        try
        {
            marker = AddProbeMarker(onProbeCompleted);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state =>
            {
                (MainHandler handler, LinkedListNode<Action?> marker, Action onCompleted) =
                    ((MainHandler Handler, LinkedListNode<Action?> Marker, Action OnCompleted))state!;
                if (handler.TryCancelProbe(marker))
                    onCompleted();
            }, (this, marker, onProbeCompleted));

            probeCommands.Add(ProbeMarkerCommand);
            if (await SendServerCommandsAsync(probeCommands, cancellationToken).ConfigureAwait(false))
            {
                QueueProbeFallback(marker, onProbeCompleted, cancellationToken);
                return true;
            }

            if (TryCancelProbe(marker, removeMarker: true))
                onProbeCompleted();

            return false;
        }
        catch
        {
            if (marker != null && TryCancelProbe(marker))
                onProbeCompleted();

            throw;
        }
        finally
        {
            _serverProbeSendGate.Release();
        }
    }

    private LinkedListNode<Action?> AddProbeMarker(Action onProbeCompleted)
    {
        lock (_serverProbeMarkerGate)
        {
            LinkedListNode<Action?> marker = _pendingServerProbeMarkers.AddLast(onProbeCompleted);
            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
            return marker;
        }
    }

    private void QueueProbeFallback(LinkedListNode<Action?> marker, Action onProbeCompleted, CancellationToken cancellationToken)
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

    private bool TryCancelProbe(LinkedListNode<Action?> marker, bool removeMarker = false)
    {
        lock (_serverProbeMarkerGate)
        {
            if (!ReferenceEquals(marker.List, _pendingServerProbeMarkers))
                return false;

            bool pending = marker.Value != null;
            if (removeMarker)
            {
                _pendingServerProbeMarkers.Remove(marker);
                Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
            }
            else if (pending)
            {
                marker.Value = null;
            }

            return pending;
        }
    }

    private bool TryHandleProbe(string line)
    {
        if (Volatile.Read(ref _pendingServerProbeMarkerCount) <= 0 ||
            string.IsNullOrEmpty(line) ||
            !line.Contains(ProbeMarkerResponse, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Action? onCompleted;
        lock (_serverProbeMarkerGate)
        {
            LinkedListNode<Action?>? marker = _pendingServerProbeMarkers.First;
            if (marker == null)
                return false;

            onCompleted = marker.Value;
            _pendingServerProbeMarkers.RemoveFirst();
            Volatile.Write(ref _pendingServerProbeMarkerCount, _pendingServerProbeMarkers.Count);
        }

        onCompleted?.Invoke();
        return true;
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
        if (flags.HasEntityData)
            return true;

        if (string.IsNullOrEmpty(line))
            return false;

        if (line.Contains("pvp is now set to", StringComparison.OrdinalIgnoreCase) || line.Contains("difficulty has been set to", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set game difficulty to", StringComparison.OrdinalIgnoreCase) || isSidebarObjectiveIssue)
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
            line.Contains("Reset [Player List:] for ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_playerlist]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Removed objective [tc_health]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Created new objective [Player List:]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot sidebar to show objective Player List:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Set display slot list to show objective Health", StringComparison.OrdinalIgnoreCase) ||
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

}
