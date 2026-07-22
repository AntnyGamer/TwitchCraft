# Installation

## Requirements

- Windows 10 or 11, 64-bit
- Minecraft Java Edition 1.20.0–1.21.11 or 26.1–26.2.0
- A Twitch account for the bot; using a separate account is recommended
- A Twitch Developer application with two-factor authentication enabled on its owner account

Install the Java Development Kit required by the Minecraft server version:

| Minecraft version | Required Java |
|---|---:|
| 1.20.0–1.20.4 | JDK 17 |
| 1.20.5–1.21.11 | JDK 21 |
| 26.1–26.2.0 | JDK 25 |

Use the 64-bit Windows installer. TwitchCraft checks `JAVA_HOME`, `PATH`, and common Java installation folders.

## Create the Twitch bot application

1. Sign in to the [Twitch Developer Console](https://dev.twitch.tv/console).
2. Register an application for the bot.
3. Set the OAuth redirect URL to `http://localhost`.
4. Run `GetBotToken.exe` from the TwitchCraft distribution.
5. Enter the application Client ID and the same redirect URL.
6. Complete Twitch authorization and copy the resulting token directly into TwitchCraft.
7. In the streamer's Twitch chat, use `/mod BOT_NAME` if the bot needs moderator privileges.

Treat the bot token like a password. Do not paste it into an issue, screenshot, stream overlay, log, or chat message.

## First launch

1. Extract the release archive to a folder you can write to.
2. Keep the distributed files together.
3. Open `TwitchCraft.exe`.
4. Enter the Minecraft version, Twitch Client ID, bot token, streamer/channel name, bot account name, and Minecraft username.
5. Leave the bind address at its default unless you understand the networking implications.
6. Optionally import an existing world before starting.
7. Select **Start** and wait for the server-ready message.

For the local server, join `127.0.0.1` from the same computer. See [MULTIPLAYER.md](MULTIPLAYER.md) before exposing the game server to other players.

## Build from source

Install the .NET 10 SDK and run from the repository root:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
dotnet test TwitchCraft.slnx -c Release --no-build
```

See [BUILDING.md](../BUILDING.md) for the application publish command.

## Next steps

- [Configuration](CONFIGURATION.md)
- [Commands](COMMANDS.md)
- [Remote control](REMOTE-CONTROL.md)
- [Troubleshooting](TROUBLESHOOTING.md)
