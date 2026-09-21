using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    public void AttachWindow(TwitchCraft shellWindow)
    {
        _shellWindow = shellWindow;
    }

    public void ApplyProfile(bool multiplayerEnabled, bool requireOnlineMode, string streamerMinecraftName, bool remoteControlEnabled = false, string remoteHost = "", int RCONPort = 25575, string RCONPassword = "")
    {
        TwitchCraftConfig config = ConfigurationStore.Load();
        if (!remoteControlEnabled)
            _minecraftSession.ClearLocalRCONPassword();
        (int localRCONPort, string localRCONPassword) = (config.Server.RCON.Port, config.Server.RCON.Password);
        config.Settings.MultiplayerEnabled = multiplayerEnabled;
        config.Settings.RequireOnlineMode = !multiplayerEnabled || requireOnlineMode;
        config.Settings.RemoteControlEnabled = remoteControlEnabled;
        if (remoteControlEnabled)
        {
            config.Server.RemoteHost = string.IsNullOrWhiteSpace(remoteHost) ? config.Server.RemoteHost : remoteHost.Trim();
            config.Server.RCON.Port = RCONPort;
            if (!string.IsNullOrWhiteSpace(RCONPassword))
            {
                if (!ConfigurationStore.TryNormalizeRCONPassword(RCONPassword, out string normalizedRCONPassword))
                    throw new InvalidOperationException("RCON password is invalid.");
                config.Server.RCON.Password = normalizedRCONPassword;
            }
        }

        ConfigurationStore.NormalizeRuntime(config);

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
            ApplyProfile(config);
        TwitchCraftConfig persistedConfig = ConfigurationStore.Clone(config);
        if (remoteControlEnabled)
            (persistedConfig.Server.RCON.Port, persistedConfig.Server.RCON.Password) = (localRCONPort, localRCONPassword);
        persistedConfig.Settings.MultiplayerEnabled = false;
        persistedConfig.Settings.RemoteControlEnabled = false;
        persistedConfig.Settings.RequireOnlineMode = true;
        ConfigurationStore.Save(persistedConfig);
        SetConfig(config);
        _profileApplied = true;
    }

    private void ApplyProfile(TwitchCraftConfig config)
    {
        _lastServerPropertiesContent = ServerPropertyEditor.ApplyProfile(config);
        _lastServerPropertiesPath = ServerPropertyEditor.GetPropertiesPath(config);
    }

    private bool ServerPropsChanged(TwitchCraftConfig config)
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

    public Task StartAsync() => StartSessionAsync(resetStatistics: true, countSessionStarted: true);

    public async Task<bool> ShutdownAsync()
    {
        Interlocked.Exchange(ref _shutdownRequested, 1);
        try
        {
            await StopSessionAsync().ConfigureAwait(false);
            await MinigameManager.StopLoopsAsync(this).ConfigureAwait(false);

            _dataMaintenance.BackupOnShutdown();
            CloseStores();
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _shutdownRequested, 0);
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

    private async Task RunEmptyShutdownAsync(CancellationToken cancellationToken)
    {
        long emptySinceTicks = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                int delayMinutes = CurrentSettings.EmptyServerShutdownDelayMinutes;
                if (delayMinutes <= 0 || RemoteControlEnabled || !_minecraftSession.ServerReady)
                {
                    emptySinceTicks = 0;
                }
                else
                {
                    bool hasPlayers;
                    lock (_playerGate)
                        hasPlayers = _knownPlayers.Count > 0;

                    if (hasPlayers)
                    {
                        emptySinceTicks = 0;
                    }
                    else
                    {
                        long nowTicks = DateTime.UtcNow.Ticks;
                        if (emptySinceTicks == 0)
                            emptySinceTicks = nowTicks;
                        else if (nowTicks - emptySinceTicks >= delayMinutes * TimeSpan.TicksPerMinute)
                        {
                            AddServerLogLine("No players have been online for " + delayMinutes + " minutes. Pausing the Minecraft server.");
                            _ = Task.Run(PauseAsync, CancellationToken.None);
                            return;
                        }
                    }
                }

                await Task.Delay(15000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task RestartAsync() => RestartCoreAsync(false);

    public Task ResetAsync() => RestartCoreAsync(true);

    public async Task RestartCoreAsync(bool wipeWorld)
    {
        try
        {
            TwitchCraftConfig configBeforeRestart = ConfigurationStore.Clone(_activeConfig ?? ConfigurationStore.Load());
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
                TwitchCraftConfig config = ConfigurationStore.Clone(_activeConfig ?? ConfigurationStore.Load());
                if (!string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
                {
                    string levelName = ServerPropertyEditor.GetLevelName(config);
                    string worldDir = ServerPropertyEditor.GetWorldDirectory(config);
                    if (Directory.Exists(worldDir))
                    {
                        string retiredWorldDir = worldDir + ".reset-" + Guid.NewGuid().ToString("N");
                        Directory.Move(worldDir, retiredWorldDir);
                        FileSystemHelper.DeleteDirectorySafe(retiredWorldDir);
                    }

                    if (config.Settings.MultiplayerEnabled)
                        DatapackInstaller.SyncLocateDatapack(config.Server.ServerDirectory, config.Server.MinecraftVersion, levelName);
                }

                Statistics.ResetSurvival();
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
        if (Volatile.Read(ref _shutdownRequested) != 0) return;
        int stopGeneration = Volatile.Read(ref _lifecycleStopGeneration);
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _shutdownRequested) != 0) return;
            if (_runtimeState == RuntimeState.Starting || _runtimeState == RuntimeState.Running)
            {
                return;
            }

            _runtimeState = RuntimeState.Starting;
            _sessionCts = new CancellationTokenSource();
            if (stopGeneration != Volatile.Read(ref _lifecycleStopGeneration)) _sessionCts.Cancel();
            CancellationToken token = _sessionCts.Token;
            TwitchCraftConfig config = ConfigurationStore.Clone(_activeConfig ?? ConfigurationStore.Load());
            if (!config.Settings.RemoteControlEnabled && _minecraftSession.TakeLocalRCONPassword() is string localRCONPassword)
                config.Server.RCON.Password = localRCONPassword;
            config = await EnsureAuthAsync(config, token).ConfigureAwait(false);
            if (!config.Settings.MultiplayerEnabled)
                config.Settings.RequireOnlineMode = true;

            if (!config.Settings.RemoteControlEnabled)
                ApplyProfile(config);
            SetupInputValidator.ValidateRuntimeConfig(config);
            SetConfig(config);
            Tokens.Load(config.Settings.MaximumTokenBalance);
            Statistics.Load();
            _minecraftSession.ServerExitExpected = false;
            ResetSession();
            _backgroundTaskTracker.Clear();

            if (config.Settings.RemoteControlEnabled)
            {
                await EnsureRCONAsync(config, token).ConfigureAwait(false);
            }
            else
            {
                await StartServerAsync(config, token).ConfigureAwait(false);
                TrackTask(Task.Run(() => ReadOutputAsync(token), token));
                TrackTask(Task.Run(() => ReadErrorAsync(token), token));
                await StartServerIfNeededAsync(token).ConfigureAwait(false);
            }

            _runtimeState = RuntimeState.Running;
            if (resetStatistics || countSessionStarted) Statistics.ResetForSession();
            if (countSessionStarted) Statistics.RecordSession();

            MinigameManager.StartLoops(this, token);
            TrackTask(RunIRCAsync(token));
            TrackTask(RunViewerRosterAsync(token));
            TrackTask(RunRosterAsync(token));
            TrackTask(RunPassiveRewardsAsync(token));
            await RestartFollowRewardsAsync().ConfigureAwait(false);
            TrackTask(Task.Run(() => _dataMaintenance.RunAsync(token), token));
            TrackTask(RunEmptyShutdownAsync(token));

            if (!config.Settings.RemoteControlEnabled)
                TrackTask(WatchServerAsync(token));
            UIThread.BeginInvoke(() => _shellModel.Navigate(ShellPage.Main));
        }
        catch (Exception)
        {
            try
            {
                await MinigameManager.StopLoopsAsync(this, true).ConfigureAwait(false);
            }
            catch
            {
            }

            var failedSessionCts = Interlocked.Exchange(ref _sessionCts, null);

            failedSessionCts?.Cancel();
            failedSessionCts?.Dispose();

            _runtimeState = RuntimeState.Stopped;
            _twitchSession.CloseSocket();

            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await StopProcessSafeAsync(false).ConfigureAwait(false);
            for (Task? timeout = null; _backgroundTaskTracker.Snapshot() is { Length: > 0 } tasks && !(timeout ??= Task.Delay(3000)).IsCompleted;)
                await Task.WhenAny(tasks.Length == 1 ? tasks[0] : Task.WhenAll(tasks), timeout).ConfigureAwait(false);
            _backgroundTaskTracker.Clear();
            StatisticsService.FlushForShutdown();
            CloseStores();

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopSessionAsync()
    {
        Interlocked.Increment(ref _lifecycleStopGeneration);
        if (_runtimeState == RuntimeState.Starting)
            try { _sessionCts?.Cancel(); } catch (ObjectDisposedException) { }
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource? sessionCts = null;
        try
        {
            if (_runtimeState == RuntimeState.Stopped || _runtimeState == RuntimeState.Stopping)
            {
                return;
            }

            _runtimeState = RuntimeState.Stopping;
            await RestartFollowRewardsAsync().ConfigureAwait(false);
            _minecraftSession.ServerReady = false;
            _minecraftSession.ServerExitExpected = true;
            ResetQueues();
            Statistics.PauseSurvival();
            sessionCts = _sessionCts;
            await LeaveIRCAsync(sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            await MinigameManager.StopLoopsAsync(this, true).ConfigureAwait(false);
            if (sessionCts != null && !sessionCts.IsCancellationRequested)
            {
                sessionCts.Cancel();
            }

            await _timedPlayerScaleController.ResetAllAsync(CancellationToken.None).ConfigureAwait(false);

            for (Task? timeout = null; _backgroundTaskTracker.Snapshot() is { Length: > 0 } tasks && !(timeout ??= Task.Delay(3000)).IsCompleted;)
                await Task.WhenAny(tasks.Length == 1 ? tasks[0] : Task.WhenAll(tasks), timeout).ConfigureAwait(false);

            if (Commands.ResetHeartEffectsAsync != null) await Commands.ResetHeartEffectsAsync(true, CancellationToken.None).ConfigureAwait(false);
            _twitchSession.CloseSocket();
            if (!RemoteControlEnabled)
                await TryStopServerAsync().ConfigureAwait(false);
            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await StopProcessSafeAsync(true).ConfigureAwait(false);

            Tokens.TryExportJson();
            StatisticsService.FlushForShutdown();
            CloseStores();
        }
        catch
        {
            Tokens.TryExportJson();
            StatisticsService.FlushForShutdown();
            CloseStores();
            _twitchSession.CloseSocket();
            await MinecraftRCONClient.DisconnectAsync().ConfigureAwait(false);
            await StopProcessSafeAsync(false).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _backgroundTaskTracker.Clear();
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

    private void CloseStores()
    {
        Tokens.Close();
        StatisticsStore.CloseConnection();
    }

    internal async Task StartServerIfNeededAsync(CancellationToken cancellationToken)
    {
        Process process = _minecraftSession.Process
            ?? throw new InvalidOperationException("Minecraft server process could not be started.");
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);

        if (!ReferenceEquals(process, _minecraftSession.Process) || process.HasExited)
        {
            throw new InvalidOperationException("Minecraft server process exited during startup.");
        }
    }
}
