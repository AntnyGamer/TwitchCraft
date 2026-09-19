using System;
using System.Collections.Generic;
using System.Net.Http;
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
    private readonly Lock _viewerGate;
    private readonly Lock _playerGate;
    private readonly Lock _relayGate;
    private readonly Lock _configPersistenceGate;
    private readonly TimedPlayerScaleController _timedPlayerScaleController;
    private readonly BackgroundTaskTracker _backgroundTaskTracker;
    private readonly DataMaintenance _dataMaintenance;
    private TwitchCraft? _shellWindow;
    private CancellationTokenSource? _sessionCts;
    private TwitchCraftConfig? _activeConfig;
    private RuntimeState _runtimeState;
    private Dictionary<string, long> _viewerRewardSchedule;
    private List<string> _knownViewers;
    private List<string> _knownPlayers;
    private List<string> _lastSidebarPlayers;
    private bool _playerSidebarInitialized, _profileApplied;
    private long _lastOnlinePlayersSnapshotTicks;
    private int _lifecycleStopGeneration;
    private int _shutdownRequested;
    private string _currentStreamerName;
    private string _currentStreamerMinecraftName;
    private string _lastServerPropertiesPath;
    private string _lastServerPropertiesContent;
    private List<string> _lootList;
    private List<string> _mobList;
    private List<EffectDefinition> _effectList;
    private MinecraftVersionSupport.MinecraftVersionInfo? _minecraftVersionInfo;

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
        Commands = new CommandService(RefreshPlayersAsync);
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
        _relayGate = new();
        _configPersistenceGate = new();
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
        Tokens = new TokenService(tokenStorePath, () => _activeConfig?.Settings.MaximumTokenBalance ?? 0);
        _dataMaintenance = new DataMaintenance(
            () => _activeConfig,
            DefaultEffectiveSettings,
            Tokens,
            (token, cancellationToken) => ValidateBotAsync(token, _activeConfig?.Twitch.ClientID ?? string.Empty, cancellationToken),
            SaveBot,
            TryRefreshAuthAsync);
        _currentStreamerName = string.Empty;
        _currentStreamerMinecraftName = string.Empty;
        _lastServerPropertiesPath = string.Empty;
        _lastServerPropertiesContent = string.Empty;
        _effectList = Catalogs.BuildEffects();
        _lootList = Catalogs.BuildLoot();
        _mobList = Catalogs.BuildMobs();
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

    public bool MultiplayerEnabled => _activeConfig?.Settings.MultiplayerEnabled == true;

    public bool RemoteControlEnabled => _activeConfig?.Settings.RemoteControlEnabled == true;

    internal bool ProfileApplied => _profileApplied || _runtimeState != RuntimeState.Stopped;
    public bool RequireOnlineMode => _activeConfig?.Settings.RequireOnlineMode != false;

    internal void StageLocalRCONPassword(string password) => _minecraftSession.StageLocalRCONPassword(password);

    public bool MultiTargetingEnabled => MultiplayerEnabled || RemoteControlEnabled;

    public bool MinigamesEnabled => _activeConfig?.Settings.MinigamesEnabled == true;

    public int MinigameCooldown => _activeConfig?.Settings.MinigameCooldown ?? 15;

    private void SetConfig(TwitchCraftConfig config)
    {
        string previousMinecraftVersion = _activeConfig?.Server.MinecraftVersion ?? string.Empty;
        _activeConfig = config;
        _currentStreamerName = NormalizeUser(config.Twitch.StreamerName);
        _twitchSession.SetChannel(_currentStreamerName);
        string configuredMinecraftPlayer = config.Identity.StreamerMinecraftName.Trim();
        _currentStreamerMinecraftName = MinecraftNameHelper.TryNormalizePlayerName(configuredMinecraftPlayer, out string normalizedMinecraftPlayer)
            ? normalizedMinecraftPlayer
            : string.Empty;

        string minecraftVersion = config.Server.MinecraftVersion;
        if (!string.Equals(previousMinecraftVersion, minecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            _minecraftVersionInfo = string.IsNullOrWhiteSpace(minecraftVersion)
                ? null
                : MinecraftVersionSupport.GetVersion(minecraftVersion);
            _mobList = Catalogs.BuildMobs(minecraftVersion);
            _lootList = Catalogs.BuildLoot(minecraftVersion);
            _effectList = Catalogs.BuildEffects(minecraftVersion);
        }

        Commands.SetContext(config);
        Statistics.SetContext(config.Settings.StatisticsEnabled, _currentStreamerName, _currentStreamerMinecraftName, config.Settings.CommandPrefix);
    }

    public string StreamerName => _currentStreamerName;
}
