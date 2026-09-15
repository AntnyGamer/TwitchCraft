using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1.Frames;

public partial class Start : UserControl
{
    private bool _showMinecraftUsernameUntilStart;
    private bool _launchClickInProgress;
    private bool _worldImportInProgress;
    private bool _remoteControllerUnlocked;
    private bool? _multiplayerBeforeRemoteControl;

    private static bool IsDigits(string text)
    {
        foreach (char c in text)
            if (!char.IsAsciiDigit(c))
                return false;
        return true;
    }

    public Start()
    {
        InitializeComponent();
        TwitchCraftVersion.Text = "Version: " + AppHelpers.DisplayVersion;
        Focusable = true;
        MCUserTextbox.TextChanged += (_, _) => UpdateLaunchUI();
        RemoteHostTextbox.TextChanged += (_, _) => UpdateLaunchUI();
        RemoteRCONPortTextbox.TextChanged += (_, _) => UpdateLaunchUI();
        RemoteRCONPortTextbox.PreviewTextInput += RCONPort_PreviewTextInput;
        DataObject.AddPastingHandler(RemoteRCONPortTextbox, RCONPort_Pasting);
        RemoteRCONPasswordBox.PasswordChanged += (_, _) => UpdateLaunchUI();
        RemoteRCONPasswordTextbox.TextChanged += (_, _) => UpdateLaunchUI();
        RemoteRCONPasswordShowCheckbox.Checked += ShowRCONPassword_Changed;
        RemoteRCONPasswordShowCheckbox.Unchecked += ShowRCONPassword_Changed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateLaunchUI();
    }

    private void RCONPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsDigits(e.Text);

    private void RCONPort_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.Text) is not string text || !IsDigits(text))
            e.CancelCommand();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Window? window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewKeyDown -= Start_PreviewKeyDown;
            window.PreviewKeyDown += Start_PreviewKeyDown;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
        {
            window.PreviewKeyDown -= Start_PreviewKeyDown;
        }
    }

    private void Start_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || e.Handled)
            return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool pressedRemoteShortcut = key == Key.R
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        if (!pressedRemoteShortcut)
        {
            return;
        }

        ClearRCONPassword();
        if (!_remoteControllerUnlocked)
        {
            _multiplayerBeforeRemoteControl = MultiplayerCheckbox.IsChecked == true;
            _remoteControllerUnlocked = true;
            MultiplayerCheckbox.IsChecked = false;
        }
        else
        {
            _remoteControllerUnlocked = false;
            RemoteRCONPasswordShowCheckbox.IsChecked = false;
            if (_multiplayerBeforeRemoteControl.HasValue)
                MultiplayerCheckbox.IsChecked = _multiplayerBeforeRemoteControl.Value;
            _multiplayerBeforeRemoteControl = null;
        }

        e.Handled = true;
        UpdateLaunchUI();
    }

    public void RefreshConfig(TwitchCraftConfig? config = null)
    {
        try
        {
            if (AppHelpers.GetTwitchCraftWindow(this) == null)
            {
                MCVersion.Text = "Couldn't open the main window.";
                MCUserTextbox.Text = string.Empty;
                return;
            }

            config ??= ConfigurationStore.Load();
            string minecraftVersion = string.IsNullOrWhiteSpace(config.Server.MinecraftVersion)
                ? "(not configured)"
                : config.Server.MinecraftVersion.Trim();

            MCVersion.Text = "Minecraft " + minecraftVersion;
            _remoteControllerUnlocked = false;
            _multiplayerBeforeRemoteControl = null;
            MultiplayerCheckbox.IsChecked = false;
            OnlineModeCheckbox.IsChecked = true;
            if (!string.Equals(RemoteHostTextbox.Text, config.Server.RemoteHost, StringComparison.Ordinal))
                RemoteHostTextbox.Text = config.Server.RemoteHost;
            string RCONPortText = config.Server.RCON.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(RemoteRCONPortTextbox.Text, RCONPortText, StringComparison.Ordinal))
                RemoteRCONPortTextbox.Text = RCONPortText;
            ClearRCONPassword();
            if (!string.Equals(MCUserTextbox.Text, config.Identity.StreamerMinecraftName, StringComparison.Ordinal))
                MCUserTextbox.Text = config.Identity.StreamerMinecraftName;
            _showMinecraftUsernameUntilStart = !MinecraftNameHelper.IsValidPlayerName(MCUserTextbox.Text);
            UpdateLaunchUI();
        }
        catch
        {
            MCVersion.Text = "Config found, but it could not be read.";
            MCUserTextbox.Text = string.Empty;
            _showMinecraftUsernameUntilStart = true;
            UpdateLaunchUI();
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_launchClickInProgress)
        {
            return;
        }

        _launchClickInProgress = true;
        UpdateLaunchUI();

        try
        {
            if (!ApplyLaunch())
            {
                return;
            }

            TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
            if (parent == null)
            {
                ErrorHandling.ShowStartWindowError(this);
                return;
            }

            if (!_remoteControllerUnlocked && MultiplayerCheckbox.IsChecked == true)
            {
                TwitchCraftConfig config = ConfigurationStore.Load();
                config.Settings.MultiplayerEnabled = true;
                DatapackInstaller.SyncLocateDatapack(config);
            }

            await parent.StartAsync();
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowStartupError(this, "Failed to start TwitchCraft.\n\n" + ex.Message);
        }
        finally
        {
            _launchClickInProgress = false;
            UpdateLaunchUI();
        }
    }

    private async void ImportWorld_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_launchClickInProgress || _worldImportInProgress)
                return;
            TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
            if (parent == null)
            {
                ErrorHandling.ShowImportWindowError(this);
                return;
            }

            TwitchCraftConfig config = ConfigurationStore.Load();
            if (_remoteControllerUnlocked)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
            {
                ErrorHandling.ShowSetupRequired(this);
                return;
            }

            string? selectedWorldPath = AppHelpers.FindWorldFolder();
            if (string.IsNullOrWhiteSpace(selectedWorldPath))
            {
                return;
            }

            if (!MinecraftWorldImporter.IsWorldFolder(selectedWorldPath))
            {
                ErrorHandling.ShowWorldFolderError(this, selectedWorldPath);
                return;
            }

            MinecraftWorldImportPlan importPlan = MinecraftWorldImporter.CreateImportPlan(config, selectedWorldPath);
            if (importPlan.SourceIsCurrentWorld)
            {
                ErrorHandling.ShowWorldLoaded(this);
                return;
            }

            if (importPlan.DestinationExists && !ErrorHandling.ConfirmOverwrite(this))
            {
                return;
            }

            bool multiplayerEnabled = MultiplayerCheckbox.IsChecked == true;
            bool requireOnlineMode = !multiplayerEnabled || OnlineModeCheckbox.IsChecked == true;
            config.Settings.MultiplayerEnabled = multiplayerEnabled;
            config.Settings.RemoteControlEnabled = false;
            config.Settings.RequireOnlineMode = requireOnlineMode;
            _worldImportInProgress = true;
            UpdateLaunchUI();
            ImportWorldButton.ToolTip = "World import is in progress.";
            ImportWorldButton.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    MinecraftWorldImporter.ReplaceWorld(importPlan, () =>
                    {
                        if (multiplayerEnabled)
                            DatapackInstaller.SyncLocateDatapack(importPlan.ServerDirectory, config.Server.MinecraftVersion, importPlan.LevelName);
                        ServerPropertyEditor.ApplyProfile(config);
                    });
                });
            }
            finally
            {
                _worldImportInProgress = false;
                ImportWorldButton.IsEnabled = true;
                ImportWorldButton.ToolTip = null;
                UpdateLaunchUI();
            }

            ErrorHandling.ShowImportSuccess(this);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowImportError(this, ex);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTwitchCraftWindow(out TwitchCraft parent))
        {
            parent.ShowSettings();
            e.Handled = true;
        }
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTwitchCraftWindow(out TwitchCraft parent))
        {
            parent.ShowStatistics();
            e.Handled = true;
        }
    }

    private bool TryGetTwitchCraftWindow(out TwitchCraft parent)
    {
        parent = AppHelpers.GetTwitchCraftWindow(this)!;
        if (parent != null)
            return true;

        ErrorHandling.ShowNavigationError(this);
        return false;
    }

    private void Multiplayer_Changed(object sender, RoutedEventArgs e)
    {
        UpdateLaunchUI();
    }

    private static void SetTopMargin(FrameworkElement element, double top)
    {
        Thickness margin = element.Margin;
        element.Margin = new Thickness(margin.Left, top, margin.Right, margin.Bottom);
    }

    private void UpdateLaunchUI()
    {
        bool remoteControlEnabled = _remoteControllerUnlocked;
        if (remoteControlEnabled && MultiplayerCheckbox.IsChecked == true)
            MultiplayerCheckbox.IsChecked = false;

        bool multiplayerEnabled = !remoteControlEnabled && MultiplayerCheckbox.IsChecked == true;
        string minecraftUser = (MCUserTextbox.Text ?? string.Empty).Trim();
        bool usernameIsValid = MinecraftNameHelper.IsValidPlayerName(minecraftUser);
        bool showMinecraftUser = multiplayerEnabled || remoteControlEnabled || _showMinecraftUsernameUntilStart || !usernameIsValid;
        OnlineMode.Visibility = multiplayerEnabled ? Visibility.Visible : Visibility.Collapsed;
        OnlineModeCheckbox.Visibility = multiplayerEnabled ? Visibility.Visible : Visibility.Collapsed;
        OnlineModeCheckbox.IsEnabled = multiplayerEnabled;

        if (!multiplayerEnabled || OnlineModeCheckbox.IsChecked == null)
            OnlineModeCheckbox.IsChecked = true;
        Visibility multiplayerVisibility = remoteControlEnabled ? Visibility.Collapsed : Visibility.Visible;
        Multiplayer.Visibility = multiplayerVisibility;
        MultiplayerCheckbox.Visibility = multiplayerVisibility;

        Visibility remoteVisibility = remoteControlEnabled ? Visibility.Visible : Visibility.Collapsed;
        RemoteControlStatus.Visibility = remoteVisibility;
        RemoteControlHelp.Visibility = remoteVisibility;
        RemoteHost.Visibility = remoteVisibility;
        RemoteHostTextbox.Visibility = remoteVisibility;
        RemoteRCONPort.Visibility = remoteVisibility;
        RemoteRCONPortTextbox.Visibility = remoteVisibility;
        RemoteRCONPassword.Visibility = remoteVisibility;
        RemoteRCONPasswordShowCheckbox.Visibility = remoteVisibility;
        UpdateRCONPassword(remoteControlEnabled);
        ImportWorldButton.Visibility = remoteControlEnabled ? Visibility.Collapsed : Visibility.Visible;
        SetTopMargin(MCUser, remoteControlEnabled ? 270 : 212);
        SetTopMargin(MCUserTextbox, remoteControlEnabled ? 270 : 212);
        SetTopMargin(StartButton, remoteControlEnabled ? 332 : 305);
        SetTopMargin(StartButtonDisabledOverlay, remoteControlEnabled ? 332 : 305);

        MCUser.Visibility = showMinecraftUser ? Visibility.Visible : Visibility.Collapsed;
        MCUserTextbox.Visibility = showMinecraftUser ? Visibility.Visible : Visibility.Collapsed;
        string? disabledReason = GetDisabledReason(minecraftUser, usernameIsValid);
        bool canStart = disabledReason == null;
        StartButton.IsEnabled = canStart;
        StartButton.ToolTip = disabledReason;
        StartButtonDisabledOverlay.Visibility = canStart ? Visibility.Collapsed : Visibility.Visible;
        StartButtonDisabledOverlay.ToolTip = disabledReason;
    }

    private string? GetDisabledReason(string minecraftUser, bool usernameIsValid)
    {
        if (_launchClickInProgress)
        {
            return "TwitchCraft is already starting.";
        }

        if (_worldImportInProgress)
        {
            return "Wait for the world import to finish before starting.";
        }

        if (!usernameIsValid)
        {
            return string.IsNullOrWhiteSpace(minecraftUser)
                ? "Enter your Minecraft username before starting."
                : "Enter a valid Minecraft username before starting.";
        }

        if (_remoteControllerUnlocked)
        {
            string remoteHost = RemoteHostTextbox.Text;
            if (string.IsNullOrWhiteSpace(remoteHost))
                return "Enter the host server address for Remote Control Mode.";

            if (!ConfigurationStore.IsValidRemoteHost(remoteHost))
                return "Enter a valid host server address for Remote Control Mode.";
            if (!TryGetRCONPort(out _))
                return "Enter a valid RCON port from 1 to 65535.";

            if (string.IsNullOrWhiteSpace(GetRCONPassword()))
                return "Enter the host server RCON password.";
        }

        return null;
    }

    private bool ApplyLaunch()
    {
        TwitchCraft? parent = AppHelpers.GetTwitchCraftWindow(this);
        if (parent == null)
        {
            ErrorHandling.ShowSettingsWindowError(this);
            return false;
        }

        bool remoteControlEnabled = _remoteControllerUnlocked;
        if (remoteControlEnabled && MultiplayerCheckbox.IsChecked == true)
            MultiplayerCheckbox.IsChecked = false;

        bool multiplayerEnabled = !remoteControlEnabled && MultiplayerCheckbox.IsChecked == true;
        bool requireOnlineMode = !multiplayerEnabled || OnlineModeCheckbox.IsChecked == true;
        string minecraftUser = (MCUserTextbox.Text ?? string.Empty).Trim();
        if (minecraftUser.Length == 0)
        {
            _showMinecraftUsernameUntilStart = true;
            UpdateLaunchUI();
            MCUserTextbox.Focus();
            ErrorHandling.ShowMissingMinecraftName(this);
            return false;
        }

        if (!MinecraftNameHelper.IsValidPlayerName(minecraftUser))
        {
            _showMinecraftUsernameUntilStart = true;
            UpdateLaunchUI();
            MCUserTextbox.Focus();
            ErrorHandling.ShowMinecraftNameError(this);
            return false;
        }

        try
        {
            int RCONPort = TryGetRCONPort(out int parsedRCONPort) ? parsedRCONPort : 25575;
            parent.Runtime.ApplyProfile(
                multiplayerEnabled,
                requireOnlineMode,
                minecraftUser,
                remoteControlEnabled,
                RemoteHostTextbox.Text,
                RCONPort,
                GetRCONPassword());
            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowSettingsUpdateError(this, ex);
            return false;
        }
    }

    private bool TryGetRCONPort(out int port)
    {
        return int.TryParse(RemoteRCONPortTextbox.Text.AsSpan().Trim(), out port)
            && port is >= 1 and <= 65535;
    }

    private string GetRCONPassword()
    {
        return RemoteRCONPasswordShowCheckbox.IsChecked == true
            ? RemoteRCONPasswordTextbox.Text ?? string.Empty
            : RemoteRCONPasswordBox.Password ?? string.Empty;
    }

    private void ClearRCONPassword()
    {
        RemoteRCONPasswordBox.Clear();
        RemoteRCONPasswordTextbox.Clear();
    }

    private void ShowRCONPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (RemoteRCONPasswordShowCheckbox.IsChecked == true)
        {
            RemoteRCONPasswordTextbox.Text = RemoteRCONPasswordBox.Password ?? string.Empty;
        }
        else
        {
            RemoteRCONPasswordBox.Password = RemoteRCONPasswordTextbox.Text ?? string.Empty;
            RemoteRCONPasswordTextbox.Clear();
        }

        UpdateRCONPassword(_remoteControllerUnlocked);
        UpdateLaunchUI();
    }

    private void UpdateRCONPassword(bool remoteControlEnabled)
    {
        if (!remoteControlEnabled)
        {
            RemoteRCONPasswordTextbox.Visibility = Visibility.Collapsed;
            RemoteRCONPasswordBox.Visibility = Visibility.Collapsed;
            return;
        }

        if (RemoteRCONPasswordShowCheckbox.IsChecked == true)
        {
            RemoteRCONPasswordBox.Visibility = Visibility.Collapsed;
            RemoteRCONPasswordTextbox.Visibility = Visibility.Visible;
        }
        else
        {
            RemoteRCONPasswordTextbox.Visibility = Visibility.Collapsed;
            RemoteRCONPasswordBox.Visibility = Visibility.Visible;
        }
    }
}
