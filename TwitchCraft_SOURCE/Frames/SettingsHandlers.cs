using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Settings
{
    private static bool TryGetIntOption(ComboBox dropdown, (int Value, string Label)[] options, out int value)
    {
        if (dropdown.SelectedItem is string selected)
            foreach ((int option, string label) in options)
                if (string.Equals(selected, label, StringComparison.Ordinal))
                {
                    value = option;
                    return true;
                }

        value = 0;
        return false;
    }

    private static bool TryReadEditableInt(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        int minimum,
        int maximum,
        out int value)
    {
        // SelectionChanged fires before WPF has reliably copied a newly selected item's label
        // into the editable Text property. Prefer SelectedItem so preset clicks always use the
        // value the user actually chose instead of the previous/blank editor text.
        if (dropdown.SelectedItem is string selected)
            foreach ((int option, string label) in options)
                if (string.Equals(selected, label, StringComparison.OrdinalIgnoreCase))
                {
                    value = option;
                    return value >= minimum && value <= maximum;
                }

        string text = (dropdown.Text ?? string.Empty).Trim();
        foreach ((int option, string label) in options)
            if (string.Equals(text, label, StringComparison.OrdinalIgnoreCase))
            {
                value = option;
                return value >= minimum && value <= maximum;
            }

        return int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    private static bool TryReadInt(ComboBox dropdown, int minimum, int maximum, out int value)
    {
        string text = (dropdown.SelectedItem as string ?? dropdown.Text ?? string.Empty).Trim();
        return int.TryParse(
            text,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value) && value >= minimum && value <= maximum;
    }

    private static bool TryReadDouble(
        ComboBox dropdown,
        (double Value, string Label)[] options,
        double minimum,
        double maximum,
        out double value)
    {
        if (dropdown.SelectedItem is string selected)
            foreach ((double option, string label) in options)
                if (string.Equals(selected, label, StringComparison.OrdinalIgnoreCase))
                {
                    value = option;
                    return value >= minimum && value <= maximum;
                }

        string text = (dropdown.Text ?? string.Empty).Trim();
        foreach ((double option, string label) in options)
            if (string.Equals(text, label, StringComparison.OrdinalIgnoreCase))
            {
                value = option;
                return value >= minimum && value <= maximum;
            }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= minimum && value <= maximum;
    }

    private async void Prefix_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        if (e is SelectionChangedEventArgs)
        {
            if (sender is not ComboBox { SelectedItem: string selected })
                return;

            ((ComboBox)sender).Text = selected;
        }

        string primary = ConfigurationStore.NormalizeCommandPrefix(CommandPrefixTextBox.Text, "!");
        string secondary = ConfigurationStore.NormalizeCommandPrefix(SecondaryCommandPrefixTextBox.Text, string.Empty);
        if (string.Equals(primary, secondary, StringComparison.Ordinal))
            secondary = string.Empty;

        CommandPrefixTextBox.Text = primary;
        SecondaryCommandPrefixTextBox.Text = secondary;
        if (string.Equals(primary, _savedCommandPrefix, StringComparison.Ordinal) &&
            string.Equals(secondary, _savedSecondaryCommandPrefix, StringComparison.Ordinal))
        {
            return;
        }

        int saveVersion = ++_prefixSaveVersion;
        bool prefixChanged = !string.Equals(primary, _savedCommandPrefix, StringComparison.Ordinal);
        _savedCommandPrefix = primary;
        _savedSecondaryCommandPrefix = secondary;
        if (prefixChanged)
            UpdateCommandPrefixes(primary);

        await SaveConfigAsync(config =>
        {
            config.Settings.CommandPrefix = primary;
            config.Settings.SecondaryCommandPrefix = secondary;
        });

        if (saveVersion != _prefixSaveVersion)
            return;

        var runtime = AppHelpers.GetTwitchCraftWindow(this)?.Runtime;
        if (runtime == null)
            return;

        CommandPrefixTextBox.Text = runtime.CommandPrefix;
        SecondaryCommandPrefixTextBox.Text = runtime.SecondaryCommandPrefix;
        if (!string.Equals(runtime.CommandPrefix, _savedCommandPrefix, StringComparison.Ordinal))
            UpdateCommandPrefixes(runtime.CommandPrefix);
        _savedCommandPrefix = runtime.CommandPrefix;
        _savedSecondaryCommandPrefix = runtime.SecondaryCommandPrefix;
    }

    private async void MentionViewers_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(MentionViewersCheckbox, static (settings, value) => settings.MentionViewersInBotReplies = value);

    private async void ExactCooldown_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ExactCooldownCheckbox, static (settings, value) => settings.ShowExactCooldownRemaining = value);

    private async void UnknownCommands_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(UnknownCommandResponseCheckbox, static (settings, value) => settings.RespondToUnknownCommands = value);

    private async void ViewerPause_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ViewerCommandsPausedCheckbox, static (settings, value) => settings.ViewerCommandsPaused = value);

    private async void RequireActivity_Changed(object sender, RoutedEventArgs e)
    {
        ActivityWindowDropdown.IsEnabled = ActivityPayoutCheckbox.IsChecked == true;
        await SaveBoolAsync(ActivityPayoutCheckbox, static (settings, value) => settings.PassiveRewardsRequireActivity = value);
    }

    private async void AllowAllTargets_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(AllowAllTargetsCheckbox, static (settings, value) => settings.AllowAllPlayerTarget = value);

    private async void AllowRandomTargets_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(AllowRandomTargetsCheckbox, static (settings, value) => settings.AllowRandomPlayerTarget = value);

    private async void RelayTimestamps_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(RelayTimestampsCheckbox, static (settings, value) => settings.IncludeRelayTimestamps = value);

    private async void ConnectionHealth_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ConnectionHealthCheckbox, static (settings, value) => settings.ShowConnectionHealth = value);

    private Task SaveBoolAsync(CheckBox checkbox, Action<StartingProfile, bool> update)
    {
        if (_initializing)
            return Task.CompletedTask;

        // WPF controls belong to the UI thread. SaveConfigAsync runs its config mutation
        // inside Task.Run, so capture the DependencyProperty value before crossing threads.
        bool value = checkbox.IsChecked == true;
        return SaveConfigAsync(config => update(config.Settings, value));
    }

    private async void PayoutAmount_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && PassivePayoutAmountDropdown.SelectedItem is string)
            await SavePayoutAmountAsync();
    }

    private async void PayoutAmount_LostFocus(object sender, RoutedEventArgs e)
        => await SavePayoutAmountAsync();

    private async void PayoutRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && sender is ComboBox { SelectedItem: string })
            await SavePayoutRangeAsync(sender);
    }

    private async void PayoutRange_LostFocus(object sender, RoutedEventArgs e)
        => await SavePayoutRangeAsync(sender);

    private async void TokenLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaximumTokenBalanceDropdown.SelectedItem is string)
            await SaveTokenLimitAsync();
    }

    private async void TokenLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveTokenLimitAsync();

    private async void ChannelLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ChannelCommandLimitDropdown.SelectedItem is string)
            await SaveChannelLimitAsync();
    }

    private async void ChannelLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveChannelLimitAsync();

    private async Task SavePayoutAmountAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryReadEditableInt(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, 1, 1_000_000, out int value))
        {
            RestoreIntValue(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, static settings => settings.PassiveTokensPerPayout);
            return;
        }
        SetIntValue(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, value);
        await SaveConfigAsync(config => config.Settings.PassiveTokensPerPayout = value);
    }

    private async Task SavePayoutRangeAsync(object changedControl)
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryReadEditableInt(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, 10, 900, out int minimum) ||
            !TryReadEditableInt(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, 10, 900, out int maximum))
        {
            RestorePayoutRange();
            return;
        }

        if (minimum > maximum)
        {
            if (ReferenceEquals(changedControl, PassivePayoutMinimumDropdown))
                maximum = minimum;
            else
                minimum = maximum;
        }

        SetIntValue(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, minimum);
        SetIntValue(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, maximum);
        await SaveConfigAsync(config =>
        {
            config.Settings.PassiveTokenPayoutMinimumSeconds = minimum;
            config.Settings.PassiveTokenPayoutMaximumSeconds = maximum;
        });
    }

    private async Task SaveTokenLimitAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryReadEditableInt(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, 0, int.MaxValue, out int value))
        {
            RestoreIntValue(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, static settings => settings.MaximumTokenBalance);
            return;
        }
        SetIntValue(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, value);
        await SaveConfigAsync(config => config.Settings.MaximumTokenBalance = value);
    }

    private async Task SaveChannelLimitAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryReadEditableInt(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, 0, 1000, out int value))
        {
            RestoreIntValue(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, static settings => settings.ChannelCommandLimitPerMinute);
            return;
        }
        SetIntValue(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, value);
        await SaveConfigAsync(config => config.Settings.ChannelCommandLimitPerMinute = value);
    }

    private void SetTextValue(ComboBox dropdown, string text)
    {
        void ApplyValue()
        {
            _updatingCustomValueControls = true;
            try
            {
                dropdown.SelectedIndex = -1;
                dropdown.Text = text;
            }
            finally
            {
                _updatingCustomValueControls = false;
            }
        }

        // Clearing SelectedIndex from inside SelectionChanged lets WPF's remaining selection
        // processing overwrite the editor afterward, which can leave it blank. Defer only that
        // case until the current input/selection event has completely finished.
        if (dropdown.SelectedIndex >= 0)
        {
            object selection = dropdown.SelectedItem;
            string originalText = dropdown.Text;
            dropdown.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (Equals(dropdown.SelectedItem, selection) && string.Equals(dropdown.Text, originalText, StringComparison.Ordinal)) ApplyValue();
                }));
            return;
        }

        ApplyValue();
    }

    private void SetIntValue(ComboBox dropdown, (int Value, string Label)[] options, int value)
    {
        foreach ((int option, string label) in options)
        {
            if (option == value)
            {
                SetTextValue(dropdown, label);
                return;
            }
        }

        SetTextValue(dropdown, value.ToString(CultureInfo.InvariantCulture));
    }

    private void SetDoubleValue(ComboBox dropdown, (double Value, string Label)[] options, double value)
    {
        foreach ((double option, string label) in options)
        {
            if (Math.Abs(option - value) < 0.0000001)
            {
                SetTextValue(dropdown, label);
                return;
            }
        }

        SetTextValue(dropdown, value.ToString(CultureInfo.InvariantCulture));
    }

    private void RestoreIntValue(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        Func<StartingProfile, int> getValue)
    {
        try { SetIntValue(dropdown, options, getValue(ConfigurationStore.Load().Settings)); }
        catch { ReloadSettings(); }
    }

    private void RestorePayoutRange()
    {
        try
        {
            StartingProfile settings = ConfigurationStore.Load().Settings;
            SetIntValue(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
            SetIntValue(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
        }
        catch { ReloadSettings(); }
    }

    private async void RelayColor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || RelayTextColorDropdown.SelectedItem is not string selected)
            return;

        foreach ((string value, string label) in RelayTextColorOptions)
            if (string.Equals(selected, label, StringComparison.Ordinal))
            {
                await SaveConfigAsync(config => config.Settings.MinecraftRelayTextColor = value);
                return;
            }
    }

    private async void SaveCommandRow(CommandSettingsRow row)
    {
        if (!_initializing)
            await SaveCommandSettingAsync(row.Name, row.Enabled, row.PerUserCooldown.Value, row.GlobalCooldown.Value);
    }

    private Task SaveCommandSettingAsync(string commandName, bool enabled, int? cooldown, double? globalCooldown)
        => SaveConfigAsync(config =>
        {
            if (enabled && !cooldown.HasValue && !globalCooldown.HasValue)
            {
                config.Settings.CommandCustomizations.Remove(commandName);
                return;
            }

            config.Settings.CommandCustomizations[commandName] = new CommandCustomization
            {
                Enabled = enabled,
                CooldownSeconds = cooldown,
                GlobalCooldownSeconds = globalCooldown
            };
        });

    private async void ViewerLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ViewerCommandLimitDropdown.SelectedItem is string)
            await SaveViewerLimitAsync();
    }

    private async void ViewerLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveViewerLimitAsync();

    private async Task SaveViewerLimitAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryReadEditableInt(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, 0, 1000, out int value))
        {
            RestoreIntValue(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, static settings => settings.ViewerCommandLimitPerMinute);
            return;
        }
        SetIntValue(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, value);
        await SaveConfigAsync(config => config.Settings.ViewerCommandLimitPerMinute = value);
    }

    private async void ActivityWindow_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(ActivityWindowDropdown, ActivityWindowOptions, static (settings, value) => settings.PassiveActivityWindowMinutes = value);

    private async void BackupInterval_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(BackupIntervalDropdown, BackupIntervalOptions, static (settings, value) => settings.AutomaticBackupIntervalHours = value);

    private async void BackupRetention_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(BackupRetentionDropdown, BackupRetentionOptions, static (settings, value) => settings.AutomaticBackupRetentionCount = value);

    private async void TwitchLogLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaxTwitchLogLinesDropdown.SelectedItem is string)
            await SaveExtraValueAsync(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
                static settings => settings.MaxVisibleTwitchLogLines,
                static (settings, value) => settings.MaxVisibleTwitchLogLines = value,
                refreshLowResourceSummary: true);
    }

    private async void TwitchLogLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
            static settings => settings.MaxVisibleTwitchLogLines,
            static (settings, value) => settings.MaxVisibleTwitchLogLines = value,
            refreshLowResourceSummary: true);

    private async void MinecraftLogLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaxMinecraftLogLinesDropdown.SelectedItem is string)
            await SaveExtraValueAsync(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
                static settings => settings.MaxVisibleMinecraftLogLines,
                static (settings, value) => settings.MaxVisibleMinecraftLogLines = value,
                refreshLowResourceSummary: true);
    }

    private async void MinecraftLogLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
            static settings => settings.MaxVisibleMinecraftLogLines,
            static (settings, value) => settings.MaxVisibleMinecraftLogLines = value,
            refreshLowResourceSummary: true);

    private async void RosterInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        await SaveOptionAsync(ViewerRosterIntervalDropdown, ViewerRosterIntervalOptions, static (settings, value) => settings.ViewerRosterRefreshIntervalSeconds = value);
        if (!_initializing)
            UpdateLowResource();
    }

    private async void RelayRate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RelayRateDropdown.SelectedItem is string)
            await SaveExtraValueAsync(RelayRateDropdown, RelayRateOptions, 0, 100,
                static settings => settings.MinecraftRelayMessagesPerSecond,
                static (settings, value) => settings.MinecraftRelayMessagesPerSecond = value,
                refreshLowResourceSummary: true);
    }

    private async void RelayRate_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(RelayRateDropdown, RelayRateOptions, 0, 100,
            static settings => settings.MinecraftRelayMessagesPerSecond,
            static (settings, value) => settings.MinecraftRelayMessagesPerSecond = value,
            refreshLowResourceSummary: true);

    private async void QueueLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && GameplayQueueDropdown.SelectedItem is string)
            await SaveExtraValueAsync(GameplayQueueDropdown, GameplayQueueOptions, 10, 1000,
                static settings => settings.MaxGameplayCommandQueue,
                static (settings, value) => settings.MaxGameplayCommandQueue = value,
                refreshLowResourceSummary: true);
    }

    private async void QueueLimit_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(GameplayQueueDropdown, GameplayQueueOptions, 10, 1000,
            static settings => settings.MaxGameplayCommandQueue,
            static (settings, value) => settings.MaxGameplayCommandQueue = value,
            refreshLowResourceSummary: true);

    private async void RCONTimeout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RCONTimeoutDropdown.SelectedItem is string)
            await SaveExtraValueAsync(RCONTimeoutDropdown, RCONTimeoutOptions, 1, 60,
                static settings => settings.RCONTimeoutSeconds,
                static (settings, value) => settings.RCONTimeoutSeconds = value);
    }

    private async void RCONTimeout_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(RCONTimeoutDropdown, RCONTimeoutOptions, 1, 60,
            static settings => settings.RCONTimeoutSeconds,
            static (settings, value) => settings.RCONTimeoutSeconds = value);

    private async void ShutdownTimeout_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(GracefulShutdownTimeoutDropdown, GracefulShutdownTimeoutOptions, static (settings, value) => settings.GracefulShutdownTimeoutSeconds = value);

    private async void SqliteOptimize_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(SqliteOptimizeDropdown, SqliteOptimizeOptions, static (settings, value) => settings.SQLiteOptimizeIntervalHours = value);
    private async void EmptyShutdown_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(EmptyShutdownDropdown, EmptyShutdownOptions, static (settings, value) => settings.EmptyServerShutdownDelayMinutes = value);

    private async void Whitelist_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        bool enabled = WhitelistCheckbox.IsChecked == true;
        await SaveConfigAsync(config => config.Settings.WhitelistEnabled = enabled, beforeSave: ApplyLocalProfile);
    }

    private async void ViewDistance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ViewDistanceDropdown.SelectedItem is string)
            await SaveServerValueAsync(ViewDistanceDropdown, DistanceOptions, 2, 32,
                static settings => settings.ViewDistance,
                static (settings, value) => settings.ViewDistance = value);
    }

    private async void ViewDistance_LostFocus(object sender, RoutedEventArgs e)
        => await SaveServerValueAsync(ViewDistanceDropdown, DistanceOptions, 2, 32,
            static settings => settings.ViewDistance,
            static (settings, value) => settings.ViewDistance = value);

    private async void Simulation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && SimulationDistanceDropdown.SelectedItem is string)
            await SaveServerValueAsync(SimulationDistanceDropdown, DistanceOptions, 2, 32,
                static settings => settings.SimulationDistance,
                static (settings, value) => settings.SimulationDistance = value);
    }

    private async void Simulation_LostFocus(object sender, RoutedEventArgs e)
        => await SaveServerValueAsync(SimulationDistanceDropdown, DistanceOptions, 2, 32,
            static settings => settings.SimulationDistance,
            static (settings, value) => settings.SimulationDistance = value);

    private async void EntityRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && EntityBroadcastRangeDropdown.SelectedItem is string)
            await SaveServerValueAsync(EntityBroadcastRangeDropdown, EntityBroadcastOptions, 10, 1000,
                static settings => settings.EntityBroadcastRangePercentage,
                static (settings, value) => settings.EntityBroadcastRangePercentage = value);
    }

    private async void EntityRange_LostFocus(object sender, RoutedEventArgs e)
        => await SaveServerValueAsync(EntityBroadcastRangeDropdown, EntityBroadcastOptions, 10, 1000,
            static settings => settings.EntityBroadcastRangePercentage,
            static (settings, value) => settings.EntityBroadcastRangePercentage = value);

    private async void Compression_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && NetworkCompressionDropdown.SelectedItem is string)
            await SaveServerValueAsync(NetworkCompressionDropdown, NetworkCompressionOptions, -1, 4096,
                static settings => settings.NetworkCompressionThreshold,
                static (settings, value) => settings.NetworkCompressionThreshold = value);
    }

    private async void Compression_LostFocus(object sender, RoutedEventArgs e)
        => await SaveServerValueAsync(NetworkCompressionDropdown, NetworkCompressionOptions, -1, 4096,
            static settings => settings.NetworkCompressionThreshold,
            static (settings, value) => settings.NetworkCompressionThreshold = value);

    private async void LowResource_Changed(object sender, RoutedEventArgs e)
    {
        await SaveBoolAsync(LowResourceModeCheckbox, static (settings, value) => settings.LowResourceModeEnabled = value);
        if (!_initializing)
            UpdateLowResource();
    }

    private async void PauseUI_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(PauseUIUpdatesCheckbox, static (settings, value) => settings.PauseUIUpdatesWhenMinimized = value);

    private async void Backups_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = AutomaticBackupsCheckbox.IsChecked == true;
        BackupIntervalDropdown.IsEnabled = enabled;
        BackupRetentionDropdown.IsEnabled = enabled;
        await SaveBoolAsync(AutomaticBackupsCheckbox, static (settings, value) => settings.AutomaticBackupsEnabled = value);
    }

    private Task SaveOptionAsync(ComboBox dropdown, (int Value, string Label)[] options, Action<StartingProfile, int> update)
        => _initializing || !TryGetIntOption(dropdown, options, out int value)
            ? Task.CompletedTask
            : SaveConfigAsync(config => update(config.Settings, value));

    private async Task SaveExtraValueAsync(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        int minimum,
        int maximum,
        Func<StartingProfile, int> getValue,
        Action<StartingProfile, int> update,
        bool refreshLowResourceSummary = false)
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryReadEditableInt(dropdown, options, minimum, maximum, out int value))
        {
            RestoreIntValue(dropdown, options, getValue);
            if (refreshLowResourceSummary)
                UpdateLowResource();
            return;
        }

        SetIntValue(dropdown, options, value);
        await SaveConfigAsync(config => update(config.Settings, value));
        if (refreshLowResourceSummary)
            UpdateLowResource();
    }

    private async Task SaveServerValueAsync(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        int minimum,
        int maximum,
        Func<StartingProfile, int> getValue,
        Action<StartingProfile, int> update)
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryReadEditableInt(dropdown, options, minimum, maximum, out int value))
        {
            RestoreIntValue(dropdown, options, getValue);
            return;
        }

        SetIntValue(dropdown, options, value);
        await SaveConfigAsync(config => update(config.Settings, value), beforeSave: ApplyLocalProfile);
    }

    private void UpdateLowResource(StartingProfile? settings = null)
    {
        bool enabled = settings?.LowResourceModeEnabled ?? LowResourceModeCheckbox.IsChecked == true;
        LowResourceEffectiveValuesBorder.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            LowResourceEffectiveValuesTextBlock.Text = string.Empty;
            return;
        }

        try
        {
            settings ??= ConfigurationStore.Load().Settings;
            int twitchLogs = Math.Min(100, settings.MaxVisibleTwitchLogLines);
            int minecraftLogs = Math.Min(100, settings.MaxVisibleMinecraftLogLines);
            int rosterSeconds = Math.Max(60, settings.ViewerRosterRefreshIntervalSeconds);
            int gameplayQueue = Math.Min(35, settings.MaxGameplayCommandQueue);
            int configuredRelay = settings.MinecraftRelayMessagesPerSecond;
            int relayRate = configuredRelay <= 0 ? 5 : Math.Min(configuredRelay, 5);
            string configuredRelayText = configuredRelay <= 0 ? "Unlimited" : configuredRelay + "/s";

            LowResourceEffectiveValuesTextBlock.Text =
                "Effective while this preset is enabled (your configured values are preserved):\n" +
                "• Connection-health refresh: about every 3 seconds\n" +
                "• UI updates while minimized: paused\n" +
                $"• Twitch log lines: {twitchLogs:N0} (configured {settings.MaxVisibleTwitchLogLines:N0})\n" +
                $"• Minecraft log lines: {minecraftLogs:N0} (configured {settings.MaxVisibleMinecraftLogLines:N0})\n" +
                $"• Viewer roster refresh: {rosterSeconds}s (configured {settings.ViewerRosterRefreshIntervalSeconds}s)\n" +
                $"• Gameplay queue: {gameplayQueue:N0} (configured {settings.MaxGameplayCommandQueue:N0})\n" +
                $"• Minecraft relay: {relayRate}/s (configured {configuredRelayText})";
        }
        catch
        {
            LowResourceEffectiveValuesTextBlock.Text =
                "Low-resource preset is enabled. Effective values could not be refreshed from the saved configuration.";
        }
    }
}
