# Privacy

TwitchCraft is a local desktop application. It does not use a TwitchCraft-operated server for analytics, telemetry, accounts, or cloud storage, and it does not automatically send your local configuration, databases, logs, or Minecraft files to the developer.

## Data TwitchCraft uses

TwitchCraft processes only the data needed for the features you use.

### Twitch data

When Twitch features are enabled, TwitchCraft may process:

- Twitch authorization access and refresh tokens
- The authorized bot account name
- The configured streamer/channel name
- Twitch usernames and user IDs
- Twitch chat messages and commands
- The current viewer/chatter roster
- Follow events, including follower username, user ID, and follow time
- Bits events
- Viewer token balances and statistics derived from TwitchCraft activity

Your Twitch password is never provided to TwitchCraft. Authorization is handled by Twitch.

Chat messages, the current viewer roster, and event data are processed while TwitchCraft is running so commands, rewards, displays, and optional Minecraft chat relay can work. TwitchCraft does not intentionally keep a permanent chat-history database.

### Minecraft and application data

Depending on how TwitchCraft is configured, it may also store or process:

- The streamer's Minecraft username
- Minecraft server paths, version, IP/host, ports, and settings
- RCON connection information and passwords
- Minecraft server output and commands
- TwitchCraft preferences and command settings
- Local Minecraft server and world files when TwitchCraft manages the server
- Diagnostic warnings and errors

## What is stored locally

TwitchCraft stores user-specific data under:

```text
%APPDATA%\TwitchCraft
```

This directory can include:

- `config.json` — settings, Twitch authorization tokens, Twitch account/channel names, Minecraft identity, server configuration, and RCON information
- `viewer_tokens.db` — viewer usernames and token balances, plus rewarded-follow records containing Twitch user IDs, usernames, and follow times
- `statistics.db` — TwitchCraft statistics, including viewer usernames and viewer scores
- `exports\` — readable JSON exports of token balances and statistics
- `backups\` — automatic copies of `config.json` and `viewer_tokens.db`
- `logs\` — local diagnostic logs
- The managed Minecraft server directory and world data, when applicable

Authorization tokens, RCON passwords, databases, backups, and configuration files should be treated as private.

## Retention

Local data remains on the computer until TwitchCraft replaces it through normal operation or the user deletes it.

Viewer token balances, follow-reward records, and statistics are intentionally persistent so they survive restarts. Automatic backup retention is configurable and defaults to three complete backup sets. Diagnostic logs rotate automatically instead of growing without limit.

Changing or reauthorizing the Twitch bot account does not automatically delete viewer token balances or statistics.

## Data sent to other services

TwitchCraft communicates directly with services needed for its features:

- **Twitch** — for authorization, chat, viewer/chatter information, user lookup, follow events, Bits, and related Twitch functionality
- **Mojang/Minecraft** — to retrieve Minecraft version information and official server files when setting up a managed local server
- **Minecraft servers** — for commands, chat relay, and RCON when those features are used

TwitchCraft does not sell user data. It does not automatically upload local databases, configuration files, logs, statistics, worlds, or authorization tokens to the TwitchCraft developer.

If a user manually shares files, logs, screenshots, exports, or diagnostic information with another person, that sharing is outside TwitchCraft's automatic data handling. Review files before sharing them because they may contain private information.

## How to delete your data

### Delete ALL TwitchCraft data from your computer

1. Close TwitchCraft completely.
2. If you want to keep a locally managed Minecraft world, copy the world or server files somewhere outside `%APPDATA%\TwitchCraft` first.
3. Delete the entire `%APPDATA%\TwitchCraft` folder.
4. To prevent the saved Twitch authorization from being used again, also revoke TwitchCraft's authorization from your Twitch account settings.

Deleting the entire folder removes TwitchCraft's local configuration, saved authorization tokens, viewer-token database, statistics database, exports, backups, logs, and any managed Minecraft server/world files stored there.

### Delete one viewer's stored Twitch-linked data

TwitchCraft does not operate a central database of viewer information. Viewer data is stored on the computer running TwitchCraft. A viewer who wants their locally stored Twitch-linked data removed must ask the person running that TwitchCraft installation to remove it.

With TwitchCraft closed, the operator should remove that viewer's records from:

- `TokenBalances` and `RewardedFollows` in `viewer_tokens.db`
- `ViewerScores` in `statistics.db`
- `exports\viewer_tokens.json` and `exports\statistics_viewers.json` by deleting the exports and allowing TwitchCraft to regenerate them
- Any retained backup containing an older copy of `viewer_tokens.db` if complete removal from backups is also required

The TwitchCraft developer cannot remotely access or erase data stored on another user's computer.

## Security and responsibility

Keep Twitch authorization tokens, RCON passwords, configuration files, databases, backups, logs, and Minecraft server files private. Do not post them publicly without reviewing and removing sensitive information first.

TwitchCraft's source code is available for inspection so users can verify how the application handles data.
