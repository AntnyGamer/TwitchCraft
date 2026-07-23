# Configuration and local data

TwitchCraft stores its user-specific data under:

```text
%APPDATA%\TwitchCraftBot
```

Depending on enabled features and runtime state, this directory can contain:

- `config.json` and backup/temporary copies
- `viewer_tokens.db`
- `statistics.db`
- JSON exports under `exports/`
- diagnostic logs
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
- Bot token
- Client ID

An `oauth:` prefix is normalized automatically. The token remains sensitive whether or not that prefix is present.

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

Transient start-mode choices are reset when configuration is loaded for a new session rather than being treated as permanent runtime state.

## Server properties

TwitchCraft manages the settings it needs in `server.properties` while preserving unrelated entries. Avoid editing the file while the managed server is running. RCON passwords may not contain newlines.

## Backups and recovery

Configuration saves use a temporary file and replacement fallback. A `.bak` file can be useful for manual recovery, but TwitchCraft does not treat every backup as authoritative. Before restoring:

1. Close TwitchCraft.
2. Make a copy of the entire `%APPDATA%\TwitchCraftBot` directory.
3. Inspect filenames carefully; never post their contents publicly.
4. Restore only the file you intend to recover.
5. Start TwitchCraft and verify settings before launching a server.

## Sensitive values

Never share:

- Twitch bot tokens
- RCON passwords
- Authorization headers
- Full `config.json` contents
- Private databases or worlds
- Public IP addresses unless disclosure is intentional
- Unreviewed logs

Reset a Twitch token immediately if it may have been exposed. Change an RCON password and restart the remote server if that password may have leaked.
