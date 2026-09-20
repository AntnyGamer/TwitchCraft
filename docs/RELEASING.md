# Releases

## Versioning

Before publishing, align the release identity:

- Application `Version` and `FileVersion` in `TwitchCraft_SOURCE/TwitchCraft.csproj` use the four-part Windows/.NET form (for example, `1.9.0.0`); `FileVersion` is the canonical version reported in structured logs
- Git tag, GitHub Release title, and `CHANGELOG.md` use the public three-part release (for example, `1.9.0`)
- The public version must match the first three components of the application/file version; use the fourth component only as an intentional revision

Do not publish from an unreviewed working tree.

## Release checklist

1. Confirm the intended source branch and review the complete diff.
2. Update `CHANGELOG.md` with subsystem-specific entries.
3. Run:

   ```powershell
   dotnet restore TwitchCraft.slnx
   dotnet build TwitchCraft.slnx -c Release --no-restore
   Push-Location TwitchCraft.Tests
   dotnet test --project TwitchCraft.Tests.csproj -c Release --no-build
   Pop-Location
   dotnet format TwitchCraft.slnx --verify-no-changes --no-restore
   ```

4. Confirm the root solution built TwitchCraft, `TwitchCraft.Tests`, and the test-process helper project.
5. Download or inspect the CI `code-coverage` artifact and confirm line coverage remains at or above the 10% CI floor.
6. Manually smoke-test startup, browser device authorization, automatic bot-account display, disabled-Start validation, saved-authorization renewal and reauthorization, Twitch connection, local server startup, remote RCON, one paid command/refund path, multiplayer targeting, settings save/load, and shutdown.
7. Check the release archive for tokens, passwords, configs, databases, logs, worlds, build symbols, and unrelated files.
8. Verify documentation links and supported Minecraft/Java versions.
9. Create the tag and release only after review.

## Release notes

Use concrete entries grouped under Added, Changed, Fixed, Security, Removed, and Known Issues. Prefer statements such as “Fixed token refunds after a failed RCON batch” over broad claims such as “performance and stability improvements.”

## Historical releases

Older releases predate the repository changelog. Preserve existing Git tags and GitHub Release notes as the historical record; do not rewrite published artifacts.
