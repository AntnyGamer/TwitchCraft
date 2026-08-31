# Releases

## Versioning

Before publishing, align these values:

- Application `Version` and `FileVersion` in `TwitchCraftBot_SOURCE/TwitchCraftBot.csproj`; `FileVersion` is the canonical user-facing version reported in structured logs
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
   dotnet format TwitchCraft.slnx --verify-no-changes --no-restore
   ```

4. Confirm the root solution built TwitchCraft, `TwitchCraftBot.Tests`, and the test-process helper project.
5. Download or inspect the CI `code-coverage` artifact; no percentage gate is enforced until a stable baseline is documented.
6. Manually smoke-test startup, Twitch connection, local server startup, remote RCON, one paid command/refund path, multiplayer targeting, settings save/load, and shutdown.
7. Launch TwitchCraft and smoke-test browser device authorization, automatic bot-account display, disabled-Start validation, saved-authorization renewal, and reauthorization from Settings.
8. Check the release archive for tokens, passwords, configs, databases, logs, worlds, build symbols, and unrelated files.
9. Verify documentation links and supported Minecraft/Java versions.
10. Create the tag and release only after review.

## Release notes

Use concrete entries grouped under Added, Changed, Fixed, Security, Removed, and Known Issues. Prefer statements such as “Fixed token refunds after a failed RCON batch” over broad claims such as “performance and stability improvements.”

## Historical releases

Older releases predate the repository changelog. Preserve existing Git tags and GitHub Release notes as the historical record; do not rewrite published artifacts.
