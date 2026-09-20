# Contributing to TwitchCraft

Thank you for helping improve TwitchCraft. Changes should preserve existing command behavior and protect users' local data.

## Development requirements

- Windows 10 or 11, 64-bit
- .NET 10 SDK
- Git
- Java 21 and/or 25 when manually validating the matching Minecraft versions

## Setup and validation

From the repository root:

```powershell
dotnet restore TwitchCraft.slnx
dotnet build TwitchCraft.slnx -c Release --no-restore
Push-Location TwitchCraft.Tests
dotnet test --project TwitchCraft.Tests.csproj -c Release --no-build
Pop-Location
```

The root solution is the canonical validation entry point. Its Release build compiles TwitchCraft and `TwitchCraft.Tests`. The Release build and all tests must pass before a pull request is ready for review.

CI also collects a Cobertura coverage report and publishes it as the `code-coverage` workflow artifact. To reproduce that collection locally:

```powershell
Push-Location TwitchCraft.Tests
dotnet test --project TwitchCraft.Tests.csproj -c Release --no-build --results-directory ..\TestResults --coverlet --coverlet-output-format cobertura
Pop-Location
```

CI enforces a 10% minimum line-coverage floor to prevent major coverage regressions.

## Source layout

- `TwitchCraft_SOURCE/` — WPF application, runtime, commands, persistence, and assets
- `TwitchCraft.Tests/` — automated regression and focused integration tests
- `docs/` — canonical user and maintainer documentation
- `.github/` — CI and contribution templates

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the runtime flow.

## Project rules

- Never commit Twitch tokens, RCON passwords, authorization headers, real `config.json` files, user databases, server worlds, logs, or public IP addresses.
- Do not block network, database, or filesystem work on the WPF UI thread.
- Preserve command names, prices, permissions, targeting, cooldowns, messages, and statistics flags unless a behavior change is explicitly approved.
- Paid commands must refund tokens exactly once when the Minecraft send path cannot confirm delivery. In Remote Control Mode, do not parse Minecraft response wording for charging decisions: require a matching RCON command-response packet with the expected response type. For a multi-command RCON action, one confirmed command response is enough to keep the charge if the rest is interrupted; if none are confirmed, return failure so the paid-command transaction refunds and releases its cooldown reservation.
- New targeted commands must validate target names, reject unavailable players, respect protected users, and scale costs consistently.
- Minecraft-version-specific syntax belongs in the existing version-support and command-building layers.
- Database schema changes require a safe migration, backward-compatibility review, and tests.
- Bug fixes should include a regression test whenever the affected behavior can be isolated without UI automation or a live Minecraft server.

## Adding or changing a command

1. Find the appropriate registration or handler file under `TwitchCraft_SOURCE/Commands/`.
2. Reuse the shared target, pricing, cooldown, refund, and command-builder helpers.
3. Check singleplayer, multiplayer, `all`, `random`, offline-player, spectator, and protected-streamer behavior as applicable.
4. Check local stdin and remote RCON modes.
5. Add or update tests before changing version-sensitive syntax.
6. Update [docs/COMMANDS.md](docs/COMMANDS.md).

## Minecraft compatibility

For version-sensitive changes, document and test the oldest and newest supported syntax families. Do not assume a command accepted by the newest server is accepted by every supported version.

## Pull requests

Keep changes focused and explain:

- What changed and why
- User-visible behavior, if any
- Token/refund/cooldown impact
- Minecraft versions and local/remote modes considered
- Tests and manual checks performed
- Any follow-up work intentionally left out

Do not combine a broad refactor with unrelated behavior changes.

## Security reports

Follow [SECURITY.md](SECURITY.md). Do not disclose vulnerabilities or secrets in public issues, discussions, logs, screenshots, or pull requests.
