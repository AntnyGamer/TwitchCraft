# Releases

## Versioning

Before publishing, align these values:

- Application `Version` and `FileVersion` in `TwitchCraftBot_SOURCE/TwitchCraftBot.csproj`
- Git tag
- GitHub Release title
- `CHANGELOG.md` heading and date

Do not publish from an unreviewed working tree.

## Release checklist

1. Confirm the intended source branch and review the complete diff.
2. Update `CHANGELOG.md` with subsystem-specific entries.
3. Run:

   ```powershell
   dotnet restore TwitchCraft.slnx
   dotnet build TwitchCraft.slnx -c Release --no-restore
   dotnet test TwitchCraft.slnx -c Release --no-build
   ```

4. Manually smoke-test startup, Twitch connection, local server startup, remote RCON, one paid command/refund path, multiplayer targeting, settings save/load, and shutdown.
5. Check the release archive for tokens, passwords, configs, databases, logs, worlds, build symbols, and unrelated files.
6. Verify documentation links and supported Minecraft/Java versions.
7. Create the tag and release only after review.

## Release notes

Use concrete entries grouped under Added, Changed, Fixed, Security, Removed, and Known Issues. Prefer statements such as “Fixed token refunds after a failed RCON batch” over broad claims such as “performance and stability improvements.”

## Historical releases

Older releases predate the repository changelog. Preserve existing Git tags and GitHub Release notes as the historical record; do not rewrite published artifacts.
