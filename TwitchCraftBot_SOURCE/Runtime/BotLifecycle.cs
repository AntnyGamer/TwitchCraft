using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    public void InitializeWindow(TwitchCraftBot shellWindow)
    {
        _shellWindow = shellWindow;
    }

    public void ApplyStartProfile(bool multiplayerEnabled, bool requireOnlineMode, string streamerMinecraftName, bool remoteControlEnabled = false, string remoteHost = "", int RCONPort = 25575, string RCONPassword = "")
    {
        BotConfig config = ConfigurationStore.Load();

        config.Settings.MultiplayerEnabled = multiplayerEnabled;
        config.Settings.RequireOnlineMode = !multiplayerEnabled || requireOnlineMode;
        config.Settings.RemoteControlEnabled = remoteControlEnabled;
        if (remoteControlEnabled)
        {
            config.Server.RemoteHost = string.IsNullOrWhiteSpace(remoteHost) ? config.Server.RemoteHost : remoteHost.Trim();
            config.Server.RCON.Port = RCONPort;
            if (!string.IsNullOrWhiteSpace(RCONPassword))
            {
                if (!ConfigurationStore.TryNormalizeRconPassword(RCONPassword, out string normalizedRCONPassword))
                    throw new InvalidOperationException("RCON password is invalid.");

                config.Server.RCON.Password = normalizedRCONPassword;
            }
        }

        ConfigurationStore.NormalizeForRuntime(config);

        string trimmedMCUser = (streamerMinecraftName ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(trimmedMCUser))
        {
            config.Identity.StreamerMinecraftName = trimmedMCUser;
        }

        string currentBind = ConfigurationStore.NormalizeBindIP(config.Server.BindIP);
        if (!remoteControlEnabled && multiplayerEnabled)
        {
            if (currentBind.Length > 0
                && !string.Equals(currentBind, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentBind, "::", StringComparison.OrdinalIgnoreCase))
            {
                config.Server.PreviousBindIP = currentBind;
            }

            if (currentBind.Length == 0 || string.Equals(currentBind, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                currentBind = "0.0.0.0";
            else if (string.Equals(currentBind, "::1", StringComparison.OrdinalIgnoreCase))
                currentBind = "::";

            config.Server.BindIP = currentBind;
            config.Server.MaxPlayers = Math.Max(5, config.Server.MaxPlayers);
        }
        else if (!remoteControlEnabled)
        {
            string restoredBind = ConfigurationStore.NormalizeBindIP(config.Server.PreviousBindIP);
            config.Server.BindIP = restoredBind.Length > 0
                && !string.Equals(restoredBind, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(restoredBind, "::", StringComparison.OrdinalIgnoreCase)
                ? restoredBind
                : "127.0.0.1";
            config.Server.MaxPlayers = 1;
        }

        if (!remoteControlEnabled)
            ApplyStartProfileAndRemember(config);

        BotConfig persistedConfig = CloneConfig(config);
        persistedConfig.Settings.MultiplayerEnabled = false;
        persistedConfig.Settings.RemoteControlEnabled = false;
        persistedConfig.Settings.RequireOnlineMode = true;
        ConfigurationStore.Save(persistedConfig);
        SetActiveConfig(config);
        RefreshCatalogLists();
    }

    private void ApplyStartProfileAndRemember(BotConfig config)
    {
        _lastServerPropertiesContent = ServerPropertyEditor.ApplyStartProfile(config);
        _lastServerPropertiesPath = ServerPropertyEditor.GetPropertiesPath(config);
    }

    private bool ServerPropertiesChangedSinceLastApply(BotConfig config)
    {
        try
        {
            string path = ServerPropertyEditor.GetPropertiesPath(config);
            return !string.Equals(path, _lastServerPropertiesPath, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(_lastServerPropertiesContent)
                || !File.Exists(path)
                || !string.Equals(File.ReadAllText(path), _lastServerPropertiesContent, StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    public Task StartMainHandlerAsync()
        => StartSessionAsync(resetStatistics: true, countSessionStarted: true);

    public async Task<bool> BeginShutdownAsync()
    {
        try
        {
            await StopSessionAsync().ConfigureAwait(false);
            await MinigameManager.StopMinigameLoopsAsync(this).ConfigureAwait(false);
            CloseDataStoreConnections();
            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Shutdown failed", ex);
            return false;
        }
    }

    public async Task PauseAsync()
    {
        try
        {
            AddServerLogLine(RemoteControlEnabled ? "Stopping remote controller..." : "Pausing server...");
            await StopSessionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Pause failed", ex);
        }
    }

    public Task Restart() => RestartInternalAsync(false);

    public Task Reset() => RestartInternalAsync(true);

    public async Task RestartInternalAsync(bool wipeWorld)
    {
        try
        {
            BotConfig configBeforeRestart = CloneConfig(_activeConfig ?? ConfigurationStore.Load());
            if (configBeforeRestart.Settings.RemoteControlEnabled)
            {
                if (wipeWorld)
                {
                    _shellWindow?.AddChatLogLine("Reset is disabled in Remote Controller Mode because this app does not own the Minecraft world.");
                    return;
                }

                await StopSessionAsync().ConfigureAwait(false);
                await StartSessionAsync(resetStatistics: false, countSessionStarted: false).ConfigureAwait(false);
                return;
            }

            await StopSessionAsync().ConfigureAwait(false);

            if (wipeWorld)
            {
                BotConfig config = CloneConfig(_activeConfig ?? ConfigurationStore.Load());
                if (!string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
                {
                    string levelName = ServerPropertyEditor.GetLevelName(config);
                    string worldDir = ServerPropertyEditor.GetWorldDirectory(config);
                    if (Directory.Exists(worldDir))
                    {
                        Directory.Delete(worldDir, true);
                    }

                    if (config.Settings.MultiplayerEnabled)
                        DatapackInstaller.SyncLocatePlayersDatapack(config.Server.ServerDirectory, config.Server.MinecraftVersion, levelName);
                }

                ResetCurrentSurvivalForStatistics();
            }

            await StartSessionAsync(resetStatistics: false, countSessionStarted: wipeWorld).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowRestartError(_shellWindow, ex.Message);
        }
    }

    public async Task StartSessionAsync(bool resetStatistics = true, bool countSessionStarted = false)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_runtimeState == RuntimeState.Starting || _runtimeState == RuntimeState.Running)
            {
                return;
            }

            _runtimeState = RuntimeState.Starting;
            BotConfig config = CloneConfig(_activeConfig ?? ConfigurationStore.Load());

            bool shouldBeOnline = !config.Settings.MultiplayerEnabled || config.Settings.RequireOnlineMode;
            if (config.Settings.RequireOnlineMode != shouldBeOnline)
            {
                config.Settings.RequireOnlineMode = shouldBeOnline;
            }

            if (!config.Settings.RemoteControlEnabled)
                ApplyStartProfileAndRemember(config);
            ValidateConfig(config);
            SetActiveConfig(config);
            RefreshCatalogLists();
            _tokenStore.Load();
            EnsureStatisticsLoaded();
            Interlocked.Exchange(ref _serverExitExpected, 0);
            ResetSessionState();

            _sessionCts = new CancellationTokenSource();
            CancellationToken token = _sessionCts.Token;

            lock (_backgroundTasksGate)
            {
                _backgroundTasks.Clear();
            }

            if (config.Settings.RemoteControlEnabled)
            {
                await EnsureRemoteControllerConnectedAsync(config, token).ConfigureAwait(false);
            }
            else
            {
                await StartJavaServerAsync(config, token).ConfigureAwait(false);
                TrackSessionBackgroundTask(Task.Run(() => ReadServerOutputAsync(token), token));
                TrackSessionBackgroundTask(Task.Run(() => ReadServerErrorAsync(token), token));
                await EnsureServerProcessStartedAsync(token).ConfigureAwait(false);
            }

            _runtimeState = RuntimeState.Running;
            if (resetStatistics || countSessionStarted) ResetStatisticsForNewSession();
            if (countSessionStarted) RecordSessionStartedForStatistics();

            MinigameManager.StartMinigameLoops(this, token);

            TrackSessionBackgroundTask(Task.Run(() => RunIRCLoopAsync(token), token));
            TrackSessionBackgroundTask(Task.Run(() => RunViewerRosterLoopAsync(token), token));
            TrackSessionBackgroundTask(Task.Run(() => RunPlayerRosterLoopAsync(token), token));
            TrackSessionBackgroundTask(Task.Run(() => RunPassiveRewardLoopAsync(token), token));

            if (!config.Settings.RemoteControlEnabled)
                TrackSessionBackgroundTask(Task.Run(() => WatchServerProcessExitAsync(token), token));
            UIThread.BeginInvoke(() => _shellModel.Navigate(ShellPage.Main));
        }
        catch (Exception)
        {
            try
            {
                await MinigameManager.StopMinigameLoopsAsync(this, true).ConfigureAwait(false);
            }
            catch
            {
            }

            var failedSessionCts = Interlocked.Exchange(ref _sessionCts, null);

            failedSessionCts?.Cancel();
            failedSessionCts?.Dispose();

            lock (_backgroundTasksGate)
            {
                _backgroundTasks.Clear();
            }

            _runtimeState = RuntimeState.Stopped;

            SafeCloseIRCSocket();

            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await SafeStopProcessAsync(false).ConfigureAwait(false);

            FlushStatisticsForShutdown();
            CloseDataStoreConnections();

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopSessionAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource? sessionCts = null;
        try
        {
            if (_runtimeState == RuntimeState.Stopped || _runtimeState == RuntimeState.Stopping)
            {
                return;
            }

            _runtimeState = RuntimeState.Stopping;
            _minecraftServerReady = false;
            Interlocked.Exchange(ref _serverExitExpected, 1);
            ResetIRCQueues();
            PauseCurrentSurvivalForStatistics();
            sessionCts = _sessionCts;
            await SendIRCPartForShutdownAsync(sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            await MinigameManager.StopMinigameLoopsAsync(this, true).ConfigureAwait(false);
            if (sessionCts != null && !sessionCts.IsCancellationRequested)
            {
                sessionCts.Cancel();
            }

            SafeCloseIRCSocket();
            if (!RemoteControlEnabled)
                await TrySendStopCommandAsync().ConfigureAwait(false);
            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await SafeStopProcessAsync(true).ConfigureAwait(false);

            Task[] runningTasks;
            lock (_backgroundTasksGate)
                runningTasks = [.. _backgroundTasks];
            if (runningTasks.Length > 0)
            {
                await Task.WhenAny(Task.WhenAll(runningTasks), Task.Delay(3000)).ConfigureAwait(false);
            }

            _tokenStore.TryExportReadableJson();
            FlushStatisticsForShutdown();
            CloseDataStoreConnections();
        }
        catch
        {
            _tokenStore.TryExportReadableJson();
            FlushStatisticsForShutdown();
            CloseDataStoreConnections();
            SafeCloseIRCSocket();
            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await SafeStopProcessAsync(false).ConfigureAwait(false);
            throw;
        }
        finally
        {
            lock (_backgroundTasksGate)
            {
                _backgroundTasks.Clear();
            }
            _sessionCts = null;
            if (sessionCts != null)
            {
                try
                {
                    sessionCts.Dispose();
                }
                catch
                {
                }
            }

            _runtimeState = RuntimeState.Stopped;
            _lifecycleGate.Release();
        }
    }

    private void CloseDataStoreConnections()
    {
        _tokenStore.CloseConnection();
        BotStatisticsStore.CloseConnection();
    }

    private async Task EnsureServerProcessStartedAsync(CancellationToken cancellationToken)
    {
        Process process = _javaServerProcess
            ?? throw new InvalidOperationException("Minecraft server process could not be started.");

        await Task.Delay(250, cancellationToken).ConfigureAwait(false);

        if (!ReferenceEquals(process, _javaServerProcess) || process.HasExited)
        {
            throw new InvalidOperationException("Minecraft server process exited during startup.");
        }
    }
}
