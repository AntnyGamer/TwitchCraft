using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    public async Task ApplySettingsAsync(TwitchCraftConfig config, bool refreshMinigameLoops = false, bool preserveTwitchAuth = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            TwitchCraftConfig activeConfig = ConfigurationStore.Clone(config);
            ConfigurationStore.NormalizeRuntime(activeConfig);
            bool minigamesEnabledChanged = false, difficultyChanged = false, pvpChanged = false, passiveScheduleChanged = false, followRewardsChanged = false, twitchAuthChanged = false, maximumBalanceNeedsClamp = false;

            lock (_configPersistenceGate)
            {
                if (_activeConfig != null)
                {
                    minigamesEnabledChanged = _activeConfig.Settings.MinigamesEnabled != activeConfig.Settings.MinigamesEnabled;
                    difficultyChanged = _activeConfig.Settings.Difficulty != activeConfig.Settings.Difficulty;
                    pvpChanged = _activeConfig.Settings.MultiplayerPvPEnabled != activeConfig.Settings.MultiplayerPvPEnabled;
                    followRewardsChanged = _activeConfig.Settings.AutomaticFollowRewardsEnabled != activeConfig.Settings.AutomaticFollowRewardsEnabled;
                    twitchAuthChanged = !preserveTwitchAuth && !string.Equals(NormalizeToken(_activeConfig.Twitch.BotToken), NormalizeToken(activeConfig.Twitch.BotToken), StringComparison.Ordinal);
                    maximumBalanceNeedsClamp = activeConfig.Settings.MaximumTokenBalance > 0 && (_activeConfig.Settings.MaximumTokenBalance == 0 || activeConfig.Settings.MaximumTokenBalance < _activeConfig.Settings.MaximumTokenBalance);
                    passiveScheduleChanged =
                        _activeConfig.Settings.PassiveTokenEarningEnabled != activeConfig.Settings.PassiveTokenEarningEnabled ||
                        _activeConfig.Settings.PassiveTokenPayoutMinimumSeconds != activeConfig.Settings.PassiveTokenPayoutMinimumSeconds ||
                        _activeConfig.Settings.PassiveTokenPayoutMaximumSeconds != activeConfig.Settings.PassiveTokenPayoutMaximumSeconds ||
                        _activeConfig.Settings.PassiveRewardsRequireActivity != activeConfig.Settings.PassiveRewardsRequireActivity ||
                        _activeConfig.Settings.PassiveActivityWindowMinutes != activeConfig.Settings.PassiveActivityWindowMinutes;
                    activeConfig.Settings.MultiplayerEnabled = _activeConfig.Settings.MultiplayerEnabled;
                    activeConfig.Settings.RemoteControlEnabled = _activeConfig.Settings.RemoteControlEnabled;
                    activeConfig.Settings.RequireOnlineMode = _activeConfig.Settings.RequireOnlineMode;
                    if (_activeConfig.Settings.RemoteControlEnabled || _runtimeState != RuntimeState.Stopped)
                        (activeConfig.Server.RCON.Port, activeConfig.Server.RCON.Password) = (_activeConfig.Server.RCON.Port, _activeConfig.Server.RCON.Password);
                    if (preserveTwitchAuth)
                    {
                        activeConfig.Twitch.BotToken = _activeConfig.Twitch.BotToken;
                        activeConfig.Twitch.RefreshToken = _activeConfig.Twitch.RefreshToken;
                        activeConfig.Twitch.BotName = _activeConfig.Twitch.BotName;
                    }
                }

                if (maximumBalanceNeedsClamp && _runtimeState == RuntimeState.Running) Tokens.ApplyMaximumBalance(activeConfig.Settings.MaximumTokenBalance);
                SetConfig(activeConfig);
            }

            if (!activeConfig.Settings.GlobalGameCommandCooldownEnabled)
                Commands.ClearGlobalCooldown();
            if (twitchAuthChanged) _twitchSession.CloseSocket();
            if (twitchAuthChanged || followRewardsChanged) await RestartFollowRewardsAsync().ConfigureAwait(false);

            if (passiveScheduleChanged)
            {
                lock (_viewerGate)
                {
                    _viewerRewardSchedule.Clear();
                    if (!activeConfig.Settings.PassiveRewardsRequireActivity)
                        _viewerLastChatActivity.Clear();
                }
            }

            if (refreshMinigameLoops || minigamesEnabledChanged)
                RefreshMinigames(activeConfig.Settings.MinigamesEnabled);

            if (difficultyChanged && !activeConfig.Settings.RemoteControlEnabled && TryGetSessionToken(requireMultiplayer: false, out CancellationToken token))
                await SendServerCommandAsync("difficulty " + (activeConfig.Settings.Difficulty == "Medium" ? "normal" : activeConfig.Settings.Difficulty.ToLowerInvariant()), token).ConfigureAwait(false);
            if (pvpChanged)
                await ApplyPvPGameRuleAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void RefreshMinigames(bool minigamesEnabled)
    {
        if (_runtimeState != RuntimeState.Running)
        {
            return;
        }

        if (!minigamesEnabled)
        {
            MinigameManager.StopLoops(this);
            return;
        }

        CancellationTokenSource? sessionCts = _sessionCts;
        if (sessionCts != null)
            MinigameManager.StartLoops(this, sessionCts.Token);
    }

    private async Task RestartFollowRewardsAsync()
    {
        CancellationTokenSource? oldCts = _twitchSession.FollowRewardsCts;
        Task? oldTask = _twitchSession.FollowRewardsTask;
        _twitchSession.FollowRewardsCts = null;
        _twitchSession.FollowRewardsTask = null;
        oldCts?.Cancel();
        if (oldTask != null) await oldTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        oldCts?.Dispose();
        if (!AutomaticFollowRewardsEnabled || _runtimeState != RuntimeState.Running || _sessionCts == null) return;
        _twitchSession.FollowRewardsCts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
        _twitchSession.FollowRewardsTask = RunFollowRewardsAsync(_twitchSession.FollowRewardsCts.Token);
        TrackTask(_twitchSession.FollowRewardsTask);
    }

    private enum RuntimeState
    {
        Stopped,
        Starting,
        Running,
        Stopping
    }
}
