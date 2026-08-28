using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

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

    private const int MaxRamGB = 256;
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
    private CancellationTokenSource? _ramSaveDebounceCts;
    private CancellationTokenSource _tokenAuthorizationCts = new();

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
        DataObject.AddPastingHandler(MinRamTextBox, RamTextBox_Pasting);
        DataObject.AddPastingHandler(MaxRamTextBox, RamTextBox_Pasting);

        ComboBox[] numericDropdowns =
        [
            ViewerCommandLimitDropdown, ChannelCommandLimitDropdown, GlobalCooldownSecondsDropdown,
            PassivePayoutAmountDropdown, PassivePayoutMinimumDropdown, PassivePayoutMaximumDropdown,
            RecentChatWindowDropdown, MaximumTokenBalanceDropdown, CommandCostMultiplierDropdown,
            FollowRewardAmountDropdown, RelayRateDropdown, MaxTwitchLogLinesDropdown,
            MaxMinecraftLogLinesDropdown, GameplayQueueDropdown, ViewDistanceDropdown,
            SimulationDistanceDropdown, EntityBroadcastRangeDropdown, NetworkCompressionDropdown,
            RconTimeoutDropdown
        ];
        foreach (ComboBox dropdown in numericDropdowns)
            ConfigureNumericComboBox(dropdown);

        ShowSettingsPage(CommandsSettingsPage, CommandsCategoryButton, SettingsCategory.Commands);
        Loaded += Settings_Loaded;
        Unloaded += Settings_Unloaded;
    }

    private void CancelRamSaveDebounce()
    {
        CancellationTokenSource? debounceCts = _ramSaveDebounceCts;
        _ramSaveDebounceCts = null;
        try
        {
            debounceCts?.Cancel();
        }
        catch
        {
        }
    }

    private void RamTextBox_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not TextBox textBox)
        {
            args.Handled = true;
            return;
        }

        args.Handled = !IsValidRamTextCandidate(BuildTextBoxCandidate(textBox, args.Text));
    }

    private void RamTextBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not TextBox textBox ||
            args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidRamTextCandidate(BuildTextBoxCandidate(textBox, pastedText)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildTextBoxCandidate(TextBox textBox, string insertedText)
    {
        string current = textBox.Text ?? string.Empty;
        int start = Math.Clamp(textBox.SelectionStart, 0, current.Length);
        int length = Math.Clamp(textBox.SelectionLength, 0, current.Length - start);
        return current.Remove(start, length).Insert(start, insertedText);
    }

    private static bool IsValidRamTextCandidate(string candidate)
    {
        if (!IsAsciiDigitsOnly(candidate))
            return false;

        return int.TryParse(candidate, out int value) && value <= MaxRamGB;
    }

    private void ConfigureNumericComboBox(ComboBox dropdown)
    {
        dropdown.PreviewTextInput += NumericComboBox_PreviewTextInput;
        dropdown.GotKeyboardFocus += NumericComboBox_GotKeyboardFocus;
        DataObject.AddPastingHandler(dropdown, NumericComboBox_Pasting);
    }

    private void NumericComboBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is TextBox editor)
            editor.SelectAll();
    }

    private void NumericComboBox_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        string candidate = BuildNumericComboBoxCandidate(dropdown, args.Text);
        args.Handled = !IsValidNumericComboBoxText(candidate, GetNumericComboBoxInputMode(dropdown));
    }

    private void NumericComboBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not ComboBox dropdown)
            return;

        if (args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidNumericComboBoxText(
                BuildNumericComboBoxCandidate(dropdown, pastedText),
                GetNumericComboBoxInputMode(dropdown)))
        {
            args.CancelCommand();
        }
    }

    private static string BuildNumericComboBoxCandidate(ComboBox dropdown, string insertedText)
    {
        dropdown.ApplyTemplate();
        if (dropdown.Template.FindName("PART_EditableTextBox", dropdown) is not TextBox editor)
            return (dropdown.Text ?? string.Empty) + insertedText;

        string current = editor.Text ?? string.Empty;
        int start = Math.Clamp(editor.SelectionStart, 0, current.Length);
        int length = Math.Clamp(editor.SelectionLength, 0, current.Length - start);
        return current.Remove(start, length).Insert(start, insertedText);
    }

    private NumericComboBoxInputMode GetNumericComboBoxInputMode(ComboBox dropdown)
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

    private static bool IsValidNumericComboBoxText(string text, NumericComboBoxInputMode mode)
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

    private void Settings_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureFreshTwitchAuthorizationState();
        ResetTwitchAuthorizationButton();
        try
        {
            if (AppHelpers.GetParentBot(this) is null)
            {
                return;
            }

            CheckMinigameCooldownItems();
            CheckGlobalCooldownItems();
            CheckEconomyItems();
            CheckSelectedSettingsItems();
            CheckAdditionalSettingsItems();

            BotConfig config = ConfigurationStore.Load();

            _initializing = true;
            LoadSettingsIntoControls(config.Settings, config.Server);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSettingsLoadFailed(this, ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void Settings_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelRamSaveDebounce();
        CancelAndReplaceTwitchAuthorization();
        ResetTwitchAuthorizationButton();
    }

    private void EnsureFreshTwitchAuthorizationState()
    {
        if (!_tokenAuthorizationCts.IsCancellationRequested)
            return;

        _tokenAuthorizationCts.Dispose();
        _tokenAuthorizationCts = new CancellationTokenSource();
    }

    private void CancelAndReplaceTwitchAuthorization()
    {
        CancellationTokenSource previous = _tokenAuthorizationCts;
        _tokenAuthorizationCts = new CancellationTokenSource();
        try
        {
            previous.Cancel();
        }
        finally
        {
            previous.Dispose();
        }
    }

    private void ResetTwitchAuthorizationButton()
    {
        AuthorizeTwitchButton.IsEnabled = true;
        AuthorizeTwitchButton.Content = "Authorize Twitch";
    }

    private void GameplayCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(GameplaySettingsPage, GameplayCategoryButton, SettingsCategory.Gameplay);

    private void CommandsCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(CommandsSettingsPage, CommandsCategoryButton, SettingsCategory.Commands);

    private void CustomCommandsCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(CustomCommandsSettingsPage, CustomCommandsCategoryButton, SettingsCategory.CustomCommands);

    private void EconomyCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(EconomySettingsPage, EconomyCategoryButton, SettingsCategory.Economy);

    private void ChatDisplayCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(ChatDisplaySettingsPage, ChatDisplayCategoryButton, SettingsCategory.ChatDisplay);

    private void PerformanceCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(PerformanceSettingsPage, PerformanceCategoryButton, SettingsCategory.Performance);

    private void ServerCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(ServerSettingsPage, ServerCategoryButton, SettingsCategory.Server);

    private void DangerousCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(DangerousSettingsPage, DangerousCategoryButton, SettingsCategory.Dangerous);

    private void ShowSettingsPage(Grid page, Button selectedButton, SettingsCategory category)
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

    private void LoadSettingsIntoControls(StartingProfile settings, ServerConfig server)
    {
        MinigamesCheckbox.IsChecked = settings.MinigamesEnabled;
        string cooldownText = settings.MinigameCooldown.ToString();
        MinigameCooldownDropdown.SelectedItem = cooldownText;
        MinigameCooldownDropdown.Text = cooldownText;
        PassiveTokensCheckbox.IsChecked = settings.PassiveTokenEarningEnabled;
        ResponseVerbosityDropdown.SelectedItem = ConfigurationStore.NormalizeBotResponseVerbosity(settings.BotResponseVerbosity);
        SetCommandCostMultiplierDropdown(settings.CommandCostMultiplier);
        FollowRewardsCheckbox.IsChecked = settings.AutomaticFollowRewardsEnabled;
        SetFollowRewardAmountDropdown(settings.FollowRewardAmount);
        UpdateFollowRewardAmountEnabled(settings.AutomaticFollowRewardsEnabled);
        BitRewardsCheckbox.IsChecked = settings.AutomaticBitRewardsEnabled;
        NonCommandChatRelayCheckbox.IsChecked = settings.NonCommandChatRelayEnabled;
        ModeratorCommandsCheckbox.IsChecked = settings.ModeratorsCanUseStreamerCommands;
        GlobalCooldownCheckbox.IsChecked = settings.GlobalGameCommandCooldownEnabled;
        SetGlobalCooldownSecondsDropdown(settings.GlobalGameCommandCooldownSeconds);
        UpdateGlobalCooldownSecondsVisibility(settings.GlobalGameCommandCooldownEnabled);
        StatisticsEnabledCheckbox.IsChecked = settings.StatisticsEnabled;
        PVPCheckbox.IsChecked = settings.MultiplayerPVPEnabled;
        HardcoreCheckbox.IsChecked = settings.HardcoreEnabled;
        SelectDifficulty(settings.Difficulty);
        MinRamTextBox.Text = server.MemoryMinGB.ToString();
        MaxRamTextBox.Text = server.MemoryMaxGB.ToString();
        LoadSelectedSettingsIntoControls(settings);
        LoadAdditionalSettingsIntoControls(settings);
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MinRamTextBox.Text, out int minRam) &&
            int.TryParse(MaxRamTextBox.Text, out int maxRam) &&
            minRam > maxRam)
        {
            ErrorHandling.ShowRamValuesWillNotSave(this);
            return;
        }

        if (TryGetRamValues(out int validMinRAM, out int validMaxRAM))
        {
            CancelRamSaveDebounce();
            await UpdateConfigAsync(config =>
            {
                config.Server.MemoryMinGB = validMinRAM;
                config.Server.MemoryMaxGB = validMaxRAM;
            });
        }

        CancelAndReplaceTwitchAuthorization();
        ResetTwitchAuthorizationButton();
        AppHelpers.NavigateBack(this);
    }

    private async void AuthorizeTwitchButton_Click(object sender, RoutedEventArgs e)
    {
        CancellationTokenSource authorizationCts = _tokenAuthorizationCts;
        try
        {
            EnsureFreshTwitchAuthorizationState();
            authorizationCts = _tokenAuthorizationCts;

            BotConfig current = ConfigurationStore.Load();
            string clientId = current.Twitch.ClientID.Trim();
            if (clientId.Length == 0)
            {
                ErrorHandling.ShowTwitchClientIdRequired(this);
                return;
            }

            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Waiting For Twitch...";

            TwitchOAuthResult result = await TwitchOAuthAuthorizer.AuthorizeAsync(clientId, authorizationCts.Token);
            if (!result.IsSuccess)
            {
                ErrorHandling.ShowTwitchAuthorizationFailed(this, result.Error);
                return;
            }

            ConfigurationStore.Update(config =>
            {
                config.Twitch.BotToken = result.Token;
                config.Twitch.RefreshToken = result.RefreshToken;
                config.Twitch.BotName = result.Login;
            });
            ErrorHandling.ShowTwitchAuthorizationSucceeded(this, result.Login, savedToConfig: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowTwitchAuthorizationFailed(this, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_tokenAuthorizationCts, authorizationCts))
                ResetTwitchAuthorizationButton();
        }
    }

    private void DeleteConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppHelpers.GetParentBot(this) is not TwitchCraftBot parent)
            {
                ErrorHandling.ShowMainWindowNotFound(this);
                return;
            }

            if (!ErrorHandling.ConfirmDeleteConfig(this))
            {
                return;
            }

            ConfigurationStore.DeleteConfigFiles();
            parent.RestartAfterConfigDelete();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowDeleteConfigFailed(this, ex);
        }
    }

    private async void MinigamesCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            MinigamesCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.MinigamesEnabled = enabled,
            refreshMinigameLoops: true);

    private async void MinigameCooldownDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        string selected = (MinigameCooldownDropdown.SelectedItem as string
            ?? MinigameCooldownDropdown.Text
            ?? string.Empty).Trim();
        if (!int.TryParse(selected, out int minutes) || minutes < 2 || minutes > 30)
        {
            return;
        }

        await UpdateConfigAsync(config => config.Settings.MinigameCooldown = minutes);
    }

    private async void PassiveTokensCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            PassiveTokensCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.PassiveTokenEarningEnabled = enabled);

    private async void ResponseVerbosityDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && ResponseVerbosityDropdown.SelectedItem is string verbosity)
            await UpdateConfigAsync(config => config.Settings.BotResponseVerbosity = verbosity);
    }

    private async void CommandCostMultiplierDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && CommandCostMultiplierDropdown.SelectedItem is string)
            await SaveCommandCostMultiplierAsync();
    }

    private async void CommandCostMultiplierDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveCommandCostMultiplierAsync();

    private async Task SaveCommandCostMultiplierAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryGetEditableDoubleOption(CommandCostMultiplierDropdown, CommandCostMultiplierOptions, 0.0, 5.0, out double multiplier))
        {
            SetCommandCostMultiplierDropdown(ConfigurationStore.Load().Settings.CommandCostMultiplier);
            return;
        }

        SetCommandCostMultiplierDropdown(multiplier);
        await UpdateConfigAsync(config => config.Settings.CommandCostMultiplier = multiplier);
    }

    private async void FollowRewardsCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = FollowRewardsCheckbox.IsChecked == true;
        UpdateFollowRewardAmountEnabled(enabled);
        await UpdateBoolSettingIfReadyAsync(
            enabled,
            static (config, value) => config.Settings.AutomaticFollowRewardsEnabled = value);
    }

    private async void FollowRewardAmountDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && FollowRewardAmountDropdown.SelectedItem is string)
            await SaveFollowRewardAmountAsync();
    }

    private async void FollowRewardAmountDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveFollowRewardAmountAsync();

    private async Task SaveFollowRewardAmountAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryGetEditableInteger(FollowRewardAmountDropdown, 1, 1_000_000, out int amount))
        {
            SetFollowRewardAmountDropdown(ConfigurationStore.Load().Settings.FollowRewardAmount);
            return;
        }

        SetFollowRewardAmountDropdown(amount);
        await UpdateConfigAsync(config => config.Settings.FollowRewardAmount = amount);
    }

    private async void BitRewardsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            BitRewardsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.AutomaticBitRewardsEnabled = enabled);

    private async void NonCommandChatRelayCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            NonCommandChatRelayCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.NonCommandChatRelayEnabled = enabled);

    private async void ModeratorCommandsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            ModeratorCommandsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.ModeratorsCanUseStreamerCommands = enabled);

    private async void GlobalCooldownCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = GlobalCooldownCheckbox.IsChecked == true;
        if (enabled && !TryGetEditableDoubleOption(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, 0.1, 120.0, out _))
            SetGlobalCooldownSecondsDropdown(DefaultGlobalCooldownSeconds);

        UpdateGlobalCooldownSecondsVisibility(enabled);
        if (!_initializing)
            await UpdateConfigAsync(config => config.Settings.GlobalGameCommandCooldownEnabled = enabled);
    }

    private async void GlobalCooldownSecondsDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && !_updatingCustomValueControls && GlobalCooldownSecondsDropdown.SelectedItem is string)
            await SaveGlobalCooldownSecondsAsync();
    }

    private async void GlobalCooldownSecondsDropdown_LostFocus(object sender, RoutedEventArgs e)
        => await SaveGlobalCooldownSecondsAsync();

    private async Task SaveGlobalCooldownSecondsAsync()
    {
        if (_initializing || _updatingCustomValueControls)
            return;

        if (!TryGetEditableDoubleOption(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, 0.1, 120.0, out double seconds))
        {
            SetGlobalCooldownSecondsDropdown(ConfigurationStore.Load().Settings.GlobalGameCommandCooldownSeconds);
            return;
        }

        SetGlobalCooldownSecondsDropdown(seconds);
        await UpdateConfigAsync(config => config.Settings.GlobalGameCommandCooldownSeconds = seconds);
    }

    private async void StatisticsEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            StatisticsEnabledCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.StatisticsEnabled = enabled);

    private async void ResetStats_Click(object sender, RoutedEventArgs e)
    {
        if (!ErrorHandling.ConfirmResetStatistics(this) || AppHelpers.GetParentBot(this) is not TwitchCraftBot parent)
            return;

        Button? button = sender as Button;
        button?.IsEnabled = false;

        try
        {
            await parent.ResetStatisticsAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetStatisticsFailed(this, ex);
        }
        finally
        {
            button?.IsEnabled = true;
        }
    }

    private async void PVPCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveGameplaySettingsAsync();

    private async void HardcoreCheckbox_Changed(object sender, RoutedEventArgs e)
        => await SaveGameplaySettingsAsync();

    private async void DifficultyDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await SaveGameplaySettingsAsync();

    private async Task SaveGameplaySettingsAsync()
    {
        if (_initializing || DifficultyDropdown == null)
        {
            return;
        }

        bool PVPEnabled = PVPCheckbox.IsChecked == true;
        bool hardcoreEnabled = HardcoreCheckbox.IsChecked != false;
        string difficulty = ConfigurationStore.NormalizeDifficulty((DifficultyDropdown.SelectedItem as ComboBoxItem)?.Content as string);

        await UpdateConfigAsync(
            config =>
            {
                config.Settings.MultiplayerPVPEnabled = PVPEnabled;
                config.Settings.HardcoreEnabled = hardcoreEnabled;
                config.Settings.Difficulty = difficulty;
            },
            beforeSave: ApplyLocalStartProfile);
    }

    private static void ApplyLocalStartProfile(BotConfig config)
    {
        if (!config.Settings.RemoteControlEnabled)
            ServerPropertyEditor.ApplyStartProfile(config);
    }

    private async void RamTextBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _ramSaveDebounceCts?.Cancel();
        CancellationTokenSource debounceCts = new();
        _ramSaveDebounceCts = debounceCts;

        try
        {
            await Task.Delay(500, debounceCts.Token);

            if (!ReferenceEquals(_ramSaveDebounceCts, debounceCts) || !TryGetRamValues(out int minRam, out int maxRam))
            {
                return;
            }

            await UpdateConfigAsync(config =>
            {
                config.Server.MemoryMinGB = minRam;
                config.Server.MemoryMaxGB = maxRam;
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_ramSaveDebounceCts, debounceCts))
            {
                _ramSaveDebounceCts = null;
            }

            debounceCts.Dispose();
        }
    }

    private async void ResetCurrentCategory_Click(object sender, RoutedEventArgs e)
    {
        SettingsCategory category = _currentCategory;
        if (!ErrorHandling.ConfirmResetCategory(this, GetCategoryDisplayName(category)))
            return;

        StartingProfile defaults = new();
        ServerConfig defaultServer = new();
        CancelRamSaveDebounce();

        Action<BotConfig>? beforeSave = category is SettingsCategory.Gameplay or SettingsCategory.Server
            ? ApplyLocalStartProfile
            : null;
        await UpdateConfigAsync(
            config => ResetCategoryToDefaults(category, defaults, defaultServer, config),
            beforeSave: beforeSave,
            refreshMinigameLoops: category == SettingsCategory.Gameplay);

        ReloadSettingsAfterReset();
    }

    private void ReloadSettingsAfterReset()
    {
        try
        {
            BotConfig saved = ConfigurationStore.Load();
            _initializing = true;
            LoadSettingsIntoControls(saved.Settings, saved.Server);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsFailed(this, ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private static string GetCategoryDisplayName(SettingsCategory category) => category switch
    {
        SettingsCategory.CustomCommands => "Custom Commands",
        SettingsCategory.ChatDisplay => "Chat & Display",
        SettingsCategory.Performance => "Performance & Data",
        SettingsCategory.Server => "Minecraft Server",
        _ => category.ToString()
    };

    private static void ResetCategoryToDefaults(
        SettingsCategory category,
        StartingProfile defaults,
        ServerConfig defaultServer,
        BotConfig config)
    {
        StartingProfile settings = config.Settings;
        switch (category)
        {
            case SettingsCategory.Commands:
                settings.CommandPrefix = defaults.CommandPrefix;
                settings.SecondaryCommandPrefix = defaults.SecondaryCommandPrefix;
                settings.ViewerCommandsPaused = defaults.ViewerCommandsPaused;
                settings.ModeratorsCanUseStreamerCommands = defaults.ModeratorsCanUseStreamerCommands;
                settings.ViewerCommandLimitPerMinute = defaults.ViewerCommandLimitPerMinute;
                settings.ChannelCommandLimitPerMinute = defaults.ChannelCommandLimitPerMinute;
                settings.GlobalGameCommandCooldownEnabled = defaults.GlobalGameCommandCooldownEnabled;
                settings.GlobalGameCommandCooldownSeconds = defaults.GlobalGameCommandCooldownSeconds;
                settings.ShowExactCooldownRemaining = defaults.ShowExactCooldownRemaining;
                settings.BotResponseVerbosity = defaults.BotResponseVerbosity;
                settings.RespondToUnknownCommands = defaults.RespondToUnknownCommands;
                settings.MentionViewersInBotReplies = defaults.MentionViewersInBotReplies;
                break;

            case SettingsCategory.CustomCommands:
                settings.CommandCustomizations.Clear();
                break;

            case SettingsCategory.Economy:
                settings.PassiveTokenEarningEnabled = defaults.PassiveTokenEarningEnabled;
                settings.PassiveTokensPerPayout = defaults.PassiveTokensPerPayout;
                settings.PassiveTokenPayoutMinimumSeconds = defaults.PassiveTokenPayoutMinimumSeconds;
                settings.PassiveTokenPayoutMaximumSeconds = defaults.PassiveTokenPayoutMaximumSeconds;
                settings.PassiveRewardsRequireRecentChat = defaults.PassiveRewardsRequireRecentChat;
                settings.PassiveRecentChatWindowMinutes = defaults.PassiveRecentChatWindowMinutes;
                settings.MaximumTokenBalance = defaults.MaximumTokenBalance;
                settings.AutomaticFollowRewardsEnabled = defaults.AutomaticFollowRewardsEnabled;
                settings.FollowRewardAmount = defaults.FollowRewardAmount;
                settings.AutomaticBitRewardsEnabled = defaults.AutomaticBitRewardsEnabled;
                settings.CommandCostMultiplier = defaults.CommandCostMultiplier;
                break;

            case SettingsCategory.Gameplay:
                settings.MinigamesEnabled = defaults.MinigamesEnabled;
                settings.MinigameCooldown = defaults.MinigameCooldown;
                settings.HardcoreEnabled = defaults.HardcoreEnabled;
                settings.Difficulty = defaults.Difficulty;
                settings.MultiplayerPVPEnabled = defaults.MultiplayerPVPEnabled;
                settings.AllowAllPlayerTarget = defaults.AllowAllPlayerTarget;
                settings.AllowRandomPlayerTarget = defaults.AllowRandomPlayerTarget;
                break;

            case SettingsCategory.ChatDisplay:
                settings.NonCommandChatRelayEnabled = defaults.NonCommandChatRelayEnabled;
                settings.IncludeRelayTimestamps = defaults.IncludeRelayTimestamps;
                settings.MinecraftRelayTextColor = defaults.MinecraftRelayTextColor;
                settings.MinecraftRelayMessagesPerSecond = defaults.MinecraftRelayMessagesPerSecond;
                settings.ShowConnectionHealth = defaults.ShowConnectionHealth;
                break;

            case SettingsCategory.Performance:
                settings.LowResourceModeEnabled = defaults.LowResourceModeEnabled;
                settings.PauseUIUpdatesWhenMinimized = defaults.PauseUIUpdatesWhenMinimized;
                settings.ViewerRosterRefreshIntervalSeconds = defaults.ViewerRosterRefreshIntervalSeconds;
                settings.MaxVisibleTwitchLogLines = defaults.MaxVisibleTwitchLogLines;
                settings.MaxVisibleMinecraftLogLines = defaults.MaxVisibleMinecraftLogLines;
                settings.MaxGameplayCommandQueue = defaults.MaxGameplayCommandQueue;
                settings.StatisticsEnabled = defaults.StatisticsEnabled;
                settings.SQLiteOptimizeIntervalHours = defaults.SQLiteOptimizeIntervalHours;
                settings.AutomaticBackupsEnabled = defaults.AutomaticBackupsEnabled;
                settings.AutomaticBackupIntervalHours = defaults.AutomaticBackupIntervalHours;
                settings.AutomaticBackupRetentionCount = defaults.AutomaticBackupRetentionCount;
                break;

            case SettingsCategory.Server:
                settings.ViewDistance = defaults.ViewDistance;
                settings.SimulationDistance = defaults.SimulationDistance;
                settings.EntityBroadcastRangePercentage = defaults.EntityBroadcastRangePercentage;
                settings.NetworkCompressionThreshold = defaults.NetworkCompressionThreshold;
                settings.RCONTimeoutSeconds = defaults.RCONTimeoutSeconds;
                settings.GracefulShutdownTimeoutSeconds = defaults.GracefulShutdownTimeoutSeconds;
                settings.EmptyServerShutdownDelayMinutes = defaults.EmptyServerShutdownDelayMinutes;
                break;

            case SettingsCategory.Dangerous:
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
                break;
        }
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        StartingProfile defaults = new();
        ServerConfig defaultServer = new();

        try
        {
            if (AppHelpers.GetParentBot(this) is null || !ErrorHandling.ConfirmResetDefaults(this))
                return;

            CancelRamSaveDebounce();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsFailed(this, ex);
            return;
        }

        await UpdateConfigAsync(
            config =>
            {
                config.Settings.MinigamesEnabled = defaults.MinigamesEnabled;
                config.Settings.MinigameCooldown = defaults.MinigameCooldown;
                config.Settings.PassiveTokenEarningEnabled = defaults.PassiveTokenEarningEnabled;
                config.Settings.AutomaticFollowRewardsEnabled = defaults.AutomaticFollowRewardsEnabled;
                config.Settings.FollowRewardAmount = defaults.FollowRewardAmount;
                config.Settings.AutomaticBitRewardsEnabled = defaults.AutomaticBitRewardsEnabled;
                config.Settings.CommandCostMultiplier = defaults.CommandCostMultiplier;
                config.Settings.BotResponseVerbosity = defaults.BotResponseVerbosity;
                config.Settings.NonCommandChatRelayEnabled = defaults.NonCommandChatRelayEnabled;
                config.Settings.ModeratorsCanUseStreamerCommands = defaults.ModeratorsCanUseStreamerCommands;
                config.Settings.GlobalGameCommandCooldownEnabled = defaults.GlobalGameCommandCooldownEnabled;
                config.Settings.GlobalGameCommandCooldownSeconds = defaults.GlobalGameCommandCooldownSeconds;
                config.Settings.StatisticsEnabled = defaults.StatisticsEnabled;
                config.Settings.MultiplayerPVPEnabled = defaults.MultiplayerPVPEnabled;
                config.Settings.HardcoreEnabled = defaults.HardcoreEnabled;
                config.Settings.Difficulty = defaults.Difficulty;
                CopySelectedSettings(defaults, config.Settings);
                CopyAdditionalSettings(defaults, config.Settings);
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
            },
            beforeSave: ApplyLocalStartProfile,
            refreshMinigameLoops: true);

        ReloadSettingsAfterReset();
    }

    private Task UpdateBoolSettingIfReadyAsync(bool enabled, Action<BotConfig, bool> update, bool refreshMinigameLoops = false)
        => _initializing
            ? Task.CompletedTask
            : UpdateConfigAsync(config => update(config, enabled), refreshMinigameLoops: refreshMinigameLoops);

    private async Task UpdateConfigAsync(Action<BotConfig> update, Action<BotConfig>? beforeSave = null, bool refreshMinigameLoops = false)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
            if (parent is null)
            {
                return;
            }

            bool activeMultiplayerEnabled = parent.Runtime.MultiplayerEnabled;
            bool activeRemoteControlEnabled = parent.Runtime.RemoteControlEnabled;
            bool activeRequireOnlineMode = parent.Runtime.RequireOnlineMode;

            BotConfig savedConfig = await Task.Run(() => ConfigurationStore.Update(config =>
            {
                update(config);
                config.Settings.MultiplayerEnabled = activeMultiplayerEnabled;
                config.Settings.RemoteControlEnabled = activeRemoteControlEnabled;
                config.Settings.RequireOnlineMode = activeRequireOnlineMode;
                beforeSave?.Invoke(config);
            }));

            await parent.Runtime.ApplySavedConfigAsync(savedConfig, refreshMinigameLoops);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSaveSettingsFailed(this, ex);
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void CheckMinigameCooldownItems()
    {
        if (MinigameCooldownDropdown.Items.Count > 0)
        {
            return;
        }

        for (int i = 2; i <= 30; i++)
        {
            MinigameCooldownDropdown.Items.Add(i.ToString());
        }
    }

    private void CheckGlobalCooldownItems()
    {
        if (GlobalCooldownSecondsDropdown.Items.Count > 0)
        {
            return;
        }

        foreach ((double _, string label) in GlobalCooldownOptions)
            GlobalCooldownSecondsDropdown.Items.Add(label);
    }

    private void CheckEconomyItems()
    {
        if (ResponseVerbosityDropdown.Items.Count == 0)
            foreach (string option in ResponseVerbosityOptions)
                ResponseVerbosityDropdown.Items.Add(option);

        if (CommandCostMultiplierDropdown.Items.Count == 0)
            foreach ((double _, string label) in CommandCostMultiplierOptions)
                CommandCostMultiplierDropdown.Items.Add(label);

        if (FollowRewardAmountDropdown.Items.Count == 0)
            foreach (int amount in FollowRewardAmountOptions)
                FollowRewardAmountDropdown.Items.Add(amount.ToString());
    }

    private void SetCommandCostMultiplierDropdown(double multiplier)
        => SetEditableDoubleValue(CommandCostMultiplierDropdown, CommandCostMultiplierOptions, multiplier);

    private void SetFollowRewardAmountDropdown(int amount)
        => SetEditableTextValue(
            FollowRewardAmountDropdown,
            amount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void UpdateFollowRewardAmountEnabled(bool enabled)
    {
        FollowRewardAmountDropdown?.IsEnabled = enabled;
    }

    private void SetGlobalCooldownSecondsDropdown(double seconds)
        => SetEditableDoubleValue(GlobalCooldownSecondsDropdown, GlobalCooldownOptions, seconds);

    private void UpdateGlobalCooldownSecondsVisibility(bool visible)
        => GlobalCooldownSecondsDropdown.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private bool TryGetRamValues(out int minRam, out int maxRam)
    {
        minRam = 0;
        maxRam = 0;

        string minText = (MinRamTextBox.Text ?? string.Empty).Trim();
        string maxText = (MaxRamTextBox.Text ?? string.Empty).Trim();

        if (!int.TryParse(minText, out int parsedMin) || !int.TryParse(maxText, out int parsedMax))
        {
            return false;
        }

        if (parsedMin <= 0 || parsedMax <= 0 || parsedMin > parsedMax || parsedMax > MaxRamGB)
        {
            return false;
        }

        minRam = parsedMin;
        maxRam = parsedMax;
        return true;
    }

    private void SelectDifficulty(string? difficulty)
    {
        DifficultyDropdown.SelectedIndex = ConfigurationStore.NormalizeDifficulty(difficulty) switch
        {
            "Easy" => 0,
            "Hard" => 2,
            _ => 1
        };
    }
}
