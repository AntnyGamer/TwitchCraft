# Building TwitchCraft From Source

## Requirements

- Windows 10/11 (64-bit)
- .NET 10 SDK

## Build

Open PowerShell or a terminal in the project folder and run:

```powershell
dotnet restore
dotnet build -c Release
```

## Publish

To publish a Windows x64 single-file build, run:

```powershell
dotnet publish ".\TwitchCraftBot.csproj" -c Release -r win-x64 --self-contained true -o ".\publish" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:IncludeAllContentForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

The published files will be created in the `publish` folder.

## Private Files

Do not commit or share private runtime files such as:

- `config.json`
- `viewer_tokens.db`
- `statistics.db`
- `.db`, `.db-shm`, or `.db-wal` files
- Twitch bot tokens
- RCON passwords
- Personal Minecraft server files

## Notes

This build is intended for Windows x64.

If you only want to run the source code while developing, use:

```powershell
dotnet run
```

If you want to create a release build for users, use the publish command above.
