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
        CommandPrefixTextBox.Text = ConfigurationStore.NormalizeCommandPrefix(settings.CommandPrefix, "!");
        SecondaryCommandPrefixTextBox.Text = ConfigurationStore.NormalizeCommandPrefix(settings.SecondaryCommandPrefix, string.Empty);
        MentionViewersCheckbox.IsChecked = settings.MentionViewersInBotReplies;
        ExactCooldownCheckbox.IsChecked = settings.ShowExactCooldownRemaining;
        UnknownCommandResponseCheckbox.IsChecked = settings.RespondToUnknownCommands;
        ViewerCommandsPausedCheckbox.IsChecked = settings.ViewerCommandsPaused;
        SetEditableInt(PassivePayoutAmountDropdown, PassivePayoutAmountOptions, settings.PassiveTokensPerPayout);
        SetEditableInt(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
        SetEditableInt(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
        SetEditableInt(MaximumTokenBalanceDropdown, MaximumTokenBalanceOptions, settings.MaximumTokenBalance);
        RecentChatPayoutCheckbox.IsChecked = settings.PassiveRewardsRequireRecentChat;
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

    private async void Prefix_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        string primary = ConfigurationStore.NormalizeCommandPrefix(CommandPrefixTextBox.Text, "!");
        string secondary = ConfigurationStore.NormalizeCommandPrefix(SecondaryCommandPrefixTextBox.Text, string.Empty);
        if (string.Equals(primary, secondary, StringComparison.Ordinal))
            secondary = string.Empty;

        CommandPrefixTextBox.Text = primary;
        SecondaryCommandPrefixTextBox.Text = secondary;
        await SaveConfigAsync(config =>
        {
            config.Settings.CommandPrefix = primary;
            config.Settings.SecondaryCommandPrefix = secondary;
        });
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
        RecentChatWindowDropdown.IsEnabled = RecentChatPayoutCheckbox.IsChecked == true;
        await SaveBoolAsync(RecentChatPayoutCheckbox, static (settings, value) => settings.PassiveRewardsRequireRecentChat = value);
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
        => UpdateBoolAsync(
            checkbox.IsChecked == true,
            (config, value) => update(config.Settings, value));

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
            dropdown.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(ApplyValue));
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
        => SetIntValue(dropdown, options, getValue(ConfigurationStore.Load().Settings));

    private void RestorePayoutRange()
    {
        StartingProfile settings = ConfigurationStore.Load().Settings;
        SetIntValue(PassivePayoutMinimumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMinimumSeconds);
        SetIntValue(PassivePayoutMaximumDropdown, PassivePayoutRangeOptions, settings.PassiveTokenPayoutMaximumSeconds);
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

    private static void CopyMainSettings(StartingProfile source, StartingProfile target)
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
