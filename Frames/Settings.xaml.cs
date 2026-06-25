using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

public partial class Settings : UserControl
{
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

    private bool _initializing;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private CancellationTokenSource? _ramSaveDebounceCts;

    private static bool IsAsciiDigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        for (int i = 0; i < value.Length; i++)
            if ((uint)(value[i] - '0') > 9u)
                return false;

        return true;
    }

    public Settings()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(MinRamTextBox, NumericTextbox_Pasting);
        DataObject.AddPastingHandler(MaxRamTextBox, NumericTextbox_Pasting);
        ShowSettingsPage(GeneralSettingsPage, GeneralCategoryButton);
        Loaded += Settings_Loaded;
        Unloaded += (_, _) => CancelRamSaveDebounce();
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

    private void NumbersOnly_PreviewTextInput(object sender, TextCompositionEventArgs args)
        => args.Handled = !IsAsciiDigitsOnly(args.Text);

    private void NumericTextbox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!IsAsciiDigitsOnly(args.DataObject.GetData(typeof(string)) as string))
            args.CancelCommand();
    }

    private void Settings_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppHelpers.GetParentBot(this) is null)
            {
                return;
            }

            CheckMinigameCooldownItems();
            CheckGlobalCooldownItems();

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

    private void GeneralCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(GeneralSettingsPage, GeneralCategoryButton);

    private void GameplayCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(GameplaySettingsPage, GameplayCategoryButton);

    private void DangerousCategory_Click(object sender, RoutedEventArgs e)
        => ShowSettingsPage(DangerousSettingsPage, DangerousCategoryButton);

    private void ShowSettingsPage(Grid page, Button selectedButton)
    {
        GeneralSettingsPage.Visibility = Visibility.Collapsed;
        GameplaySettingsPage.Visibility = Visibility.Collapsed;
        DangerousSettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        GeneralCategoryButton.FontWeight = FontWeights.Normal;
        GameplayCategoryButton.FontWeight = FontWeights.Normal;
        DangerousCategoryButton.FontWeight = FontWeights.Normal;
        selectedButton.FontWeight = FontWeights.Bold;

        GeneralCategoryButton.Opacity = 0.78;
        GameplayCategoryButton.Opacity = 0.78;
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
        NonCommandChatTellrawsCheckbox.IsChecked = settings.NonCommandChatTellrawsEnabled;
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

        AppHelpers.NavigateBack(this);
    }

    private void OpenGetBotToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exeDirectory = AppHelpers.GetExecutableDirectory();
            string getBotTokenPath = Path.Combine(exeDirectory, "GetBotToken.exe");

            if (!File.Exists(getBotTokenPath))
            {
                ErrorHandling.ShowGetBotTokenNotFound(this, getBotTokenPath);
                return;
            }

            AppHelpers.OpenShellTarget(getBotTokenPath, exeDirectory);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowOpenGetBotTokenFailed(this, ex);
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

    private async void NonCommandChatTellrawsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            NonCommandChatTellrawsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.NonCommandChatTellrawsEnabled = enabled);

    private async void ModeratorCommandsCheckbox_Changed(object sender, RoutedEventArgs e)
        => await UpdateBoolSettingIfReadyAsync(
            ModeratorCommandsCheckbox.IsChecked == true,
            static (config, enabled) => config.Settings.ModeratorsCanUseStreamerCommands = enabled);

    private async void GlobalCooldownCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = GlobalCooldownCheckbox.IsChecked == true;
        if (enabled && GlobalCooldownSecondsDropdown.SelectedItem is not string)
            SetGlobalCooldownSecondsDropdown(DefaultGlobalCooldownSeconds);

        UpdateGlobalCooldownSecondsVisibility(enabled);
        if (!_initializing)
        {
            await UpdateConfigAsync(config =>
            {
                config.Settings.GlobalGameCommandCooldownEnabled = enabled;
                if (enabled && !IsValidGlobalCooldownSeconds(config.Settings.GlobalGameCommandCooldownSeconds))
                    config.Settings.GlobalGameCommandCooldownSeconds = DefaultGlobalCooldownSeconds;
            });
        }
    }

    private async void GlobalCooldownSecondsDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && TryGetSelectedGlobalCooldownSeconds(out double seconds))
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
        string difficulty = GetSelectedDifficulty();

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

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        StartingProfile defaults = new();
        ServerConfig defaultServer = new();

        try
        {
            if (AppHelpers.GetParentBot(this) is null)
            {
                return;
            }

            if (!ErrorHandling.ConfirmResetDefaults(this))
            {
                return;
            }

            CancelRamSaveDebounce();
            _initializing = true;
            LoadSettingsIntoControls(defaults, defaultServer);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowResetDefaultsFailed(this, ex);
            return;
        }
        finally
        {
            _initializing = false;
        }

        await UpdateConfigAsync(
            config =>
            {
                config.Settings.MinigamesEnabled = defaults.MinigamesEnabled;
                config.Settings.MinigameCooldown = defaults.MinigameCooldown;
                config.Settings.PassiveTokenEarningEnabled = defaults.PassiveTokenEarningEnabled;
                config.Settings.NonCommandChatTellrawsEnabled = defaults.NonCommandChatTellrawsEnabled;
                config.Settings.ModeratorsCanUseStreamerCommands = defaults.ModeratorsCanUseStreamerCommands;
                config.Settings.GlobalGameCommandCooldownEnabled = defaults.GlobalGameCommandCooldownEnabled;
                config.Settings.GlobalGameCommandCooldownSeconds = defaults.GlobalGameCommandCooldownSeconds;
                config.Settings.StatisticsEnabled = defaults.StatisticsEnabled;
                config.Settings.MultiplayerPVPEnabled = defaults.MultiplayerPVPEnabled;
                config.Settings.HardcoreEnabled = defaults.HardcoreEnabled;
                config.Settings.Difficulty = defaults.Difficulty;
                config.Server.MemoryMinGB = defaultServer.MemoryMinGB;
                config.Server.MemoryMaxGB = defaultServer.MemoryMaxGB;
            },
            beforeSave: ApplyLocalStartProfile,
            refreshMinigameLoops: true);
    }

    private async Task UpdateBoolSettingIfReadyAsync(bool enabled, Action<BotConfig, bool> update, bool refreshMinigameLoops = false)
    {
        if (!_initializing)
            await UpdateConfigAsync(config => update(config, enabled), refreshMinigameLoops: refreshMinigameLoops);
    }

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

            BotConfig savedConfig = await Task.Run(() =>
            {
                BotConfig config = ConfigurationStore.Load();
                update(config);
                config.Settings.MultiplayerEnabled = activeMultiplayerEnabled;
                config.Settings.RemoteControlEnabled = activeRemoteControlEnabled;
                config.Settings.RequireOnlineMode = activeRequireOnlineMode;
                beforeSave?.Invoke(config);
                ConfigurationStore.Save(config);
                return config;
            });

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

    private void SetGlobalCooldownSecondsDropdown(double seconds)
    {
        if (!TryGetGlobalCooldownLabel(seconds, out string label))
            _ = TryGetGlobalCooldownLabel(DefaultGlobalCooldownSeconds, out label);

        GlobalCooldownSecondsDropdown.SelectedItem = label;
    }

    private static bool IsValidGlobalCooldownSeconds(double seconds)
        => TryGetGlobalCooldownLabel(seconds, out _);

    private static bool TryGetGlobalCooldownLabel(double seconds, out string label)
    {
        foreach ((double optionSeconds, string optionLabel) in GlobalCooldownOptions)
        {
            if (seconds == optionSeconds)
            {
                label = optionLabel;
                return true;
            }
        }

        label = string.Empty;
        return false;
    }

    private bool TryGetSelectedGlobalCooldownSeconds(out double seconds)
    {
        if (GlobalCooldownSecondsDropdown.SelectedItem is string selectedLabel)
        {
            foreach ((double optionSeconds, string optionLabel) in GlobalCooldownOptions)
            {
                if (string.Equals(selectedLabel, optionLabel, StringComparison.Ordinal))
                {
                    seconds = optionSeconds;
                    return true;
                }
            }
        }

        seconds = 0.0;
        return false;
    }

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

    private string GetSelectedDifficulty()
        => ConfigurationStore.NormalizeDifficulty((DifficultyDropdown.SelectedItem as ComboBoxItem)?.Content as string);
}
