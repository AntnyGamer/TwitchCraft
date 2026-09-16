using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraft_V1.Setup;

namespace TwitchCraft_V1;

public sealed partial class MainHandler
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly AppShellViewModel _shellModel;
    private readonly ChatCommandRegistry _commandRegistry;
    private readonly SemaphoreSlim _lifecycleGate;
    private readonly TwitchSession _twitchSession;
    private readonly MinecraftSession _minecraftSession;
    private const int MaxQueuedIRCCommands = 75;
    private const int MaxQueuedIRCQuickWork = 500;
    private readonly Lock _viewerGate;
    private readonly Lock _playerGate;
    private readonly Lock _cooldownGate;
    private readonly Lock _configPersistenceGate;
    private readonly Lock _effectCacheGate;
    private readonly TimedPlayerScaleController _timedPlayerScaleController;
    private readonly BackgroundTaskTracker _backgroundTaskTracker;
    private readonly DataMaintenance _dataMaintenance;
    private TwitchCraft? _shellWindow;
    private CancellationTokenSource? _sessionCts, _followRewardsCts;
    private Task? _followRewardsTask;
    private TwitchCraftConfig? _activeConfig;
    private string? _nextLocalRCONPassword;
    private RuntimeState _runtimeState;
    private Dictionary<string, long> _viewerRewardSchedule;
    private List<string> _knownViewers;
    private List<string> _knownPlayers;
    private List<string> _lastSidebarPlayers;
    private bool _playerSidebarInitialized, _profileApplied;
    private long _lastOnlinePlayersSnapshotTicks;
    private readonly IRCWorkQueueState _IRCCommandQueue;
    private readonly IRCWorkQueueState _IRCQuickQueue;
    private int _IRCQueueGeneration;
    private int _lifecycleStopGeneration;
    private int _shutdownRequested;
    private long _lastIRCCommandOverflowNoticeTicks;
    private string _currentStreamerName;
    private string _currentCommandPrefix;
    private string _currentSecondaryCommandPrefix;
    private string _currentMinecraftRelayTextColor;
    private string _currentBotResponseVerbosity;
    private string _IRCChannelPrefix;
    private int _IRCChannelMessageMaxBytes;
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

    private SemaphoreSlim _serverWriteGate => _minecraftSession.WriteGate;
    private SemaphoreSlim _IRCWriteGate => _twitchSession.WriteGate;
    private SemaphoreSlim _IRCChatRateGate => _twitchSession.ChatRateGate;
    private SemaphoreSlim _botIdentityResolveGate => _twitchSession.BotIdentityResolveGate;
    private SemaphoreSlim _twitchTokenRefreshGate => _twitchSession.TokenRefreshGate;
    private bool _minecraftServerReady
    {
        get => _minecraftSession.ServerReady;
        set => _minecraftSession.ServerReady = value;
    }
    private Process? _javaServerProcess
    {
        get => _minecraftSession.Process;
        set => _minecraftSession.Process = value;
    }
    private StreamWriter? _IRCWriter
    {
        get => _twitchSession.Writer;
        set => _twitchSession.Writer = value;
    }

    public TokenService Tokens { get; }

    public CommandService Commands { get; }

    public StatisticsService Statistics { get; }

    internal void AddChatLogLine(string line) => _shellWindow?.AddChatLogLine(line);

    internal void AddServerLogLine(string line) => _shellWindow?.AddServerLogLine(line);

    public MainHandler(AppShellViewModel shellModel)
        : this(shellModel, ConfigurationStore.ViewerTokensPath)
    {
        InitializeApplicationState();
    }

    internal MainHandler(AppShellViewModel shellModel, string tokenStorePath)
    {
        ArgumentNullException.ThrowIfNull(shellModel);

        _shellModel = shellModel;
        _twitchSession = new();
        _minecraftSession = new();
        Commands = new CommandService(
            HasGlobalCooldownOverride,
            HasPerUserCooldownOverride,
            RefreshPlayersAsync);
        _commandRegistry = ChatCommandRegistry.CreateDefault(this);
        Statistics = new StatisticsService(new StatisticsDependencies(
            command => _commandRegistry.GetStatisticFlags(command),
            GetKnownPlayers,
            IsSpectatorPlayer,
            QueueSnapshot,
            QueueGamemode,
            () => QueueDeathScore(),
            playerName => QueueDeathScore(playerName),
            QueueRespawn));
        _lifecycleGate = new(1, 1);
        _viewerGate = new();
        _playerGate = new();
        _cooldownGate = new();
        _configPersistenceGate = new();
        _effectCacheGate = new();
        _backgroundTaskTracker = new();
        _timedPlayerScaleController = new(
            (command, token) => SendServerCommandAsync(command, token),
            IsKnownPlayer,
            TrackTask,
            AddServerLogLine);
        _viewerRewardSchedule = new(StringComparer.OrdinalIgnoreCase);
        _knownViewers = [];
        _knownPlayers = [];
        _lastSidebarPlayers = [];
        Tokens = new TokenService(tokenStorePath, () => MaximumTokenBalance);
        _dataMaintenance = new DataMaintenance(
            () => _activeConfig,
            DefaultEffectiveSettings,
            Tokens,
            (token, cancellationToken) => ValidateBotAsync(token, _activeConfig?.Twitch.ClientID ?? string.Empty, cancellationToken),
            SaveBot,
            TryRefreshAuthAsync);
        _IRCCommandQueue = new(MaxQueuedIRCCommands);
        _IRCQuickQueue = new(MaxQueuedIRCQuickWork);
        _currentStreamerName = string.Empty;
        _currentCommandPrefix = "!";
        _currentSecondaryCommandPrefix = string.Empty;
        _currentMinecraftRelayTextColor = "white";
        _currentBotResponseVerbosity = BotResponseVerbositySettings.Normal;
        _IRCChannelPrefix = string.Empty;
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
        // Load lifetime totals off the UI thread so construction does not block on disk I/O.
        _ = Task.Run(Statistics.Load);

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

    internal void TrackTask(Task task) => _backgroundTaskTracker.Track(task);

    public static int SecureRandomInt(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum);

    public static int SecureRandomInt(int minimum, int exclusiveMaximum) => RandomNumberGenerator.GetInt32(minimum, exclusiveMaximum);

    public static bool SecureRandomChance(double probability)
        => probability >= 1 || (probability > 0 && RandomNumberGenerator.GetInt32(int.MaxValue) < probability * int.MaxValue);

    public static Random Randomizer => Random.Shared;

    public bool MultiplayerEnabled => _activeConfig?.Settings.MultiplayerEnabled == true;

    public bool RemoteControlEnabled => _activeConfig?.Settings.RemoteControlEnabled == true;

    internal bool ProfileApplied => _profileApplied || _runtimeState != RuntimeState.Stopped;
    public bool RequireOnlineMode => _activeConfig?.Settings.RequireOnlineMode != false;

    internal void StageLocalRCONPassword(string password) => Interlocked.Exchange(ref _nextLocalRCONPassword, password);

    public bool MultiTargetingEnabled => MultiplayerEnabled || RemoteControlEnabled;

    public bool MinigamesEnabled => _activeConfig?.Settings.MinigamesEnabled == true;

    public int MinigameCooldown => _activeConfig?.Settings.MinigameCooldown ?? 15;

    private void SetConfig(TwitchCraftConfig config)
    {
        _activeConfig = config;
        _currentStreamerName = NormalizeUser(config.Twitch.StreamerName);
        _currentCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(config.Settings.CommandPrefix, "!");
        _currentSecondaryCommandPrefix = ConfigurationStore.NormalizeCommandPrefix(config.Settings.SecondaryCommandPrefix, string.Empty);
        if (string.Equals(_currentCommandPrefix, _currentSecondaryCommandPrefix, StringComparison.Ordinal))
            _currentSecondaryCommandPrefix = string.Empty;
        _currentMinecraftRelayTextColor = ConfigurationStore.NormalizeColor(config.Settings.MinecraftRelayTextColor);
        _currentBotResponseVerbosity = ConfigurationStore.NormalizeVerbosity(config.Settings.BotResponseVerbosity);
        _IRCChannelPrefix = _currentStreamerName.Length == 0 ? string.Empty : "PRIVMSG #" + _currentStreamerName + " :";
        _IRCChannelMessageMaxBytes = _IRCChannelPrefix.Length == 0 ? 0 : 510 - IRCUTF8NoBOM.GetByteCount(_IRCChannelPrefix);
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
        Commands.SetContext(config, _currentDefaultMinecraftPlayer);
        Statistics.SetContext(config.Settings.StatisticsEnabled, _currentStreamerName, _currentStreamerMinecraftName, _currentCommandPrefix);
    }

    public string DefaultMinecraftPlayer => _currentDefaultMinecraftPlayer;

    public string DefaultMinecraftPlayerName => _currentDefaultMinecraftPlayerName;

    public string StreamerName => _currentStreamerName;
}
