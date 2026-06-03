# TwitchCraft User Guide

**// TWITCHCRAFT BOT SCREENSHOT SHOWCASE**

<p align="center">
  <img src="Screenshots/1TC_Setup.png" width="700">
</p>
<p align="center">
  <img src="Screenshots/2TC_Start.png" width="700">
</p>
<p align="center">
  <img src="Screenshots/3TC_Start_MP.png" width="700">
</p>
<p align="center">
  <img src="Screenshots/4TC_Main.png" width="700">
</p>
<p align="center">
  <img src="Screenshots/5TC_Settings.png" width="700">
</p>

## 1. Requirements

- Windows 10/11
- Install the correct Java Development Kit (JDK) version for the Minecraft version you plan to run
- Use Minecraft Java Edition version 1.20.0–26.1.2

## 2. Bot Account Setup

- Register an application for the bot on the Twitch Developer Console: https://dev.twitch.tv/console
- Do not use your main Twitch account for the bot if possible
- Make sure 2FA is enabled, since it is required to register an application
- Set the OAuth Redirect URL to: `http://localhost`
- Add the bot as a moderator in your Twitch chat

## 3. Java / JDK Setup

For Minecraft versions **1.20.0–1.20.4**:

- You will need Java SE 17 (JDK 17)
- Download: https://www.oracle.com/java/technologies/javase/jdk17-archive-downloads.html
- Use the Windows x64 Installer version
- Any JDK 17.x version should work

For Minecraft versions **1.20.5–1.21.11**:

- You will need Java SE 21 (JDK 21)
- Download: https://www.oracle.com/java/technologies/downloads/#jdk21-windows
- Use the Windows x64 Installer version
- Any JDK 21.x version should work

For Minecraft versions **26.1–26.1.2**:

- You will need Java SE 25 (JDK 25)
- Download: https://www.oracle.com/java/technologies/downloads/#jdk25-windows
- Use the Windows x64 Installer version
- Any JDK 25.x version should work

## 4. Getting A Bot Token

1. Open `GetBotToken.exe`
2. Enter your bot's Client ID
   - This can be found at: https://dev.twitch.tv/console
3. Enter the Redirect URL: `http://localhost`
4. Get a new Twitch token

## 5. Starting TwitchCraft

1. Extract the `.zip` to a folder
2. Do not remove any files from the extracted folder
3. Open `TwitchCraft.exe`
4. Enter your:
   - Minecraft version
   - Server address
   - Twitch Client ID
   - Twitch bot token
   - Twitch channel / streamer name
   - Twitch bot name
   - Minecraft username
5. If you want to use an existing world, import it before starting
6. Wait for any world import to finish
7. Click **Start**

**Notes:**

- If the Start button is disabled, hover over it to see why you cannot start yet
- Make sure the Minecraft username is entered correctly
- Make sure the server port is between 1 and 65535

## 6. Joining The Server As The Streamer

1. Make sure you are on the same Minecraft version TwitchCraft is running on
2. Open the Multiplayer tab in Minecraft
3. Add a new server
4. Enter this server address:

   ```text
   127.0.0.1
   ```

   Unless you specified a different address.

5. Wait for TwitchCraft and the Minecraft server to fully start before joining the server

## 7. Multiplayer Optional

Enabling multiplayer in TwitchCraft does not automatically make your server publicly reachable.

These steps work for most routers, but menu names and settings may vary:

1. Enable multiplayer in TwitchCraft before starting the server
2. Find your local IP and Default Gateway
   1. Press **Win + R**
   2. Type: `cmd`
   3. Type: `ipconfig`
   4. Find:
      - `"IPv4 Address"`
      - The number-only `"Default Gateway"` example: `192.168.1.1`
3. Open your router login page
   - Type your Default Gateway into a browser
4. Log in to your router
   - You may need your router username and password
5. Find the router setting named one of the following:
   - Port Forwarding
   - NAT
   - Virtual Server
6. Forward TCP port `25565` to your local IP
7. Allow Java through Windows Firewall
8. Have friends connect using your public IP
   - Search `"what is my ip"` in a browser

**Notes:**

- If someone is on the same network as you, they should use your local IP instead
- Some ISPs or router setups may require extra steps

## 8. Server Features

- Twitch chat messages are sent into Minecraft
- Typing `/trigger locateplayers` in Minecraft chat lets you see the coordinates of all players when in multiplayer
- A player list sidebar is shown in multiplayer
- Player health bars are displayed in the tab list when in multiplayer

## 9. Tokens

**Real token file location:**

```text
AppData --> Roaming --> TwitchCraftBot --> viewer_tokens.db
```

**Readable token export location:**

```text
AppData --> Roaming --> TwitchCraftBot --> exports --> viewer_tokens.json
```

**Notes:**

- Viewer tokens are stored in `viewer_tokens.db`
- The exported `viewer_tokens.json` file is only for viewing
- Editing the exported JSON file will not affect TwitchCraft
- Do not edit `viewer_tokens.db` while TwitchCraft is active
- If you manually adjust tokens, edit `viewer_tokens.db` with a SQLite editor while TwitchCraft is shut down

**Other ways to manage tokens:**

While TwitchCraft is active, you can use these Twitch chat commands:

```text
!givetokens [<user>] [<amount>]
!removetokens [<user>] [<amount>]
```

**Token earning:**

- All viewers who have your stream open in a browser or the Twitch app earn 1 token every 30–60 seconds

## 10. Statistics

**Statistics file location:**

```text
AppData --> Roaming --> TwitchCraftBot --> statistics.db
```

**Readable statistics export locations:**

```text
AppData --> Roaming --> TwitchCraftBot --> exports --> statistics.json
AppData --> Roaming --> TwitchCraftBot --> exports --> statistics_viewers.json
```

**Notes:**

- Statistics can be enabled or disabled in Settings
- Existing saved stats are still shown when statistics are disabled, but new stats are not counted
- Reset Statistics clears the saved statistics after confirmation
- Editing the exported statistics JSON files will not affect TwitchCraft
- Do not edit `statistics.db` while TwitchCraft is active
- If you manually adjust statistics, edit `statistics.db` with a SQLite editor while TwitchCraft is shut down

**Statistics track:**

- Game commands run
- Most used command
- Coins spent
- Effects received by the streamer
- Nicest viewer
- Most dangerous viewer
- Deaths
- Time survived
- Sessions started

## 11. Troubleshooting

### Invalid Token Or Long Error On Startup

1. Repeat the steps in the **Getting A Bot Token** section
2. Paste the token into `config.json` at:

   ```text
   AppData --> Roaming --> TwitchCraftBot --> config.json
   ```

3. Replace:

   ```json
   "BotToken": "INSERT_TOKEN_HERE"
   ```

### Missing Or Not Found Error

1. Open `config.json` at:

   ```text
   AppData --> Roaming --> TwitchCraftBot --> config.json
   ```

2. Find:
   - `"ServerDirectory"`
   - `"JarPath"`
3. Make sure your Windows username is correctly entered both times in:

   ```text
   C:\Users\YOUR_USER\
   ```

   Specifically between `Users\` and `\AppData`.

### Start Button Is Disabled

1. Hover over the disabled Start button
2. Read the tooltip telling you why TwitchCraft cannot start yet
3. Make sure:
   - Your Minecraft username is entered
   - Your Minecraft username is valid
   - The server address / port is valid
   - A world import is not still running
   - TwitchCraft is not already starting

### TwitchCraft Crashing Or No Error Reason

1. Open Task Manager
2. Check for any existing instances of:
   - `TwitchCraft.exe`
   - `javaw.exe`
   - `java.exe`
3. Close them if they are already running
4. Open `config.json` at:

   ```text
   AppData --> Roaming --> TwitchCraftBot --> config.json
   ```

5. Find:
   - `"StreamerName"`
   - `"BotName"`
6. Make sure the Twitch username of the account you are streaming on is correctly entered
7. Make sure the username of your Twitch bot is correctly entered
8. If the crashing still continues, make sure:
   - All TwitchCraft files are installed
   - All files are in the correct location
   - You followed the Java / JDK Setup instructions above

## 12. Changing The Bot Account Or Application

- If you change the account or application used by your bot on the Twitch Developer Console, you must delete or update `config.json`
- After doing this, restart TwitchCraft
- If you delete `config.json`, you must redo the bot setup
- If you have a saved world on the TwitchCraft Minecraft server, it may be affected
- Changing the bot account does not delete `viewer_tokens.db` or `statistics.db`

## 13. Links

- TwitchCraft website: https://antnygamer.wixsite.com/twitchcraft
- TwitchCraft commands: https://rentry.co/bot-commands
- TwitchCraft trailer: https://www.youtube.com/watch?v=HM2Um3Uf1hk
- TwitchCraft setup tutorial: https://bit.ly/twitchcraft-tutorial

## 14. Other Info

- Never share your Twitch bot token publicly. Anyone with the token may be able to act as your bot account until the token is reset.
- TwitchCraft creates backup `.bak` config files. These are for reference and are not normally used by TwitchCraft.
- Special thanks to Lil_KleinStein, whose Minecraft streams inspired TwitchCraft's theme and creation!
