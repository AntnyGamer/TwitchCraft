# Installation

## Requirements

- Windows 10 or 11, 64-bit
- Minecraft Java Edition 1.20.5–1.21.11 or 26.1–26.2.0
- A Twitch account for the bot; using a separate account is recommended
- A Twitch Developer application with two-factor authentication enabled on its owner account

Install the Java Development Kit required by the Minecraft server version:

| Minecraft version | Required Java |
|---|---:|
| 1.20.5–1.21.11 | JDK 21 |
| 26.1–26.2.0 | JDK 25 |

Use the 64-bit Windows installer. TwitchCraft checks `JAVA_HOME`, `PATH`, and common Java installation folders.

## Create the Twitch bot application

1. Sign in to the [Twitch Developer Console](https://dev.twitch.tv/console).
2. Register an application for the bot.
3. Set the OAuth redirect URL to `http://localhost:3000` as the recommended value. Any other valid localhost URL is also acceptable because TwitchCraft's device authorization does not depend on the exact redirect address.
4. Open TwitchCraft and enter the application Client ID on the Setup screen. No client secret is required.
5. Select **Authorize Twitch** above **Start**.
6. Complete the Twitch device authorization in the browser. TwitchCraft securely keeps the hidden renewable authorization and fills in the bot account name automatically.
7. In the streamer's Twitch chat, use `/mod BOT_NAME` if the bot needs moderator privileges.

TwitchCraft does not ask you to copy or paste a bot token. Treat `config.json` as sensitive because it stores the authorization used by the app.

## First launch

1. Extract the release archive to a folder you can write to.
2. Keep the distributed files together.
3. Open `TwitchCraft.exe`.
4. Enter the Minecraft version, Twitch Client ID, and streamer/channel name. Select **Authorize Twitch** to connect and fill the bot account automatically.
5. Leave the bind address at its default unless you understand the networking implications.
6. Optionally import an existing world before starting.
7. Once every required setup value is valid, **Start** becomes available. Select it and wait for the server-ready message.

For the local server, join `127.0.0.1` from the same computer. See [MULTIPLAYER.md](MULTIPLAYER.md) before exposing the game server to other players.

## Build from source

Install the .NET 10 SDK and run from the repository root:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
dotnet test TwitchCraft.slnx -c Release --no-build
```

The root solution validates TwitchCraft and its regression tests.

See [BUILDING.md](../BUILDING.md) for the application publish command.

## Next steps

- [Configuration](CONFIGURATION.md)
- [Commands](COMMANDS.md)
- [Remote control](REMOTE-CONTROL.md)
- [Troubleshooting](TROUBLESHOOTING.md)
