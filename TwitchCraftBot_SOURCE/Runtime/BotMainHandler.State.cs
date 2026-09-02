using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan FiveMinuteCommandCooldown = TimeSpan.FromMinutes(5);

    private readonly AppShellViewModel _shellModel;
    private readonly ChatCommandRegistry _commandRegistry;
    private readonly SemaphoreSlim _lifecycleGate;
    private readonly SemaphoreSlim _serverWriteGate;
    private readonly SemaphoreSlim _IRCWriteGate;
    private readonly SemaphoreSlim _botIdentityResolveGate;
    private readonly SemaphoreSlim _twitchTokenRefreshGate;
    private const int MaxQueuedIRCCommands = 75;
    private const int MaxQueuedIRCQuickWork = 500;

    private readonly Lock _viewerGate;
    private readonly Lock _playerGate;
    private readonly Lock _cooldownGate;
    private readonly Lock _configPersistenceGate;
    private readonly Lock _backgroundTasksGate;
    private readonly Lock _effectCacheGate;
    private readonly TimedPlayerScaleController _timedPlayerScaleController;

    private TwitchCraftBot? _shellWindow;
    private Process? _javaServerProcess;
    private CancellationTokenSource? _sessionCts;
    private readonly List<Task> _backgroundTasks;
    private BotConfig? _activeConfig;
    private RuntimeState _runtimeState;
    private readonly TokenHandler _tokenStore;
    private Dictionary<string, long> _viewerRewardSchedule;
    private List<string> _knownChatters;
    private List<string> _knownPlayers;
    private List<string> _lastSidebarPlayers;
    private bool _playerSidebarInitialized;
    private DateTime _lastOnlinePlayersSnapshotUtc;
    private DateTime _lastLightningUtc;
    private readonly Dictionary<string, DateTime> _timedScaleCommandCooldowns;
    private readonly Dictionary<string, DateTime> _gambleCooldowns;
    private TcpClient? _IRCSocket;
    private StreamWriter? _IRCWriter;
    private readonly IRCWorkQueueState _IRCCommandQueue;
    private readonly IRCWorkQueueState _IRCQuickQueue;
    private int _IRCQueueGeneration;
    private int _serverExitExpected;
    private int _lifecycleStopGeneration;
    private int _fireworksRepeatActive;
    private long _lastIRCCommandOverflowNoticeTicks;
    private string _currentStreamerName;
    private string _currentCommandPrefix;
    private string _currentSecondaryCommandPrefix;
    private string _currentMinecraftRelayTextColor;
    private string _currentBotResponseVerbosity;
    private string _ircChannelPrefix;
    private int _ircChannelMessageMaxBytes;
    private string _currentDefaultMinecraftPlayer;
    private string _currentDefaultMinecraftPlayerName;
    private string _currentStreamerMinecraftName;
    private string _currentMinecraftVersion;
    private string _lastServerPropertiesPath;
    private string _lastServerPropertiesContent;

    private readonly List<EffectDefinition> _effectList;
    private List<string> _lootList;
    private List<string> _mobList;
    private string _cachedSupportedEffectsVersion;
    private List<EffectDefinition> _cachedSupportedEffects;
    private string _cachedMinecraftFeatureVersion;
    private MinecraftVersionSupport.MinecraftVersionInfo? _cachedMinecraftFeatureInfo;

    internal void AddChatLogLine(string line) => _shellWindow?.AddChatLogLine(line);

    internal void AddServerLogLine(string line) => _shellWindow?.AddServerLogLine(line);

    public BotMainHandler(AppShellViewModel shellModel)
        : this(shellModel, ConfigurationStore.ViewerTokensPath)
    {
        InitializeApplicationState();
    }

    internal BotMainHandler(AppShellViewModel shellModel, string tokenStorePath)
    {
        ArgumentNullException.ThrowIfNull(shellModel);

        _shellModel = shellModel;
        _commandRegistry = ChatCommandRegistry.CreateDefault(this);
        _lifecycleGate = new(1, 1);
        _serverWriteGate = new(1, 1);
        _IRCWriteGate = new(1, 1);
        _botIdentityResolveGate = new(1, 1);
        _twitchTokenRefreshGate = new(1, 1);
        _viewerGate = new();
        _playerGate = new();
        _cooldownGate = new();
        _configPersistenceGate = new();
        _backgroundTasksGate = new();
        _effectCacheGate = new();
        _timedPlayerScaleController = new(
            (command, token) => SendServerCommandAsync(command, token),
            TrackTask,
            AddServerLogLine);
        _backgroundTasks = [];
        _viewerRewardSchedule = new(StringComparer.OrdinalIgnoreCase);
        _knownChatters = [];
        _knownPlayers = [];
        _lastSidebarPlayers = [];
        _tokenStore = new(tokenStorePath);
        _timedScaleCommandCooldowns = new(StringComparer.OrdinalIgnoreCase);
        _gambleCooldowns = new(StringComparer.OrdinalIgnoreCase);
        _IRCCommandQueue = new(MaxQueuedIRCCommands);
        _IRCQuickQueue = new(MaxQueuedIRCQuickWork);
        _currentStreamerName = string.Empty;
        _currentCommandPrefix = "!";
        _currentSecondaryCommandPrefix = string.Empty;
        _currentMinecraftRelayTextColor = "white";
        _currentBotResponseVerbosity = BotResponseVerbositySettings.Normal;
        _ircChannelPrefix = string.Empty;
        _currentDefaultMinecraftPlayer = string.Empty;
        _currentDefaultMinecraftPlayerName = string.Empty;
        _currentStreamerMinecraftName = string.Empty;
        _currentMinecraftVersion = string.Empty;
        _lastServerPropertiesPath = string.Empty;
        _lastServerPropertiesContent = string.Empty;
        _effectList = Catalogs.BuildEffects();
        _lootList = Catalogs.BuildLoot();
        _mobList = Catalogs.BuildMobs();
        _cachedSupportedEffectsVersion = string.Empty;
        _cachedSupportedEffects = _effectList;
        _cachedMinecraftFeatureVersion = string.Empty;
    }

    private void InitializeApplicationState()
    {
        // SQLite loads individual rows on demand and queries top viewers by index.
        // Load lifetime totals off the UI thread so construction does not block on disk I/O.
        _ = Task.Run(EnsureLoaded);

        try
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => SafeCleanup();
        }
        catch
        {
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => SafeCleanup();
        }
        catch
        {
        }
    }

    internal void TrackTask(Task task)
    {
        if (task == null)
            return;

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
                ErrorHandling.LogNonFatal("Background task failed", task.Exception);
            return;
        }

        lock (_backgroundTasksGate)
            _backgroundTasks.Add(task);

        _ = task.ContinueWith(
            completedTask =>
            {
                if (completedTask.IsFaulted)
                    ErrorHandling.LogNonFatal("Background task failed", completedTask.Exception);

                lock (_backgroundTasksGate)
                    _backgroundTasks.Remove(completedTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static int SecureRandomInt(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum);

    public static int SecureRandomInt(int minimum, int exclusiveMaximum) => RandomNumberGenerator.GetInt32(minimum, exclusiveMaximum);

    public static bool SecureRandomChance(double probability)
        => probability >= 1 || (probability > 0 && RandomNumberGenerator.GetInt32(int.MaxValue) < probability * int.MaxValue);

    public static Random Randomizer => Random.Shared;

    public bool MultiplayerEnabled => _activeConfig?.Settings.MultiplayerEnabled == true;

    public bool RemoteControlEnabled => _activeConfig?.Settings.RemoteControlEnabled == true;

    public bool RequireOnlineMode => _activeConfig?.Settings.RequireOnlineMode != false;

    public bool MultiTargetingEnabled => MultiplayerEnabled || RemoteControlEnabled;

    public bool MinigamesEnabled => _activeConfig?.Settings.MinigamesEnabled == true;

    public int MinigameCooldown
    {
        get
        {
            int minutes = _activeConfig?.Settings.MinigameCooldown ?? 15;
            return minutes is < 2 or > 30 ? 15 : minutes;
        }
    }

    private void SetConfig(BotConfig config)
    {
        _activeConfig = config;
        _currentStreamerName = NormalizeUser(config.Twitch.StreamerName);
        _currentCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(config.Settings.CommandPrefix, "!");
        _currentSecondaryCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(config.Settings.SecondaryCommandPrefix, string.Empty);
        if (string.Equals(_currentCommandPrefix, _currentSecondaryCommandPrefix, StringComparison.Ordinal))
            _currentSecondaryCommandPrefix = string.Empty;
        _currentMinecraftRelayTextColor = ConfigurationStore.NormalizeColor(config.Settings.MinecraftRelayTextColor);
        _currentBotResponseVerbosity = ConfigurationStore.NormalizeVerbosity(config.Settings.BotResponseVerbosity);
        _ircChannelPrefix = _currentStreamerName.Length == 0 ? string.Empty : "PRIVMSG #" + _currentStreamerName + " :";
        _ircChannelMessageMaxBytes = _ircChannelPrefix.Length == 0 ? 0 : 510 - IRCUtf8NoBom.GetByteCount(_ircChannelPrefix);
        string configuredMinecraftPlayer = config.Identity.StreamerMinecraftName.Trim();
        _currentDefaultMinecraftPlayer = configuredMinecraftPlayer.Length > 0
            ? configuredMinecraftPlayer
            : config.Twitch.StreamerName.Trim();
        _currentDefaultMinecraftPlayerName = MinecraftNameHelper.TryNormalizePlayerName(_currentDefaultMinecraftPlayer, out string normalizedDefaultMinecraftPlayer)
            ? normalizedDefaultMinecraftPlayer
            : string.Empty;
        _currentStreamerMinecraftName = MinecraftNameHelper.TryNormalizePlayerName(configuredMinecraftPlayer, out string normalizedMinecraftPlayer)
            ? normalizedMinecraftPlayer
            : string.Empty;
        _currentMinecraftVersion = (config.Server.MinecraftVersion ?? string.Empty).Trim();
    }

    public string DefaultMinecraftPlayer => _currentDefaultMinecraftPlayer;

    public string DefaultMinecraftPlayerName => _currentDefaultMinecraftPlayerName;

    public string StreamerName => _currentStreamerName;
}
