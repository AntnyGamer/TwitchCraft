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
dotnet test TwitchCraft.slnx -c Release --no-build
```

The root solution is the canonical validation entry point. Its Release build compiles TwitchCraft and `TwitchCraftBot.Tests`. The Release build and all tests must pass before a pull request is ready for review.

CI also collects an XPlat Code Coverage report and publishes `coverage.cobertura.xml` as the `code-coverage` workflow artifact. To reproduce that collection locally:

```powershell
dotnet test TwitchCraft.slnx -c Release --no-build --collect:"XPlat Code Coverage" --results-directory TestResults
```

No coverage percentage gate is enforced until a stable baseline is measured and documented.

## Source layout

- `TwitchCraftBot_SOURCE/` — WPF application, runtime, commands, persistence, and assets
- `TwitchCraftBot.Tests/` — non-UI regression tests
- `docs/` — canonical user and maintainer documentation
- `.github/` — CI and contribution templates

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the runtime flow.

## Project rules

- Never commit Twitch tokens, RCON passwords, authorization headers, real `config.json` files, user databases, server worlds, logs, or public IP addresses.
- Do not block network, database, or filesystem work on the WPF UI thread.
- Preserve command names, prices, permissions, targeting, cooldowns, messages, and statistics flags unless a behavior change is explicitly approved.
- Paid commands must refund tokens exactly once when dispatch fails and must not refund after a successful dispatch.
- New targeted commands must validate target names, reject unavailable players, respect protected users, and scale costs consistently.
- Minecraft-version-specific syntax belongs in the existing version-support and command-building layers.
- Database schema changes require a safe migration, backward-compatibility review, and tests.
- Bug fixes should include a regression test whenever the affected behavior can be isolated without UI automation or a live Minecraft server.

## Adding or changing a command

1. Find the appropriate registration or handler file under `TwitchCraftBot_SOURCE/Commands/`.
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
