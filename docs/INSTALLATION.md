# Installation

## Requirements

- Windows 10 or 11, 64-bit
- Minecraft Java Edition 1.20.5–1.21.11 or 26.1–26.2.0
- A Twitch account for the bot; using a separate account is recommended

Install the Java Development Kit required by the Minecraft server version:

| Minecraft version | Required Java |
|---|---:|
| 1.20.5–1.21.11 | JDK 21 |
| 26.1–26.2.0 | JDK 25 |

Use the 64-bit Windows installer. TwitchCraft checks `JAVA_HOME`, `PATH`, and common Java installation folders.

## Authorize the Twitch bot account

1. Open TwitchCraft and select **Authorize Twitch** above **Start**.
2. Sign in with the Twitch account you want TwitchCraft to use as the bot.
3. Complete the Twitch device authorization in the browser.
4. Confirm that TwitchCraft fills in the authorized bot account automatically.
5. In the streamer's Twitch chat, use `/mod BOT_NAME` if the bot needs moderator privileges.

TwitchCraft uses its built-in public Twitch application. You do not create a Twitch Developer application, enter a Client ID, configure a localhost redirect, provide a Client Secret, or copy and paste a bot token. Treat `config.json` as sensitive because it stores the renewable authorization used by the app.

## First launch

1. Extract the release archive to a folder you can write to.
2. Keep the distributed files together.
3. Open `TwitchCraft.exe`.
4. Enter the Minecraft version and streamer/channel name. Select **Authorize Twitch** to connect and fill the bot account automatically.
5. Leave the bind address at its default unless you understand the networking implications.
6. Once every required setup value is valid, **Start** becomes available. Select it. TwitchCraft verifies and prepares the local server files, then opens the Start screen.

On the Start screen, enter the streamer's Minecraft username, choose whether to enable multiplayer and Online Mode, optionally import a world, then select **Start** and wait for the server-ready message.

For the local server, join `127.0.0.1` from the same computer. See [MULTIPLAYER.md](MULTIPLAYER.md) before exposing the game server to other players.

## Build from source

Install the .NET 10 SDK and run from the repository root:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
dotnet test TwitchCraft.slnx -c Release --no-build
```

The root solution validates TwitchCraft and its regression tests.

See [BUILDING.md](BUILDING.md) for the application publish command.

## Next steps

- [Configuration](CONFIGURATION.md)
- [Commands](COMMANDS.md)
- [Remote control](REMOTE-CONTROL.md)
- [Troubleshooting](TROUBLESHOOTING.md)
