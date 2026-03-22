**// HOW TO USE:**

**// REQUIREMENTS:**
- Windows 10/11
- Make sure you install the correct Java Development Kit (JDK) version for the Minecraft version you plan to run
- Make sure you are on Minecraft Java Edition version 1.20.0–1.21.4 (1.20.4 preferred)

**// BOT ACCOUNT AND APPLICATION SETUP:**
- You need to register an application for the bot (should not be on your main Twitch account) on the Twitch Developer Console (https://dev.twitch.tv/console)
- Ensure 2FA is enabled as it is necessary for registering an application
- The OAuth Redirect URL of the bot should be http://localhost
- Add the bot as a moderator in your Twitch chat

**// TOKENS:**
- The file for tokens can be found at AppData --> Roaming --> TwitchCraftBot --> viewer_tokens.json
- You can manually adjust token amounts within this file as long as the bot is not active
- FORMAT: <br>
{<br>
"VIEWER_NAME": TOKEN_AMOUNT,<br>
"VIEWER_NAME": TOKEN_AMOUNT,<br>
"VIEWER_NAME": TOKEN_AMOUNT<br>
}
- Do not add a comma after the last entry
- You only need the { and } at the start and end of the file respectively
- If you manually adjust token amounts, make sure you keep formatting correct
- You can also give tokens to viewers while the bot is active by sending commands in your Twitch chat (`!givetokens [<user>] [<amount>]` and `!removetokens [<user>] [<amount>]`)
- All viewers who have your stream open in a browser or the Twitch app earn 1 token every 30–60 seconds.

**// JDK17:**
   - For versions 1.20.0–1.20.4, you may need to download Java SE 17 (jdk17) from https://www.oracle.com/java/technologies/javase/jdk17-archive-downloads.html
   - Use the Windows x64 Installer version of Java SE Development Kit (JDK) 17. Any JDK 17.x version should work.

**// JDK21:**
   - For versions 1.20.5–1.21.4, you may need to download Java SE 21 (jdk21) from https://www.oracle.com/java/technologies/downloads/#jdk21-windows
   - Use the Windows x64 Installer version of Java SE Development Kit (JDK) 21. Any JDK 21.x version should work.

**// JOINING THE SERVER (AS THE STREAMER):**
   1. Make sure you are on the same Minecraft version the bot is running on
   2. On the Multiplayer tab, add a new server
   3. The server address is 127.0.0.1 (unless you specified a different address)

**// MULTIPLAYER (NOT REQUIRED):**  
_Making your server multiplayer is harder than just checking the Multiplayer checkbox. These steps work for most home routers, but menu names and settings may vary:_
1. Find your local IP and Default Gateway:
   1. Press Win + R
   2. Type cmd
   3. Type ipconfig
   4. Find "IPv4 Address" and the number-only "Default Gateway" (example: 192.168.1.1)
2. Open your router login by typing your Default Gateway into a browser
3. Log in to your router if required (you may need to know your router username and password)
4. Find Port Forwarding / NAT / Virtual Server
5. Forward TCP port 25565 to your local IP
6. Allow Java through Windows Firewall
7. Friends connect using your PUBLIC IP (search "what is my ip" in a browser)

_If on the same WiFi, use your local IP instead_  
_Some ISPs or router setups may require extra steps_

**// FEATURES IN THE SERVER:**
1. Any chat messages from your Twitch stream are sent into Minecraft
2. Typing /trigger locateplayers in the Minecraft chat will allow you to see the coordinates of all players when in Multiplayer
3. A player list sidebar and player healthbars displayed in the tab list when in Multiplayer

**// IF YOU GET AN INVALID TOKEN OR LONG ERROR UPON STARTING:**
1. Get a new Twitch token by opening the file GetBotToken.exe
2. Enter your bot's Client ID (can be found on https://dev.twitch.tv/console after creating your bot)
3. Enter the Redirect URL, which is http://localhost
4. Paste the token into config.json (AppData --> Roaming --> TwitchCraftBot --> config.json) where it says `"BotToken": "INSERT_TOKEN_HERE"`

**// MISSING OR NOT FOUND ERROR:**
1. Open config.json (AppData --> Roaming --> TwitchCraftBot --> config.json)
2. Find the values for "ServerDirectory" and "JarPath"
3. Ensure that your Windows username is correctly entered both times where it says `C:\\Users\\YOUR_USER\\` (between `Users\\` and `\\AppData`)

**// BOT CRASHING OR NO ERROR REASON:**
1. Check Task Manager for any instances of the bot or javaw.exe already running and close them
2. Open config.json (AppData --> Roaming --> TwitchCraftBot --> config.json)
3. Find the values for "StreamerName" and "BotName"
4. Ensure that the Twitch username of the account you are streaming on is correctly entered
5. Ensure that the username of your Twitch bot is correctly entered
6. If the crashing still persists, ensure you have all files associated with the bot installed and in the correct location, and refer to JDK17/21 instructions above

**// CHANGING THE BOT ACCOUNT OR APPLICATION:**
- When changing the account or application of your bot on the Twitch Developer Console, you must delete or update the config.json file
- After doing this, you must restart the bot
- If you delete the config.json file, you must redo the bot setup
- If you have a world saved on the TwitchCraft Minecraft server, it may be affected

_Commands for the bot can be found at https://rentry.co/bot-commands_  
_Special thanks to Lil_KleinStein, whose Minecraft streams inspired the theme and creation of this bot._

Watch the trailer for the mod here!  
[TwitchCraft - Trailer](https://www.youtube.com/watch?v=HM2Um3Uf1hk)
