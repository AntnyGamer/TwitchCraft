# Troubleshooting

Before changing files, close TwitchCraft and make a backup of `%APPDATA%\TwitchCraftBot`.

## TwitchCraft does not open

**Likely causes:** incomplete extraction, blocked download, missing runtime dependencies, or a startup exception.

**Check:** extract the complete release to a writable folder, confirm Windows 10/11 64-bit, and look for the latest TwitchCraft diagnostic log under `%APPDATA%\TwitchCraftBot`.

**Fix:** re-extract a trusted release, keep distributed files together, and retry. Do not download replacement DLLs from unrelated sites.

## The bot does not join Twitch chat

**Likely causes:** expired/revoked token, wrong bot name, wrong channel, incorrect Client ID, or network/TLS filtering.

**Check:** verify the bot account and channel spelling and generate a fresh token with the configured Twitch application.

**Fix:** update the token in TwitchCraft and restart. Never post the old or new token while asking for help.

## The bot joins, but commands do nothing

**Likely causes:** Minecraft is not ready, command spelling/arguments are invalid, target is offline, moderator permission is disabled, or a cooldown is active.

**Check:** try `!tokens`, `!playerlist`, and a low-cost command; read the bot's Twitch response and the TwitchCraft server/chat panels.

**Fix:** wait for server readiness, use the exact syntax in [COMMANDS.md](COMMANDS.md), and verify the target and permission settings.

## The Minecraft server does not start

**Likely causes:** wrong Java version, invalid/missing server jar, locked server directory, insufficient memory, port conflict, or a damaged world.

**Check:** match Java to the table in [INSTALLATION.md](INSTALLATION.md), confirm no stale Java server uses the same folder/port, and inspect the latest sanitized log.

**Fix:** correct Java selection, close the stale process, verify the jar, reduce memory to a value the computer can supply, or test with a backed-up clean world.

## TwitchCraft reports the wrong Java version

**Check:** run the selected `java.exe -version` and inspect `JAVA_HOME` and `PATH`.

**Fix:** install the required 64-bit JDK and select its executable/home path. Multiple installed JDKs are allowed if TwitchCraft points to the intended one.

## RCON authentication fails

**Likely causes:** RCON disabled, mismatched password/port, server not restarted, or firewall/tunnel misconfiguration.

**Fix:** follow [REMOTE-CONTROL.md](REMOTE-CONTROL.md). Do not expose the RCON port publicly as a troubleshooting shortcut.

## Friends cannot join

**Check:** confirm server readiness, matching Minecraft version, game port, Windows Firewall, router forwarding or VPN, and whether the ISP uses carrier-grade NAT.

**Fix:** follow [MULTIPLAYER.md](MULTIPLAYER.md). Forward the game port only; RCON is not required for players.

## A command charged tokens but appeared to fail

**Check:** read Twitch chat and the server panel. TwitchCraft refunds a paid command when dispatch reports failure. A command accepted by the server may still have no visible effect because of game state, target state, permissions, or version syntax.

**Fix:** capture the command name, target mode, Minecraft version, local/remote mode, time, and sanitized diagnostic event. Do not manually edit the database while TwitchCraft is running.

## The token or statistics database is locked

**Likely causes:** another TwitchCraft process, a database browser, backup software, or a previous process that has not exited.

**Fix:** close TwitchCraft and every database tool, verify no duplicate process remains, then retry. Preserve `.db`, `.db-wal`, and `.db-shm` together when making a live-state backup.

## World import fails

**Check:** select the actual world folder containing `level.dat`, verify free disk space, close Minecraft/server processes using the folder, and confirm access permissions.

**Fix:** retry from a copied world backup. Never use the only copy of a world as the import source.

## Sharing diagnostics safely

Include:

- TwitchCraft version
- Windows version
- Minecraft and Java versions
- Local or remote mode
- Singleplayer or multiplayer
- Exact reproduction steps and time of failure
- The smallest relevant sanitized log excerpt

Remove or replace:

- Twitch bot tokens and OAuth strings
- RCON passwords
- Authorization headers
- Client secrets
- Public IP addresses and private hostnames when unnecessary
- Full local usernames/paths when unnecessary
- Raw private chat, databases, worlds, and `config.json`

If a secret may have been shared, rotate it before continuing the support conversation.
