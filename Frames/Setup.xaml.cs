using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
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
    private CancellationTokenSource _setupLifetimeCts = new();
    private readonly SecretTextBoxController _clientIDSecret;
    private readonly SecretTextBoxController _botTokenSecret;

    private sealed class VersionOption(string ID, string label, int requiredJDK)
    {
        public string ID { get; } = ID;
        public string Label { get; } = label;
        public int RequiredJDK { get; } = requiredJDK;
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
        _clientIDSecret = new SecretTextBoxController(ClientIDPasswordBox, ClientIDTextbox, ClientIDCheckbox);
        _botTokenSecret = new SecretTextBoxController(BotTokenPasswordBox, BotTokenTextbox, BotTokenCheckbox);
        Loaded += Setup_Load;
        Unloaded += Setup_Unloaded;
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

    private async void Setup_Load(object sender, RoutedEventArgs e)
    {
        if (_setupLifetimeCts.IsCancellationRequested)
        {
            _setupLifetimeCts.Dispose();
            _setupLifetimeCts = new();
        }

        try
        {
            await RefreshVersionsAsync(_setupLifetimeCts.Token);
            _clientIDSecret.Hide();
            _botTokenSecret.Hide();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Setup load failed", ex);
        }
    }

    private void Setup_Unloaded(object sender, RoutedEventArgs e)
        => _setupLifetimeCts.Cancel();

    private async void MCVersionCheckbox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshVersionsAsync(_setupLifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorHandling.LogNonFatal("Minecraft version refresh failed", ex);
        }
    }

    private void MCVersionDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateJavaRequirementText();

    private void BotTokenHelpButton_Click(object sender, RoutedEventArgs e)
        => ErrorHandling.ShowBotTokenHelp(this);

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
                ErrorHandling.ShowLocalManifestNotFound(this, path);
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
            ErrorHandling.ShowManifestLoadFailed(this, ex);
        }
    }

    private async Task LoadOnlineManifestAsync(CancellationToken cancellationToken)
    {
        string json = await SetupHttpClient.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json", cancellationToken);
        _manifest = JObject.Parse(json);
        _manifestLoadAttempted = true;
    }

    private static string GetSelectedVersionId(ComboBox comboBox)
        => comboBox.SelectedItem is VersionOption option ? option.ID : (comboBox.Text ?? string.Empty).Trim();

    private static string ResolveMinecraftVersionId(string versionID)
        => MinecraftVersionSupport.TryGetVersion(versionID, out MinecraftVersionSupport.MinecraftVersionInfo resolvedVersion)
            ? resolvedVersion.ID
            : (versionID ?? string.Empty).Trim();

    private void UpdateJavaRequirementText()
    {
        JavaRequirementText.Text = MCVersionCheckbox.IsChecked == true && MCVersionDropdown.SelectedItem is VersionOption option
            ? option.Group
            : string.Empty;
    }

    private void SelectVersionById(string versionID)
    {
        MCVersionDropdown.SelectedItem = string.IsNullOrWhiteSpace(versionID)
            ? null
            : MCVersionDropdown.Items.Cast<object>().FirstOrDefault(item =>
                item is VersionOption option
                    ? string.Equals(option.ID, versionID, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(option.Label, versionID, StringComparison.OrdinalIgnoreCase)
                    : item is string text && string.Equals(text, versionID, StringComparison.OrdinalIgnoreCase));

        UpdateJavaRequirementText();
    }

    private async Task RefreshVersionsAsync(CancellationToken cancellationToken)
    {
        string selectedBeforeRefresh = GetSelectedVersionId(MCVersionDropdown);

        if (MCVersionCheckbox.IsChecked == true)
        {
            CollectionViewSource supportedVersions = new() { Source = SupportedVersionOptions };
            supportedVersions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VersionOption.Group)));

            MCVersionDropdown.ItemTemplate = (DataTemplate?)FindResource("VersionItemTemplate");
            MCVersionDropdown.DisplayMemberPath = string.Empty;
            MCVersionDropdown.SelectedValuePath = string.Empty;
            MCVersionDropdown.ItemsSource = supportedVersions.View;
        }
        else
        {
            await LoadManifestAsync(cancellationToken);

            if (_manifest?["versions"] is not JArray versions)
            {
                MCVersionDropdown.ItemsSource = null;
                MCVersionDropdown.SelectedItem = null;
                MCVersionDropdown.IsEnabled = false;
                UpdateJavaRequirementText();
                return;
            }

            MCVersionDropdown.ItemTemplate = null;
            MCVersionDropdown.DisplayMemberPath = string.Empty;
            MCVersionDropdown.SelectedValuePath = string.Empty;
            MCVersionDropdown.ItemsSource = versions.Select(v => (string?)v["id"]).OfType<string>().ToList();
        }

        SelectVersionById(selectedBeforeRefresh);

        if (MCVersionDropdown.SelectedItem == null)
            SelectVersionById(DefaultMinecraftVersion);

        if (MCVersionDropdown.SelectedItem == null && MCVersionDropdown.Items.Count > 0)
            MCVersionDropdown.SelectedItem = MCVersionDropdown.Items[0];

        MCVersionDropdown.IsEnabled = true;
        UpdateJavaRequirementText();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        string MCVersion = ResolveMinecraftVersionId(GetSelectedVersionId(MCVersionDropdown));
        string typedBindIP = MCBindIPTextbox.Text;
        string bindIP = ConfigurationStore.NormalizeBindIP(typedBindIP);
        string clientID = _clientIDSecret.Text.Trim();
        string botToken = _botTokenSecret.Text.Trim();
        string channel = (TwitchUserTextbox.Text ?? string.Empty).Trim().ToLowerInvariant();
        string botUser = (BotUserTextbox.Text ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(MCVersion) || string.IsNullOrWhiteSpace(bindIP) || botToken.Length == 0 || channel.Length == 0 || clientID.Length == 0)
        {
            ErrorHandling.ShowSetupRequiredFields(this);
            return;
        }

        if (ConfigurationStore.ShouldShowAdvancedBindIPWarning(typedBindIP) && ErrorHandling.ShowAdvancedBindIPWarning(this))
        {
            MCBindIPTextbox.Text = "127.0.0.1";
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
        StartButton.IsEnabled = false;
        StartButton.Content = "Starting...";

        try
        {
            CancellationToken cancellationToken = _setupLifetimeCts.Token;
            await ErrorHandling.RunSetupActionAsync(this, async () =>
            {
                if (_manifest == null)
                    await LoadManifestAsync(cancellationToken);

                if (_manifest?["versions"] is not JArray versions)
                    throw new InvalidOperationException("Minecraft version metadata could not be loaded.");

                JToken? version = versions.FirstOrDefault(v => string.Equals((string?)v["id"], MCVersion, StringComparison.OrdinalIgnoreCase));
                if (version == null)
                {
                    await LoadOnlineManifestAsync(cancellationToken);

                    if (_manifest?["versions"] is not JArray onlineVersions)
                        throw new InvalidOperationException("Minecraft version metadata could not be loaded from Mojang.");

                    version = onlineVersions.FirstOrDefault(v => string.Equals((string?)v["id"], MCVersion, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("The selected Minecraft version could not be found in Mojang's online manifest.");
                }

                string detailUrl = (string?)version["url"]
                    ?? throw new InvalidOperationException("The selected version did not include a detail manifest URL.");
                Uri detailUri = CreateHttpsUri(detailUrl, "Minecraft version detail manifest");

                TwitchCraftBot host = AppHelpers.GetParentBot(this)
                    ?? throw new InvalidOperationException("Unable to find the main TwitchCraftBot window.");

                JObject detail = await LoadVersionDetailAsync(MCVersion, detailUri, cancellationToken);
                JToken server = detail["downloads"]?["server"]
                    ?? throw new InvalidOperationException("This Minecraft version does not expose a downloadable server.jar.");

                string serverUrl = (string?)server["url"]
                    ?? throw new InvalidOperationException("The selected version did not include a server download URL.");
                string serverSha = (string?)server["sha1"]
                    ?? throw new InvalidOperationException("The selected version did not include a SHA-1 checksum.");

                ConfigurationStore.CheckRootFolder();
                string serverDir = Path.Combine(ConfigurationStore.WorkingDirectory, "MCServer");
                string jarPath = Path.Combine(serverDir, $"twitchcraft-server-{MCVersion}.jar");
                Directory.CreateDirectory(serverDir);

                int requiredJavaVersion = (int?)detail["javaVersion"]?["majorVersion"]
                    ?? (MinecraftVersionSupport.TryGetVersion(MCVersion, out MinecraftVersionSupport.MinecraftVersionInfo fallbackVersion) ? fallbackVersion.RequiredJDK : 17);
                (string javaExe, string javaHome) = await ResolveJavaExecutableAsync(requiredJavaVersion, cancellationToken);

                if (!ErrorHandling.ConfirmVerifyServerJar(this, MCVersion))
                    return;

                await CheckServerJarAsync(SetupHttpClient, serverUrl, jarPath, serverSha, cancellationToken);
                ErrorHandling.ShowVerificationMatched(this);

                ServerPropertyEditor.CleanupUnusedServerJars(serverDir, jarPath);

                BotConfig config = BuildConfig(MCVersion, serverDir, jarPath, bindIP, javaExe, javaHome, clientID, botToken, channel, botUser);
                ServerPropertyEditor.WriteInitialFiles(config);
                ConfigurationStore.Save(config);
                host.NavigateToStart();
                navigatedToStart = true;
            });
        }
        finally
        {
            _setupInProgress = false;
            if (!navigatedToStart)
            {
                StartButton.IsEnabled = true;
                StartButton.Content = "Start";
            }
        }
    }

    private static async Task<JObject> LoadVersionDetailAsync(string versionID, Uri detailUri, CancellationToken cancellationToken)
    {
        string localPath = Environment.ExpandEnvironmentVariables($@"%APPDATA%\.minecraft\versions\{versionID}\{versionID}.json");
        if (File.Exists(localPath))
        {
            try
            {
                JObject detail = JObject.Parse(await File.ReadAllTextAsync(localPath, cancellationToken));
                string? serverUrl = (string?)detail["downloads"]?["server"]?["url"];
                string? serverSha = (string?)detail["downloads"]?["server"]?["sha1"];
                if (string.Equals((string?)detail["id"], versionID, StringComparison.OrdinalIgnoreCase)
                    && IsValidSHA1Hex(serverSha)
                    && Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? serverUri)
                    && serverUri.Scheme == Uri.UriSchemeHttps)
                {
                    return detail;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException)
            {
                ErrorHandling.LogNonFatal("Local Minecraft version detail manifest could not be used", ex);
            }
        }

        return JObject.Parse(await SetupHttpClient.GetStringAsync(detailUri, cancellationToken));
    }

    private static async Task CheckServerJarAsync(HttpClient http, string serverUrl, string jarPath, string expectedSha, CancellationToken cancellationToken)
    {
        if (!IsValidSHA1Hex(expectedSha))
            throw new InvalidOperationException("The selected Minecraft server checksum was missing or invalid.");

        Uri downloadUri = CreateHttpsUri(serverUrl, "Minecraft server download");

        if (File.Exists(jarPath))
        {
            bool existingJarValid = await Task.Run(() => VerifySHA1Matches(jarPath, expectedSha), cancellationToken).ConfigureAwait(false);
            if (existingJarValid)
                return;
        }

        string tempPath = FileSystemHelper.GetUniqueTempPath(jarPath);
        string backupPath = jarPath + ".bak";

        try
        {
            await DownloadServerJarAsync(http, downloadUri, tempPath, cancellationToken).ConfigureAwait(false);
            await Task.Run(() => VerifySha1(tempPath, expectedSha), cancellationToken).ConfigureAwait(false);
            bool atomicReplace = FileSystemHelper.ReplaceOrMoveWithFallback(tempPath, jarPath, backupPath, "Atomic server jar replace failed; falling back to copy");
            if (!atomicReplace)
                await Task.Run(() => VerifySha1(jarPath, expectedSha), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is HttpRequestException || ex is InvalidOperationException)
        {
            throw new InvalidOperationException(
                "TwitchCraft could not create a verified Minecraft server jar. Close any leftover bot/java processes and try again.\n\nJar path: " + jarPath,
                ex);
        }
        finally
        {
            FileSystemHelper.TryDeleteFile(tempPath);
        }
    }

    private static async Task DownloadServerJarAsync(HttpClient http, Uri downloadUri, string outputPath, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && (contentLength.Value <= 0 || contentLength.Value > MaxServerJarDownloadBytes))
            throw new InvalidOperationException("The Minecraft server download size was invalid.");

        using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize, FileOptions.SequentialScan);
        await CopyToAsyncWithLimit(input, output, MaxServerJarDownloadBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyToAsyncWithLimit(Stream input, Stream output, long maxBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(DownloadBufferSize);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                int bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    return;

                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                    throw new InvalidOperationException("The Minecraft server download was larger than expected.");

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Uri CreateHttpsUri(string? url, string description)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The " + description + " URL was not a valid HTTPS URL.");

        return uri;
    }

    private static bool IsValidSHA1Hex(string? expectedSha)
    {
        if (expectedSha == null || expectedSha.Length != SHA1HexLength)
            return false;

        for (int i = 0; i < expectedSha.Length; i++)
        {
            char c = expectedSha[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }

        return true;
    }

    private static bool VerifySHA1Matches(string filePath, string expectedSha)
    {
        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DownloadBufferSize, FileOptions.SequentialScan);
            string actual = Convert.ToHexStringLower(SHA1.HashData(fs));
            return string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
        {
            ErrorHandling.LogNonFatal("Failed to verify existing Minecraft server jar", ex);
            return false;
        }
    }

    private static void VerifySha1(string filePath, string expectedSha)
    {
        if (!VerifySHA1Matches(filePath, expectedSha))
            throw new InvalidOperationException("The downloaded server jar did not match the expected SHA-1.");
    }

    private static BotConfig BuildConfig(string MCVersion, string serverDir, string jarPath, string bindIP, string javaExe, string javaHome, string clientID, string botToken, string channel, string botUser)
    {
        return new BotConfig
        {
            Server =
            {
                MinecraftVersion = MCVersion,
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
                StreamerName = channel,
                BotName = botUser
            }
        };
    }

    private async Task<(string JavaExe, string JavaHome)> ResolveJavaExecutableAsync(int javaVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string javaExe, string javaHome) = await Task.Run(() => JavaInstallationFinder.FindMatching(javaVersion), cancellationToken);
            if (!string.IsNullOrWhiteSpace(javaExe))
                return (javaExe, javaHome);

            if (!ErrorHandling.ConfirmRetryMissingJava(this, javaVersion))
                throw new OperationCanceledException("Setup cancelled by user (No Java).");
        }
    }

    private sealed class SecretTextBoxController
    {
        private readonly PasswordBox _hiddenBox;
        private readonly TextBox _visibleBox;
        private readonly CheckBox _toggle;
        private bool _syncing;

        public SecretTextBoxController(PasswordBox hiddenBox, TextBox visibleBox, CheckBox toggle)
        {
            ArgumentNullException.ThrowIfNull(hiddenBox);
            ArgumentNullException.ThrowIfNull(visibleBox);
            ArgumentNullException.ThrowIfNull(toggle);

            _hiddenBox = hiddenBox;
            _visibleBox = visibleBox;
            _toggle = toggle;

            _visibleBox.IsUndoEnabled = false;
            _toggle.Checked += VisibilityToggle_Changed;
            _toggle.Unchecked += VisibilityToggle_Changed;
        }

        public string Text => IsVisible ? _visibleBox.Text ?? string.Empty : _hiddenBox.Password ?? string.Empty;

        public void Hide()
        {
            _toggle.IsChecked = false;
            SyncVisibility();
        }

        private bool IsVisible => _toggle.IsChecked == true;

        private void VisibilityToggle_Changed(object sender, RoutedEventArgs e) => SyncVisibility();

        private void SyncVisibility()
        {
            if (_syncing)
                return;

            _syncing = true;
            try
            {
                if (IsVisible)
                {
                    _visibleBox.Text = _hiddenBox.Password ?? string.Empty;
                    _hiddenBox.Visibility = Visibility.Collapsed;
                    _visibleBox.Visibility = Visibility.Visible;
                }
                else
                {
                    _hiddenBox.Password = _visibleBox.Text ?? string.Empty;
                    _visibleBox.Clear();
                    _visibleBox.Visibility = Visibility.Collapsed;
                    _hiddenBox.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
