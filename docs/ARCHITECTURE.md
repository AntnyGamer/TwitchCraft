# Architecture

TwitchCraft is a Windows WPF application that coordinates Twitch IRC, a local or remote Minecraft Java server, persistence, and the desktop UI. `MainHandler` coordinates the application while focused components own Twitch/Minecraft sessions, commands, tokens, statistics, maintenance, and background tasks.

```text
WPF shell
    ↓
MainHandler (application coordinator)
    ├── TwitchSession (IRC transport/queue state, throttling, deduplication, session task state)
    ├── MinecraftSession (Java process, serialized writes, readiness/RCON/exit state, process cleanup)
    ├── CommandService (targeting, costs, authorization, command throttling/cooldowns, execution context)
    ├── TokenService (viewer economy and token-database lifecycle)
    ├── StatisticsService (session/lifetime tracking and snapshots)
    ├── DataMaintenance (backups, optimization, auth validation)
    └── BackgroundTaskTracker (owned task collection and fault observation)
```

## Main folders

* `Application/` — application helpers, shared infrastructure, and UI-thread dispatch
* `Setup/` — configuration, validation, server properties, Java discovery, world import, and datapack setup
* `Commands/` — command parsing/building, registration, gameplay/economy handlers, targeting, refunds, and `Minigames/`
* `Diagnostics/` — exception handling, structured rolling logs, and application-version metadata
* `Identity/` — Twitch token, Twitch username, and Minecraft username normalization
* `Infrastructure/` — shared file, JSON export, sorted-list, and text-segment helpers
* `Runtime/` — the coordinator, lifecycle, task/maintenance owners, and `Minecraft/`, `Players/`, and `Twitch/` transport/monitoring areas
* `Statistics/` — `StatisticsService`, lifetime/session models, SQLite persistence, and JSON exports
* `Tokens/` — `TokenService`, viewer-token accounting, SQLite persistence, and JSON export
* `Frames/` — WPF pages and their event logic
* `Assets/` — images, icon, server icon, and locate-players datapack
* `TwitchCraft.Tests/` — behavioral regression tests, including focused WPF state tests

`MainHandler` exposes the focused components through `runtime.Commands`, `runtime.Tokens`, and `runtime.Statistics`. `TwitchSession` and `MinecraftSession` own their transport/session resources while `MainHandler` continues coordinating lifecycle, command, viewer, and player-monitor behavior.

## Startup flow

1. WPF starts and installs global exception handlers.
2. The application checks its `%APPDATA%\TwitchCraft` working directory and loads normalized configuration.
3. The user selects local or remote mode and the session-specific Start options.
4. The TwitchCraft runtime initializes token/statistics stores and Twitch identity.
5. Local mode prepares the server directory and starts the Java process detected during setup; remote mode verifies RCON.
6. TwitchCraft renews its locally stored device authorization when needed, then Twitch IRC connects over TLS, joins the configured channel, and starts bounded processing queues. Helix and EventSub provide viewer-roster and follow data.
7. Player monitoring and optional minigame/statistics loops start after Minecraft readiness.

## Component ownership

* `MainHandler` owns application composition and overall session lifecycle coordination.
* `TwitchSession` owns the live IRC socket/writer, Twitch write/rate-limit and identity/token-refresh synchronization, IRC work queues, message de-duplication, send-rate/channel state, and follow-reward task lifetime.
* `MinecraftSession` owns the local Java process, serialized Minecraft writes, server readiness, RCON-health state, expected-exit state, staged local RCON state, and process cleanup.
* `CommandService` owns command execution context, rate limits and cooldowns, command cost scaling, moderator authorization, and player-target resolution.
* `TokenService` owns the `TokenStore` database and all balance/reward operations.
* `StatisticsService` owns statistics locks, session/lifetime state, persistence deltas, death tracking, and snapshot caches.
* `DataMaintenance` owns backup schedules, retention, SQLite optimization, and periodic Twitch-token validation.
* `BackgroundTaskTracker` owns tracked task state and observes task faults.

Dependencies flow into components through small callbacks or focused collaborators. Components do not reach into the coordinator's private state.

## Command execution

1. Twitch IRC parses tags, sender, moderator state, and `PRIVMSG` content.
2. The command parser normalizes the command name and arguments.
3. The registry resolves the handler and statistics flags.
4. Shared helpers validate permissions, server readiness, cooldowns, token balance, and targets.
5. Paid commands reserve/charge tokens before dispatch.
6. Commands are built with selector, JSON, SNBT, and version-aware escaping.
7. The local transport serializes writes to Java stdin; remote mode sends RCON packets.
8. `PaidCommandTransaction` records statistics only after confirmed Minecraft delivery. Local write failures refund once and release that command's cooldown reservation. Remote RCON uses matching command-response packets rather than response wording; any confirmed command keeps a multi-command charge. If none are confirmed, authentication, transport, timeout, or protocol failures refund it.

## Local and remote modes

Local mode owns Java process startup, output/error readers, server preparation, shutdown, and local command writes. Remote mode connects to an existing server and owns only its RCON/query connection. Both modes expose common high-level send/query methods to command handlers.

## Persistence

* `config.json` uses normalized models, temporary-file writes, and replacement fallback.
* Automatic timestamped backups pair `config.json` with consistent SQLite copies of `viewer_tokens.db` and `statistics.db`; all three files are required for a complete set before retention pruning.
* Viewer balances use SQLite with a readable JSON export.
* Statistics use SQLite with aggregate/viewer JSON exports.
* Database operations use synchronization and parameterized statements.
* Server/world files remain separate from configuration and databases.

## Shutdown and recovery

Cancellation tokens stop background loops. Local mode requests graceful server shutdown using the configured timeout before forceful process cleanup. RCON and network clients disconnect, background tasks are observed, stores are flushed/disposed, and the diagnostic writer closes on application exit. Unexpected exceptions are captured for diagnostics while user-facing dialogs remain concise.

## Testability direction

Tests cover pure builders, normalizers, Twitch/IRC parsers, viewer-token persistence, rolling-log behavior, paid-command transaction semantics, focused WPF state, and fake process/socket integrations without requiring a live Twitch channel or Minecraft server. The runtime uses explicit construction and narrow callback seams rather than a service container.
