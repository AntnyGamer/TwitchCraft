using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

public partial class Settings
{
    private static readonly (int Value, string Label)[] ViewerCommandLimitOptions =
        [(0, "Unlimited"), (3, "3"), (5, "5"), (10, "10"), (15, "15"), (20, "20"), (30, "30"), (60, "60")];
    private static readonly (int Value, string Label)[] RecentChatWindowOptions =
        [(1, "1 minute"), (2, "2 minutes"), (5, "5 minutes"), (10, "10 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 hour"), (120, "2 hours")];
    private static readonly (int Value, string Label)[] BackupIntervalOptions =
        [(1, "1 hour"), (6, "6 hours"), (12, "12 hours"), (24, "24 hours"), (48, "2 days"), (168, "1 week")];
    private static readonly (int Value, string Label)[] BackupRetentionOptions =
        [(1, "1 backup"), (3, "3 backups"), (5, "5 backups"), (10, "10 backups"), (20, "20 backups")];
    private static readonly (int Value, string Label)[] VisibleLogLineOptions =
        [(50, "50"), (100, "100"), (250, "250"), (500, "500"), (1000, "1,000"), (2500, "2,500"), (5000, "5,000")];
    private static readonly (int Value, string Label)[] ViewerRosterIntervalOptions =
        [(15, "15 seconds"), (30, "30 seconds"), (60, "1 minute"), (120, "2 minutes"), (300, "5 minutes")];
    private static readonly (int Value, string Label)[] RelayRateOptions =
        [(0, "Unlimited"), (1, "1"), (2, "2"), (5, "5"), (10, "10"), (20, "20"), (50, "50")];
    private static readonly (int Value, string Label)[] GameplayQueueOptions =
        [(10, "10"), (25, "25"), (35, "35"), (50, "50"), (75, "75"), (100, "100"), (200, "200"), (500, "500")];
    private static readonly (int Value, string Label)[] RconTimeoutOptions =
        [(1, "1 second"), (2, "2 seconds"), (5, "5 seconds"), (10, "10 seconds"), (15, "15 seconds"), (30, "30 seconds"), (60, "60 seconds")];
    private static readonly (int Value, string Label)[] GracefulShutdownTimeoutOptions =
        [(3, "3 seconds"), (5, "5 seconds"), (10, "10 seconds"), (15, "15 seconds"), (30, "30 seconds"), (60, "60 seconds")];
    private static readonly (int Value, string Label)[] SqliteOptimizeOptions =
        [(0, "Off"), (1, "Hourly"), (6, "Every 6 hours"), (12, "Every 12 hours"), (24, "Daily"), (168, "Weekly")];
    private static readonly (int Value, string Label)[] DistanceOptions =
        [(2, "2"), (4, "4"), (6, "6"), (8, "8"), (10, "10"), (12, "12"), (16, "16"), (20, "20"), (24, "24"), (32, "32")];
    private static readonly (int Value, string Label)[] EntityBroadcastOptions =
        [(25, "25%"), (50, "50%"), (75, "75%"), (100, "100%"), (150, "150%"), (200, "200%")];
    private static readonly (int Value, string Label)[] NetworkCompressionOptions =
        [(-1, "Disabled"), (0, "Always"), (64, "64 bytes"), (128, "128 bytes"), (256, "256 bytes"), (512, "512 bytes"), (1024, "1,024 bytes")];
    private static readonly (int Value, string Label)[] EmptyShutdownOptions =
        [(0, "Off"), (5, "5 minutes"), (10, "10 minutes"), (15, "15 minutes"), (30, "30 minutes"), (60, "1 hour"), (120, "2 hours")];
    private static readonly (int? Value, string Label)[] CommandCooldownOptions =
        [(null, "Default"), (0, "None"), (1, "1 second"), (3, "3 seconds"), (5, "5 seconds"), (10, "10 seconds"), (15, "15 seconds"), (30, "30 seconds"), (60, "1 minute"), (300, "5 minutes"), (600, "10 minutes")];

    private void CheckAdditionalSettingsItems()
    {
        AddOptions(ViewerCommandLimitDropdown, ViewerCommandLimitOptions);
        AddOptions(RecentChatWindowDropdown, RecentChatWindowOptions);
        AddOptions(BackupIntervalDropdown, BackupIntervalOptions);
        AddOptions(BackupRetentionDropdown, BackupRetentionOptions);
        AddOptions(MaxTwitchLogLinesDropdown, VisibleLogLineOptions);
        AddOptions(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions);
        AddOptions(ViewerRosterIntervalDropdown, ViewerRosterIntervalOptions);
        AddOptions(RelayRateDropdown, RelayRateOptions);
        AddOptions(GameplayQueueDropdown, GameplayQueueOptions);
        AddOptions(RconTimeoutDropdown, RconTimeoutOptions);
        AddOptions(GracefulShutdownTimeoutDropdown, GracefulShutdownTimeoutOptions);
        AddOptions(SqliteOptimizeDropdown, SqliteOptimizeOptions);
        AddOptions(ViewDistanceDropdown, DistanceOptions);
        AddOptions(SimulationDistanceDropdown, DistanceOptions);
        AddOptions(EntityBroadcastRangeDropdown, EntityBroadcastOptions);
        AddOptions(NetworkCompressionDropdown, NetworkCompressionOptions);
        AddOptions(EmptyShutdownDropdown, EmptyShutdownOptions);
    }

    private void LoadAdditionalSettingsIntoControls(StartingProfile settings)
    {
        SelectEditableOption(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, settings.ViewerCommandLimitPerMinute);
        SelectEditableOption(RecentChatWindowDropdown, RecentChatWindowOptions, settings.PassiveRecentChatWindowMinutes);
        RecentChatWindowDropdown.IsEnabled = settings.PassiveRewardsRequireRecentChat;
        AutomaticBackupsCheckbox.IsChecked = settings.AutomaticBackupsEnabled;
        SelectOption(BackupIntervalDropdown, BackupIntervalOptions, settings.AutomaticBackupIntervalHours, 24);
        SelectOption(BackupRetentionDropdown, BackupRetentionOptions, settings.AutomaticBackupRetentionCount, StartingProfile.DefaultAutomaticBackupRetentionCount);
        BackupIntervalDropdown.IsEnabled = settings.AutomaticBackupsEnabled;
        BackupRetentionDropdown.IsEnabled = settings.AutomaticBackupsEnabled;
        LowResourceModeCheckbox.IsChecked = settings.LowResourceModeEnabled;
        PauseUIUpdatesCheckbox.IsChecked = settings.PauseUIUpdatesWhenMinimized;
        SelectEditableOption(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, settings.MaxVisibleTwitchLogLines);
        SelectEditableOption(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, settings.MaxVisibleMinecraftLogLines);
        SelectOption(ViewerRosterIntervalDropdown, ViewerRosterIntervalOptions, settings.ViewerRosterRefreshIntervalSeconds, 30);
        SelectEditableOption(RelayRateDropdown, RelayRateOptions, settings.MinecraftRelayMessagesPerSecond);
        SelectEditableOption(GameplayQueueDropdown, GameplayQueueOptions, settings.MaxGameplayCommandQueue);
        SelectEditableOption(RconTimeoutDropdown, RconTimeoutOptions, settings.RCONTimeoutSeconds);
        SelectOption(GracefulShutdownTimeoutDropdown, GracefulShutdownTimeoutOptions, settings.GracefulShutdownTimeoutSeconds, 5);
        SelectOption(SqliteOptimizeDropdown, SqliteOptimizeOptions, settings.SQLiteOptimizeIntervalHours, 0);
        SelectEditableOption(ViewDistanceDropdown, DistanceOptions, settings.ViewDistance);
        SelectEditableOption(SimulationDistanceDropdown, DistanceOptions, settings.SimulationDistance);
        SelectEditableOption(EntityBroadcastRangeDropdown, EntityBroadcastOptions, settings.EntityBroadcastRangePercentage);
        SelectEditableOption(NetworkCompressionDropdown, NetworkCompressionOptions, settings.NetworkCompressionThreshold);
        SelectOption(EmptyShutdownDropdown, EmptyShutdownOptions, settings.EmptyServerShutdownDelayMinutes, 0);
        UpdateLowResourceEffectiveValuesDisplay(settings);
        BuildCommandCustomizationControls(settings);
    }

    private void BuildCommandCustomizationControls(StartingProfile settings)
    {
        CommandCustomizationPanel.Children.Clear();
        BotMainHandler? runtime = AppHelpers.GetParentBot(this)?.Runtime;
        if (runtime == null)
            return;

        foreach (string commandName in runtime.RegisteredCommandNames)
        {
            settings.CommandCustomizations.TryGetValue(commandName, out CommandCustomization? customization);
            DockPanel row = new() { MinHeight = 34, LastChildFill = false };
            TextBlock name = new()
            {
                Text = "!" + commandName,
                Width = 230,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 14,
                Foreground = Brushes.White
            };
            CheckBox enabled = new()
            {
                Content = "Enabled",
                Width = 85,
                Tag = commandName,
                IsChecked = customization?.Enabled ?? true,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            ComboBox cooldown = new()
            {
                Width = 128,
                Tag = commandName,
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach ((int? _, string label) in CommandCooldownOptions)
                cooldown.Items.Add(label);
            SelectCommandCooldown(cooldown, customization?.CooldownSeconds);
            enabled.Checked += CommandEnabled_Changed;
            enabled.Unchecked += CommandEnabled_Changed;
            cooldown.SelectionChanged += CommandCooldown_SelectionChanged;
            row.Children.Add(name);
            row.Children.Add(enabled);
            row.Children.Add(cooldown);
            CommandCustomizationPanel.Children.Add(row);
        }
    }

    private static void SelectCommandCooldown(ComboBox dropdown, int? seconds)
    {
        foreach ((int? value, string label) in CommandCooldownOptions)
            if (value == seconds)
            {
                dropdown.SelectedItem = label;
                return;
            }

        if (seconds.HasValue && seconds.Value is >= 0 and <= 86400)
        {
            string customLabel = seconds.Value.ToString(CultureInfo.InvariantCulture) + " seconds";
            dropdown.Items.Add(customLabel);
            dropdown.SelectedItem = customLabel;
            return;
        }

        dropdown.SelectedItem = "Default";
    }

    private static bool TryGetCommandCooldown(ComboBox dropdown, out int? seconds)
    {
        if (dropdown.SelectedItem is string selected)
        {
            foreach ((int? value, string label) in CommandCooldownOptions)
                if (string.Equals(selected, label, StringComparison.Ordinal))
                {
                    seconds = value;
                    return true;
                }

            const string suffix = " seconds";
            if (selected.EndsWith(suffix, StringComparison.Ordinal) &&
                int.TryParse(selected[..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out int customSeconds) &&
                customSeconds is >= 0 and <= 86400)
            {
                seconds = customSeconds;
                return true;
            }
        }

        seconds = null;
        return false;
    }

    private async void CommandEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not CheckBox checkbox || checkbox.Tag is not string commandName)
            return;
        await SaveCommandCustomizationAsync(commandName, checkbox.IsChecked == true, FindCommandCooldown(commandName));
    }

    private async void CommandCooldown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || sender is not ComboBox dropdown || dropdown.Tag is not string commandName || !TryGetCommandCooldown(dropdown, out int? cooldown))
            return;
        await SaveCommandCustomizationAsync(commandName, FindCommandEnabled(commandName), cooldown);
    }

    private bool FindCommandEnabled(string commandName)
    {
        foreach (DockPanel row in CommandCustomizationPanel.Children)
            foreach (object child in row.Children)
                if (child is CheckBox checkbox && string.Equals(checkbox.Tag as string, commandName, StringComparison.OrdinalIgnoreCase))
                    return checkbox.IsChecked == true;
        return true;
    }

    private int? FindCommandCooldown(string commandName)
    {
        foreach (DockPanel row in CommandCustomizationPanel.Children)
            foreach (object child in row.Children)
                if (child is ComboBox dropdown && string.Equals(dropdown.Tag as string, commandName, StringComparison.OrdinalIgnoreCase) && TryGetCommandCooldown(dropdown, out int? cooldown))
                    return cooldown;
        return null;
    }

    private Task SaveCommandCustomizationAsync(string commandName, bool enabled, int? cooldown)
        => UpdateConfigAsync(config =>
        {
            if (enabled && !cooldown.HasValue)
                config.Settings.CommandCustomizations.Remove(commandName);
            else
                config.Settings.CommandCustomizations[commandName] = new CommandCustomization { Enabled = enabled, CooldownSeconds = cooldown };
        });

    private async void ViewerCommandLimitDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ViewerCommandLimitDropdown.SelectedItem is string)
            await SaveViewerCommandLimitAsync();
    }

    private async void ViewerCommandLimitDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveViewerCommandLimitAsync();

    private async Task SaveViewerCommandLimitAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryGetEditableOption(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, 0, 1000, out int value))
        {
            RestoreEditableValue(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, static settings => settings.ViewerCommandLimitPerMinute);
            return;
        }
        SetEditableValue(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, value);
        await UpdateConfigAsync(config => config.Settings.ViewerCommandLimitPerMinute = value);
    }
    private async void RecentChatWindowDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RecentChatWindowDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(RecentChatWindowDropdown, RecentChatWindowOptions, 1, 120,
                static settings => settings.PassiveRecentChatWindowMinutes,
                static (settings, value) => settings.PassiveRecentChatWindowMinutes = value);
    }

    private async void RecentChatWindowDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(RecentChatWindowDropdown, RecentChatWindowOptions, 1, 120,
            static settings => settings.PassiveRecentChatWindowMinutes,
            static (settings, value) => settings.PassiveRecentChatWindowMinutes = value);

    private async void BackupIntervalDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(BackupIntervalDropdown, BackupIntervalOptions, static (settings, value) => settings.AutomaticBackupIntervalHours = value);

    private async void BackupRetentionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(BackupRetentionDropdown, BackupRetentionOptions, static (settings, value) => settings.AutomaticBackupRetentionCount = value);

    private async void MaxTwitchLogLinesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaxTwitchLogLinesDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
                static settings => settings.MaxVisibleTwitchLogLines,
                static (settings, value) => settings.MaxVisibleTwitchLogLines = value,
                refreshLowResourceSummary: true);
    }

    private async void MaxTwitchLogLinesDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
            static settings => settings.MaxVisibleTwitchLogLines,
            static (settings, value) => settings.MaxVisibleTwitchLogLines = value,
            refreshLowResourceSummary: true);

    private async void MaxMinecraftLogLinesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaxMinecraftLogLinesDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
                static settings => settings.MaxVisibleMinecraftLogLines,
                static (settings, value) => settings.MaxVisibleMinecraftLogLines = value,
                refreshLowResourceSummary: true);
    }

    private async void MaxMinecraftLogLinesDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, 50, 5000,
            static settings => settings.MaxVisibleMinecraftLogLines,
            static (settings, value) => settings.MaxVisibleMinecraftLogLines = value,
            refreshLowResourceSummary: true);

    private async void ViewerRosterIntervalDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await SaveOptionAsync(ViewerRosterIntervalDropdown, ViewerRosterIntervalOptions, static (settings, value) => settings.ViewerRosterRefreshIntervalSeconds = value);
        if (!_initializing)
            UpdateLowResourceEffectiveValuesDisplay();
    }

    private async void RelayRateDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RelayRateDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(RelayRateDropdown, RelayRateOptions, 0, 100,
                static settings => settings.MinecraftRelayMessagesPerSecond,
                static (settings, value) => settings.MinecraftRelayMessagesPerSecond = value,
                refreshLowResourceSummary: true);
    }

    private async void RelayRateDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(RelayRateDropdown, RelayRateOptions, 0, 100,
            static settings => settings.MinecraftRelayMessagesPerSecond,
            static (settings, value) => settings.MinecraftRelayMessagesPerSecond = value,
            refreshLowResourceSummary: true);

    private async void GameplayQueueDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && GameplayQueueDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(GameplayQueueDropdown, GameplayQueueOptions, 10, 1000,
                static settings => settings.MaxGameplayCommandQueue,
                static (settings, value) => settings.MaxGameplayCommandQueue = value,
                refreshLowResourceSummary: true);
    }

    private async void GameplayQueueDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(GameplayQueueDropdown, GameplayQueueOptions, 10, 1000,
            static settings => settings.MaxGameplayCommandQueue,
            static (settings, value) => settings.MaxGameplayCommandQueue = value,
            refreshLowResourceSummary: true);

    private async void RconTimeoutDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RconTimeoutDropdown.SelectedItem is string)
            await SaveEditableAdditionalOptionAsync(RconTimeoutDropdown, RconTimeoutOptions, 1, 60,
                static settings => settings.RCONTimeoutSeconds,
                static (settings, value) => settings.RCONTimeoutSeconds = value);
    }

    private async void RconTimeoutDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableAdditionalOptionAsync(RconTimeoutDropdown, RconTimeoutOptions, 1, 60,
            static settings => settings.RCONTimeoutSeconds,
            static (settings, value) => settings.RCONTimeoutSeconds = value);

    private async void GracefulShutdownTimeoutDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(GracefulShutdownTimeoutDropdown, GracefulShutdownTimeoutOptions, static (settings, value) => settings.GracefulShutdownTimeoutSeconds = value);

    private async void SqliteOptimizeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(SqliteOptimizeDropdown, SqliteOptimizeOptions, static (settings, value) => settings.SQLiteOptimizeIntervalHours = value);
    private async void EmptyShutdownDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveOptionAsync(EmptyShutdownDropdown, EmptyShutdownOptions, static (settings, value) => settings.EmptyServerShutdownDelayMinutes = value);

    private async void ViewDistanceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ViewDistanceDropdown.SelectedItem is string)
            await SaveEditableServerOptionAsync(ViewDistanceDropdown, DistanceOptions, 2, 32,
                static settings => settings.ViewDistance,
                static (settings, value) => settings.ViewDistance = value);
    }

    private async void ViewDistanceDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableServerOptionAsync(ViewDistanceDropdown, DistanceOptions, 2, 32,
            static settings => settings.ViewDistance,
            static (settings, value) => settings.ViewDistance = value);

    private async void SimulationDistanceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && SimulationDistanceDropdown.SelectedItem is string)
            await SaveEditableServerOptionAsync(SimulationDistanceDropdown, DistanceOptions, 2, 32,
                static settings => settings.SimulationDistance,
                static (settings, value) => settings.SimulationDistance = value);
    }

    private async void SimulationDistanceDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableServerOptionAsync(SimulationDistanceDropdown, DistanceOptions, 2, 32,
            static settings => settings.SimulationDistance,
            static (settings, value) => settings.SimulationDistance = value);

    private async void EntityBroadcastRangeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && EntityBroadcastRangeDropdown.SelectedItem is string)
            await SaveEditableServerOptionAsync(EntityBroadcastRangeDropdown, EntityBroadcastOptions, 10, 1000,
                static settings => settings.EntityBroadcastRangePercentage,
                static (settings, value) => settings.EntityBroadcastRangePercentage = value);
    }

    private async void EntityBroadcastRangeDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableServerOptionAsync(EntityBroadcastRangeDropdown, EntityBroadcastOptions, 10, 1000,
            static settings => settings.EntityBroadcastRangePercentage,
            static (settings, value) => settings.EntityBroadcastRangePercentage = value);

    private async void NetworkCompressionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && NetworkCompressionDropdown.SelectedItem is string)
            await SaveEditableServerOptionAsync(NetworkCompressionDropdown, NetworkCompressionOptions, -1, 4096,
                static settings => settings.NetworkCompressionThreshold,
                static (settings, value) => settings.NetworkCompressionThreshold = value);
    }

    private async void NetworkCompressionDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveEditableServerOptionAsync(NetworkCompressionDropdown, NetworkCompressionOptions, -1, 4096,
            static settings => settings.NetworkCompressionThreshold,
            static (settings, value) => settings.NetworkCompressionThreshold = value);

    private async void LowResourceModeCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        await SaveAdditionalBoolAsync(LowResourceModeCheckbox, static (settings, value) => settings.LowResourceModeEnabled = value);
        if (!_initializing)
            UpdateLowResourceEffectiveValuesDisplay();
    }

    private async void PauseUIUpdatesCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveAdditionalBoolAsync(PauseUIUpdatesCheckbox, static (settings, value) => settings.PauseUIUpdatesWhenMinimized = value);
    private async void AutomaticBackupsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = AutomaticBackupsCheckbox.IsChecked == true;
        BackupIntervalDropdown.IsEnabled = enabled;
        BackupRetentionDropdown.IsEnabled = enabled;
        await SaveAdditionalBoolAsync(AutomaticBackupsCheckbox, static (settings, value) => settings.AutomaticBackupsEnabled = value);
    }

    private Task SaveAdditionalBoolAsync(CheckBox checkbox, Action<StartingProfile, bool> update)
    {
        if (_initializing)
            return Task.CompletedTask;

        // WPF controls belong to the UI thread. UpdateConfigAsync runs its config mutation
        // inside Task.Run, so capture the DependencyProperty value before crossing threads.
        bool value = checkbox.IsChecked == true;
        return UpdateConfigAsync(config => update(config.Settings, value));
    }

    private Task SaveOptionAsync(ComboBox dropdown, (int Value, string Label)[] options, Action<StartingProfile, int> update)
        => _initializing || !TryGetSelectedOption(dropdown, options, out int value)
            ? Task.CompletedTask
            : UpdateConfigAsync(config => update(config.Settings, value));

    private async Task SaveEditableAdditionalOptionAsync(
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

        if (!TryGetEditableOption(dropdown, options, minimum, maximum, out int value))
        {
            RestoreEditableValue(dropdown, options, getValue);
            if (refreshLowResourceSummary)
                UpdateLowResourceEffectiveValuesDisplay();
            return;
        }

        SetEditableValue(dropdown, options, value);
        await UpdateConfigAsync(config => update(config.Settings, value));
        if (refreshLowResourceSummary)
            UpdateLowResourceEffectiveValuesDisplay();
    }

    private async Task SaveEditableServerOptionAsync(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        int minimum,
        int maximum,
        Func<StartingProfile, int> getValue,
        Action<StartingProfile, int> update)
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryGetEditableOption(dropdown, options, minimum, maximum, out int value))
        {
            RestoreEditableValue(dropdown, options, getValue);
            return;
        }

        SetEditableValue(dropdown, options, value);
        await UpdateConfigAsync(config => update(config.Settings, value), beforeSave: ApplyLocalStartProfile);
    }

    private void UpdateLowResourceEffectiveValuesDisplay(StartingProfile? settings = null)
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
                $"• Connection-health refresh: about every 3 seconds\n" +
                $"• UI updates while minimized: paused\n" +
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

    private static void CopyAdditionalSettings(StartingProfile source, StartingProfile target)
    {
        target.ViewerCommandLimitPerMinute = source.ViewerCommandLimitPerMinute;
        target.PassiveRecentChatWindowMinutes = source.PassiveRecentChatWindowMinutes;
        target.AutomaticBackupsEnabled = source.AutomaticBackupsEnabled;
        target.AutomaticBackupIntervalHours = source.AutomaticBackupIntervalHours;
        target.AutomaticBackupRetentionCount = source.AutomaticBackupRetentionCount;
        target.LowResourceModeEnabled = source.LowResourceModeEnabled;
        target.PauseUIUpdatesWhenMinimized = source.PauseUIUpdatesWhenMinimized;
        target.MaxVisibleTwitchLogLines = source.MaxVisibleTwitchLogLines;
        target.MaxVisibleMinecraftLogLines = source.MaxVisibleMinecraftLogLines;
        target.ViewerRosterRefreshIntervalSeconds = source.ViewerRosterRefreshIntervalSeconds;
        target.MinecraftRelayMessagesPerSecond = source.MinecraftRelayMessagesPerSecond;
        target.MaxGameplayCommandQueue = source.MaxGameplayCommandQueue;
        target.RCONTimeoutSeconds = source.RCONTimeoutSeconds;
        target.GracefulShutdownTimeoutSeconds = source.GracefulShutdownTimeoutSeconds;
        target.SQLiteOptimizeIntervalHours = source.SQLiteOptimizeIntervalHours;
        target.ViewDistance = source.ViewDistance;
        target.SimulationDistance = source.SimulationDistance;
        target.EntityBroadcastRangePercentage = source.EntityBroadcastRangePercentage;
        target.NetworkCompressionThreshold = source.NetworkCompressionThreshold;
        target.EmptyServerShutdownDelayMinutes = source.EmptyServerShutdownDelayMinutes;
        target.CommandCustomizations = new Dictionary<string, CommandCustomization>(StringComparer.OrdinalIgnoreCase);
    }
}
