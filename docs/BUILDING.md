# Building TwitchCraft From Source

## Requirements

- Windows 10/11 (64-bit)
- .NET 10 SDK

## Build

Open PowerShell or a terminal in the repository root and run:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
Push-Location TwitchCraft.Tests
dotnet test --project TwitchCraft.Tests.csproj -c Release --no-build
Pop-Location
dotnet format TwitchCraft.slnx --verify-no-changes --no-restore
```

`TwitchCraft.slnx` is the canonical repository validation entry point. Its Release build compiles the TwitchCraft WPF application, the regression test project, and the test-process helper. The app-only `TwitchCraft_SOURCE\TwitchCraft.slnx` solution may still be used for isolated application development.

To produce a local coverage report equivalent to CI, run:

```powershell
Push-Location TwitchCraft.Tests
dotnet test --project TwitchCraft.Tests.csproj -c Release --no-build --results-directory ..\TestResults --coverlet --coverlet-output-format cobertura
Pop-Location
```

Coverage output is written under `TestResults/` and is not committed.

## Publish

To publish a Windows x64 single-file build, run:

```powershell
dotnet publish ".\TwitchCraft_SOURCE\TwitchCraft.csproj" -c Release -r win-x64 --self-contained true -o ".\TwitchCraft_SOURCE\publish" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None
```

The published files will be created in `TwitchCraft_SOURCE\publish`.

## Private Files

Do not commit or share private runtime files such as:

- `config.json`
- `viewer_tokens.db`
- `statistics.db`
- `.db`, `.db-shm`, or `.db-wal` files
- Twitch access and refresh tokens
- RCON passwords
- Personal Minecraft server files

## Notes

This build is intended for Windows x64.

If you only want to run the source code while developing, use:

```powershell
dotnet run --project ".\TwitchCraft_SOURCE\TwitchCraft.csproj"
```

If you want to create a release build for users, use the publish command above.
