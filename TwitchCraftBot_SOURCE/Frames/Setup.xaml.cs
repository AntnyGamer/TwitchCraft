using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1.Frames;

public partial class Setup : UserControl
{
    private const string DefaultMinecraftVersion = "26.1.2";
    private const int SHA1HexLength = 40;
    private const int DownloadBufferSize = 256 * 1024;
    private const long MaxServerJarDownloadBytes = 1024L * 1024L * 1024L;

    private JObject? _manifest;
    private bool _manifestLoadAttempted;
    private bool _manifestMissingWarningShown;
    private bool _setupInProgress;
    private CancellationTokenSource? _authorizationCts;
    private string _botToken = string.Empty;
    private string _refreshToken = string.Empty;
    private string _authorizedClientId = string.Empty;
    private CancellationTokenSource _setupLifetimeCts = new();

    private sealed class VersionOption(string ID, string label, int requiredJDK)
    {
        public string ID { get; } = ID;
        public string Label { get; } = label;
        public string Group { get; } = $"JDK {requiredJDK} REQUIRED";
    }

    private static readonly VersionOption[] SupportedVersionOptions =
    [
        .. MinecraftVersionSupport.SupportedVersions.Select(version => new VersionOption(version.ID, version.DisplayID, version.RequiredJDK))
    ];

    private static readonly HttpClient SetupHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Setup()
    {
        InitializeComponent();
        MCBindIPTextbox.TextChanged += Field_Changed;
        TwitchUserTextbox.TextChanged += Field_Changed;
        BotUserTextbox.TextChanged += Field_Changed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        UpdateStartButton();
    }

    private static string GeneratePassword(int length)
    {
        if (length < 4)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 4.");

        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "123456789";
        const string symbols = "!@$%^&*-_=+?.";
        const string pool = lower + upper + digits + symbols;
        char[] result = new char[length];
        int pos = 0;

        result[pos++] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        result[pos++] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        result[pos++] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        result[pos++] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

        for (; pos < result.Length; pos++)
            result[pos] = pool[RandomNumberGenerator.GetInt32(pool.Length)];

        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_setupLifetimeCts.IsCancellationRequested)
        {
            _setupLifetimeCts.Dispose();
            _setupLifetimeCts = new();
        }

        try
        {
            await RefreshVersionsAsync(_setupLifetimeCts.Token);
            UpdateStartButton();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Setup load failed", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelAuthorization();
        _setupLifetimeCts.Cancel();
    }

    private void CancelAuthorization()
    {
        _authorizationCts?.Cancel();
        _authorizationCts = null;
    }

    private void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateJavaText();
        UpdateStartButton();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
        => UpdateStartButton();

    private async void AuthorizeTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_authorizationCts != null)
        {
            AuthorizeTwitchButton.IsEnabled = false;
            AuthorizeTwitchButton.Content = "Canceling...";
            _authorizationCts.Cancel();
            return;
        }

        string clientId = TwitchOAuthAuthorizer.TwitchCraftClientId;
        if (!TwitchOAuthAuthorizer.TwitchCraftOAuthConfigured)
        {
            ErrorHandling.ShowAuthError(this, "This TwitchCraft build is missing TwitchCraft's public Twitch Client ID. The release maintainer must add it before publishing the build.");
            return;
        }

        using CancellationTokenSource authorizationCts = new();
        _authorizationCts = authorizationCts;
        UpdateStartButton();
        try
        {
            TwitchOAuthResult result = await TwitchOAuthAuthorizer.AuthorizeAsync(clientId, authorizationCts.Token);
            if (!ReferenceEquals(_authorizationCts, authorizationCts))
                return;
            if (!result.IsSuccess)
            {
                ErrorHandling.ShowAuthError(this, result.Error);
                return;
            }

            _botToken = result.Token;
            _refreshToken = result.RefreshToken;
            _authorizedClientId = clientId;
            BotUserTextbox.Text = result.Login;
            ErrorHandling.ShowAuthSuccess(this, result.Login, savedToConfig: false);
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
            if (ReferenceEquals(_authorizationCts, authorizationCts))
            {
                _authorizationCts = null;
                UpdateStartButton();
            }
        }
    }

    private void UpdateStartButton()
    {
        string clientId = TwitchOAuthAuthorizer.TwitchCraftClientId;
        AuthorizeTwitchButton.IsEnabled = !_setupInProgress;
        if (_authorizationCts != null)
        {
            AuthorizeTwitchButton.Content = "Cancel Authorization";
        }
        else
        {
            bool isAuthorized = !string.IsNullOrWhiteSpace(_botToken)
                && string.Equals(clientId, _authorizedClientId, StringComparison.Ordinal);
            AuthorizeTwitchButton.Content = isAuthorized
                ? "Reauthorize Twitch"
                : "Authorize Twitch";
        }

        string? blockingReason = SetupInputValidator.GetBlockingReason(
            GetVersionId(MCVersionDropdown),
            ConfigurationStore.NormalizeBindIP(MCBindIPTextbox.Text),
            clientId,
            _authorizedClientId,
            _botToken,
            TwitchUserTextbox.Text,
            BotUserTextbox.Text);
        StartButton.IsEnabled = !_setupInProgress && _authorizationCts == null && blockingReason == null;
        StartButton.ToolTip = blockingReason;
    }

    private async Task LoadManifestAsync(CancellationToken cancellationToken)
    {
        if (_manifest != null || _manifestLoadAttempted)
            return;

        _manifestLoadAttempted = true;
        string path = Environment.ExpandEnvironmentVariables(@"%APPDATA%\.minecraft\versions\version_manifest_v2.json");

        try
        {
            if (File.Exists(path))
            {
                try
                {
                    JObject localManifest = JObject.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                    if (localManifest["versions"] is JArray)
                    {
                        _manifest = localManifest;
                        return;
                    }

                    ErrorHandling.LogNonFatal("Local Minecraft version manifest did not contain a valid versions list", null);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException)
                {
                    ErrorHandling.LogNonFatal("Local Minecraft version manifest could not be used", ex);
                }
            }
            else if (!_manifestMissingWarningShown)
            {
                _manifestMissingWarningShown = true;
                ErrorHandling.ShowMissingManifest(this, path);
            }

            await LoadOnlineManifestAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _manifestLoadAttempted = false;
            _manifest = null;
            ErrorHandling.ShowManifestError(this, ex);
        }
    }

    private async Task LoadOnlineManifestAsync(CancellationToken cancellationToken)
    {
        string json = await SetupHttpClient.GetStringAsync(new Uri("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json"), cancellationToken);
        _manifest = JObject.Parse(json);
        _manifestLoadAttempted = true;
    }

    private static string GetVersionId(ComboBox comboBox)
        => comboBox.SelectedItem is VersionOption option ? option.ID : (comboBox.Text ?? string.Empty).Trim();

    private void UpdateJavaText()
    {
        JavaRequirementText.Text = MCVersionDropdown.SelectedItem is VersionOption option ? option.Group : string.Empty;
    }

    private void SelectVersion(string versionID)
    {
        MCVersionDropdown.SelectedItem = string.IsNullOrWhiteSpace(versionID)
            ? null
            : MCVersionDropdown.Items.Cast<object>().FirstOrDefault(item =>
                item is VersionOption option
                    ? string.Equals(option.ID, versionID, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(option.Label, versionID, StringComparison.OrdinalIgnoreCase)
                    : item is string text && string.Equals(text, versionID, StringComparison.OrdinalIgnoreCase));

        UpdateJavaText();
    }

    private Task RefreshVersionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string selectedBeforeRefresh = GetVersionId(MCVersionDropdown);

        CollectionViewSource supportedVersions = new() { Source = SupportedVersionOptions };
        supportedVersions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VersionOption.Group)));

        MCVersionDropdown.ItemTemplate = (DataTemplate?)FindResource("VersionItemTemplate");
        MCVersionDropdown.DisplayMemberPath = string.Empty;
        MCVersionDropdown.SelectedValuePath = string.Empty;
        MCVersionDropdown.ItemsSource = supportedVersions.View;

        SelectVersion(selectedBeforeRefresh);
        if (MCVersionDropdown.SelectedItem == null)
            SelectVersion(DefaultMinecraftVersion);
        if (MCVersionDropdown.SelectedItem == null && MCVersionDropdown.Items.Count > 0)
            MCVersionDropdown.SelectedItem = MCVersionDropdown.Items[0];

        MCVersionDropdown.IsEnabled = true;
        UpdateJavaText();
        UpdateStartButton();
        return Task.CompletedTask;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        string selectedVersion = GetVersionId(MCVersionDropdown);
        string typedBindIP = MCBindIPTextbox.Text;
        string bindIP = ConfigurationStore.NormalizeBindIP(typedBindIP);
        string clientID = TwitchOAuthAuthorizer.TwitchCraftClientId;
        string botToken = _botToken.Trim();
        string refreshToken = _refreshToken.Trim();
        _ = CommandUserHelper.TryNormalizeTwitchUser(TwitchUserTextbox.Text, out string channel);
        _ = CommandUserHelper.TryNormalizeTwitchUser(BotUserTextbox.Text, out string botUser);

        if (!SetupInputValidator.CanStart(
                selectedVersion,
                bindIP,
                clientID,
                _authorizedClientId,
                botToken,
                channel,
                botUser))
        {
            ErrorHandling.ShowSetupIncomplete(this);
            return;
        }

        string minecraftVersion = MinecraftVersionSupport.GetVersion(selectedVersion).ID;

        if (ConfigurationStore.ShouldWarnAboutBindIP(typedBindIP) && ErrorHandling.ConfirmBindIpReset(this))
        {
            MCBindIPTextbox.Text = "127.0.0.1";
            UpdateStartButton();
            return;
        }

        if (!ConfigurationStore.IsValidBindIP(bindIP))
        {
            ErrorHandling.ShowInvalidBindIP(this, bindIP);
            return;
        }

        if (_setupInProgress)
            return;

        _setupInProgress = true;
        bool navigatedToStart = false;
        StartButton.Content = "Starting...";
        UpdateStartButton();

        try
        {
            CancellationToken cancellationToken = _setupLifetimeCts.Token;
            await ErrorHandling.RunSetupAsync(this, async () =>
            {
                if (_manifest == null)
                    await LoadManifestAsync(cancellationToken);

                if (_manifest?["versions"] is not JArray versions)
                    throw new InvalidOperationException("Minecraft version metadata could not be loaded.");

                JToken? version = versions.FirstOrDefault(v => string.Equals((string?)v["id"], minecraftVersion, StringComparison.OrdinalIgnoreCase));
                if (version == null)
                {
                    await LoadOnlineManifestAsync(cancellationToken);

                    if (_manifest?["versions"] is not JArray onlineVersions)
                        throw new InvalidOperationException("Minecraft version metadata could not be loaded from Mojang.");

                    version = onlineVersions.FirstOrDefault(v => string.Equals((string?)v["id"], minecraftVersion, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("The selected Minecraft version could not be found in Mojang's online manifest.");
                }

                string detailUrl = (string?)version["url"]
                    ?? throw new InvalidOperationException("The selected version did not include a detail manifest URL.");
                Uri detailUri = CreateHttpsUri(detailUrl, "Minecraft version detail manifest");

                TwitchCraftBot host = AppHelpers.GetBotWindow(this)
                    ?? throw new InvalidOperationException("Unable to find the main TwitchCraftBot window.");

                JObject detail = await LoadVersionAsync(minecraftVersion, detailUri, cancellationToken);
                JToken server = detail["downloads"]?["server"]
                    ?? throw new InvalidOperationException("This Minecraft version does not expose a downloadable server.jar.");

                string serverUrl = (string?)server["url"]
                    ?? throw new InvalidOperationException("The selected version did not include a server download URL.");
                string serverSha = (string?)server["sha1"]
                    ?? throw new InvalidOperationException("The selected version did not include a SHA-1 checksum.");
                long? serverSize = (long?)server["size"];
                if (!serverSize.HasValue || serverSize.Value <= 0)
                    serverSize = null;

                ConfigurationStore.EnsureWorkDir();
                string serverDir = Path.Combine(ConfigurationStore.WorkingDirectory, "MCServer");
                string jarPath = Path.Combine(serverDir, $"twitchcraft-server-{minecraftVersion}.jar");
                Directory.CreateDirectory(serverDir);

                int requiredJavaVersion = (int?)detail["javaVersion"]?["majorVersion"]
                    ?? MinecraftVersionSupport.GetVersion(minecraftVersion).RequiredJDK;
                (string javaExe, string javaHome) = await FindJavaAsync(requiredJavaVersion, cancellationToken);

                if (!ErrorHandling.ConfirmJarVerify(this, minecraftVersion))
                    return;

                await EnsureServerJarAsync(SetupHttpClient, serverUrl, jarPath, serverSha, serverSize, cancellationToken);
                ErrorHandling.ShowVerifySuccess(this);

                ServerPropertyEditor.CleanupServerJars(serverDir, jarPath);

                BotConfig config = BuildConfig(minecraftVersion, serverDir, jarPath, bindIP, javaExe, javaHome, clientID, botToken, refreshToken, channel, botUser);
                ServerPropertyEditor.WriteInitialFiles(config);
                ConfigurationStore.Save(config);
                host.ShowStart();
                navigatedToStart = true;
            });
        }
        finally
        {
            _setupInProgress = false;
            if (!navigatedToStart)
            {
                StartButton.Content = "Start";
                UpdateStartButton();
            }
        }
    }

    private static BotConfig BuildConfig(string minecraftVersion, string serverDir, string jarPath, string bindIP, string javaExe, string javaHome, string clientID, string botToken, string refreshToken, string channel, string botUser)
    {
        return new BotConfig
        {
            Server =
            {
                MinecraftVersion = minecraftVersion,
                ServerDirectory = serverDir,
                JarPath = jarPath,
                BindIP = bindIP,
                PreviousBindIP = bindIP,
                RCON = { Password = GeneratePassword(32) },
                Java = { ExecutablePath = javaExe, HomeDirectory = javaHome }
            },
            Twitch =
            {
                ClientID = clientID,
                BotToken = botToken,
                RefreshToken = refreshToken,
                StreamerName = channel,
                BotName = botUser
            }
        };
    }

    private async Task<(string JavaExe, string JavaHome)> FindJavaAsync(int javaVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string javaExe, string javaHome) = await Task.Run(() => JavaInstallationFinder.FindMatching(javaVersion), cancellationToken);
            if (!string.IsNullOrWhiteSpace(javaExe))
                return (javaExe, javaHome);

            if (!ErrorHandling.ConfirmJavaRetry(this, javaVersion))
                throw new OperationCanceledException("Setup cancelled by user (No Java).");
        }
    }

}
