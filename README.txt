// HOW TO USE:

// REQUIREMENTS:
  - Windows 10/11
  - Make sure you have Java Runtime installed from https://www.java.com/en/download/manual.jsp
  - Make sure you are on Minecraft Java Edition on Version 1.20.0-1.20.6 (1.20.4 preferred)

// BOT CREATION:
  - You need to have an application for the bot registered (preferably not on your main Twitch account) on the Twitch Developer Console (https://dev.twitch.tv/console)
  - The OAuth Redirect URL of the bot should be http://localhost
  
// TOKENS:
  - The file for tokens can be found at AppData --> Roaming --> TwitchCraftBot --> viewer_tokens.json
  - You can manually alter token amounts within this file for viewers as long as the bot is not active
  - FORMAT: {
			"VIEWER_NAME": TOKEN_AMOUNT,
			"VIEWER_NAME": TOKEN_AMOUNT,
			"VIEWER_NAME": TOKEN_AMOUNT
			}
  - You only need the { and } at the start and end of the file respectively
  - If you manually alter token amounts, make sure you keep formatting correct
  - You can also give tokens to viewers while the bot is actively by sending commands in your Twitch chat (!givetokens [<user>] [<amount>] and !removetokens [<user>] [<amount>])
  - All viewers who have your Twitch stream open within the Twitch app or a browser tab will earn 1 token every 30-60 seconds
  
// JDK17:
  - If needed, you may need to download Java SE 17 (jdk17) at https://www.oracle.com/java/technologies/javase/jdk17-archive-downloads.html
  - Use the Installer version of Java SE Development Kit 17 and make sure it is just version 17 and not 17.0.*
  
// MULTIPLAYER:
  - Making your server multiplayer is harder than just checking the Multiplayer checkbox. Here are the steps to setup a multiplayer TwitchCraft server:
  1. Find your local IP:
    1. Press Win + R
    2. Type cmd
    3. Type ipconfig
    4. Find "IPv4 Address" (example: 192.168.1.42)
  2. Log into your router by typing 192.168.1.1 into a browser
  3. Find Port Forwarding / NAT
  4. Forward TCP 25565 to your local IP
  5. Allow Java through Windows Firewall
  6. Friends connect using your PUBLIC IP (search "what is my ip" on a browser)
  - If on same WiFi, use your local IP instead

// IF YOU GET AN INVALID TOKEN OR LONG ERROR UPON STARTING:
  1. Get a new Twitch token by opening the file GetBotToken.exe
  2. Input your Client ID and Client Secret (can be found on https://dev.twitch.tv/console after creating your bot)
  3. Input the Redirect URL which is http://localhost
  4. Paste the token into config.json (AppData --> Roaming --> TwitchCraftBot --> config.json) where it says "twitch_bottoken": "INSERT_TOKEN_HERE"
  5. If this doesn't work, make sure you followed the steps correctly. If it still errors, refer to the jdk17 instructions above as that is likely the issue
  
// MISSING OR NOT FOUND ERROR:
  1. Open config.json (AppData --> Roaming --> TwitchCraftBot --> config.json)
  2. Find lines 13 and 14 ("ServerDirectory" and "JarPath")
  3. Ensure that your username on your PC is inputted both times where it says C:\\Users\\YOUR_USER\\ (in between Users\\ and \\AppData)
  
// BOT CRASHING OR NO ERROR REASON:
  1. Check Task Manager to for any instances of the bot or javaw.exe already running and then close them
  2. Open config.json (AppData --> Roaming --> TwitchCraftBot --> config.json)
  3. Find lines 22 and 23 ("StreamerName" and "BotName")
  4. Ensure that the Twitch username of the account you are streaming on is correctly inputted on line 22
  5. Ensure that the username of your Twitch bot is correctly inputted on line 23
  6. If the crashing still persists, ensure you have all files that are associated with the bot installed and in the correct place
 
// CHANGING THE BOT ACCOUNT OR APPLICATION:
  - When changing the account or application of your bot on the Twitch Developer Console, you must delete or update the config.json file
  - After doing this, you must restart the bot
  - If you delete the config.json file, you must redo the bot setup
  - If you have a world on the TwitchCraft Minecraft server saved, it may be affected

- Thank you to Lil_KleinStein for inspiration for this mod, with his Minecraft streams inspiring the theme and creation!
