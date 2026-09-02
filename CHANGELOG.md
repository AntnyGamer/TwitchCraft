# Changelog

All notable changes to this project are documented here. Release entries are listed in reverse chronological order.

## [1.8.0.0] - 2026-09-02

- Completely redesigned and greatly expanded Settings with many new command, economy, chat, performance, backup, and Minecraft server options
- Added per-command customization, customizable command prefixes, rate limits, and cooldown controls
- Added many new Twitch and Minecraft commands and expanded token/economy features
- Replaced the separate bot-token setup tool with built-in Twitch authorization and automatic token refreshing
- Added automatic config and token database backups
- Improved Twitch, Minecraft, RCON, viewer tracking, logging, and server startup/shutdown handling
- Dropped support for Minecraft versions older than 1.20.5
- Added many new tests, increasing the test suite from 200 to 275 total
- Updated README and documentation
- Minor UI and usability improvements
- Minor bug fixes
- Minor security and performance improvements

## [1.7.1.3] - 2026-08-16

- Added many new tests, now 200 total
- Made randomness even more random when necessary
- Preserved existing custom and unmanaged server.properties values during server setup
- Improved world importing
- Updated NuGet packages
- Updated README
- Minor bug fixes
- Minor security and performance improvements

## [1.7.1.2] - 2026-07-23

- Completely overhauled the internal structure of TwitchCraft
- Secured command and diagnostic handling
- Logs now support up to 5MB of data
- Improved GetBotToken.exe
- Minor security, stability, and performance improvements

## [1.7.1.1] - 2026-07-18

- Updated .NET and SQLite components
- Improved error logging
- Minor performance optimizations
- Minor security and stability improvements

## [1.7.1] - 2026-07-04

- Published TwitchCraft's source code
- Added a new setting that allows the streamer to turn off chat relay into Minecraft from Twitch
- Overhauled Settings page
- Improved Remote Control Mode safety and security
- Improved Minecraft .jar downloading safety and verification
- Removed unused effect amplifiers
- Many performance improvements
- Minor bug fixes
- Other minor safety and security updates

## [1.7.0.2] - 2026-06-22

- HOTFIX: server.properties can properly be edited now
- Updated death tracking
- Improved shutdown handling
- statistics.json has been moved to exports folder
- Improved !ban and !unban command handling
- Updated NuGet packages
- Multiple performance optimizations, especially for Remote Control Mode
- Minor code updates

## [1.7.0.1] - 2026-06-18

- HOTFIX: Death and Time Survived statistics now track and save properly
- HOTFIX: Chicken Run and Wither Battle minigames no longer give extra tokens
- HOTFIX: Max second bets on Chicken Run can now win
- HOTFIX: Fixed numbering issue in the README.txt
- You can now chose a custom cooldown time for the global command cooldown setting
- Minor performance optimizations

## [1.7.0] - 2026-06-18

- Added Remote Control Mode with RCON support (2+ streamers' chats can now control one Minecraft server)
- Added support for different Bind IPs (VPN, IPv6, localhost)
- Failed paid commands no longer activate the global cooldown
- Updated UI
- Updated README and moved troubleshooting to a separate document
- Server properties unused by TwitchCraft are no longer overwritten (so you can now lower certain settings like view distance if you are on a low-end device)
- Startup/shutdown handling improved
- Many bug fixes
- Many performance improvements
- Slightly improved security

## [1.6.1] - 2026-06-16

- Added support for Minecraft version 26.2
- Improved Java detection
- Enabled Minecraft querying
- Updated README
- Clarified that server address is the Bind IP
- Heavy bug fixes
- Heavy code optimizations

## [1.6.0.1] - 2026-06-05

- HOTFIX: All-time stats are now properly working
- TwitchCraft now sends you to the Start screen instead of directly into the Main screen when first setting up the bot.
- Improved bits to tokens handling
- Fixed minor bugs
- Minor code updates

## [1.6.0] - 2026-06-03

- Added a new Statistics page
- Added session and all-time statistics tracking
- Added tracking for commands run, coins spent, effects received, deaths, time survived, nicest viewer, and most dangerous viewer
- Added SQLite database storage for viewer tokens
- Added SQLite database storage for statistics
- Added readable JSON exports for tokens and statistics
- Added safer world importing with staging and backup handling
- Improved Twitch chat queue handling for better performance under load
- Improved player targeting and multiplayer player detection
- Improved death tracking for the streamer
- Cleaned up and simplified parts of the project structure
- Added better validation for server ports
- Security fixes
- MANY bug fixes and performance fixes

## [1.5.1] - 2026-05-01

- Fixed Chicken Run in-game text to display when it's first announced
- When your local version manifest is out of date, an online fallback is now used
- When deleting your config, the .exe is now restarted instead of completely shut down
- Updated scrolling for Client ID and Bot Token setup textboxes
- Clicking "No" on java verification now allows you to re-edit setup
- Fixed and improved multiple commands (!lightning, !switchmilk, !ban, !unban)
- Slightly updated RCON password generation
- 26.1.2 is now treated as the preferred version
- Updated GetBotToken.exe
- Updated README
- Numerous performance upgrades, especially for heavier overload
- Numerous bug fixes
- Removed redundant and unused code

## [1.5.0.2] - 2026-04-09

- HOTFIX: Tightened and secured minigame handling to prevent wrong minigame display
- HOTFIX: !insult command now works again
- Added support for Minecraft version 26.1.2
- Johnny now glows
- Added a “Scaredy Cat!” title/subtitle to the !scared command
- Minor code update

## [1.5.0.1] - 2026-04-06

- HOTFIX: !slaughter no longer deletes items
- Updated 2 command prices: !givelight (5 --> 3 tokens) and !clear (120 --> 125 tokens)
- Minor adjustments to !johnny command
- Fixed stale viewer list bug
- Minor README update

## [1.5.0] - 2026-04-05

- Added version support from 1.21.5-->26.1.1
- Added 5 new commands (!slaughter, !rename, !johnny, !scared, !xp)
- Upgraded !givetokens and !removetokens commands (now works with all, random, self)
- The bot now prompts you to close any stale javaw.exe processes
- Added "Infested" and "Trial Omen" effects to effect list
- The Minecraft username textbox no longer disappears after inputting a name for the first time
- The bot now warns you if you input an invalid Minecraft username
- Upgraded Twitch IRC and token handling
- A small text now appears on screen when a minigame is about to start
- Improved startup and shutdown speeds
- Updated the README to reflect new version support
- Removed unused code
- Many small code updates, optimizations, and bug fixes

## [1.4.5.1] - 2026-04-02

- HOTFIX: Minigame cooldowns now save correctly
- HOTFIX: The bot now shuts down after deleting the config
- Swapped position of Client ID and Bot Token on Setup page
- You can now only guess numbers in the proper range for the Guess The Number minigame
- Removed unused code from the bot
- Minor code updates, optimizations, and bug fixes

## [1.4.5] - 2026-04-02

- The Minecraft username textbox no longer incorrectly disappears
- Old server.jar files are now wiped when changing versions to save storage
- GetBotToken.exe should now correctly send your Bot Token to localhost
- Deleting your config now keeps the .bak file for user reference
- Minor code updates, bug fixes, and optimizations

## [1.4.4] - 2026-04-02

- Fixed !mlg command in the nether
- Overhauled the README
- Resetting settings to defaults now applies non-game settings in real-time
- Improved error handling
- Minor code updates and optimizations

## [1.4.3.1] - 2026-03-27

- MLG command now works in the nether (with slightly altered effects)
- You can now only type numbers into min and max RAM settings
- Usage message for !gambletokens now displays the correct command
- Minor code updates and optimizations

## [1.4.3] - 2026-03-22

- Removed silent errors on much of the code
- Improved Java detection
- Updated README.txt
- Minor bug fixes
- Minor code optimizations

## [1.4.2] - 2026-03-21

- Added "Reset to Defaults" button on Settings page
- Changing minigame cooldown in settings now actually works
- Locateplayers datapack now only loads on multiplayer worlds
- Minor bug fixes
- Minor code optimizations

## [1.4.1.2] - 2026-03-19

- Updated and secured IRC handling
- Reduced viewer list refresh time from 60 to 30 seconds
- Minor bug fixes
- Minor code optimizations

## [1.4.1.1] - 2026-03-17

- HOTFIX: Fixed potential issue with multiplayer PVP setting not saving
- Twitch and Minecraft logs are now limited at 250 lines each, older lines will be removed
- Minor bug fixes
- Minor code optimizations

## [1.4.1] - 2026-03-17

- Added support for Minecraft versions 1.20.5 through 1.21.4
- Added grouped Minecraft version selection by JDK requirement
- Added dynamic JDK requirement text on the setup page
- Improved Java detection and version handling
- Added support for newer mobs on supported versions
- Improved datapack installation and cross-version compatibility
- Updated datapack handling for 1.20.x and 1.21.x differences
- Improved world and player naming handling
- Improved minigame logic
- Added an icon for GetBotToken.exe
- Cleaned up repeated config, settings, and helper code
- Improved chat/output stability and viewer handling
- Minor cleanup, fixes, compatibility improvements, and optimizations

## [1.4.0.1] - 2026-03-16

- HOTFIX: Removed 1.20.5 and 1.20.6 as supported versions (reupload fix)
- Fixed "unknown scoreboard objective" appearing in Minecraft log
- Updated README for enhanced readability and specification that any Java 17.x version works with the bot
- Minor updates to config.json (reupload fix)

## [1.4.0] - 2026-03-15

- Added MULTIPLE new settings (cooldown between minigames, difficulty, hardcore, PVP, and min and max RAM)
- Added a separate page for Settings
- Updated GetBotToken.exe
- Added the ability to access GetBotToken.exe through the main bot
- Minor updates and fixes to minigames
- Updated multiplayer player list leaderboard
- Added help button next to "Bot Token:" on setup page
- Minor code updates and optimizations
- Minor bug fixes

## [1.3.1] - 2026-03-15

- Updated GetBotToken.exe
- Fixed incorrect display of Minecraft name on singleplayer in replies to commands
- Potentially fixed firework command
- The timer between minigames now doesn't reset with world resets
- Adjusted !night command cost from 5-->15 tokens
- Minor code updates
- Minor bug fixes

## [1.3.0] - 2026-03-14

- Added an import world feature
- Added the ability to allow your Twitch moderators to use typically streamer-only commands
- Added the ability to place a 10 second cooldown on all game-effecting commands
- Passive token and minigame enabling choices now persist after closing the bot
- Updated the UI of the Help tab
- Bug fixes
- Minor code optimizations

## [1.2.2.1] - 2026-03-12

- HOTFIX: Fixed TNT immediately exploding
- Minor code formatting updates

## [1.2.2] - 2026-03-07

- Disabling passive token earning actually works now
- Added internet fallback for version manifest
- Minor bug fixes
- Minor design updates
- Slightly reduced file size

## [1.2.1] - 2026-03-07

- Many code optimizations
- Heavily updated token logic (to try to fix bug)
- Updated README
- Slightly reduced file size

## [1.2.0] - 2026-03-06

- Upgraded GetBotToken.exe while heavily decreasing file size
- Added minigame enable/disable checkbox in Help window
- Added sound effects when minigames start
- Added weighted payouts for Chicken Run
- Chicken Run now cancels if nobody bets
- Improved singleplayer command messages (uses streamer name)
- Lowered player-idle-timeout to 500 minutes
- Other minor changes and fixes

## [1.1.1] - 2026-03-05

- Upgraded code to C#14
- Fixed tokens getting wiped randomly
- Fixed javaw.eve running after shutdown
- Bug fixes
- Other minor changes

## [1.1.0] - 2026-03-04

- Fixed commands working on spectator
- Upgraded to .NET 10
- Added health bars on player list
- Bug fixes
- Updated teleport command
- Added new icon for the server
- Other changes

## [1.0.1] - 2026-03-03

- Added support for Minecraft versions 1.20, 1.20.1, and 1.20.2
- Fixed the icon of the .EXE
- Minor optimizations and fixes

## [1.0.0] - 2026-03-03

- Initial TwitchCraft release.
