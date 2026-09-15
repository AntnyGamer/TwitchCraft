using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Settings
{
    private static readonly (int Value, string Label)[] PassivePayoutAmountOptions =
    [
        (1, "1"), (2, "2"), (5, "5"), (10, "10"), (25, "25"), (50, "50"), (100, "100")
    ];

    private static readonly (int Value, string Label)[] PassivePayoutRangeOptions =
    [
        (10, "10"), (15, "15"), (30, "30"), (60, "60"),
        (120, "120"), (300, "300"), (600, "600"), (900, "900")
    ];

    private static readonly (int Value, string Label)[] MaximumTokenBalanceOptions =
    [
        (0, "Unlimited"), (500, "500"), (1_000, "1,000"),
        (2_500, "2,500"), (5_000, "5,000"), (50_000, "50,000")
    ];

    private static readonly (int Value, string Label)[] ChannelCommandLimitOptions =
    [
        (0, "Unlimited"), (10, "10"), (20, "20"), (30, "30"),
        (60, "60"), (120, "120"), (300, "300")
    ];

    private static readonly string[] CommonCommandPrefixes =
    [
        "!", "?", ".", "#", "$", "%", "&", "+", "-", "~"
    ];

    private static readonly (string Value, string Label)[] RelayTextColorOptions =
    [
        ("black", "Black"), ("dark_blue", "Dark Blue"), ("dark_green", "Dark Green"),
        ("dark_aqua", "Dark Aqua"), ("dark_red", "Dark Red"), ("dark_purple", "Dark Purple"),
        ("gold", "Gold"), ("gray", "Gray"), ("dark_gray", "Dark Gray"),
        ("blue", "Blue"), ("green", "Green"), ("aqua", "Aqua"),
        ("red", "Red"), ("light_purple", "Light Purple"), ("yellow", "Yellow"),
        ("white", "White")
    ];

    private void AddMainOptions()
    {
        AddOptions(PassivePayoutAmountDropdown, PassivePayoutAmountOptions);
        AddOptions(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions);
        AddOptions(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions);
        AddOptions(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions);
        AddOptions(ChannelCommandLimitDropdown, ChannelCommandLimitOptions);

        if (RelayTextColorDropdown.Items.Count == 0)
            foreach ((string _, string label) in RelayTextColorOptions)
                RelayTextColorDropdown.Items.Add(label);
    }

    private static void AddPrefixOptions(ComboBox dropdown)
    {
        if (dropdown.Items.Count == 0)
            foreach (string prefix in CommonCommandPrefixes)
                dropdown.Items.Add(prefix);

        dropdown.Loaded -= Prefix_Loaded;
        dropdown.Loaded += Prefix_Loaded;
        SetupPrefixBox(dropdown);
    }

    private static void Prefix_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox dropdown)
            SetupPrefixBox(dropdown);
    }

    private static void SetupPrefixBox(ComboBox dropdown)
    {
        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is TextBox editor)
            editor.MaxLength = 2;
    }

    private static void AddOptions(ComboBox dropdown, (int Value, string Label)[] options)
    {
        if (dropdown.Items.Count == 0)
            foreach ((int _, string label) in options)
                dropdown.Items.Add(label);
    }

    private void LoadMainSettings(StartingProfile settings)
    {
        _savedCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(settings.CommandPrefix, "!");
        _savedSecondaryCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(settings.SecondaryCommandPrefix, string.Empty);
        CommandPrefixTextBox.Text = _savedCommandPrefix;
        SecondaryCommandPrefixTextBox.Text = _savedSecondaryCommandPrefix;
        MentionViewersCheckbox.IsChecked = settings.MentionViewersInBotReplies;
        ExactCooldownCheckbox.IsChecked = settings.ShowExactCooldownRemaining;
        UnknownCommandResponseCheckbox.IsChecked = settings.RespondToUnknownCommands;
        ViewerCommandsPausedCheckbox.IsChecked = settings.ViewerCommandsPaused;
        SetEditableInt(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, settings.PassiveTokensPerPayout);
        SetEditableInt(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
        SetEditableInt(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
        SetEditableInt(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, settings.MaximumTokenBalance);
        ActivityPayoutCheckbox.IsChecked = settings.PassiveRewardsRequireActivity;
        SetEditableInt(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, settings.ChannelCommandLimitPerMinute);
        AllowAllTargetsCheckbox.IsChecked = settings.AllowAllPlayerTarget;
        AllowRandomTargetsCheckbox.IsChecked = settings.AllowRandomPlayerTarget;
        RelayTimestampsCheckbox.IsChecked = settings.IncludeRelayTimestamps;
        SetRelayColor(settings.MinecraftRelayTextColor);
        ConnectionHealthCheckbox.IsChecked = settings.ShowConnectionHealth;
    }

    private static void SetIntOption(ComboBox dropdown, (int Value, string Label)[] options, int value, int fallback)
    {
        foreach ((int option, string label) in options)
        {
            if (option == value)
            {
                dropdown.SelectedItem = label;
                return;
            }
        }

        foreach ((int option, string label) in options)
            if (option == fallback)
            {
                dropdown.SelectedItem = label;
                return;
            }
    }

    private static void SetEditableInt(ComboBox dropdown, (int Value, string Label)[] options, int value)
    {
        dropdown.SelectedIndex = -1;
        foreach ((int option, string label) in options)
        {
            if (option == value)
            {
                dropdown.Text = label;
                return;
            }
        }

        dropdown.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private void SetRelayColor(string color)
    {
        string normalized = ConfigurationStore.NormalizeColor(color);
        foreach ((string value, string label) in RelayTextColorOptions)
            if (string.Equals(value, normalized, StringComparison.Ordinal))
            {
                RelayTextColorDropdown.SelectedItem = label;
                return;
            }

        RelayTextColorDropdown.SelectedItem = "White";
    }

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
    private static readonly (int Value, string Label)[] RCONTimeoutOptions =
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
    private static readonly CooldownOption<int>[] CommandCooldownOptions =
        [new(null, "Default"), new(0, "None"), new(1, "1 second"), new(3, "3 seconds"), new(5, "5 seconds"), new(10, "10 seconds"), new(15, "15 seconds"), new(30, "30 seconds"), new(60, "1 minute"), new(300, "5 minutes"), new(600, "10 minutes")];
    private static readonly CooldownOption<double>[] CommandGlobalCooldownOptions =
        [new(null, "Default"), new(0, "None"), new(0.1, "0.1 second"), new(0.5, "0.5 seconds"), new(1, "1 second"), new(3, "3 seconds"), new(5, "5 seconds"), new(10, "10 seconds"), new(15, "15 seconds"), new(30, "30 seconds"), new(60, "1 minute"), new(300, "5 minutes"), new(600, "10 minutes")];

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
        AddOptions(RCONTimeoutDropdown, RCONTimeoutOptions);
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
        SetEditableInt(RCONTimeoutDropdown, RCONTimeoutOptions, settings.RCONTimeoutSeconds);
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
        UpdateCommandPrefixes(_savedCommandPrefix);
        MainHandler? runtime = AppHelpers.GetTwitchCraftWindow(this)?.Runtime;
        List<object> rows = [FindResource("CommandCustomizationHeader")];
        if (runtime != null)
            foreach (string name in runtime.RegisteredCommandNames)
            {
                settings.CommandCustomizations.TryGetValue(name, out CommandCustomization? customization);
                rows.Add(new CommandSettingsRow(name, customization, SaveCommandRow));
            }
        rows.Add(FindResource("CommandCustomizationFooter"));
        CommandCustomizationList.ItemsSource = rows;
    }

    private void UpdateCommandPrefixes(string prefix) => Resources["CommandSettingsPrefix"] = prefix;
    internal sealed record CooldownOption<T>(T? Value, string Label) where T : struct;

    internal sealed class CommandSettingsRow : INotifyPropertyChanged
    {
        private readonly Action<CommandSettingsRow> _save;
        private bool _enabled;
        private CooldownOption<int> _perUserCooldown;
        private CooldownOption<double> _globalCooldown;

        internal CommandSettingsRow(string name, CommandCustomization? customization, Action<CommandSettingsRow> save)
        {
            Name = name;
            _save = save;
            _enabled = customization?.Enabled ?? true;
            int? perUser = customization?.CooldownSeconds;
            double? global = customization?.GlobalCooldownSeconds;
            if (perUser is < 0 or > 86400) perUser = null;
            if (global.HasValue && (!double.IsFinite(global.Value) || global is < 0 or > 86400)) global = null;
            (PerUserOptions, _perUserCooldown) = OptionsFor(CommandCooldownOptions, perUser);
            (GlobalOptions, _globalCooldown) = OptionsFor(CommandGlobalCooldownOptions, global);
        }

        public string Name { get; }
        public CooldownOption<int>[] PerUserOptions { get; }
        public CooldownOption<double>[] GlobalOptions { get; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Enabled { get => _enabled; set => Change(ref _enabled, value); }
        // A recycled ComboBox may temporarily clear its selection; that is not an edit.
        public CooldownOption<int> PerUserCooldown
        {
            get => _perUserCooldown;
            set { if (value != null) Change(ref _perUserCooldown, value); }
        }
        public CooldownOption<double> GlobalCooldown
        {
            get => _globalCooldown;
            set { if (value != null) Change(ref _globalCooldown, value); }
        }

        private void Change<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            _save(this);
        }

        private static (CooldownOption<T>[] Options, CooldownOption<T> Selected) OptionsFor<T>(
            CooldownOption<T>[] presets, T? value) where T : struct, IFormattable
        {
            foreach (CooldownOption<T> option in presets)
                if (EqualityComparer<T?>.Default.Equals(option.Value, value)) return (presets, option);
            CooldownOption<T> selected = new(value, value?.ToString(null, CultureInfo.InvariantCulture) + " seconds");
            return ([.. presets, selected], selected);
        }
    }
}
