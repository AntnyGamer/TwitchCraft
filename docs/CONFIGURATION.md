# Configuration and local data

TwitchCraft stores its user-specific data under:

```text
%APPDATA%\TwitchCraftBot
```

Depending on enabled features and runtime state, this directory can contain:

- `config.json` and its temporary save file
- `viewer_tokens.db`
- `statistics.db`
- JSON exports under `exports/`
- diagnostic logs
- automatic point-in-time backups under `Backups/`
- the managed local Minecraft server directory and world data

Close TwitchCraft before manually copying, replacing, or inspecting databases and server files.

## Configuration sections

The application owns the shape of `config.json` and normalizes values when loading and saving it.

### Server

- Minecraft version and server paths
- Local bind IP and game port
- Maximum players
- Minimum and maximum Java memory
- Java executable/home paths
- Remote host, RCON port, and RCON password

Invalid ports return to safe defaults. Minimum memory is constrained not to exceed maximum memory. Bind and remote-host values are validated before use.

### Twitch

- Streamer/channel name
- Bot account name
- Client ID
- Hidden access and refresh tokens used by TwitchCraft

The access and refresh tokens remain sensitive and are intentionally not exposed in Setup textboxes.

The recommended Twitch Developer Console redirect is `http://localhost:3000`, but any valid localhost redirect is acceptable. The Setup screen uses Twitch device authorization after the Client ID is entered, so it does not run a callback or depend on that exact redirect. Existing installations can replace the saved authorization from **Settings → Dangerous → Authorize Twitch**. TwitchCraft automatically renews an expired authorization when possible. The authorization requests chat, viewer-roster, and follower permissions.

New follows grant 100 tokens once per Twitch account. The persistent follow-reward record is stored in `viewer_tokens.db`, so EventSub reconnects, duplicate notifications, and unfollow/refollow cycles cannot pay the same account twice.

### Identity

- Streamer's Minecraft username

Minecraft usernames must be 3–16 characters containing letters, numbers, or `_`.

### Starting profile

- Multiplayer and PvP
- Online-mode requirement
- Local versus remote control mode
- Hardcore and difficulty
- Minigames and their interval
- Optional global game-command cooldown
- Passive token earning
- Chat relay
- Moderator access to broadcaster commands
- Statistics collection
- Per-viewer and channel-wide command limits
- Per-command enable/disable and cooldown customizations
- Passive payout amount, interval, maximum balance, and recent-chat window
- Follow and Bit reward controls
- Chat display, relay throughput, and log-retention controls
- UI, queue, roster, RCON, SQLite, and low-resource performance controls
- Automatic-backup interval and retention, plus graceful-shutdown timeout
- Managed Minecraft distance, broadcast, compression, and empty-server behavior

Transient start-mode choices are reset when configuration is loaded for a new session rather than being treated as permanent runtime state.

Economy and command-rate dropdowns retain convenient presets while allowing custom numeric entry. Passive payout timing uses a minimum and maximum of 10–900 seconds; TwitchCraft selects a new inclusive random delay for every payout. Equal minimum and maximum values create a fixed interval. Zero means unlimited for maximum balance and command-rate limits.

## Server properties

TwitchCraft manages the settings it needs in `server.properties` while preserving unrelated entries. Avoid editing the file while the managed server is running. RCON passwords may not contain newlines.

## Backups and recovery

Configuration saves use a temporary file and replacement fallback; TwitchCraft removes stale temporary `.bak` files instead of treating them as recovery data. Automatic backups are enabled by default. TwitchCraft creates a consistent SQLite copy of `viewer_tokens.db` beside a copy of `config.json` under `Backups/` at the selected interval and during a clean shutdown. Retention can be set to 1, 3, 5, 10, or 20 complete backup sets and defaults to three. These copies include sensitive Twitch authorization data and must remain private.

Before restoring an automatic backup:

1. Close TwitchCraft.
2. Make a copy of the entire `%APPDATA%\TwitchCraftBot` directory.
3. Inspect filenames carefully; never post their contents publicly.
4. Restore `config.json` and `viewer_tokens.db` from the same timestamped backup folder.
5. Start TwitchCraft and verify settings and balances before launching a server.

## Sensitive values

Never share:

- Twitch bot tokens
- Twitch refresh tokens
- RCON passwords
- Authorization headers
- Full `config.json` contents
- Private databases or worlds
- Public IP addresses unless disclosure is intentional
- Unreviewed logs

Reset a Twitch token immediately if it may have been exposed. Change an RCON password and restart the remote server if that password may have leaked.
