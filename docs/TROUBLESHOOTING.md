# TWITCHCRAFT TROUBLESHOOTING

## ACCESSING LOG FILE

1. Open **Help --> Open Logs Folder**, or open the log directory manually at:

   * `AppData --> Roaming --> TwitchCraft --> logs`
2. `TwitchCraft.log` is created when TwitchCraft records an error or warning
3. If the log file reaches 1 MB in size, previous log entries will be moved to files called:

   * `TwitchCraft.log.old1` through `TwitchCraft.log.old4`
4. Up to five log files are retained: the current file and four older files, each limited to about 1 MB
5. Use **Help --> Copy Diagnostics** for a non-secret system/status summary when reporting a problem
6. Review any log excerpt before sharing it, as error information may contain private or personal information

## START BUTTON IS DISABLED

On the Setup screen:

1. Hover over the disabled **Start** button and read the tooltip
2. Select a Minecraft version and enter a valid bind IP and Twitch channel name
3. Select **Authorize Twitch** and wait for the authorized bot account to appear

On the Start screen, make sure:

   * Your Minecraft username is entered and valid
   * In Remote Control Mode, the remote host and RCON port are valid and an RCON password is entered
   * A world import is not still running
   * TwitchCraft is not already starting

## INVALID TOKEN OR LONG ERROR ON STARTUP

1. Open **Settings --> Dangerous --> Reauthorize Twitch** (or **Authorize Twitch** if no authorization is currently saved)
2. Approve Twitch device authorization again in the browser
3. Do not paste a token into `config.json`; TwitchCraft saves and renews its hidden authorization automatically
4. TwitchCraft uses its built-in public Twitch application; do not create a developer application or add a Client ID, Client Secret, or localhost redirect
5. If the error says this build is missing TwitchCraft's public Client ID, reinstall a complete official build

## JAVA OR SERVER JAR ERROR

1. Make sure the correct Java / JDK version is installed for your Minecraft version
2. If TwitchCraft is already configured and you need to rerun initial Setup, open **Settings --> Dangerous --> Delete Config**. After confirmation, TwitchCraft restarts into Setup. This deletes the app configuration, not your Minecraft world or token/statistics databases
3. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraft --> config.json`
4. Check that these paths point to real files or folders:

   * `"ExecutablePath"`
   * `"ServerDirectory"`
   * `"JarPath"`
5. If the Minecraft server JAR download fails, check your internet connection and rerun initial Setup using **Settings --> Dangerous --> Delete Config** if needed
6. If the downloaded server JAR fails verification, delete the bad `twitchcraft-server-VERSION.jar` file from the `MCServer` folder and rerun initial Setup the same way

## CONFIG.JSON COULD NOT BE READ

1. Close TwitchCraft before editing `config.json`
2. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraft --> config.json`
3. Check for missing commas, missing quotes, or extra brackets
4. If you are not sure what changed, restore `config.json` and `viewer_tokens.db` from the same timestamped folder under `TwitchCraft\backups`
5. If there is no complete automatic backup, run Setup again to recreate the config file

## TWITCHCRAFT CRASHING OR NO ERROR REASON

1. Open Task Manager
2. Check for any existing instances of:

   * `TwitchCraft.exe`
   * `javaw.exe`
   * `java.exe`
3. Close them if they are already running
4. On the Setup page, make sure **Twitch Channel Name** is the channel you are streaming on
5. Make sure **Authorized Bot Account** shows the bot account you intended to use
6. If the bot account is wrong or authorization looks stale, open **Settings --> Dangerous --> Reauthorize Twitch** and authorize the correct bot account
7. If the crashing still continues, make sure:

   * All TwitchCraft files are installed
   * All files are in the correct location
   * You followed the Java / JDK Setup in the README

## SERVER STARTS THEN CLOSES

1. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraft --> config.json`
2. Check these settings:

   * `"MemoryMinGB"`
   * `"MemoryMaxGB"`
   * `"Port"`
   * `"RCON"`
3. Make sure minimum RAM is not higher than maximum RAM
4. Make sure RAM is between 1 GB and 256 GB
5. Make sure the Minecraft server port is between 1 and 65535
6. Make sure the Minecraft server port and RCON port are different
7. Close any other program already using the same server port

## BOT CONNECTS BUT COMMANDS DO NOTHING

1. Make sure the bot account is actually in your Twitch chat
2. Make the bot a moderator in your Twitch chat by typing:

   * `/mod BOT_NAME`
3. Make sure **Twitch Channel Name** on the Setup page is the channel you are streaming on
4. If **Authorized Bot Account** is wrong, open **Settings --> Dangerous --> Reauthorize Twitch** and authorize the correct bot account
5. Make sure the Minecraft username entered on the Start page is correct
6. Wait until the Minecraft server is fully loaded before testing commands
7. If a command costs tokens, make sure the viewer has enough tokens

## VIEWER LIST OR PLAYER LIST NOT UPDATING

1. Make sure the bot is a moderator in your Twitch chat
2. If Twitch chatters or follow rewards stop updating, open **Settings --> Dangerous --> Reauthorize Twitch** (or **Authorize Twitch** if no authorization is currently saved) and approve Twitch authorization again
3. If Minecraft players stop updating, restart the Minecraft server and TwitchCraft
4. Make sure the Minecraft server port and connection settings in TwitchCraft are correct
5. Do not change Minecraft connection or RCON settings while the server is already running
6. Wait a few seconds after a player joins or leaves because TwitchCraft refreshes player snapshots in the background

## MULTIPLAYER NOT WORKING FOR OTHERS

1. Enable Multiplayer before pressing Start
2. Remember that Multiplayer does not stay enabled after reopening TwitchCraft
3. Give trusted players the deliberately configured public endpoint, server domain, or VPN/tunnel address—not `localhost` or `127.0.0.1`
4. Port forward the Minecraft server port in your router if needed
5. Allow `java.exe` and `javaw.exe` through Windows Firewall
6. Make sure the port you give people, if needed, matches the Port setting in TwitchCraft
7. Restart TwitchCraft after changing ports or firewall settings

## LOCATEPLAYERS DATAPACK OR PLAYER COORDINATES NOT WORKING

1. Make sure Multiplayer is enabled before pressing Start
2. Restart TwitchCraft and the Minecraft server so TwitchCraft can try to install the bundled `locateplayers` datapack again
3. Make sure the current world folder and its `datapacks` folder are not read-only
4. If you manually edited or removed the `locateplayers` datapack, restart TwitchCraft so it can recreate it
5. Check `TwitchCraft.log` for the exact `locateplayers` datapack installation error
6. If the log says the bundled datapack resources are missing, reinstall a complete official build of TwitchCraft

## REMOTE CONTROL MODE OR RCON WILL NOT CONNECT

1. Only use Remote Control Mode if you want to gain control of an already running server
2. Press `Ctrl + Alt + R` on the Start page to show Remote Control Mode options
3. Make sure RCON is enabled on the remote Minecraft server
4. Enter the remote server host in TwitchCraft without extra spaces
5. Enter a valid RCON port from 1 to 65535
6. Make sure the RCON password in TwitchCraft exactly matches the server RCON password
7. Restart the host Minecraft server after changing RCON settings
8. If Twitch chat says `Minecraft RCON is unavailable`, wait a few seconds for the next health check; if the message persists, recheck the host, RCON port, password, routing, and firewall
9. A paid-command refund in Remote Control Mode means TwitchCraft did not receive a valid matching RCON command-response confirmation; Minecraft's response wording itself is intentionally not used to decide refunds
10. Remember that Remote Control Mode does not stay enabled after reopening TwitchCraft

## WORLD IMPORT WILL NOT FINISH

1. Wait for the import to finish before pressing Start
2. Make sure you selected the actual Minecraft world folder
3. The selected world folder should contain `level.dat`
4. Make sure the server folder is not read-only
5. Make sure your drive has enough free space for a temporary import and backup
6. If an import failed earlier, restart TwitchCraft and try the import again

## RESET OR RESTART DOES NOT WORK

1. Remote Control Mode cannot reset a remote server world because TwitchCraft does not own that world
2. Open Task Manager and close any stuck `java.exe` or `javaw.exe` processes
3. Make sure the Minecraft server log is not locked by another Java process
4. Make sure the server folder and world folder are not read-only
5. If reset still fails, restart your computer and try again before deleting any files manually

## SETTINGS DO NOT STAY ENABLED AFTER REOPENING

1. Multiplayer and Remote Control Mode are startup choices
2. Enable Multiplayer again before pressing Start if you want a multiplayer server
3. Press `Ctrl + Alt + R` again if you need Remote Control Mode after reopening TwitchCraft
4. Other Settings page options should still save normally
5. If normal settings do not save, close TwitchCraft and check that `config.json` is not read-only or broken

## STATISTICS OR TOKENS NOT SAVING

1. Close TwitchCraft normally so it can finish saving data
2. Open the TwitchCraft folder at:

   * `AppData --> Roaming --> TwitchCraft`
3. Make sure these database files are not deleted while TwitchCraft is running:

   * `viewer_tokens.db`
   * `statistics.db`
4. Do not edit the database files while TwitchCraft is open
5. JSON exports are for viewing only and should not be used as the real save files
6. For statistics:

   * Make sure Statistics are enabled in Settings if statistics are not changing
   * Make sure your server logs are in English
   * Do not change the wording of common Minecraft messages, such as join, leave, and death messages
7. For tokens:

   * Make sure passive token earning is enabled if viewers are not gaining passive tokens

## TWITCHCRAFT OR MINECRAFT SERVER IS LAGGING OR LOW FPS

1. Open Task Manager
2. Close any background applications that are using high amounts of CPU, GPU, or RAM
3. Open **Settings --> Performance**
4. Enable the low-resource preset if you want TwitchCraft to automatically use smaller logs and queues, slower roster and health refreshes, minimized UI pausing, and a safe relay limit
5. For Minecraft server performance, consider setting these values if needed:

   * `entity-broadcast-range-percentage=50–75`
   * `simulation-distance=6–8`
   * `view-distance=6–10`
   * `sync-chunk-writes=false` (this property can be set in `server.properties`)
6. Lower your in-game Minecraft settings, such as render distance and graphics
7. Open **Settings --> Dangerous** and lower the minimum and maximum RAM if Minecraft is using more memory than your PC can comfortably handle
8. If you are still experiencing lag or low FPS, consider using a more powerful PC or reducing background usage

If this troubleshooting guide does not resolve your issue or question, please contact the creator of TwitchCraft with the copied diagnostics and the smallest relevant sanitized excerpt from `TwitchCraft.log`, along with a description of the problem.
