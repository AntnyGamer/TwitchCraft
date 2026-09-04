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
    private static readonly (int Value, string Label)[] ActivityWindowOptions =
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
    private static readonly (double? Value, string Label)[] CommandGlobalCooldownOptions =
        [(null, "Default"), (0, "None"), (0.1, "0.1 second"), (0.5, "0.5 seconds"), (1, "1 second"), (3, "3 seconds"), (5, "5 seconds"), (10, "10 seconds"), (15, "15 seconds"), (30, "30 seconds"), (60, "1 minute"), (300, "5 minutes"), (600, "10 minutes")];

    private void AddExtraOptions()
    {
        AddOptions(ViewerCommandLimitDropdown, ViewerCommandLimitOptions);
        AddOptions(ActivityWindowDropdown, ActivityWindowOptions);
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

    private void LoadExtraSettings(StartingProfile settings)
    {
        SetEditableInt(ViewerCommandLimitDropdown, ViewerCommandLimitOptions, settings.ViewerCommandLimitPerMinute);
        SetIntOption(ActivityWindowDropdown, ActivityWindowOptions, settings.PassiveActivityWindowMinutes, 10);
        ActivityWindowDropdown.IsEnabled = settings.PassiveRewardsRequireActivity;
        AutomaticBackupsCheckbox.IsChecked = settings.AutomaticBackupsEnabled;
        SetIntOption(BackupIntervalDropdown, BackupIntervalOptions, settings.AutomaticBackupIntervalHours, 24);
        SetIntOption(BackupRetentionDropdown, BackupRetentionOptions, settings.AutomaticBackupRetentionCount, StartingProfile.DefaultAutomaticBackupRetentionCount);
        BackupIntervalDropdown.IsEnabled = settings.AutomaticBackupsEnabled;
        BackupRetentionDropdown.IsEnabled = settings.AutomaticBackupsEnabled;
        LowResourceModeCheckbox.IsChecked = settings.LowResourceModeEnabled;
        PauseUIUpdatesCheckbox.IsChecked = settings.PauseUIUpdatesWhenMinimized;
        SetEditableInt(MaxTwitchLogLinesDropdown, VisibleLogLineOptions, settings.MaxVisibleTwitchLogLines);
        SetEditableInt(MaxMinecraftLogLinesDropdown, VisibleLogLineOptions, settings.MaxVisibleMinecraftLogLines);
        SetIntOption(ViewerRosterIntervalDropdown, ViewerRosterIntervalOptions, settings.ViewerRosterRefreshIntervalSeconds, 30);
        SetEditableInt(RelayRateDropdown, RelayRateOptions, settings.MinecraftRelayMessagesPerSecond);
        SetEditableInt(GameplayQueueDropdown, GameplayQueueOptions, settings.MaxGameplayCommandQueue);
        SetEditableInt(RconTimeoutDropdown, RconTimeoutOptions, settings.RCONTimeoutSeconds);
        SetIntOption(GracefulShutdownTimeoutDropdown, GracefulShutdownTimeoutOptions, settings.GracefulShutdownTimeoutSeconds, 5);
        SetIntOption(SqliteOptimizeDropdown, SqliteOptimizeOptions, settings.SQLiteOptimizeIntervalHours, 0);
        SetEditableInt(ViewDistanceDropdown, DistanceOptions, settings.ViewDistance);
        SetEditableInt(SimulationDistanceDropdown, DistanceOptions, settings.SimulationDistance);
        SetEditableInt(EntityBroadcastRangeDropdown, EntityBroadcastOptions, settings.EntityBroadcastRangePercentage);
        SetEditableInt(NetworkCompressionDropdown, NetworkCompressionOptions, settings.NetworkCompressionThreshold);
        SetIntOption(EmptyShutdownDropdown, EmptyShutdownOptions, settings.EmptyServerShutdownDelayMinutes, 0);
        WhitelistCheckbox.IsChecked = settings.WhitelistEnabled;
        UpdateLowResource(settings);
        BuildCommandSettings(settings);
    }

    private void BuildCommandSettings(StartingProfile settings)
    {
        CommandCustomizationPanel.Children.Clear();
        BotMainHandler? runtime = AppHelpers.GetBotWindow(this)?.Runtime;
        if (runtime == null)
            return;

        Grid header = CreateCommandSettingsRow();
        header.Margin = new Thickness(0, 0, 0, 4);
        AddCommandHeader(header, "Command", 0);
        AddCommandHeader(header, "Enabled", 1);
        AddCommandHeader(header, "Per-user", 2);
        AddCommandHeader(header, "Global", 3);
        CommandCustomizationPanel.Children.Add(header);

        foreach (string commandName in runtime.RegisteredCommandNames)
        {
            settings.CommandCustomizations.TryGetValue(commandName, out CommandCustomization? customization);
            Grid row = CreateCommandSettingsRow();

            TextBlock name = new()
            {
                Text = _savedCommandPrefix + commandName,
                Tag = commandName,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 14,
                Foreground = Brushes.White
            };
            CheckBox enabled = new()
            {
                Tag = commandName,
                IsChecked = customization?.Enabled ?? true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            ComboBox perUserCooldown = new()
            {
                Tag = commandName,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Cooldown for each viewer separately. Default keeps the command's built-in per-user behavior."
            };
            ComboBox globalCooldown = new()
            {
                Tag = commandName,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Cooldown shared by all viewers for this command. Default keeps the command's normal global cooldown behavior."
            };

            foreach ((int? _, string label) in CommandCooldownOptions)
                perUserCooldown.Items.Add(label);
            foreach ((double? _, string label) in CommandGlobalCooldownOptions)
                globalCooldown.Items.Add(label);

            SetCommandCooldown(perUserCooldown, customization?.CooldownSeconds);
            SetCommandGlobalCooldown(globalCooldown, customization?.GlobalCooldownSeconds);

            enabled.Checked += CommandEnabled_Changed;
            enabled.Unchecked += CommandEnabled_Changed;
            perUserCooldown.SelectionChanged += CommandCooldown_Changed;
            globalCooldown.SelectionChanged += CommandCooldown_Changed;

            Grid.SetColumn(name, 0);
            Grid.SetColumn(enabled, 1);
            Grid.SetColumn(perUserCooldown, 2);
            Grid.SetColumn(globalCooldown, 3);
            row.Children.Add(name);
            row.Children.Add(enabled);
            row.Children.Add(perUserCooldown);
            row.Children.Add(globalCooldown);
            CommandCustomizationPanel.Children.Add(row);
        }
    }

    private void UpdateCommandPrefixes(string prefix)
    {
        for (int i = 1; i < CommandCustomizationPanel.Children.Count; i++)
            if (CommandCustomizationPanel.Children[i] is Grid row &&
                row.Children.Count > 0 &&
                row.Children[0] is TextBlock name &&
                name.Tag is string commandName)
            {
                name.Text = prefix + commandName;
            }
    }

    private static Grid CreateCommandSettingsRow()
    {
        Grid row = new() { MinHeight = 34 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        return row;
    }

    private static void AddCommandHeader(Grid row, string text, int column)
    {
        TextBlock header = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(header, column);
        row.Children.Add(header);
    }

    private static void SetCommandCooldown(ComboBox dropdown, int? seconds)
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

    private static void SetCommandGlobalCooldown(ComboBox dropdown, double? seconds)
    {
        foreach ((double? value, string label) in CommandGlobalCooldownOptions)
            if (value == seconds)
            {
                dropdown.SelectedItem = label;
                return;
            }

        if (seconds.HasValue && double.IsFinite(seconds.Value) && seconds.Value is >= 0.0 and <= 86400.0)
        {
            string customLabel = seconds.Value.ToString("0.###", CultureInfo.InvariantCulture) + " seconds";
            dropdown.Items.Add(customLabel);
            dropdown.SelectedItem = customLabel;
            return;
        }

        dropdown.SelectedItem = "Default";
    }

    private static bool TryReadCommandCooldown(ComboBox dropdown, out int? seconds)
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

    private static bool TryReadCommandGlobalCooldown(ComboBox dropdown, out double? seconds)
    {
        if (dropdown.SelectedItem is string selected)
        {
            foreach ((double? value, string label) in CommandGlobalCooldownOptions)
                if (string.Equals(selected, label, StringComparison.Ordinal))
                {
                    seconds = value;
                    return true;
                }

            const string suffix = " seconds";
            if (selected.EndsWith(suffix, StringComparison.Ordinal) &&
                double.TryParse(selected[..^suffix.Length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double customSeconds) &&
                double.IsFinite(customSeconds) && customSeconds is >= 0.0 and <= 86400.0)
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
        await SaveCommandRowAsync(checkbox, commandName);
    }

    private async void CommandCooldown_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || sender is not ComboBox dropdown || dropdown.Tag is not string commandName)
            return;

        int column = Grid.GetColumn(dropdown);
        if (column == 2)
        {
            if (!TryReadCommandCooldown(dropdown, out _))
                return;
        }
        else if (column == 3)
        {
            if (!TryReadCommandGlobalCooldown(dropdown, out _))
                return;
        }
        else
        {
            return;
        }

        await SaveCommandRowAsync(dropdown, commandName);
    }

    private Task SaveCommandRowAsync(FrameworkElement control, string commandName)
    {
        if (control.Parent is not Grid row ||
            row.Children.Count < 4 ||
            row.Children[1] is not CheckBox enabled ||
            row.Children[2] is not ComboBox perUser ||
            row.Children[3] is not ComboBox global ||
            !TryReadCommandCooldown(perUser, out int? cooldown) ||
            !TryReadCommandGlobalCooldown(global, out double? globalCooldown))
        {
            return Task.CompletedTask;
        }

        return SaveCommandSettingAsync(commandName, enabled.IsChecked == true, cooldown, globalCooldown);
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

    private async void RconTimeout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && RconTimeoutDropdown.SelectedItem is string)
            await SaveExtraValueAsync(RconTimeoutDropdown, RconTimeoutOptions, 1, 60,
                static settings => settings.RCONTimeoutSeconds,
                static (settings, value) => settings.RCONTimeoutSeconds = value);
    }

    private async void RconTimeout_LostFocus(object sender, RoutedEventArgs e)
        => await SaveExtraValueAsync(RconTimeoutDropdown, RconTimeoutOptions, 1, 60,
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
        await SaveExtraBoolAsync(LowResourceModeCheckbox, static (settings, value) => settings.LowResourceModeEnabled = value);
        if (!_initializing)
            UpdateLowResource();
    }

    private async void PauseUi_Changed(object sender, RoutedEventArgs e)
        => await SaveExtraBoolAsync(PauseUIUpdatesCheckbox, static (settings, value) => settings.PauseUIUpdatesWhenMinimized = value);
    private async void Backups_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = AutomaticBackupsCheckbox.IsChecked == true;
        BackupIntervalDropdown.IsEnabled = enabled;
        BackupRetentionDropdown.IsEnabled = enabled;
        await SaveExtraBoolAsync(AutomaticBackupsCheckbox, static (settings, value) => settings.AutomaticBackupsEnabled = value);
    }

    private Task SaveExtraBoolAsync(CheckBox checkbox, Action<StartingProfile, bool> update)
    {
        if (_initializing)
            return Task.CompletedTask;

        // WPF controls belong to the UI thread. SaveConfigAsync runs its config mutation
        // inside Task.Run, so capture the DependencyProperty value before crossing threads.
        bool value = checkbox.IsChecked == true;
        return SaveConfigAsync(config => update(config.Settings, value));
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

    private static void CopyExtraSettings(StartingProfile source, StartingProfile target)
    {
        target.ViewerCommandLimitPerMinute = source.ViewerCommandLimitPerMinute;
        target.PassiveActivityWindowMinutes = source.PassiveActivityWindowMinutes;
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
        target.WhitelistEnabled = source.WhitelistEnabled;
        target.CommandCustomizations = new Dictionary<string, CommandCustomization>(StringComparer.OrdinalIgnoreCase);
    }
}
