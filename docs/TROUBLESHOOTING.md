# TWITCHCRAFT TROUBLESHOOTING

## ACCESSING LOG FILE

1. A log file will be created when an error or warning occurs inside TwitchCraft
2. You can find the log file at:

   * `AppData --> Roaming --> TwitchCraftBot --> TwitchCraftBot.log`
3. If the log file reaches 1 MB in size, previous log entries will be moved to files called:

   * `TwitchCraftBot.log.1` through `TwitchCraftBot.log.5`
4. Only a maximum of 6 MB of log data can be stored
5. TwitchCraft automatically removes Twitch and RCON secrets from the log file. However, you should still review the log before sharing it, as error information may contain other personal information

## INVALID TOKEN OR LONG ERROR ON STARTUP

1. Repeat the steps in the Getting A Bot Token section in the README
2. Paste the token into `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
3. Replace:

   * `"BotToken": "INSERT_TOKEN_HERE"`

## JAVA OR SERVER JAR ERROR

1. Open the Setup page in TwitchCraft
2. Make sure the correct Java / JDK version is installed for your Minecraft version
3. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
4. Check that these paths point to real files or folders:

   * `"ExecutablePath"`
   * `"ServerDirectory"`
   * `"JarPath"`
5. If the `server.jar` download fails, check your internet connection and run Setup again
6. If the downloaded server jar fails verification, delete the bad `server.jar` and download it again

## START BUTTON IS DISABLED

1. Hover over the disabled Start button
2. Read the tooltip telling you why TwitchCraft cannot start yet
3. Make sure:

   * Your Minecraft username is entered and valid
   * The server address / port is valid
   * A world import is not still running
   * TwitchCraft is not already starting

## TWITCHCRAFT CRASHING OR NO ERROR REASON

1. Open Task Manager
2. Check for any existing instances of:

   * `TwitchCraft.exe`
   * `javaw.exe`
   * `java.exe`
3. Close them if they are already running
4. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
5. Find:

   * `"StreamerName"`
   * `"BotName"`
6. Make sure the Twitch username of the account you are streaming on is correctly entered
7. Make sure the username of your Twitch bot is correctly entered
8. If the crashing still continues, make sure:

   * All TwitchCraft files are installed
   * All files are in the correct location
   * You followed the Java / JDK Setup in the README

## SERVER STARTS THEN CLOSES

1. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
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
3. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
4. Make sure these names are correct:

   * `"StreamerName"` is the channel you are streaming on
   * `"BotName"` is the Twitch bot account
5. Make sure the Minecraft username entered on the Start page is correct
6. Wait until the Minecraft server is fully loaded before testing commands
7. If a command costs tokens, make sure the viewer has enough tokens

## VIEWER LIST OR PLAYER LIST NOT UPDATING

1. Make sure the bot is a moderator in your Twitch chat
2. If Twitch chatters stop updating, repeat the bot token setup steps
3. If Minecraft players stop updating, restart the Minecraft server and TwitchCraft
4. If you manually edited `server.properties`, make sure it contains:

   * `enable-query=true`
   * `query.port` matches your Minecraft server port
5. Do not change query or RCON settings while the server is already running
6. Wait a few seconds after a player joins or leaves because TwitchCraft refreshes player snapshots in the background

## MULTIPLAYER NOT WORKING FOR OTHERS

1. Enable Multiplayer before pressing Start
2. Remember that Multiplayer does not stay enabled after reopening TwitchCraft
3. Give other people your public IP address or server domain, not `localhost` or `127.0.0.1`
4. Port forward the Minecraft server port in your router if needed
5. Allow `java.exe` and `javaw.exe` through Windows Firewall
6. Make sure the port you give people, if needed, matches the Port setting in TwitchCraft
7. Restart TwitchCraft after changing ports or firewall settings

## REMOTE CONTROL MODE OR RCON WILL NOT CONNECT

1. Only use Remote Control Mode if you want to gain control of an already running server
2. Press `Ctrl + Alt + R` on the Start page to show Remote Control Mode options
3. Make sure the remote host server has RCON enabled in `server.properties`:

   * `enable-rcon=true`
   * `rcon.port=THE_RCON_PORT`
   * `rcon.password=THE_RCON_PASSWORD`
4. Enter the remote server host in TwitchCraft without extra spaces
5. Enter a valid RCON port from 1 to 65535
6. Make sure the RCON password in TwitchCraft exactly matches the server RCON password
7. Restart the host Minecraft server after changing RCON settings
8. Remember that Remote Control Mode does not stay enabled after reopening TwitchCraft

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

## STATISTICS OR TOKENS NOT SAVING

1. Close TwitchCraft normally so it can finish saving data
2. Open the TwitchCraftBot folder at:

   * `AppData --> Roaming --> TwitchCraftBot`
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

   * Make sure passive token earning is enabled if viewers are not gaining passive coins

## SETTINGS DO NOT STAY ENABLED AFTER REOPENING

1. Multiplayer and Remote Control Mode are startup choices
2. Enable Multiplayer again before pressing Start if you want a multiplayer server
3. Press `Ctrl + Alt + R` again if you need Remote Control Mode after reopening TwitchCraft
4. Other Settings page options should still save normally
5. If normal settings do not save, close TwitchCraft and check that `config.json` is not read-only or broken

## CONFIG.JSON COULD NOT BE READ

1. Close TwitchCraft before editing `config.json`
2. Open `config.json` at:

   * `AppData --> Roaming --> TwitchCraftBot --> config.json`
3. Check for missing commas, missing quotes, or extra brackets
4. If you are not sure what changed, restore a backup of `config.json`
5. If there is no backup, run Setup again to recreate the config file

## TWITCHCRAFT OR MINECRAFT SERVER IS LAGGING OR LOW FPS

1. Open Task Manager
2. Close any background applications that are using high amounts of CPU, GPU, or RAM
3. Find the Dangerous Settings within TwitchCraft and lower the minimum and maximum RAM
4. Lower your in-game Minecraft settings, such as render distance and graphics
5. If you are still experiencing lag or low FPS, open `server.properties` at:

   * `AppData --> Roaming --> TwitchCraftBot --> MCServer --> server.properties`
6. Find the properties:

   * `entity-broadcast-range-percentage`
   * `simulation-distance`
   * `sync-chunk-writes`
   * `view-distance`
7. Lower these values if needed:

   * `entity-broadcast-range-percentage=50–75`
   * `simulation-distance=6–8`
   * `view-distance=6–10`
   * `sync-chunk-writes=false`
8. If you are still experiencing lag or low FPS, consider using a more powerful PC or reducing background usage

If this troubleshooting guide does not resolve your issue or question, please contact the creator of TwitchCraft and include your `TwitchCraftBot.log` file, if possible, along with a description of the problem.
