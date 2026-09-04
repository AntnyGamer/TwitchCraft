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
- automatic point-in-time backups under `backups/`
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
- Remote host plus the managed local server's RCON port and password

The RCON password shown under **Settings --> Dangerous** is the password for TwitchCraft's managed local server. A port/password entered in Remote Control Mode is used for that remote session instead of replacing the local-server RCON settings.

Invalid ports return to safe defaults. Minimum memory is constrained not to exceed maximum memory. Bind and remote-host values are validated before use.

### Twitch

- Streamer/channel name
- Bot account name
- TwitchCraft's built-in public Client ID
- Hidden access and refresh tokens used by TwitchCraft

The access and refresh tokens remain sensitive and are intentionally not exposed in Setup textboxes.

TwitchCraft uses its built-in public Twitch application and device authorization. Users do not enter a Client ID or Client Secret, create a developer application, configure a localhost redirect, or run a callback listener. Existing installations can replace the saved authorization from **Settings --> Dangerous --> Reauthorize Twitch**. If no authorization is currently saved, the button is labeled **Authorize Twitch** instead. TwitchCraft automatically renews an expired authorization when possible; otherwise it asks you to reauthorize. The authorization requests chat, viewer-roster, and follower permissions.

New follows grant up to the configured number of tokens once per Twitch account (100 by default), limited by the configured maximum balance. The persistent follow-reward record is stored in `viewer_tokens.db`, so EventSub reconnects, duplicate notifications, and unfollow/refollow cycles cannot pay the same account twice. When automatic Bit rewards are enabled, each Bit awards one token, limited by the configured maximum balance.

### Identity

- Streamer's Minecraft username

Minecraft usernames must be 3–16 characters containing letters, numbers, or `_`.

### Settings

The `Settings` object is written in the same category order shown by the Settings screen so the saved file is easier to inspect:

1. `Commands`
2. `Custom Commands`
3. `Economy`
4. `Gameplay`
5. `Chat & Display`
6. `Performance`
7. `Minecraft Server`

The Minecraft Server category includes a **Whitelist** toggle. It keeps both `white-list` and `enforce-whitelist` synchronized and applies on the next locally hosted server start. Player entries are managed with `!whitelistadd` and `!whitelistremove`.

Properties inside each category are also written in the same top-to-bottom order as their controls in the UI.

Transient start-mode choices such as local multiplayer, remote-control mode, and online-mode selection are reset for each new launch and are not written as permanent Settings entries. Java memory and the RCON password remain under `Server` even though the UI exposes them under **Dangerous**.

Most economy and command-rate dropdowns retain convenient presets while allowing custom numeric entry; the activity eligibility window is preset-only. Passive payout timing uses a minimum and maximum of 10–900 seconds; TwitchCraft selects a new inclusive random delay for every payout. Equal minimum and maximum values create a fixed interval. Zero means unlimited for maximum balance and command-rate limits.

## Server properties

TwitchCraft rewrites `server.properties` into clearly labeled sections while preserving values it does not own:

- **Not managed by TwitchCraft** contains vanilla or custom properties TwitchCraft preserves, including `level-name` and other values that can safely be edited directly in the file.
- **Managed by TwitchCraft** contains gameplay, distance, connection, port, bind, player-limit, MOTD, and online-mode values controlled by TwitchCraft. On 1.21.9+, TwitchCraft applies PVP with the replacement game rule because Minecraft no longer supports the `pvp` server property.
- **RCON Password** is kept in its own clearly marked section and should be changed from **Settings --> Dangerous**.

Direct edits to a TwitchCraft-managed property may be replaced the next time TwitchCraft applies the settings. Avoid editing the file while the managed server is running. RCON passwords may not contain newlines.

## Backups and recovery

Configuration saves use `config.json.tmp` as a temporary file with a replacement fallback. If the main config cannot be read, TwitchCraft can recover from a valid temporary file. Automatic backups are enabled by default. TwitchCraft creates a consistent SQLite copy of `viewer_tokens.db` beside a copy of `config.json` under `backups/` at the selected interval and during a clean shutdown. Retention can be set to 1, 3, 5, 10, or 20 complete backup sets and defaults to three. These copies include sensitive Twitch authorization data and must remain private.

Before restoring an automatic backup:

1. Close TwitchCraft.
2. Make a copy of the entire `%APPDATA%\TwitchCraftBot` directory.
3. Inspect filenames carefully; never post their contents publicly.
4. Restore `config.json` and `viewer_tokens.db` from the same timestamped backup folder.
5. Start TwitchCraft and verify settings and balances before launching a server.

## Sensitive values

Never share:

- Twitch access and refresh tokens
- RCON passwords
- Authorization headers
- Full `config.json` contents
- Private databases or worlds
- Public IP addresses unless disclosure is intentional
- Unreviewed logs

Revoke TwitchCraft's Twitch authorization immediately if its tokens may have been exposed. Change the affected RCON password and restart that server if the password may have leaked.
