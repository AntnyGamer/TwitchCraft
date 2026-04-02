**// TWITCHCRAFT BOT**

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

**// HOW TO USE:**

**// REQUIREMENTS:**
- Windows 10/11
- Install the correct Java Development Kit (JDK) version for the Minecraft version you plan to run
- Use Minecraft Java Edition version 1.20.0–1.21.4 (1.20.4 preferred)

**// BOT ACCOUNT SETUP:**
- Register an application for the bot on the Twitch Developer Console (https://dev.twitch.tv/console)
- Do not use your main Twitch account for the bot if possible
- Make sure 2FA is enabled, since it is required to register an application
- Set the OAuth Redirect URL to http://localhost
- Add the bot as a moderator in your Twitch chat

**// JDK SETUP:**
- For Minecraft versions 1.20.0–1.20.4:
  - You will need Java SE 17 (JDK 17)
  - Download: https://www.oracle.com/java/technologies/javase/jdk17-archive-downloads.html
  - Use the Windows x64 Installer version
  - Any JDK 17.x version should work

- For Minecraft versions 1.20.5–1.21.4:
  - You will need Java SE 21 (JDK 21)
  - Download: https://www.oracle.com/java/technologies/downloads/#jdk21-windows
  - Use the Windows x64 Installer version
  - Any JDK 21.x version should work

**// GETTING A BOT TOKEN:**
1. Open GetBotToken.exe
2. Enter your bot's Client ID
   - This can be found at: https://dev.twitch.tv/console
3. Enter the Redirect URL: http://localhost
4. Get a new Twitch token

**// STARTING TWITCHCRAFT:**
1. Extract the .zip to a folder
2. Do not remove any files from the extracted folder
3. Open TwitchCraft.exe
4. Enter your:
   - Minecraft version
   - Server address
   - Twitch bot token
   - Twitch Client ID
   - Twitch channel / streamer name
   - Twitch bot name
5. If you want to use an existing world, import it before starting
6. Click Start

**// JOINING THE SERVER (AS THE STREAMER):**
1. Make sure you are on the same Minecraft version TwitchCraft is running on
2. Open the Multiplayer tab in Minecraft
3. Add a new server
4. Enter this server address: 127.0.0.1 (unless you specified a different address)
5. Wait for TwitchCraft and the Minecraft server to fully start before joining the server

**// MULTIPLAYER (OPTIONAL):**

_Enabling multiplayer in TwitchCraft does not automatically make your server publicly reachable._

_These steps work for most routers, but menu names and settings may vary:_

1. Enable multiplayer in TwitchCraft before starting the server
2. Find your local IP and Default Gateway
   1. Press Win + R
   2. Type: cmd
   3. Type: ipconfig
   4. Find:
      - "IPv4 Address"
      - the number-only "Default Gateway" (example: 192.168.1.1)
3. Open your router login page
   - Type your Default Gateway into a browser
4. Log in to your router
   - You may need your router username and password
5. Find the router setting named one of the following:
   - Port Forwarding
   - NAT
   - Virtual Server
6. Forward TCP port 25565 to your local IP
7. Allow Java through Windows Firewall
8. Have friends connect using your PUBLIC IP
   - Search "what is my ip" in a browser

_Notes:_
- If someone is on the same Wi-Fi as you, they should use your local IP instead
- Some ISPs or router setups may require extra steps

**// SERVER FEATURES:**
- Twitch chat messages are sent into Minecraft
- Typing `/trigger locateplayers` in Minecraft chat lets you see the coordinates of all players when in multiplayer
- A player list sidebar is shown in multiplayer
- Player healthbars are displayed in the tab list when in multiplayer

**// TOKENS:**
- Token file location: AppData --> Roaming --> TwitchCraftBot --> viewer_tokens.json
- You can manually adjust token amounts in this file as long as TwitchCraft is not active
- If you manually adjust token amounts, make sure the formatting stays correct
- Do not add a comma after the last entry
- You only need the opening { and closing } at the start and end of the file

- FORMAT: <br>
{<br>
"VIEWER_NAME": TOKEN_AMOUNT,<br>
"VIEWER_NAME": TOKEN_AMOUNT,<br>
"VIEWER_NAME": TOKEN_AMOUNT<br>
}

- Other ways to manage tokens:
  - While TwitchCraft is active, you can use these Twitch chat commands:
    <br>`!givetokens [<user>] [<amount>]`
    <br>`!removetokens [<user>] [<amount>]`

- Token earning:
  - All viewers who have your stream open in a browser or the Twitch app earn 1 token every 30–60 seconds

**// TROUBLESHOOTING:**

**INVALID TOKEN OR LONG ERROR ON STARTUP**
1. Repeat the steps in the Getting A Bot Token section
2. Paste the token into config.json at:
   AppData --> Roaming --> TwitchCraftBot --> config.json
3. Replace:
   `"BotToken": "INSERT_TOKEN_HERE"`

**MISSING OR NOT FOUND ERROR**
1. Open config.json at:
   AppData --> Roaming --> TwitchCraftBot --> config.json
2. Find:
   - `"ServerDirectory"`
   - `"JarPath"`
3. Make sure your Windows username is correctly entered both times in:
   <br>`C:\\Users\\YOUR_USER\\`
   <br>specifically between `Users\\` and `\\AppData`

**TWITCHCRAFT CRASHING OR NO ERROR REASON**
1. Open Task Manager
2. Check for any existing instances of:
   - TwitchCraft.exe
   - javaw.exe
3. Close them if they are already running
4. Open config.json at:
   AppData --> Roaming --> TwitchCraftBot --> config.json
5. Find:
   - `"StreamerName"`
   - `"BotName"`
6. Make sure the Twitch username of the account you are streaming on is correctly entered
7. Make sure the username of your Twitch bot is correctly entered
8. If the crashing still continues, make sure:
   - All TwitchCraft files are installed
   - All files are in the correct location
   - You followed the JDK 17 / JDK 21 instructions above

**// CHANGING THE BOT ACCOUNT OR APPLICATION:**
- If you change the account or application used by your bot on the Twitch Developer Console, you must delete or update config.json
- After doing this, restart TwitchCraft
- If you delete config.json, you must redo the bot setup
- If you have a saved world on the TwitchCraft Minecraft server, it may be affected

**// LINKS:**
- TwitchCraft website:
  https://antnygamer.wixsite.com/twitchcraft
- TwitchCraft commands:
  https://rentry.co/bot-commands
- TwitchCraft trailer:
  https://www.youtube.com/watch?v=HM2Um3Uf1hk
- TwitchCraft setup tutorial:
  https://bit.ly/twitchcraft_tutorial

**// OTHER INFO:**
- Special thanks to Lil_KleinStein, whose Minecraft streams inspired the theme and creation of TwitchCraft!