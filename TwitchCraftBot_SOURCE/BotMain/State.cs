using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan LightningCooldown = TimeSpan.FromMinutes(5);

    private readonly AppShellViewModel _shellModel;
    private readonly ChatCommandRegistry _commandRegistry;
    private readonly SemaphoreSlim _lifecycleGate;
    private readonly SemaphoreSlim _serverWriteGate;
    private readonly SemaphoreSlim _IRCWriteGate;
    private readonly SemaphoreSlim _botIdentityResolveGate;
    private const int MaxQueuedIRCCommands = 75;
    private const int MaxQueuedIRCQuickWork = 500;

    private readonly Lock _botIdentityCacheGate;
    private readonly Lock _viewerGate;
    private readonly Lock _playerGate;
    private readonly Lock _cooldownGate;
    private readonly Lock _configPersistenceGate;
    private readonly Lock _backgroundTasksGate;
    private readonly Lock _effectCacheGate;

    private TwitchCraftBot? _shellWindow;
    private Process? _javaServerProcess;
    private CancellationTokenSource? _sessionCts;
    private List<Task> _backgroundTasks;
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
    private readonly Dictionary<string, DateTime> _gambleCooldowns;
    private TcpClient? _IRCSocket;
    private StreamWriter? _IRCWriter;
    private readonly IRCWorkQueueState _IRCCommandQueue;
    private readonly IRCWorkQueueState _IRCQuickQueue;
    private int _IRCQueueGeneration;
    private int _serverExitExpected;
    private int _fireworksRepeatActive;
    private long _lastIRCCommandOverflowNoticeTicks;
    private string _cachedBotToken;
    private string _cachedBotName;
    private string _currentStreamerName;
    private string _currentBotName;
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
    {
        ArgumentNullException.ThrowIfNull(shellModel);

        _shellModel = shellModel;
        _commandRegistry = ChatCommandRegistry.CreateDefault(this);
        _lifecycleGate = new(1, 1);
        _serverWriteGate = new(1, 1);
        _IRCWriteGate = new(1, 1);
        _botIdentityResolveGate = new(1, 1);
        _botIdentityCacheGate = new();
        _viewerGate = new();
        _playerGate = new();
        _cooldownGate = new();
        _configPersistenceGate = new();
        _backgroundTasksGate = new();
        _effectCacheGate = new();
        _backgroundTasks = [];
        _viewerRewardSchedule = new(StringComparer.OrdinalIgnoreCase);
        _knownChatters = [];
        _knownPlayers = [];
        _lastSidebarPlayers = [];
        _tokenStore = new(ConfigurationStore.ViewerTokensPath);
        _gambleCooldowns = new(StringComparer.OrdinalIgnoreCase);
        _IRCCommandQueue = new(MaxQueuedIRCCommands);
        _IRCQuickQueue = new(MaxQueuedIRCQuickWork);
        _cachedBotToken = string.Empty;
        _cachedBotName = string.Empty;
        _currentStreamerName = string.Empty;
        _currentBotName = string.Empty;
        _ircChannelPrefix = string.Empty;
        _ircChannelMessageMaxBytes = 0;
        _currentDefaultMinecraftPlayer = string.Empty;
        _currentDefaultMinecraftPlayerName = string.Empty;
        _currentStreamerMinecraftName = string.Empty;
        _currentMinecraftVersion = string.Empty;
        _lastServerPropertiesPath = string.Empty;
        _lastServerPropertiesContent = string.Empty;
        _effectList = TwitchCraftCatalogs.BuildEffectList();
        _lootList = TwitchCraftCatalogs.BuildLootList();
        _mobList = TwitchCraftCatalogs.BuildMobList();
        _cachedSupportedEffectsVersion = string.Empty;
        _cachedSupportedEffects = _effectList;
        _cachedMinecraftFeatureVersion = string.Empty;
        _cachedMinecraftFeatureInfo = null;

        // SQLite loads individual rows on demand and queries top viewers by index.
        // Load lifetime totals off the UI thread so construction does not block on disk I/O.
        _ = Task.Run(EnsureStatisticsLoaded);

        try
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => SafeSynchronousCleanup();
        }
        catch
        {
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => SafeSynchronousCleanup();
        }
        catch
        {
        }
    }

    internal void TrackSessionBackgroundTask(Task task)
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

    public static Random Randomizer => Random.Shared;

    public bool MultiplayerEnabled => _activeConfig != null && _activeConfig.Settings.MultiplayerEnabled;

    public bool RemoteControlEnabled => _activeConfig != null && _activeConfig.Settings.RemoteControlEnabled;

    public bool RequireOnlineMode => _activeConfig == null || _activeConfig.Settings.RequireOnlineMode;

    public bool MultiTargetingEnabled => MultiplayerEnabled || RemoteControlEnabled;

    public bool MinigamesEnabled => _activeConfig != null && _activeConfig.Settings.MinigamesEnabled;

    public int MinigameCooldown
    {
        get
        {
            if (_activeConfig == null)
            {
                return 15;
            }

            int minutes = _activeConfig.Settings.MinigameCooldown;
            return minutes < 2 || minutes > 30 ? 15 : minutes;
        }
    }

    private void SetActiveConfig(BotConfig config)
    {
        _activeConfig = config;
        _currentStreamerName = NormalizeUser(config.Twitch.StreamerName);
        _currentBotName = NormalizeUser(config.Twitch.BotName);
        _ircChannelPrefix = _currentStreamerName.Length == 0 ? string.Empty : "PRIVMSG #" + _currentStreamerName + " :";
        _ircChannelMessageMaxBytes = _ircChannelPrefix.Length == 0 ? 0 : 510 - IRCUtf8NoBom.GetByteCount(_ircChannelPrefix);
        string configuredMinecraftPlayer = config.Identity.StreamerMinecraftName.Trim();
        _currentDefaultMinecraftPlayer = configuredMinecraftPlayer.Length > 0
            ? configuredMinecraftPlayer
            : config.Twitch.StreamerName.Trim();
        _currentDefaultMinecraftPlayerName = MinecraftNameHelper.TryNormalizePlayerName(_currentDefaultMinecraftPlayer, out string normalizedDefaultMinecraftPlayer)
            ? normalizedDefaultMinecraftPlayer
            : string.Empty;
        _currentStreamerMinecraftName = MinecraftNameHelper.TryNormalizePlayerName(config.Identity.StreamerMinecraftName, out string normalizedMinecraftPlayer)
            ? normalizedMinecraftPlayer
            : string.Empty;
        _currentMinecraftVersion = (config.Server.MinecraftVersion ?? string.Empty).Trim();
    }

    public string DefaultMinecraftPlayer => _currentDefaultMinecraftPlayer;

    public string DefaultMinecraftPlayerName => _currentDefaultMinecraftPlayerName;

    public string StreamerName => _currentStreamerName;
}
