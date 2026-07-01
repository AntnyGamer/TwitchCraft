using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

public partial class Start : UserControl
{
    private bool _showMinecraftUsernameUntilStart;
    private bool _launchClickInProgress;
    private bool _worldImportInProgress;
    private bool _remoteControllerUnlocked;
    private bool? _multiplayerBeforeRemoteControl;
    private bool _syncingRemoteRCONPassword;

    private static bool IsDigits(string text)
    {
        foreach (char c in text)
            if (c is < '0' or > '9')
                return false;
        return true;
    }

    public Start()
    {
        InitializeComponent();
        string? botVersion = typeof(Start).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        BotVersion.Text = "Bot Version: " + (botVersion ?? "unknown").Split('+', 2)[0];
        Focusable = true;
        MCUserTextbox.TextChanged += (_, _) => UpdateMultiplayerUi();
        RemoteHostTextbox.TextChanged += (_, _) => UpdateMultiplayerUi();
        RemoteRCONPortTextbox.TextChanged += (_, _) => UpdateMultiplayerUi();
        RemoteRCONPortTextbox.PreviewTextInput += RemoteRCONPortTextbox_PreviewTextInput;
        DataObject.AddPastingHandler(RemoteRCONPortTextbox, RemoteRCONPortTextbox_Pasting);
        RemoteRCONPasswordBox.PasswordChanged += (_, _) => UpdateMultiplayerUi();
        RemoteRCONPasswordTextbox.TextChanged += (_, _) => UpdateMultiplayerUi();
        RemoteRCONPasswordShowCheckbox.Checked += RemoteRCONPasswordShowCheckbox_Changed;
        RemoteRCONPasswordShowCheckbox.Unchecked += RemoteRCONPasswordShowCheckbox_Changed;
        Loaded += Start_Loaded;
        Unloaded += Start_Unloaded;
        UpdateMultiplayerUi();
    }

    private void RemoteRCONPortTextbox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsDigits(e.Text);

    private void RemoteRCONPortTextbox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.Text) is not string text || !IsDigits(text))
            e.CancelCommand();
    }

    private void Start_Loaded(object sender, RoutedEventArgs e)
    {
        Window? window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewKeyDown -= StartWindow_PreviewKeyDown;
            window.PreviewKeyDown += StartWindow_PreviewKeyDown;
        }
    }

    private void Start_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
        {
            window.PreviewKeyDown -= StartWindow_PreviewKeyDown;
        }
    }

    private void StartWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool pressedRemoteShortcut = key == Key.R
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        if (!pressedRemoteShortcut)
        {
            return;
        }

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
        UpdateMultiplayerUi();
    }

    public void RefreshFromConfig()
    {
        try
        {
            if (AppHelpers.GetParentBot(this) == null)
            {
                MCVersion.Text = "Main window was not found.";
                MCUserTextbox.Text = string.Empty;
                return;
            }

            BotConfig config = ConfigurationStore.Load();
            string minecraftVersion = string.IsNullOrWhiteSpace(config.Server.MinecraftVersion)
                ? "(unknown version)"
                : config.Server.MinecraftVersion.Trim();

            MCVersion.Text = "Minecraft " + minecraftVersion;
            _remoteControllerUnlocked = false;
            _multiplayerBeforeRemoteControl = null;
            MultiplayerCheckbox.IsChecked = false;
            OnlineModeCheckbox.IsChecked = true;
            if (!string.Equals(RemoteHostTextbox.Text, config.Server.RemoteHost, StringComparison.Ordinal))
                RemoteHostTextbox.Text = config.Server.RemoteHost;
            string RCONPortText = config.Server.RCON.Port.ToString();
            if (!string.Equals(RemoteRCONPortTextbox.Text, RCONPortText, StringComparison.Ordinal))
                RemoteRCONPortTextbox.Text = RCONPortText;
            if (!string.Equals(GetRemoteRCONPasswordText(), config.Server.RCON.Password, StringComparison.Ordinal))
                SetRemoteRCONPasswordText(config.Server.RCON.Password);
            if (!string.Equals(MCUserTextbox.Text, config.Identity.StreamerMinecraftName, StringComparison.Ordinal))
                MCUserTextbox.Text = config.Identity.StreamerMinecraftName;
            _showMinecraftUsernameUntilStart = !MinecraftNameHelper.IsValidPlayerName(MCUserTextbox.Text);

            UpdateMultiplayerUi();
        }
        catch
        {
            MCVersion.Text = "Config found, but it could not be read.";
            MCUserTextbox.Text = string.Empty;
            _showMinecraftUsernameUntilStart = true;
            UpdateMultiplayerUi();
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launchClickInProgress)
        {
            return;
        }

        _launchClickInProgress = true;
        UpdateMultiplayerUi();

        try
        {
            if (!ApplyLaunchSettingsToConfig())
            {
                return;
            }

            TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
            if (parent == null)
            {
                ErrorHandling.ShowStartWindowNotFound(this);
                return;
            }

            if (!_remoteControllerUnlocked && MultiplayerCheckbox.IsChecked == true)
            {
                BotConfig config = ConfigurationStore.Load();
                config.Settings.MultiplayerEnabled = true;
                DatapackInstaller.SyncLocatePlayersDatapack(config);
            }

            await parent.BeginLaunchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowStartupError(this, "Failed to start TwitchCraft.\n\n" + ex.Message);
        }
        finally
        {
            _launchClickInProgress = false;
            UpdateMultiplayerUi();
        }
    }

    private async void ImportWorldButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_launchClickInProgress || _worldImportInProgress)
                return;

            TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
            if (parent == null)
            {
                ErrorHandling.ShowImportWorldWindowNotFound(this);
                return;
            }

            BotConfig config = ConfigurationStore.Load();
            if (_remoteControllerUnlocked)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.Server.ServerDirectory))
            {
                ErrorHandling.ShowSetupRequiredBeforeImportWorld(this);
                return;
            }

            string? selectedWorldPath = LookForMinecraftWorldFolder();
            if (string.IsNullOrWhiteSpace(selectedWorldPath))
            {
                return;
            }

            if (!MinecraftWorldImporter.IsMinecraftWorldFolder(selectedWorldPath))
            {
                ErrorHandling.ShowInvalidWorldFolder(this, selectedWorldPath);
                return;
            }

            MinecraftWorldImportPlan importPlan = MinecraftWorldImporter.CreateImportPlan(config, selectedWorldPath);
            if (importPlan.SourceIsCurrentWorld)
            {
                ErrorHandling.ShowWorldAlreadyCurrent(this);
                return;
            }

            if (importPlan.DestinationExists && !ErrorHandling.ConfirmOverwriteExistingWorld(this))
            {
                return;
            }

            bool multiplayerEnabled = MultiplayerCheckbox.IsChecked == true;
            bool requireOnlineMode = !multiplayerEnabled || OnlineModeCheckbox.IsChecked == true;
            config.Settings.MultiplayerEnabled = multiplayerEnabled;
            config.Settings.RemoteControlEnabled = false;
            config.Settings.RequireOnlineMode = requireOnlineMode;

            _worldImportInProgress = true;
            UpdateMultiplayerUi();
            ImportWorldButton.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    MinecraftWorldImporter.ReplaceWorldSafely(importPlan);
                    if (multiplayerEnabled)
                        DatapackInstaller.SyncLocatePlayersDatapack(importPlan.ServerDirectory, config.Server.MinecraftVersion, importPlan.LevelName);
                    ServerPropertyEditor.ApplyStartProfile(config);
                });
            }
            finally
            {
                _worldImportInProgress = false;
                ImportWorldButton.IsEnabled = true;
                UpdateMultiplayerUi();
            }

            ErrorHandling.ShowWorldImportSucceeded(this);
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowWorldImportFailed(this, ex);
        }
    }

    private void Help_Click(object sender, MouseButtonEventArgs e)
    {
        if (TryGetNavigationParent(out TwitchCraftBot parent))
        {
            parent.NavigateToHelp();
            e.Handled = true;
        }
    }

    private void Stats_Click(object sender, MouseButtonEventArgs e)
    {
        if (TryGetNavigationParent(out TwitchCraftBot parent))
        {
            parent.NavigateToStatistics();
            e.Handled = true;
        }
    }

    private bool TryGetNavigationParent(out TwitchCraftBot parent)
    {
        parent = AppHelpers.GetParentBot(this)!;
        if (parent != null)
            return true;

        ErrorHandling.ShowNavigationWindowNotFound(this);
        return false;
    }

    private void MultiplayerCheckbox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateMultiplayerUi();
    }

    private static void SetLayoutTop(FrameworkElement element, double top)
    {
        Thickness margin = element.Margin;
        element.Margin = new Thickness(margin.Left, top, margin.Right, margin.Bottom);
    }

    private void UpdateMultiplayerUi()
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

        if (!multiplayerEnabled)
        {
            OnlineModeCheckbox.IsChecked = true;
        }
        else
        {
            OnlineModeCheckbox.IsChecked ??= true;
        }

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
        SyncRemoteRCONPasswordVisibility(remoteControlEnabled);
        ImportWorldButton.Visibility = remoteControlEnabled ? Visibility.Collapsed : Visibility.Visible;

        SetLayoutTop(MCUser, remoteControlEnabled ? 270 : 212);
        SetLayoutTop(MCUserTextbox, remoteControlEnabled ? 270 : 212);
        SetLayoutTop(StartButton, remoteControlEnabled ? 332 : 305);
        SetLayoutTop(StartButtonDisabledOverlay, remoteControlEnabled ? 332 : 305);

        MCUser.Visibility = showMinecraftUser ? Visibility.Visible : Visibility.Collapsed;
        MCUserTextbox.Visibility = showMinecraftUser ? Visibility.Visible : Visibility.Collapsed;

        string? disabledReason = GetStartButtonDisabledReason(minecraftUser, usernameIsValid);
        bool canStart = disabledReason == null;
        StartButton.IsEnabled = canStart;
        StartButton.ToolTip = disabledReason;
        StartButtonDisabledOverlay.Visibility = canStart ? Visibility.Collapsed : Visibility.Visible;
        StartButtonDisabledOverlay.ToolTip = disabledReason;
    }

    private string? GetStartButtonDisabledReason(string minecraftUser, bool usernameIsValid)
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
            string remoteHost = (RemoteHostTextbox.Text ?? string.Empty).Trim();
            if (remoteHost.Length == 0)
                return "Enter the host server address for Remote Control Mode.";

            if (!ConfigurationStore.IsValidRemoteHost(remoteHost))
                return "Enter a valid host server address for Remote Control Mode.";

            if (!TryGetRemoteRCONPort(out _))
                return "Enter a valid RCON port from 1 to 65535.";

            if (string.IsNullOrWhiteSpace(GetRemoteRCONPasswordText()))
                return "Enter the host server RCON password.";
        }

        return null;
    }

    private bool ApplyLaunchSettingsToConfig()
    {
        TwitchCraftBot? parent = AppHelpers.GetParentBot(this);
        if (parent == null)
        {
            ErrorHandling.ShowLaunchSettingsWindowNotFound(this);
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
            UpdateMultiplayerUi();
            MCUserTextbox.Focus();
            ErrorHandling.ShowMissingMinecraftUsername(this);
            return false;
        }

        if (!MinecraftNameHelper.IsValidPlayerName(minecraftUser))
        {
            _showMinecraftUsernameUntilStart = true;
            UpdateMultiplayerUi();
            MCUserTextbox.Focus();
            ErrorHandling.ShowInvalidMinecraftUsername(this);
            return false;
        }

        try
        {
            int RCONPort = TryGetRemoteRCONPort(out int parsedRCONPort) ? parsedRCONPort : 25575;
            parent.Runtime.ApplyStartProfile(
                multiplayerEnabled,
                requireOnlineMode,
                minecraftUser,
                remoteControlEnabled,
                RemoteHostTextbox.Text,
                RCONPort,
                GetRemoteRCONPasswordText());
            return true;
        }
        catch (Exception ex)
        {
            ErrorHandling.ShowLaunchSettingsUpdateFailed(this, ex);
            return false;
        }
    }

    private bool TryGetRemoteRCONPort(out int port)
    {
        return int.TryParse((RemoteRCONPortTextbox.Text ?? string.Empty).Trim(), out port)
            && port is >= 1 and <= 65535;
    }

    private string GetRemoteRCONPasswordText()
    {
        return RemoteRCONPasswordShowCheckbox.IsChecked == true
            ? RemoteRCONPasswordTextbox.Text ?? string.Empty
            : RemoteRCONPasswordBox.Password ?? string.Empty;
    }

    private void SetRemoteRCONPasswordText(string value)
    {
        if (RemoteRCONPasswordShowCheckbox.IsChecked == true)
            RemoteRCONPasswordTextbox.Text = value;
        else
            RemoteRCONPasswordBox.Password = value;
    }

    private void RemoteRCONPasswordShowCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncingRemoteRCONPassword)
        {
            _syncingRemoteRCONPassword = true;
            try
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
            }
            finally
            {
                _syncingRemoteRCONPassword = false;
            }
        }

        SyncRemoteRCONPasswordVisibility(_remoteControllerUnlocked);
        UpdateMultiplayerUi();
    }

    private void SyncRemoteRCONPasswordVisibility(bool remoteControlEnabled)
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

    private static string? LookForMinecraftWorldFolder()
    {
        string initialPath = GetPreferredWorldBrowserPath();

        OpenFolderDialog dialog = new()
        {
            Multiselect = false,
            Title = "Select a Minecraft world folder.",
            InitialDirectory = initialPath,
            DefaultDirectory = initialPath,
            FolderName = initialPath
        };

        bool? result = dialog.ShowDialog();
        if (result != true)
        {
            return null;
        }

        string selectedPath = dialog.FolderName ?? string.Empty;
        return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
    }

    private static string GetPreferredWorldBrowserPath()
    {
        string minecraftSavesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft",
            "saves");

        if (Directory.Exists(minecraftSavesPath))
        {
            return minecraftSavesPath;
        }

        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return Directory.Exists(downloadsPath)
            ? downloadsPath
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
