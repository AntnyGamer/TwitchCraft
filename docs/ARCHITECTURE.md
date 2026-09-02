# Architecture

TwitchCraft is a Windows WPF application that coordinates Twitch IRC, a local or remote Minecraft Java server, token/statistics persistence, and the desktop UI. `BotMainHandler` is the application-facing coordinator; focused components own command, token, statistics, maintenance, and background-task behavior.

```text
WPF shell
    ↓
BotMainHandler (application coordinator)
    ├── CommandService (targeting, costs, authorization, cooldown state)
    ├── TokenService (viewer economy and token-database lifecycle)
    ├── StatisticsService (session/lifetime tracking and snapshots)
    ├── DataMaintenance (backups, optimization, auth validation)
    ├── BackgroundTaskTracker (owned task collection and fault observation)
    ├── Twitch transport (IRC, Helix, EventSub)
    └── Minecraft runtime
          ├── Local Java process + stdin
          └── Remote RCON + query clients
```

## Main folders

- `Application/` — application helpers, shared infrastructure, and UI-thread dispatch
- `BotSetup/` — configuration, validation, server properties, Java discovery, world import, and datapack setup
- `Commands/` — command parsing/building, registration, gameplay/economy handlers, targeting, refunds, and `Minigames/`
- `Diagnostics/` — exception handling, structured rolling logs, and application-version metadata
- `Identity/` — Twitch token, Twitch username, and Minecraft username normalization
- `Infrastructure/` — shared file, JSON export, sorted-list, and text-segment helpers
- `Runtime/` — the coordinator, lifecycle, task/maintenance owners, and `Minecraft/`, `Players/`, and `Twitch/` transport/monitoring areas
- `Statistics/` — `StatisticsService`, lifetime/session models, SQLite persistence, and JSON exports
- `Tokens/` — `TokenService`, viewer-token accounting, SQLite persistence, and JSON export
- `Frames/` — WPF pages and their event logic
- `Assets/` — images, icon, server icon, and locate-players datapack
- `TwitchCraftBot.Tests/` — non-UI behavioral regression tests

`BotMainHandler` exposes the focused components through `runtime.Commands`, `runtime.Tokens`, and `runtime.Statistics`. Twitch, Minecraft, and player-monitor code remain coordinated partials for now; move them behind similarly focused ownership boundaries incrementally rather than adding more state to the coordinator.

## Startup flow

1. WPF starts and installs global exception handlers.
2. The application checks its `%APPDATA%\TwitchCraftBot` working directory and loads normalized configuration.
3. The user selects local or remote mode and a starting profile.
4. The bot runtime initializes token/statistics stores and Twitch identity.
5. Local mode prepares the server directory, locates Java, and starts the Java process; remote mode verifies RCON.
6. TwitchCraft renews its locally stored device authorization when needed, then Twitch IRC connects over TLS, joins the configured channel, and starts bounded processing queues. Helix and EventSub provide viewer-roster and follow data.
7. Player monitoring and optional minigame/statistics loops start after Minecraft readiness.

## Component ownership

- `BotMainHandler` owns application composition and session lifecycle coordination.
- `CommandService` owns mutable command cooldowns, command cost scaling, moderator authorization, and player-target resolution.
- `TokenService` owns the `TokenHandler` database and all balance/reward operations.
- `StatisticsService` owns statistics locks, session/lifetime state, persistence deltas, death tracking, and snapshot caches.
- `DataMaintenance` owns backup schedules, retention, SQLite optimization, and periodic Twitch-token validation.
- `BackgroundTaskTracker` owns tracked task state and observes task faults.

Dependencies flow into components through small callbacks or focused collaborators. Components do not reach into the coordinator's private state.

## Command execution

1. Twitch IRC parses tags, sender, moderator state, and `PRIVMSG` content.
2. The command parser normalizes the command name and arguments.
3. The registry resolves the handler and statistics flags.
4. Shared helpers validate permissions, server readiness, cooldowns, token balance, and targets.
5. Paid commands reserve/charge tokens before dispatch.
6. Commands are built with selector, JSON, SNBT, and version-aware escaping.
7. The local transport serializes writes to Java stdin; remote mode sends RCON packets.
8. The narrow `PaidCommandTransaction` coordinator records statistics only after dispatch succeeds. Before success, a failure refunds the charge exactly once and releases only that command's cooldown reservation.

## Local and remote modes

Local mode owns Java process startup, output/error readers, server preparation, shutdown, and local command writes. Remote mode connects to an existing server and owns only its RCON/query connection. Both modes expose common high-level send/query methods to command handlers.

## Persistence

- `config.json` uses normalized models, temporary-file writes, and replacement fallback.
- Automatic timestamped backups pair `config.json` with a consistent SQLite copy of `viewer_tokens.db` and prune complete sets according to configured retention.
- Viewer balances use SQLite with a readable JSON export.
- Statistics use SQLite with aggregate/viewer JSON exports.
- Database operations use synchronization and parameterized statements.
- Server/world files remain separate from configuration and databases.

## Shutdown and recovery

Cancellation tokens stop background loops. Local mode requests graceful server shutdown using the configured timeout before forceful process cleanup. RCON and network clients disconnect, background tasks are observed, stores are flushed/disposed, and the diagnostic writer closes on application exit. Unexpected exceptions are captured for diagnostics while user-facing dialogs remain concise.

## Testability direction

Tests cover pure builders, normalizers, Twitch/IRC parsers, viewer-token persistence, rolling-log behavior, application-version metadata, paid-command transaction semantics, focused WPF state, and fake process/socket integrations without requiring a live Twitch channel or Minecraft server. The runtime uses explicit construction and narrow callback seams rather than a service container. Future safe seams include a constructor-injected `TimeProvider`, Minecraft command client, Twitch client, and statistics store; introduce them one dependency at a time without a repository-wide dependency-injection framework conversion.
