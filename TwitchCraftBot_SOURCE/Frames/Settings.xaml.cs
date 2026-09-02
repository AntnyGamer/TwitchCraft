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
    private CancellationTokenSource? _tokenAuthorizationCts;
    private bool _hasSavedTwitchAuthorization;

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
        DataObject.AddPastingHandler(MinRamTextBox, Ram_Pasting);
        DataObject.AddPastingHandler(MaxRamTextBox, Ram_Pasting);
        AddPrefixOptions(CommandPrefixTextBox);
        AddPrefixOptions(SecondaryCommandPrefixTextBox);

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
            SetupNumericBox(dropdown);

        ShowPage(CommandsSettingsPage, CommandsCategoryButton, SettingsCategory.Commands);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void CancelRamSave()
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

    private void Ram_PreviewTextInput(object sender, TextCompositionEventArgs args)
    {
        if (sender is not TextBox textBox)
        {
            args.Handled = true;
            return;
        }

        args.Handled = !IsValidRamText(BuildTextCandidate(textBox, args.Text));
    }

    private void Ram_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (sender is not TextBox textBox ||
            args.DataObject.GetData(typeof(string)) is not string pastedText ||
            !IsValidRamText(BuildTextCandidate(textBox, pastedText)))
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

    private static bool IsValidRamText(string candidate)
    {
        if (!IsAsciiDigitsOnly(candidate))
            return false;

        return int.TryParse(candidate, out int value) && value <= MaxRamGB;
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

        string current = editor.Text ?? string.Empty;
        int start = Math.Clamp(editor.SelectionStart, 0, current.Length);
        int length = Math.Clamp(editor.SelectionLength, 0, current.Length - start);
        return current.Remove(start, length).Insert(start, insertedText);
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hasSavedTwitchAuthorization = false;
        ResetAuthButton();
        try
        {
            BotConfig config = ConfigurationStore.Load();
            _hasSavedTwitchAuthorization = HasTwitchAuth(config);
            ResetAuthButton();

            if (AppHelpers.GetBotWindow(this) is null)
                return;

            AddMinigameOptions();
            AddCooldownOptions();
            AddEconomyOptions();
            AddMainOptions();
            AddExtraOptions();

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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelRamSave();
        CancelAuthorization();
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
        AuthorizeTwitchButton.Content = _tokenAuthorizationCts != null
            ? "Cancel Authorization"
            : _hasSavedTwitchAuthorization
                ? "Reauthorize Twitch"
                : "Authorize Twitch";
    }

    private static bool HasTwitchAuth(BotConfig config)
        => !string.IsNullOrWhiteSpace(config.Twitch.BotToken)
            && string.Equals(
                (config.Twitch.ClientID ?? string.Empty).Trim(),
                TwitchOAuthAuthorizer.TwitchCraftClientId,
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
        string cooldownText = settings.MinigameCooldown.ToString();
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
        MinRamTextBox.Text = server.MemoryMinGB.ToString();
        MaxRamTextBox.Text = server.MemoryMaxGB.ToString();
        LoadMainSettings(settings);
        LoadExtraSettings(settings);
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MinRamTextBox.Text, out int minRam) &&
            int.TryParse(MaxRamTextBox.Text, out int maxRam) &&
            minRam > maxRam)
        {
            ErrorHandling.ShowInvalidRam(this);
            return;
        }

        if (TryGetRam(out int validMinRAM, out int validMaxRAM))
        {
            CancelRamSave();
            await SaveConfigAsync(config =>
            {
                config.Server.MemoryMinGB = validMinRAM;
                config.Server.MemoryMaxGB = validMaxRAM;
            });
        }

        CancelAuthorization();
        AppHelpers.NavigateBack(this);
    }

    private async void AuthorizeTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_tokenAuthorizationCts != null)
        {
            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Canceling...";
            _tokenAuthorizationCts.Cancel();
            return;
        }

        if (!TwitchOAuthAuthorizer.TwitchCraftOAuthConfigured)
        {
            ErrorHandling.ShowAuthError(this, "This TwitchCraft build is missing TwitchCraft's public Twitch Client ID. The release maintainer must add it before publishing the build.");
            return;
        }

        using CancellationTokenSource authorizationCts = new();
        _tokenAuthorizationCts = authorizationCts;
        ResetAuthButton();
        try
        {
            TwitchOAuthResult result = await TwitchOAuthAuthorizer.AuthorizeAsync(TwitchOAuthAuthorizer.TwitchCraftClientId, authorizationCts.Token);
            if (!ReferenceEquals(_tokenAuthorizationCts, authorizationCts))
                return;
            if (!result.IsSuccess)
            {
                ErrorHandling.ShowAuthError(this, result.Error);
                return;
            }

            BotConfig updated = ConfigurationStore.Update(config =>
            {
                config.Twitch.ClientID = TwitchOAuthAuthorizer.TwitchCraftClientId;
                config.Twitch.BotToken = result.Token;
                config.Twitch.RefreshToken = result.RefreshToken;
                config.Twitch.BotName = result.Login;
            });
            _hasSavedTwitchAuthorization = true;
            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Applying Twitch...";
            if (AppHelpers.GetBotWindow(this) is TwitchCraftBot parent)
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
            if (AppHelpers.GetBotWindow(this) is not TwitchCraftBot parent)
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
        if (!int.TryParse(selected, out int minutes) || minutes < 2 || minutes > 30)
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
            SetCostMultiplier(ConfigurationStore.Load().Settings.CommandCostMultiplier);
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
            SetFollowReward(ConfigurationStore.Load().Settings.FollowRewardAmount);
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
            SetGlobalCooldown(ConfigurationStore.Load().Settings.GlobalGameCommandCooldownSeconds);
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
        if (!ErrorHandling.ConfirmStatsReset(this) || AppHelpers.GetBotWindow(this) is not TwitchCraftBot parent)
            return;

        Button? button = sender as Button;
        button?.IsEnabled = false;

        try
        {
            await parent.ResetStatisticsAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowStatsResetError(this, ex);
        }
        finally
        {
            button?.IsEnabled = true;
        }
    }

    private async void Pvp_Changed(object sender, RoutedEventArgs e)
        => await SaveGameplayAsync();

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

    private static void ApplyLocalProfile(BotConfig config)
    {
        if (!config.Settings.RemoteControlEnabled)
            ServerPropertyEditor.ApplyProfile(config);
    }

    private async void Ram_Changed(object sender, TextChangedEventArgs e)
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

            if (!ReferenceEquals(_ramSaveDebounceCts, debounceCts) || !TryGetRam(out int minRam, out int maxRam))
            {
                return;
            }

            await SaveConfigAsync(config =>
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

    private async void ResetCategory_Click(object sender, RoutedEventArgs e)
    {
        SettingsCategory category = _currentCategory;
        if (!ErrorHandling.ConfirmResetCategory(this, GetCategoryName(category)))
            return;

        StartingProfile defaults = new();
        ServerConfig defaultServer = new();
        CancelRamSave();

        Action<BotConfig>? beforeSave = category is SettingsCategory.Gameplay or SettingsCategory.Server
            ? ApplyLocalProfile
            : null;
        await SaveConfigAsync(
            config => ResetCategory(category, defaults, defaultServer, config),
            beforeSave: beforeSave,
            refreshMinigameLoops: category == SettingsCategory.Gameplay);

        ReloadAfterReset();
    }

    private void ReloadAfterReset()
    {
        try
        {
            BotConfig saved = ConfigurationStore.Load();
            _initializing = true;
            LoadSettings(saved.Settings, saved.Server);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsError(this, ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private static string GetCategoryName(SettingsCategory category) => category switch
    {
        SettingsCategory.CustomCommands => "Custom Commands",
        SettingsCategory.ChatDisplay => "Chat & Display",
        SettingsCategory.Performance => "Performance & Data",
        SettingsCategory.Server => "Minecraft Server",
        _ => category.ToString()
    };

    private static void ResetCategory(
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
            if (AppHelpers.GetBotWindow(this) is null || !ErrorHandling.ConfirmResetDefaults(this))
                return;

            CancelRamSave();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsError(this, ex);
            return;
        }

        await SaveConfigAsync(
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
                CopyMainSettings(defaults, config.Settings);
                CopyExtraSettings(defaults, config.Settings);
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
            },
            beforeSave: ApplyLocalProfile,
            refreshMinigameLoops: true);

        ReloadAfterReset();
    }

    private Task UpdateBoolAsync(bool enabled, Action<BotConfig, bool> update, bool refreshMinigameLoops = false)
        => _initializing
            ? Task.CompletedTask
            : SaveConfigAsync(config => update(config, enabled), refreshMinigameLoops: refreshMinigameLoops);

    private async Task SaveConfigAsync(Action<BotConfig> update, Action<BotConfig>? beforeSave = null, bool refreshMinigameLoops = false)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            TwitchCraftBot? parent = AppHelpers.GetBotWindow(this);
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

            await parent.Runtime.ApplySettingsAsync(savedConfig, refreshMinigameLoops, preserveTwitchAuth: true);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSaveSettingsError(this, ex);
        }
        finally
        {
            _settingsSaveGate.Release();
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
            MinigameCooldownDropdown.Items.Add(i.ToString());
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
                FollowRewardAmountDropdown.Items.Add(amount.ToString());
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

    private bool TryGetRam(out int minRam, out int maxRam)
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
