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

    private bool _initializing = true;
    private SettingsCategory _currentCategory = SettingsCategory.Commands;
    private bool _updatingCustomValueControls;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly (Grid Page, Button Button)[] _categoryControls;
    private CancellationTokenSource? _RAMSaveDebounceCts;
    private CancellationTokenSource? _tokenAuthorizationCts;
    private bool _hasSavedTwitchAuthorization;
    private int _prefixSaveVersion;
    private string _savedCommandPrefix = "!";
    private string _savedSecondaryCommandPrefix = string.Empty;
    private string _savedRCONPassword = string.Empty;

    public Settings()
    {
        InitializeComponent();
        _categoryControls =
        [
            (CommandsSettingsPage, CommandsCategoryButton),
            (CustomCommandsSettingsPage, CustomCommandsCategoryButton),
            (GameplaySettingsPage, GameplayCategoryButton),
            (EconomySettingsPage, EconomyCategoryButton),
            (ChatDisplaySettingsPage, ChatDisplayCategoryButton),
            (PerformanceSettingsPage, PerformanceCategoryButton),
            (ServerSettingsPage, ServerCategoryButton),
            (DangerousSettingsPage, DangerousCategoryButton)
        ];
        AddMinigameOptions();
        AddOptions(GlobalCooldownSecondsDropdown, GlobalCooldownOptions);
        AddEconomyOptions();
        AddMainOptions();
        AddExtraOptions();

        DataObject.AddPastingHandler(MinRAMTextBox, RAM_Pasting);
        DataObject.AddPastingHandler(MaxRAMTextBox, RAM_Pasting);
        AddPrefixOptions(CommandPrefixTextBox);
        AddPrefixOptions(SecondaryCommandPrefixTextBox);

        ReadOnlySpan<ComboBox> numericDropdowns =
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
        {
            SetupNumericBox(dropdown);
        }

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

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            ReloadSettings();
        }
        else
        {
            HideSettings();
        }
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
            {
                return;
            }

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
        foreach (var (categoryPage, _) in _categoryControls)
        {
            categoryPage.Visibility = Visibility.Collapsed;
        }

        page.Visibility = Visibility.Visible;

        foreach (var (_, categoryButton) in _categoryControls)
        {
            categoryButton.FontWeight = FontWeights.Normal;
        }

        selectedButton.FontWeight = FontWeights.Bold;

        foreach (var (_, categoryButton) in _categoryControls)
        {
            categoryButton.Opacity = 0.78;
        }

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
        PvPCheckbox.IsChecked = settings.MultiplayerPvPEnabled;
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
            {
                return;
            }

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
            {
                await parent.Runtime.ApplySettingsAsync(updated);
            }

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

        ReadOnlySpan<char> selected = (MinigameCooldownDropdown.SelectedItem as string
            ?? MinigameCooldownDropdown.Text
            ?? string.Empty).AsSpan().Trim();
        int firstSpace = selected.IndexOf(' ');
        ReadOnlySpan<char> minutesText = firstSpace < 0 ? selected : selected[..firstSpace];
        if (!int.TryParse(minutesText, out int minutes) || minutes < 2 || minutes > 30)
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
        {
            await SaveConfigAsync(config => config.Settings.BotResponseVerbosity = verbosity);
        }
    }

    private async void CostMultiplier_Changed(object sender, RoutedEventArgs e)
    {
        if (ShouldSaveEditableValue(sender, e))
        {
            await SaveCostMultiplierAsync();
        }
    }

    private async Task SaveCostMultiplierAsync()
    {
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

    private async void FollowReward_Changed(object sender, RoutedEventArgs e)
    {
        if (ShouldSaveEditableValue(sender, e))
        {
            await SaveFollowRewardAsync();
        }
    }

    private async Task SaveFollowRewardAsync()
    {
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
        {
            SetGlobalCooldown(DefaultGlobalCooldownSeconds);
        }

        UpdateGlobalCooldown(enabled);
        if (!_initializing)
        {
            await SaveConfigAsync(config => config.Settings.GlobalGameCommandCooldownEnabled = enabled);
        }
    }

    private async void CooldownTime_Changed(object sender, RoutedEventArgs e)
    {
        if (ShouldSaveEditableValue(sender, e))
        {
            await SaveGlobalCooldownAsync();
        }
    }

    private async Task SaveGlobalCooldownAsync()
    {
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
        {
            return;
        }

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

    private async void PvP_Changed(object sender, RoutedEventArgs e)
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

        bool PvPEnabled = PvPCheckbox.IsChecked == true;
        bool hardcoreEnabled = HardcoreCheckbox.IsChecked != false;
        string difficulty = ConfigurationStore.NormalizeDifficulty((DifficultyDropdown.SelectedItem as ComboBoxItem)?.Content as string);

        await SaveConfigAsync(
            config =>
            {
                config.Settings.MultiplayerPvPEnabled = PvPEnabled;
                config.Settings.HardcoreEnabled = hardcoreEnabled;
                config.Settings.Difficulty = difficulty;
            },
            beforeSave: ApplyLocalProfile);
    }

    private static void ApplyLocalProfile(TwitchCraftConfig config)
    {
        if (!config.Settings.RemoteControlEnabled)
        {
            ServerPropertyEditor.ApplyProfile(config);
        }
    }

    private string GetRCONPassword() => RCONPasswordTextBox.Visibility == Visibility.Visible
        ? RCONPasswordTextBox.Text ?? string.Empty
        : RCONPasswordBox.Password ?? string.Empty;

    private void SetRCONPasswordInput(string password)
    {
        if (RCONPasswordTextBox.Visibility == Visibility.Visible)
        {
            RCONPasswordTextBox.Text = password;
        }
        else
        {
            RCONPasswordBox.Password = password;
        }
    }

    private void CopyRCONPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GetRCONPassword() is { Length: > 0 } password)
            {
                Clipboard.SetText(password);
            }
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("RCON password copy failed", ex);
        }
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

    private async void RCONPasswordEditor_FocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_initializing || RCONPasswordEditor.IsKeyboardFocusWithin)
        {
            return;
        }

        string currentPassword = GetRCONPassword();
        if (!ConfigurationStore.TryNormalizeRCONPassword(currentPassword, out string password))
        {
            if (!string.Equals(currentPassword, _savedRCONPassword, StringComparison.Ordinal))
            {
                ErrorHandling.ShowInvalidRCONPassword(this);
                SetRCONPasswordInput(_savedRCONPassword);
            }
            return;
        }

        if (string.Equals(password, _savedRCONPassword, StringComparison.Ordinal))
        {
            if (!string.Equals(currentPassword, password, StringComparison.Ordinal))
            {
                SetRCONPasswordInput(password);
            }
            return;
        }

        await _settingsSaveGate.WaitAsync();
        try
        {
            if (string.Equals(password, _savedRCONPassword, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Run(() => ConfigurationStore.Update(config => config.Server.RCON.Password = password));
            _savedRCONPassword = password;
            SetRCONPasswordInput(password);

            AppHelpers.GetTwitchCraftWindow(this)?.Runtime.StageLocalRCONPassword(password);
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

}
