using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Settings : UserControl
{
    private enum NumericComboBoxInputMode
    {
        UnsignedInteger,
        SignedInteger,
        Decimal
    }

    private enum SettingsCategory
    {
        Commands,
        CustomCommands,
        Economy,
        Gameplay,
        ChatDisplay,
        Performance,
        Server,
        Dangerous
    }

    private const int MaxRAMGB = 256;
    private const double DefaultGlobalCooldownSeconds = 10.0;
    private static readonly (double Seconds, string Label)[] GlobalCooldownOptions =
    [
        (0.1, "0.1s"),
        (0.5, "0.5s"),
        (1.0, "1s"),
        (2.0, "2s"),
        (3.0, "3s"),
        (5.0, "5s"),
        (10.0, "10s"),
        (15.0, "15s"),
        (30.0, "30s"),
        (60.0, "60s"),
        (120.0, "120s")
    ];
    private static readonly (double Multiplier, string Label)[] CommandCostMultiplierOptions =
    [
        (0.0, "0x"),
        (0.5, "0.5x"),
        (0.75, "0.75x"),
        (1.0, "1x"),
        (1.25, "1.25x"),
        (1.5, "1.5x"),
        (2.0, "2x"),
        (3.0, "3x")
    ];
    private static readonly int[] FollowRewardAmountOptions = [25, 50, 100, 200, 500, 1000];
    private static readonly string[] ResponseVerbosityOptions =
    [
        BotResponseVerbositySettings.Normal,
        BotResponseVerbositySettings.Reduced,
        BotResponseVerbositySettings.EssentialOnly
    ];

    private bool _initializing = true;
    private SettingsCategory _currentCategory = SettingsCategory.Commands;
    private bool _updatingCustomValueControls;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private CancellationTokenSource? _RAMSaveDebounceCts;
    private CancellationTokenSource? _tokenAuthorizationCts;
    private bool _hasSavedTwitchAuthorization;
    private int _prefixSaveVersion;
    private string _savedCommandPrefix = "!";
    private string _savedSecondaryCommandPrefix = string.Empty;
    private string _savedRCONPassword = string.Empty;

    private static bool IsAsciiDigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        for (int i = 0; i < value.Length; i++)
            if (!char.IsAsciiDigit(value[i]))
                return false;

        return true;
    }

    public Settings()
    {
        InitializeComponent();
        AddMinigameOptions();
        AddCooldownOptions();
        AddEconomyOptions();
        AddMainOptions();
        AddExtraOptions();

        DataObject.AddPastingHandler(MinRAMTextBox, RAM_Pasting);
        DataObject.AddPastingHandler(MaxRAMTextBox, RAM_Pasting);
        AddPrefixOptions(CommandPrefixTextBox);
        AddPrefixOptions(SecondaryCommandPrefixTextBox);

        ComboBox[] numericDropdowns =
        [
            ViewerCommandLimitDropdown, ChannelCommandLimitDropdown, GlobalCooldownSecondsDropdown,
            PassivePayoutAmountDropdown, PassivePayoutMinimumDropdown, PassivePayoutMaximumDropdown,
            MaximumTokenBalanceDropdown, CommandCostMultiplierDropdown,
            FollowRewardAmountDropdown, RelayRateDropdown, MaxTwitchLogLinesDropdown,
            MaxMinecraftLogLinesDropdown, GameplayQueueDropdown, ViewDistanceDropdown,
            SimulationDistanceDropdown, EntityBroadcastRangeDropdown, NetworkCompressionDropdown,
            RCONTimeoutDropdown
        ];
        foreach (ComboBox dropdown in numericDropdowns)
            SetupNumericBox(dropdown);

        ShowPage(CommandsSettingsPage, CommandsCategoryButton, SettingsCategory.Commands);
        IsVisibleChanged += OnVisibilityChanged;
        Loaded += (_, _) => ResetAuthButton();
        Unloaded += (_, _) => HideSettings();
    }

    private void CancelRAMSave()
    {
        CancellationTokenSource? debounceCts = _RAMSaveDebounceCts;
        _RAMSaveDebounceCts = null;
        try
        {
            debounceCts?.Cancel();
        }
        catch
        {
        }
    }

    private void RAM_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not TextBox textBox)
        {
            args.Handled = true;
            return;
        }

        args.Handled = !IsValidRAMText(BuildTextCandidate(textBox, args.Text));
    }

    private void RAM_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not TextBox textBox ||
            args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidRAMText(BuildTextCandidate(textBox, pastedText)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildTextCandidate(TextBox textBox, string insertedText)
    {
        string current = textBox.Text ?? string.Empty;
        int start = Math.Clamp(textBox.SelectionStart, 0, current.Length);
        int length = Math.Clamp(textBox.SelectionLength, 0, current.Length - start);
        return current.Remove(start, length).Insert(start, insertedText);
    }

    private static bool IsValidRAMText(string candidate)
    {
        if (!IsAsciiDigitsOnly(candidate))
            return false;

        return int.TryParse(candidate, out int value) && value <= MaxRAMGB;
    }

    private void SetupNumericBox(ComboBox dropdown)
    {
        dropdown.PreviewTextInput += NumericBox_PreviewTextInput;
        dropdown.GotKeyboardFocus += NumericBox_GotFocus;
        DataObject.AddPastingHandler(dropdown, NumericBox_Pasting);
    }

    private void NumericBox_GotFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is TextBox editor)
            editor.SelectAll();
    }

    private void NumericBox_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        string candidate = BuildNumberCandidate(dropdown, args.Text);
        args.Handled = !IsValidNumberText(candidate, GetNumberMode(dropdown));
    }

    private void NumericBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        if (args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidNumberText(
                BuildNumberCandidate(dropdown, pastedText),
                GetNumberMode(dropdown)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildNumberCandidate(ComboBox dropdown, string insertedText)
    {
        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is not TextBox editor)
            return (dropdown.Text ?? string.Empty) + insertedText;

        return BuildTextCandidate(editor, insertedText);
    }

    private NumericComboBoxInputMode GetNumberMode(ComboBox dropdown)
    {
        if (ReferenceEquals(dropdown, GlobalCooldownSecondsDropdown) ||
            ReferenceEquals(dropdown, CommandCostMultiplierDropdown))
        {
            return NumericComboBoxInputMode.Decimal;
        }

        return ReferenceEquals(dropdown, NetworkCompressionDropdown)
            ? NumericComboBoxInputMode.SignedInteger
            : NumericComboBoxInputMode.UnsignedInteger;
    }

    private static bool IsValidNumberText(string text, NumericComboBoxInputMode mode)
    {
        if (text.Length == 0)
            return true;

        int index = 0;
        if (mode == NumericComboBoxInputMode.SignedInteger && text[0] == '-')
        {
            index = 1;
            if (text.Length == 1)
                return true;
        }

        bool decimalPointSeen = false;
        for (; index < text.Length; index++)
        {
            char character = text[index];
            if (character >= '0' && character <= '9')
                continue;

            if (mode == NumericComboBoxInputMode.Decimal && character == '.' && !decimalPointSeen)
            {
                decimalPointSeen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) ReloadSettings();
        else HideSettings();
    }

    private void ReloadSettings()
    {
        _hasSavedTwitchAuthorization = false;
        ResetAuthButton();
        try
        {
            TwitchCraftConfig config = ConfigurationStore.Load();
            _hasSavedTwitchAuthorization = HasTwitchAuth(config);
            ResetAuthButton();

            if (AppHelpers.GetTwitchCraftWindow(this) is null)
                return;

            _initializing = true;
            LoadSettings(config.Settings, config.Server);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSettingsLoadError(this, ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void HideSettings()
    {
        _initializing = true;
        CancelRAMSave();
        CancelAuthorization();
        RCONPasswordBox.Clear();
        RCONPasswordTextBox.Clear();
        RCONPasswordBox.Visibility = Visibility.Visible;
        RCONPasswordTextBox.Visibility = Visibility.Collapsed;
        RCONPasswordShowButton.Content = "Show";
        ResetAuthButton();
    }

    private void CancelAuthorization()
    {
        _tokenAuthorizationCts?.Cancel();
        _tokenAuthorizationCts = null;
    }

    private void ResetAuthButton()
    {
        AuthorizeTwitchButton.IsEnabled = true;
        AuthorizeTwitchButton.ToolTip = null;
        AuthorizeTwitchButton.Content = _tokenAuthorizationCts != null
            ? "Cancel Authorization"
            : _hasSavedTwitchAuthorization
                ? "Reauthorize Twitch"
                : "Authorize Twitch";
    }

    private static bool HasTwitchAuth(TwitchCraftConfig config)
        => !string.IsNullOrWhiteSpace(config.Twitch.BotToken)
            && string.Equals(
                (config.Twitch.ClientID ?? string.Empty).Trim(),
                TwitchOAuthAuthorizer.ApplicationClientID,
                StringComparison.Ordinal);

    private void Gameplay_Click(object sender, RoutedEventArgs e)
        => ShowPage(GameplaySettingsPage, GameplayCategoryButton, SettingsCategory.Gameplay);

    private void Commands_Click(object sender, RoutedEventArgs e)
        => ShowPage(CommandsSettingsPage, CommandsCategoryButton, SettingsCategory.Commands);

    private void CustomCommands_Click(object sender, RoutedEventArgs e)
        => ShowPage(CustomCommandsSettingsPage, CustomCommandsCategoryButton, SettingsCategory.CustomCommands);

    private void Economy_Click(object sender, RoutedEventArgs e)
        => ShowPage(EconomySettingsPage, EconomyCategoryButton, SettingsCategory.Economy);

    private void ChatDisplay_Click(object sender, RoutedEventArgs e)
        => ShowPage(ChatDisplaySettingsPage, ChatDisplayCategoryButton, SettingsCategory.ChatDisplay);

    private void Performance_Click(object sender, RoutedEventArgs e)
        => ShowPage(PerformanceSettingsPage, PerformanceCategoryButton, SettingsCategory.Performance);

    private void Server_Click(object sender, RoutedEventArgs e)
        => ShowPage(ServerSettingsPage, ServerCategoryButton, SettingsCategory.Server);

    private void Dangerous_Click(object sender, RoutedEventArgs e)
        => ShowPage(DangerousSettingsPage, DangerousCategoryButton, SettingsCategory.Dangerous);

    private void ShowPage(Grid page, Button selectedButton, SettingsCategory category)
    {
        _currentCategory = category;
        CommandsSettingsPage.Visibility = Visibility.Collapsed;
        CustomCommandsSettingsPage.Visibility = Visibility.Collapsed;
        GameplaySettingsPage.Visibility = Visibility.Collapsed;
        EconomySettingsPage.Visibility = Visibility.Collapsed;
        ChatDisplaySettingsPage.Visibility = Visibility.Collapsed;
        PerformanceSettingsPage.Visibility = Visibility.Collapsed;
        ServerSettingsPage.Visibility = Visibility.Collapsed;
        DangerousSettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        CommandsCategoryButton.FontWeight = FontWeights.Normal;
        CustomCommandsCategoryButton.FontWeight = FontWeights.Normal;
        GameplayCategoryButton.FontWeight = FontWeights.Normal;
        EconomyCategoryButton.FontWeight = FontWeights.Normal;
        ChatDisplayCategoryButton.FontWeight = FontWeights.Normal;
        PerformanceCategoryButton.FontWeight = FontWeights.Normal;
        ServerCategoryButton.FontWeight = FontWeights.Normal;
        DangerousCategoryButton.FontWeight = FontWeights.Normal;
        selectedButton.FontWeight = FontWeights.Bold;

        CommandsCategoryButton.Opacity = 0.78;
        CustomCommandsCategoryButton.Opacity = 0.78;
        GameplayCategoryButton.Opacity = 0.78;
        EconomyCategoryButton.Opacity = 0.78;
        ChatDisplayCategoryButton.Opacity = 0.78;
        PerformanceCategoryButton.Opacity = 0.78;
        ServerCategoryButton.Opacity = 0.78;
        DangerousCategoryButton.Opacity = 0.78;
        selectedButton.Opacity = 1;
    }

    private void LoadSettings(StartingProfile settings, ServerConfig server)
    {
        MinigamesCheckbox.IsChecked = settings.MinigamesEnabled;
        string cooldownText = settings.MinigameCooldown + " minutes";
        MinigameCooldownDropdown.SelectedItem = cooldownText;
        MinigameCooldownDropdown.Text = cooldownText;
        PassiveTokensCheckbox.IsChecked = settings.PassiveTokenEarningEnabled;
        ResponseVerbosityDropdown.SelectedItem = ConfigurationStore.NormalizeVerbosity(settings.BotResponseVerbosity);
        SetCostMultiplier(settings.CommandCostMultiplier);
        FollowRewardsCheckbox.IsChecked = settings.AutomaticFollowRewardsEnabled;
        SetFollowReward(settings.FollowRewardAmount);
        UpdateFollowReward(settings.AutomaticFollowRewardsEnabled);
        BitRewardsCheckbox.IsChecked = settings.AutomaticBitRewardsEnabled;
        NonCommandChatRelayCheckbox.IsChecked = settings.NonCommandChatRelayEnabled;
        ModeratorCommandsCheckbox.IsChecked = settings.ModeratorsCanUseStreamerCommands;
        GlobalCooldownCheckbox.IsChecked = settings.GlobalGameCommandCooldownEnabled;
        SetGlobalCooldown(settings.GlobalGameCommandCooldownSeconds);
        UpdateGlobalCooldown(settings.GlobalGameCommandCooldownEnabled);
        StatisticsEnabledCheckbox.IsChecked = settings.StatisticsEnabled;
        PVPCheckbox.IsChecked = settings.MultiplayerPVPEnabled;
        HardcoreCheckbox.IsChecked = settings.HardcoreEnabled;
        SetDifficulty(settings.Difficulty);
        MinRAMTextBox.Text = server.MemoryMinGB.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MaxRAMTextBox.Text = server.MemoryMaxGB.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _savedRCONPassword = server.RCON.Password;
        RCONPasswordBox.Password = _savedRCONPassword;
        RCONPasswordTextBox.Clear();
        RCONPasswordBox.Visibility = Visibility.Visible;
        RCONPasswordTextBox.Visibility = Visibility.Collapsed;
        RCONPasswordShowButton.Content = "Show";
        LoadMainSettings(settings);
        LoadExtraSettings(settings);
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MinRAMTextBox.Text, out int minRAM) &&
            int.TryParse(MaxRAMTextBox.Text, out int maxRAM) &&
            minRAM > maxRAM)
        {
            ErrorHandling.ShowInvalidRAM(this);
            return;
        }

        if (TryGetRAM(out int validMinRAM, out int validMaxRAM))
        {
            CancelRAMSave();
            await SaveConfigAsync(config =>
            {
                config.Server.MemoryMinGB = validMinRAM;
                config.Server.MemoryMaxGB = validMaxRAM;
            });
        }

        CancelAuthorization();
        AppHelpers.NavigateBack(this);
    }

    private void Back_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Back_Click(sender, e);
        }
    }

    private async void AuthorizeTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_tokenAuthorizationCts != null)
        {
            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Canceling...";
            AuthorizeTwitchButton.ToolTip = "Authorization is being canceled.";
            _tokenAuthorizationCts.Cancel();
            return;
        }

        if (!TwitchOAuthAuthorizer.IsOAuthConfigured)
        {
            ErrorHandling.ShowAuthError(this, "This TwitchCraft build is missing TwitchCraft's public Twitch Client ID. The release maintainer must add it before publishing the build.");
            return;
        }

        using CancellationTokenSource authorizationCts = new();
        _tokenAuthorizationCts = authorizationCts;
        ResetAuthButton();
        try
        {
            TwitchOAuthResult result = await TwitchOAuthAuthorizer.AuthorizeAsync(TwitchOAuthAuthorizer.ApplicationClientID, authorizationCts.Token);
            if (!ReferenceEquals(_tokenAuthorizationCts, authorizationCts))
                return;
            if (!result.IsSuccess)
            {
                ErrorHandling.ShowAuthError(this, result.Error);
                return;
            }

            TwitchCraftConfig updated = ConfigurationStore.Update(config =>
            {
                config.Twitch.ClientID = TwitchOAuthAuthorizer.ApplicationClientID;
                config.Twitch.BotToken = result.Token;
                config.Twitch.RefreshToken = result.RefreshToken;
                config.Twitch.BotName = result.Login;
            });
            _hasSavedTwitchAuthorization = true;
            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Applying Twitch...";
            AuthorizeTwitchButton.ToolTip = "Twitch authorization is being applied.";
            if (AppHelpers.GetTwitchCraftWindow(this) is TwitchCraft parent)
                await parent.Runtime.ApplySettingsAsync(updated);

            ErrorHandling.ShowAuthSuccess(this, result.Login, savedToConfig: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowAuthError(this, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_tokenAuthorizationCts, authorizationCts))
            {
                _tokenAuthorizationCts = null;
                ResetAuthButton();
            }
        }
    }

    private void DeleteConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppHelpers.GetTwitchCraftWindow(this) is not TwitchCraft parent)
            {
                ErrorHandling.ShowMainWindowError(this);
                return;
            }

            if (!ErrorHandling.ConfirmDeleteConfig(this))
            {
                return;
            }

            ConfigurationStore.DeleteConfigFiles();
            parent.RestartAfterReset();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowDeleteConfigError(this, ex);
        }
    }

    private async void Minigames_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            MinigamesCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.MinigamesEnabled = enabled,
            refreshMinigameLoops: true);

    private async void MinigameCooldown_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        string selected = (MinigameCooldownDropdown.SelectedItem as string
            ?? MinigameCooldownDropdown.Text
            ?? string.Empty).Trim();
        if (!int.TryParse(selected.Split(' ')[0], out int minutes) || minutes < 2 || minutes > 30)
        {
            return;
        }

        await SaveConfigAsync(config => config.Settings.MinigameCooldown = minutes);
    }

    private async void PassiveTokens_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            PassiveTokensCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.PassiveTokenEarningEnabled = enabled);

    private async void Verbosity_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && ResponseVerbosityDropdown.SelectedItem is string verbosity)
            await SaveConfigAsync(config => config.Settings.BotResponseVerbosity = verbosity);
    }

    private async void CostMultiplier_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && CommandCostMultiplierDropdown.SelectedItem is string)
            await SaveCostMultiplierAsync();
    }

    private async void CostMultiplier_LostFocus(object sender, RoutedEventArgs e)
        => await SaveCostMultiplierAsync();

    private async Task SaveCostMultiplierAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryReadDouble(CommandCostMultiplierDropdown, CommandCostMultiplierOptions, 0.0, 5.0, out double multiplier))
        {
            ReloadSettings();
            return;
        }

        SetCostMultiplier(multiplier);
        await SaveConfigAsync(config => config.Settings.CommandCostMultiplier = multiplier);
    }

    private async void FollowRewards_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = FollowRewardsCheckbox.IsChecked == true;
        UpdateFollowReward(enabled);
        await UpdateBoolAsync(
            enabled,
            static (config, value) => config.Settings.AutomaticFollowRewardsEnabled = value);
    }

    private async void FollowReward_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && FollowRewardAmountDropdown.SelectedItem is string)
            await SaveFollowRewardAsync();
    }

    private async void FollowReward_LostFocus(object sender, RoutedEventArgs e)
        => await SaveFollowRewardAsync();

    private async Task SaveFollowRewardAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryReadInt(FollowRewardAmountDropdown, 1, 1_000_000, out int amount))
        {
            ReloadSettings();
            return;
        }

        SetFollowReward(amount);
        await SaveConfigAsync(config => config.Settings.FollowRewardAmount = amount);
    }

    private async void BitRewards_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            BitRewardsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.AutomaticBitRewardsEnabled = enabled);

    private async void ChatRelay_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            NonCommandChatRelayCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.NonCommandChatRelayEnabled = enabled);

    private async void ModeratorCommands_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            ModeratorCommandsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.ModeratorsCanUseStreamerCommands = enabled);

    private async void GlobalCooldown_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = GlobalCooldownCheckbox.IsChecked == true;
        if (enabled && !TryReadDouble(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, 0.1, 120.0, out _))
            SetGlobalCooldown(DefaultGlobalCooldownSeconds);

        UpdateGlobalCooldown(enabled);
        if (!_initializing)
            await SaveConfigAsync(config => config.Settings.GlobalGameCommandCooldownEnabled = enabled);
    }

    private async void CooldownTime_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && GlobalCooldownSecondsDropdown.SelectedItem is string)
            await SaveGlobalCooldownAsync();
    }

    private async void CooldownTime_LostFocus(object sender, RoutedEventArgs e)
        => await SaveGlobalCooldownAsync();

    private async Task SaveGlobalCooldownAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryReadDouble(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, 0.1, 120.0, out double seconds))
        {
            ReloadSettings();
            return;
        }

        SetGlobalCooldown(seconds);
        await SaveConfigAsync(config => config.Settings.GlobalGameCommandCooldownSeconds = seconds);
    }

    private async void Statistics_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolAsync(
            StatisticsEnabledCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.StatisticsEnabled = enabled);

    private async void ResetStats_Click(object sender, RoutedEventArgs e)
    {
        if (!ErrorHandling.ConfirmStatsReset(this) || AppHelpers.GetTwitchCraftWindow(this) is not TwitchCraft parent)
            return;

        Button? button = sender as Button;
        if (button != null)
        {
            button.ToolTip = "Statistics are being reset.";
            button.IsEnabled = false;
        }

        try
        {
            await parent.Runtime.Statistics.ResetAllAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowStatsResetError(this, ex);
        }
        finally
        {
            if (button != null)
            {
                button.IsEnabled = true;
                button.ToolTip = null;
            }
        }
    }

    private async void PVP_Changed(object sender, RoutedEventArgs e)
    {
        await SaveGameplayAsync();
        if (!_initializing && AppHelpers.GetTwitchCraftWindow(this) is TwitchCraft parent)
            await parent.Runtime.ApplyPVPGameRuleAsync();
    }

    private async void Hardcore_Changed(object sender, RoutedEventArgs e)
        => await SaveGameplayAsync();

    private async void Difficulty_Changed(object sender, SelectionChangedEventArgs e)
        => await SaveGameplayAsync();

    private async Task SaveGameplayAsync()
    {
        if (_initializing || DifficultyDropdown == null)
        {
            return;
        }

        bool PVPEnabled = PVPCheckbox.IsChecked == true;
        bool hardcoreEnabled = HardcoreCheckbox.IsChecked != false;
        string difficulty = ConfigurationStore.NormalizeDifficulty((DifficultyDropdown.SelectedItem as ComboBoxItem)?.Content as string);

        await SaveConfigAsync(
            config =>
            {
                config.Settings.MultiplayerPVPEnabled = PVPEnabled;
                config.Settings.HardcoreEnabled = hardcoreEnabled;
                config.Settings.Difficulty = difficulty;
            },
            beforeSave: ApplyLocalProfile);
    }

    private static void ApplyLocalProfile(TwitchCraftConfig config)
    {
        if (!config.Settings.RemoteControlEnabled)
            ServerPropertyEditor.ApplyProfile(config);
    }

    private string GetRCONPassword() => RCONPasswordTextBox.Visibility == Visibility.Visible
        ? RCONPasswordTextBox.Text ?? string.Empty
        : RCONPasswordBox.Password ?? string.Empty;

    private void CopyRCONPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetRCONPassword() is { Length: > 0 } password) Clipboard.SetText(password);
        }
        catch (Exception ex) { ErrorHandling.LogNonFatal("RCON password copy failed", ex); }
    }

    private void GenerateRCONPassword_Click(object sender, RoutedEventArgs e)
    {
        string password = ConfigurationStore.GenerateRCONPassword();
        if (RCONPasswordTextBox.Visibility == Visibility.Visible)
        {
            RCONPasswordTextBox.Text = password;
            RCONPasswordTextBox.Focus();
            RCONPasswordTextBox.CaretIndex = password.Length;
        }
        else
        {
            RCONPasswordBox.Password = password;
            RCONPasswordBox.Focus();
        }
    }

    private void ToggleRCONPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (RCONPasswordTextBox.Visibility == Visibility.Visible)
        {
            RCONPasswordBox.Password = RCONPasswordTextBox.Text ?? string.Empty;
            RCONPasswordTextBox.Clear();
            RCONPasswordTextBox.Visibility = Visibility.Collapsed;
            RCONPasswordBox.Visibility = Visibility.Visible;
            RCONPasswordShowButton.Content = "Show";
            RCONPasswordBox.Focus();
        }
        else
        {
            RCONPasswordTextBox.Text = RCONPasswordBox.Password ?? string.Empty;
            RCONPasswordBox.Visibility = Visibility.Collapsed;
            RCONPasswordTextBox.Visibility = Visibility.Visible;
            RCONPasswordShowButton.Content = "Hide";
            RCONPasswordTextBox.Focus();
            RCONPasswordTextBox.CaretIndex = RCONPasswordTextBox.Text.Length;
        }
    }

    private async void RCONPasswordEditor_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_initializing || RCONPasswordEditor.IsKeyboardFocusWithin)
            return;

        string currentPassword = GetRCONPassword();
        if (!ConfigurationStore.TryNormalizeRCONPassword(currentPassword, out string password))
        {
            if (!string.Equals(currentPassword, _savedRCONPassword, StringComparison.Ordinal))
            {
                ErrorHandling.ShowInvalidRCONPassword(this);
                if (RCONPasswordTextBox.Visibility == Visibility.Visible) RCONPasswordTextBox.Text = _savedRCONPassword;
                else RCONPasswordBox.Password = _savedRCONPassword;
            }
            return;
        }

        if (string.Equals(password, _savedRCONPassword, StringComparison.Ordinal))
        {
            if (!string.Equals(currentPassword, password, StringComparison.Ordinal))
            {
                if (RCONPasswordTextBox.Visibility == Visibility.Visible) RCONPasswordTextBox.Text = password;
                else RCONPasswordBox.Password = password;
            }
            return;
        }

        await _settingsSaveGate.WaitAsync();
        try
        {
            if (string.Equals(password, _savedRCONPassword, StringComparison.Ordinal))
                return;

            await Task.Run(() => ConfigurationStore.Update(config => config.Server.RCON.Password = password));
            _savedRCONPassword = password;
            if (RCONPasswordTextBox.Visibility == Visibility.Visible) RCONPasswordTextBox.Text = password;
            else RCONPasswordBox.Password = password;
            AppHelpers.GetTwitchCraftWindow(this)?.Runtime.StageLocalRCONPassword(password);
        }
        catch (Exception ex) { ErrorHandling.ShowSaveSettingsError(this, ex); }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private async void RAM_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _RAMSaveDebounceCts?.Cancel();
        CancellationTokenSource debounceCts = new();
        _RAMSaveDebounceCts = debounceCts;

        try
        {
            await Task.Delay(500, debounceCts.Token);

            if (!ReferenceEquals(_RAMSaveDebounceCts, debounceCts) || !TryGetRAM(out int minRAM, out int maxRAM))
            {
                return;
            }

            await SaveConfigAsync(config =>
            {
                config.Server.MemoryMinGB = minRAM;
                config.Server.MemoryMaxGB = maxRAM;
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_RAMSaveDebounceCts, debounceCts))
            {
                _RAMSaveDebounceCts = null;
            }

            debounceCts.Dispose();
        }
    }

    private void AddMinigameOptions()
    {
        if (MinigameCooldownDropdown.Items.Count > 0)
        {
            return;
        }

        for (int i = 2; i <= 30; i++)
        {
            MinigameCooldownDropdown.Items.Add(i + " minutes");
        }
    }

    private void AddCooldownOptions()
    {
        if (GlobalCooldownSecondsDropdown.Items.Count > 0)
        {
            return;
        }

        foreach ((double _, string label) in GlobalCooldownOptions)
            GlobalCooldownSecondsDropdown.Items.Add(label);
    }

    private void AddEconomyOptions()
    {
        if (ResponseVerbosityDropdown.Items.Count == 0)
            foreach (string option in ResponseVerbosityOptions)
                ResponseVerbosityDropdown.Items.Add(option);

        if (CommandCostMultiplierDropdown.Items.Count == 0)
            foreach ((double _, string label) in CommandCostMultiplierOptions)
                CommandCostMultiplierDropdown.Items.Add(label);

        if (FollowRewardAmountDropdown.Items.Count == 0)
            foreach (int amount in FollowRewardAmountOptions)
                FollowRewardAmountDropdown.Items.Add(amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void SetCostMultiplier(double multiplier)
        => SetDoubleValue(CommandCostMultiplierDropdown, CommandCostMultiplierOptions, multiplier);

    private void SetFollowReward(int amount)
        => SetTextValue(
            FollowRewardAmountDropdown,
            amount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void UpdateFollowReward(bool enabled)
    {
        FollowRewardAmountDropdown?.IsEnabled = enabled;
    }

    private void SetGlobalCooldown(double seconds)
        => SetDoubleValue(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, seconds);

    private void UpdateGlobalCooldown(bool visible)
        => GlobalCooldownSecondsDropdown.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private bool TryGetRAM(out int minRAM, out int maxRAM)
    {
        minRAM = 0;
        maxRAM = 0;

        string minText = (MinRAMTextBox.Text ?? string.Empty).Trim();
        string maxText = (MaxRAMTextBox.Text ?? string.Empty).Trim();

        if (!int.TryParse(minText, out int parsedMin) || !int.TryParse(maxText, out int parsedMax))
        {
            return false;
        }

        if (parsedMin <= 0 || parsedMax <= 0 || parsedMin > parsedMax || parsedMax > MaxRAMGB)
        {
            return false;
        }

        minRAM = parsedMin;
        maxRAM = parsedMax;
        return true;
    }

    private void SetDifficulty(string? difficulty)
    {
        DifficultyDropdown.SelectedIndex = ConfigurationStore.NormalizeDifficulty(difficulty) switch
        {
            "Easy" => 0,
            "Hard" => 2,
            _ => 1
        };
    }
}
