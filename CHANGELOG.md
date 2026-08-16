# Changelog

All notable project changes should be recorded here. This project follows a Keep a Changelog-style structure; version numbers should match the application metadata and release tag.

## [Unreleased]

## [1.7.1.3] - 2026-08-16

### Added

* Expanded the high-value automated test suite to 200 tests.

### Changed

* Kept cryptographic randomness for chance-based token transfers and minigame outcomes while restoring ordinary randomness for cosmetic gameplay behavior and passive reward scheduling.

### Fixed

* Missing or incomplete bundled `locateplayers` datapack files now produce a logged warning and warning popup without aborting startup or world import.
* Preserved shared player probes when one caller cancels, serialized IRC queue generations during reset, retained custom server properties during setup, and kept safer world-import rollback behavior.

### Security

* Restricted Restart Manager DLL resolution to `System32` and retained cryptographic randomness for token and minigame outcomes.

## [1.7.1.2] - 2026-07-23

### Added

* Added automated tests, GitHub Actions build validation, and code coverage artifacts.
* Added project documentation and contributor templates.
* Added custom redirect port support and account confirmation to GetBotToken.
* Added new helpers for commands, configuration, diagnostics, minigames, statistics, and runtime features.

### Changed

* Updated and reorganized the TwitchCraft source code.
* Updated SQLite components and improved diagnostic logging.

### Fixed

* Fixed paid-command refunds, cooldown release, and statistics recording after failed dispatches.
* Fixed application version reporting and in-session log rotation.

## [1.7.1.1] - 2026-07-18

### Changed

* Updated .NET and SQLite components.
* Improved error logging.
* Added minor performance optimizations.

### Security

* Added minor security and stability improvements.

## [1.7.1] - 2026-07-04

### Added

* Published TwitchCraft's source code.
* Added a setting that allows the streamer to disable Twitch chat relay into Minecraft.

### Changed

* Overhauled the Settings page.
* Improved Remote Control Mode safety and security.
* Improved Minecraft `.jar` download safety and verification.
* Added many performance improvements.

### Fixed

* Fixed minor bugs.

### Security

* Added other minor safety and security updates.

### Removed

* Removed unused effect amplifiers.

## [1.7.0.2] - 2026-06-22

### Changed

* Updated death tracking.
* Improved shutdown handling.
* Moved `statistics.json` to the exports folder.
* Improved `!ban` and `!unban` command handling.
* Updated NuGet packages.
* Added multiple performance optimizations, especially for Remote Control Mode.
* Applied minor code updates.

### Fixed

* Fixed `server.properties` editing.

## [1.7.0.1] - 2026-06-18

### Added

* Added a custom cooldown-time option for the global command cooldown setting.

### Changed

* Added minor performance optimizations.

### Fixed

* Fixed Death and Time Survived statistics so they track and save properly.
* Fixed Chicken Run and Wither Battle so they no longer award extra tokens.
* Fixed maximum-second Chicken Run bets so they can win.
* Fixed a numbering issue in `README.txt`.

## [1.7.0] - 2026-06-18

### Added

* Added Remote Control Mode with RCON support, allowing two or more streamers' chats to control one Minecraft server.
* Added support for different bind IPs, including VPN, IPv6, and localhost.

### Changed

* Failed paid commands no longer activate the global cooldown.
* Updated the UI.
* Updated the README and moved troubleshooting into a separate document.
* Stopped overwriting server properties that TwitchCraft does not use, allowing settings such as view distance to be lowered on low-end devices.
* Improved startup and shutdown handling.
* Added many performance improvements.

### Fixed

* Fixed many bugs.

### Security

* Slightly improved security.

## [1.6.1] - 2026-06-16

### Added

* Added support for Minecraft version 26.2.
* Enabled Minecraft querying.

### Changed

* Improved Java detection.
* Updated the README.
* Clarified that the server address is the bind IP.
* Added heavy code optimizations.

### Fixed

* Added heavy bug fixes.

## [1.6.0.1] - 2026-06-05

### Changed

* TwitchCraft now sends users to the Start screen instead of directly to the Main screen during initial setup.
* Improved Bits-to-Tokens handling.
* Applied minor code updates.

### Fixed

* Fixed all-time statistics.
* Fixed minor bugs.

## [1.6.0] - 2026-06-03

### Added

* Added a Statistics page.
* Added session and all-time statistics tracking.
* Added tracking for commands run, coins spent, effects received, deaths, time survived, nicest viewer, and most dangerous viewer.
* Added SQLite database storage for viewer tokens.
* Added SQLite database storage for statistics.
* Added readable JSON exports for tokens and statistics.
* Added safer world importing with staging and backup handling.
* Added better server-port validation.

### Changed

* Improved Twitch chat queue handling for better performance under load.
* Improved player targeting and multiplayer player detection.
* Improved streamer death tracking.
* Cleaned up and simplified parts of the project structure.
* Added many performance improvements.

### Fixed

* Fixed many bugs.

### Security

* Added security fixes.

## [1.5.1] - 2026-05-01

### Changed

* Added an online fallback when the local version manifest is outdated.
* Restarted the executable instead of completely shutting down after config deletion.
* Updated scrolling for the Client ID and Bot Token setup text boxes.
* Allowed setup to be edited again after selecting **No** during Java verification.
* Slightly updated RCON password generation.
* Set Minecraft 26.1.2 as the preferred version.
* Updated `GetBotToken.exe`.
* Updated the README.
* Added numerous performance improvements, especially under heavier load.

### Fixed

* Fixed Chicken Run's in-game text so it displays when first announced.
* Fixed and improved `!lightning`, `!switchmilk`, `!ban`, and `!unban`.
* Fixed numerous bugs.

### Removed

* Removed redundant and unused code.

## [1.5.0.2] - 2026-04-09

### Added

* Added support for Minecraft version 26.1.2.
* Made Johnny glow.
* Added a **Scaredy Cat!** title and subtitle to the `!scared` command.

### Changed

* Applied a minor code update.

### Fixed

* Tightened minigame handling to prevent the wrong minigame from being displayed.
* Fixed the `!insult` command.

## [1.4.5.1] - 2026-04-02

### Changed

* Swapped the positions of Client ID and Bot Token on the Setup page.
* Applied minor code updates and optimizations.

### Fixed

* Fixed minigame cooldown saving.
* Fixed shutdown after deleting the config.
* Restricted Guess the Number guesses to the proper range.
* Fixed minor bugs.

### Removed

* Removed unused bot code.

## [1.4.5] - 2026-04-02

### Changed

* Old `server.jar` files are now deleted when changing versions to save storage.
* Config deletion now retains the `.bak` file for reference.
* Applied minor code updates and optimizations.

### Fixed

* Fixed the Minecraft username text box incorrectly disappearing.
* Fixed `GetBotToken.exe` sending the Bot Token to localhost.
* Fixed minor bugs.

## [1.4.4] - 2026-04-02

### Changed

* Overhauled the README.
* Resetting settings to defaults now applies non-game settings in real time.
* Improved error handling.
* Applied minor code updates and optimizations.

### Fixed

* Fixed the `!mlg` command in the Nether.

## [1.4.3.1] - 2026-03-27

### Changed

* Restricted minimum and maximum RAM settings to numeric input.
* Applied minor code updates and optimizations.

### Fixed

* Made the MLG command work in the Nether with slightly altered effects.
* Fixed the `!gambletokens` usage message.

## [1.4.3] - 2026-03-22

### Changed

* Removed silent errors from much of the code.
* Improved Java detection.
* Updated `README.txt`.
* Added minor code optimizations.

### Fixed

* Fixed minor bugs.

## [1.4.2] - 2026-03-21

### Added

* Added a **Reset to Defaults** button to the Settings page.

### Changed

* Changed the LocatePlayers datapack so it only loads in multiplayer worlds.
* Added minor code optimizations.

### Fixed

* Fixed the minigame cooldown setting.
* Fixed minor bugs.

## [1.4.1.2] - 2026-03-19

### Changed

* Reduced the viewer-list refresh interval from 60 seconds to 30 seconds.
* Added minor code optimizations.

### Fixed

* Fixed minor bugs.

### Security

* Updated and secured IRC handling.

## [1.4.1.1] - 2026-03-17

### Changed

* Limited Twitch and Minecraft logs to 250 lines each, with older lines removed automatically.
* Added minor code optimizations.

### Fixed

* Fixed a potential issue with the multiplayer PVP setting not saving.
* Fixed minor bugs.

## [1.4.1] - 2026-03-17

### Added

* Added support for Minecraft versions 1.20.5 through 1.21.4.
* Added grouped Minecraft version selection based on JDK requirements.
* Added dynamic JDK requirement text to the Setup page.
* Added support for newer mobs on supported versions.
* Added an icon for `GetBotToken.exe`.

### Changed

* Improved Java detection and version handling.
* Improved datapack installation and cross-version compatibility.
* Updated datapack handling for differences between Minecraft 1.20.x and 1.21.x.
* Improved world and player naming handling.
* Improved minigame logic.
* Cleaned up repeated configuration, settings, and helper code.
* Improved chat/output stability and viewer handling.
* Added minor cleanup, compatibility improvements, and optimizations.

### Fixed

* Added minor fixes.

## [1.4.0.1] - 2026-03-16

### Changed

* Updated the README for improved readability and clarified that any Java 17.x version works with TwitchCraft.
* Applied minor updates to `config.json`.

### Fixed

* Fixed the **unknown scoreboard objective** message appearing in the Minecraft log.

### Removed

* Removed Minecraft 1.20.5 and 1.20.6 from the supported-version list.

## [1.4.0] - 2026-03-15

### Added

* Added settings for minigame cooldown, difficulty, Hardcore mode, PVP, and minimum and maximum RAM.
* Added a separate Settings page.
* Added access to `GetBotToken.exe` through the main application.
* Added a Help button next to **Bot Token** on the Setup page.

### Changed

* Updated `GetBotToken.exe`.
* Updated and fixed minigames.
* Updated the multiplayer player-list leaderboard.
* Applied minor code updates and optimizations.

### Fixed

* Fixed minor bugs.

## [1.3.1] - 2026-03-15

### Changed

* Updated `GetBotToken.exe`.
* Prevented the minigame timer from resetting during world resets.
* Increased the `!night` command cost from 5 to 15 tokens.
* Applied minor code updates.

### Fixed

* Fixed the Minecraft name displayed in singleplayer command replies.
* Potentially fixed the fireworks command.
* Fixed minor bugs.

## [1.3.0] - 2026-03-14

### Added

* Added world importing.
* Added an option allowing Twitch moderators to use commands normally restricted to the streamer.
* Added an optional 10-second cooldown for gameplay-affecting commands.
* Added persistence for passive-token and minigame enablement settings.

### Changed

* Updated the Help tab UI.
* Added minor code optimizations.

### Fixed

* Fixed bugs.

## [1.2.2.1] - 2026-03-12

### Changed

* Applied minor code-formatting updates.

### Fixed

* Fixed TNT exploding immediately.

## [1.2.2] - 2026-03-07

### Added

* Added an internet fallback for the version manifest.

### Changed

* Applied minor design updates.
* Slightly reduced the file size.

### Fixed

* Fixed disabling passive-token earning.
* Fixed minor bugs.

## [1.2.1] - 2026-03-07

### Changed

* Added many code optimizations.
* Heavily updated token logic to address token issues.
* Updated the README.
* Slightly reduced the file size.

## [1.2.0] - 2026-03-06

### Added

* Added a minigame enable/disable checkbox to the Help window.
* Added sound effects when minigames start.
* Added weighted Chicken Run payouts.

### Changed

* Upgraded `GetBotToken.exe` while heavily reducing its file size.
* Chicken Run now cancels when nobody places a bet.
* Improved singleplayer command messages to use the streamer name.
* Lowered `player-idle-timeout` to 500 minutes.

### Fixed

* Added other minor changes and fixes.

## [1.1.1] - 2026-03-05

### Changed

* Upgraded the codebase to C# 14.
* Added other minor changes.

### Fixed

* Fixed tokens being wiped unexpectedly.
* Fixed `javaw.exe` continuing to run after shutdown.
* Fixed bugs.

## [1.1.0] - 2026-03-04

### Added

* Added health bars to the player list.
* Added a new server icon.

### Changed

* Upgraded TwitchCraft to .NET 10.
* Updated the teleport command.
* Added other changes.

### Fixed

* Fixed gameplay commands affecting spectators.
* Fixed bugs.

## [1.0.1] - 2026-03-03

### Added

* Added support for Minecraft versions 1.20, 1.20.1, and 1.20.2.

### Changed

* Added minor optimizations.

### Fixed

* Fixed the executable icon.
* Added minor fixes.

## [1.0.0] - 2026-03-03

### Added

* Initial TwitchCraft release.
