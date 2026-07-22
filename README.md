# TwitchCraft

TwitchCraft is a Windows desktop Twitch-to-Minecraft integration application. It runs or remotely controls a Minecraft Java Edition server and lets Twitch viewers affect gameplay through chat commands in real time.

**No Forge, Fabric, NeoForge, client-side mod loader, or server plugin is required.**

<p align="center">
  <img src="Screenshots/1TC_Setup.png" width="700" alt="TwitchCraft setup screen">
</p>

<p align="center">
  <img src="Screenshots/4TC_Main.png" width="700" alt="TwitchCraft main screen">
</p>

## What TwitchCraft is

- A Windows desktop application
- A Twitch chat bot
- A local Minecraft Java server launcher or remote RCON controller
- A command, token, minigame, multiplayer-targeting, and statistics system

## What TwitchCraft is not

- A Forge, Fabric, or NeoForge mod
- A client-side Minecraft mod
- A file placed in a server's `plugins` folder

TwitchCraft creates a mod-like interactive streaming experience while remaining a standalone application.

## Requirements

- Windows 10 or 11, 64-bit
- Minecraft Java Edition 1.20.0–1.21.11 or 26.1–26.2.0
- Java 17 for Minecraft 1.20.0–1.20.4
- Java 21 for Minecraft 1.20.5–1.21.11
- Java 25 for Minecraft 26.1–26.2.0
- A separate Twitch bot account is recommended

See [Installation](docs/INSTALLATION.md) for the complete setup.

## Documentation

| Guide | Contents |
|---|---|
| [Installation](docs/INSTALLATION.md) | Requirements, Twitch bot setup, Java, and first launch |
| [Commands](docs/COMMANDS.md) | Current chat commands, prices, targeting, permissions, and refunds |
| [Configuration](docs/CONFIGURATION.md) | Settings, local data, backups, and secret-handling guidance |
| [Multiplayer](docs/MULTIPLAYER.md) | Local/LAN/public connections and targeting behavior |
| [Remote control](docs/REMOTE-CONTROL.md) | RCON setup and safer networking guidance |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Symptom-based fixes and safe diagnostic sharing |
| [Architecture](docs/ARCHITECTURE.md) | Runtime flow, persistence, source layout, and recovery behavior |
| [Releases](docs/RELEASES.md) | Versioning and release checklist |

The older external command and troubleshooting pages may remain available as mirrors, but the files above are the canonical project documentation.

## Local data

TwitchCraft stores its local runtime data under:

```text
%APPDATA%\TwitchCraftBot
```

This folder can contain `config.json`, databases, exports, logs, backups, and Minecraft server data. Never publish it without reviewing and sanitizing every file. In particular, never share Twitch tokens, RCON passwords, private databases, public IP addresses, or unredacted logs.

## Build and test

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
dotnet test TwitchCraft.slnx -c Release --no-build
```

More details are in [BUILDING.md](BUILDING.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

Read [SECURITY.md](SECURITY.md) before reporting a vulnerability. Do not open a public issue containing a Twitch token, RCON password, private database, full `config.json`, or unredacted log.

## Links

- [TwitchCraft website](https://antnygamer.wixsite.com/twitchcraft)
- [Trailer](https://www.youtube.com/watch?v=HM2Um3Uf1hk)
- [Setup tutorial](https://bit.ly/twitchcraft-tutorial)

Special thanks to Lil_KleinStein, whose Minecraft streams inspired TwitchCraft's theme and creation.
