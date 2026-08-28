using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

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
        (0, "Unlimited"), (1_000, "1,000"), (10_000, "10,000"),
        (100_000, "100,000"), (1_000_000, "1,000,000"), (10_000_000, "10,000,000")
    ];

    private static readonly (int Value, string Label)[] ChannelCommandLimitOptions =
    [
        (0, "Unlimited"), (10, "10"), (20, "20"), (30, "30"),
        (60, "60"), (120, "120"), (300, "300")
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

    private void CheckSelectedSettingsItems()
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

    private static void AddOptions(ComboBox dropdown, (int Value, string Label)[] options)
    {
        if (dropdown.Items.Count == 0)
            foreach ((int _, string label) in options)
                dropdown.Items.Add(label);
    }

    private void LoadSelectedSettingsIntoControls(StartingProfile settings)
    {
        CommandPrefixTextBox.Text = ConfigurationStore.NormalizeCommandPrefix(settings.CommandPrefix, "!");
        SecondaryCommandPrefixTextBox.Text = ConfigurationStore.NormalizeCommandPrefix(settings.SecondaryCommandPrefix, string.Empty);
        MentionViewersCheckbox.IsChecked = settings.MentionViewersInBotReplies;
        ExactCooldownCheckbox.IsChecked = settings.ShowExactCooldownRemaining;
        UnknownCommandResponseCheckbox.IsChecked = settings.RespondToUnknownCommands;
        ViewerCommandsPausedCheckbox.IsChecked = settings.ViewerCommandsPaused;
        SelectEditableOption(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, settings.PassiveTokensPerPayout);
        SelectEditableOption(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
        SelectEditableOption(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
        SelectEditableOption(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, settings.MaximumTokenBalance);
        RecentChatPayoutCheckbox.IsChecked = settings.PassiveRewardsRequireRecentChat;
        SelectEditableOption(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, settings.ChannelCommandLimitPerMinute);
        AllowAllTargetsCheckbox.IsChecked = settings.AllowAllPlayerTarget;
        AllowRandomTargetsCheckbox.IsChecked = settings.AllowRandomPlayerTarget;
        RelayTimestampsCheckbox.IsChecked = settings.IncludeRelayTimestamps;
        SelectRelayColor(settings.MinecraftRelayTextColor);
        ConnectionHealthCheckbox.IsChecked = settings.ShowConnectionHealth;
    }

    private static void SelectOption(ComboBox dropdown, (int Value, string Label)[] options, int value, int fallback)
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

    private static bool TryGetSelectedOption(ComboBox dropdown, (int Value, string Label)[] options, out int value)
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

    private static void SelectEditableOption(ComboBox dropdown, (int Value, string Label)[] options, int value)
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

    private static bool TryGetEditableOption(
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

    private static bool TryGetEditableInteger(ComboBox dropdown, int minimum, int maximum, out int value)
    {
        string text = (dropdown.SelectedItem as string ?? dropdown.Text ?? string.Empty).Trim();
        return int.TryParse(
            text,
            NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value) && value >= minimum && value <= maximum;
    }

    private static bool TryGetEditableDoubleOption(
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

    private void SelectRelayColor(string color)
    {
        string normalized = ConfigurationStore.NormalizeMinecraftChatColor(color);
        foreach ((string value, string label) in RelayTextColorOptions)
            if (string.Equals(value, normalized, StringComparison.Ordinal))
            {
                RelayTextColorDropdown.SelectedItem = label;
                return;
            }

        RelayTextColorDropdown.SelectedItem = "White";
    }

    private async void CommandPrefixTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        string primary = ConfigurationStore.NormalizeCommandPrefix(CommandPrefixTextBox.Text, "!");
        string secondary = ConfigurationStore.NormalizeCommandPrefix(SecondaryCommandPrefixTextBox.Text, string.Empty);
        if (string.Equals(primary, secondary, StringComparison.Ordinal))
            secondary = string.Empty;

        CommandPrefixTextBox.Text = primary;
        SecondaryCommandPrefixTextBox.Text = secondary;
        await UpdateConfigAsync(config =>
        {
            config.Settings.CommandPrefix = primary;
            config.Settings.SecondaryCommandPrefix = secondary;
        });
    }

    private async void MentionViewersCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(MentionViewersCheckbox, static (settings, value) => settings.MentionViewersInBotReplies = value);

    private async void ExactCooldownCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ExactCooldownCheckbox, static (settings, value) => settings.ShowExactCooldownRemaining = value);

    private async void UnknownCommandResponseCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(UnknownCommandResponseCheckbox, static (settings, value) => settings.RespondToUnknownCommands = value);

    private async void ViewerCommandsPausedCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ViewerCommandsPausedCheckbox, static (settings, value) => settings.ViewerCommandsPaused = value);

    private async void RecentChatPayoutCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        RecentChatWindowDropdown.IsEnabled = RecentChatPayoutCheckbox.IsChecked == true;
        await SaveBoolAsync(RecentChatPayoutCheckbox, static (settings, value) => settings.PassiveRewardsRequireRecentChat = value);
    }

    private async void AllowAllTargetsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(AllowAllTargetsCheckbox, static (settings, value) => settings.AllowAllPlayerTarget = value);

    private async void AllowRandomTargetsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(AllowRandomTargetsCheckbox, static (settings, value) => settings.AllowRandomPlayerTarget = value);

    private async void RelayTimestampsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(RelayTimestampsCheckbox, static (settings, value) => settings.IncludeRelayTimestamps = value);

    private async void ConnectionHealthCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveBoolAsync(ConnectionHealthCheckbox, static (settings, value) => settings.ShowConnectionHealth = value);

    private Task SaveBoolAsync(CheckBox checkbox, Action<StartingProfile, bool> update)
        => UpdateBoolSettingIfReadyAsync(
            checkbox.IsChecked == true,
            (config, value) => update(config.Settings, value));

    private async void PassivePayoutAmountDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && PassivePayoutAmountDropdown.SelectedItem is string)
            await SavePassivePayoutAmountAsync();
    }

    private async void PassivePayoutAmountDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SavePassivePayoutAmountAsync();

    private async void PassivePayoutRangeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && sender is ComboBox { SelectedItem: string })
            await SavePassivePayoutRangeAsync(sender);
    }

    private async void PassivePayoutRangeDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SavePassivePayoutRangeAsync(sender);

    private async void MaximumTokenBalanceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && MaximumTokenBalanceDropdown.SelectedItem is string)
            await SaveMaximumTokenBalanceAsync();
    }

    private async void MaximumTokenBalanceDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveMaximumTokenBalanceAsync();

    private async void ChannelCommandLimitDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && ChannelCommandLimitDropdown.SelectedItem is string)
            await SaveChannelCommandLimitAsync();
    }

    private async void ChannelCommandLimitDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveChannelCommandLimitAsync();

    private async Task SavePassivePayoutAmountAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryGetEditableOption(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, 1, 1_000_000, out int value))
        {
            RestoreEditableValue(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, static settings => settings.PassiveTokensPerPayout);
            return;
        }
        SetEditableValue(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, value);
        await UpdateConfigAsync(config => config.Settings.PassiveTokensPerPayout = value);
    }

    private async Task SavePassivePayoutRangeAsync(object changedControl)
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryGetEditableOption(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, 10, 900, out int minimum) ||
            !TryGetEditableOption(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, 10, 900, out int maximum))
        {
            RestorePassivePayoutRange();
            return;
        }

        if (minimum > maximum)
        {
            if (ReferenceEquals(changedControl, PassivePayoutMinimumDropdown))
                maximum = minimum;
            else
                minimum = maximum;
        }

        SetEditableValue(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, minimum);
        SetEditableValue(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, maximum);
        await UpdateConfigAsync(config =>
        {
            config.Settings.PassiveTokenPayoutMinimumSeconds = minimum;
            config.Settings.PassiveTokenPayoutMaximumSeconds = maximum;
        });
    }

    private async Task SaveMaximumTokenBalanceAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryGetEditableOption(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, 0, int.MaxValue, out int value))
        {
            RestoreEditableValue(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, static settings => settings.MaximumTokenBalance);
            return;
        }
        SetEditableValue(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, value);
        await UpdateConfigAsync(config => config.Settings.MaximumTokenBalance = value);
    }

    private async Task SaveChannelCommandLimitAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;
        if (!TryGetEditableOption(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, 0, 1000, out int value))
        {
            RestoreEditableValue(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, static settings => settings.ChannelCommandLimitPerMinute);
            return;
        }
        SetEditableValue(ChannelCommandLimitDropdown, ChannelCommandLimitOptions, value);
        await UpdateConfigAsync(config => config.Settings.ChannelCommandLimitPerMinute = value);
    }

    private void SetEditableTextValue(ComboBox dropdown, string text)
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
            dropdown.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ApplyValue));
            return;
        }

        ApplyValue();
    }

    private void SetEditableValue(ComboBox dropdown, (int Value, string Label)[] options, int value)
    {
        foreach ((int option, string label) in options)
        {
            if (option == value)
            {
                SetEditableTextValue(dropdown, label);
                return;
            }
        }

        SetEditableTextValue(dropdown, value.ToString(CultureInfo.InvariantCulture));
    }

    private void SetEditableDoubleValue(ComboBox dropdown, (double Value, string Label)[] options, double value)
    {
        foreach ((double option, string label) in options)
        {
            if (Math.Abs(option - value) < 0.0000001)
            {
                SetEditableTextValue(dropdown, label);
                return;
            }
        }

        SetEditableTextValue(dropdown, value.ToString(CultureInfo.InvariantCulture));
    }

    private void RestoreEditableValue(
        ComboBox dropdown,
        (int Value, string Label)[] options,
        Func<StartingProfile, int> getValue)
        => SetEditableValue(dropdown, options, getValue(ConfigurationStore.Load().Settings));

    private void RestorePassivePayoutRange()
    {
        StartingProfile settings = ConfigurationStore.Load().Settings;
        SetEditableValue(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
        SetEditableValue(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
    }

    private async void RelayTextColorDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || RelayTextColorDropdown.SelectedItem is not string selected)
            return;

        foreach ((string value, string label) in RelayTextColorOptions)
            if (string.Equals(selected, label, StringComparison.Ordinal))
            {
                await UpdateConfigAsync(config => config.Settings.MinecraftRelayTextColor = value);
                return;
            }
    }

    private static void CopySelectedSettings(StartingProfile source, StartingProfile target)
    {
        target.CommandPrefix = source.CommandPrefix;
        target.SecondaryCommandPrefix = source.SecondaryCommandPrefix;
        target.MentionViewersInBotReplies = source.MentionViewersInBotReplies;
        target.ShowExactCooldownRemaining = source.ShowExactCooldownRemaining;
        target.RespondToUnknownCommands = source.RespondToUnknownCommands;
        target.ViewerCommandsPaused = source.ViewerCommandsPaused;
        target.PassiveTokensPerPayout = source.PassiveTokensPerPayout;
        target.PassiveTokenPayoutMinimumSeconds = source.PassiveTokenPayoutMinimumSeconds;
        target.PassiveTokenPayoutMaximumSeconds = source.PassiveTokenPayoutMaximumSeconds;
        target.MaximumTokenBalance = source.MaximumTokenBalance;
        target.PassiveRewardsRequireRecentChat = source.PassiveRewardsRequireRecentChat;
        target.ChannelCommandLimitPerMinute = source.ChannelCommandLimitPerMinute;
        target.AllowAllPlayerTarget = source.AllowAllPlayerTarget;
        target.AllowRandomPlayerTarget = source.AllowRandomPlayerTarget;
        target.IncludeRelayTimestamps = source.IncludeRelayTimestamps;
        target.MinecraftRelayTextColor = source.MinecraftRelayTextColor;
        target.ShowConnectionHealth = source.ShowConnectionHealth;
    }
}
