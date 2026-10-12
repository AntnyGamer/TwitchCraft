# GitHub Pages command browser

This folder contains a small, static command browser for TwitchCraft viewers.

## Files

- `index.html`: redirect from the old Pages root to `/commands/`
- `commands/index.html`: public command catalog, search and navigation
- `commands/server-icon.png`: the original logo image already present at `TwitchCraft_SOURCE/Assets/server-icon.png`
- `command-browser.css`: responsive styling
- `command-browser.js`: parses command documentation, handles filtering and copying syntax
- `.nojekyll`: tells GitHub Pages to serve `COMMANDS.md` as the original Markdown text rather than converting it to HTML
- `COMMANDS.md`: **the only maintained source of built-in command information**

The browser fetches `../COMMANDS.md` from the same origin. No duplicated command JSON, frontend framework, build step, database, or tracking is needed. The price-multiplier selector loads its preset choices from the public repository's `main` branch `TwitchCraft_SOURCE/Frames/Settings.xaml.cs` using `raw.githubusercontent.com` on each visit. If GitHub's raw host is unavailable, the dropdown keeps the locally bundled presets so the catalog remains usable. The dropdown doesn't change any streamer's real bot settings.

## Preview locally

From the repository root, run:

```sh
python -m http.server 8000
```

Then open `http://127.0.0.1:8000/docs/commands/`.

Opening the HTML directly from `file://` is not supported because browsers generally block local `fetch` access to `COMMANDS.md`.

## Publish after reviewing this branch

This feature was developed on `feature/command-browser`; creating the branch **does not publish** the website or change the current `main` branch.

1. Review and merge the branch into `main` when ready.
2. In GitHub repository **Settings → Pages**, choose **Deploy from a branch**.
3. Select branch `main` and folder `/docs`, then save.
4. Verify `https://antnygamer.github.io/TwitchCraft/commands/` displays commands and that search, category filters, and clipboard copying work on desktop and mobile.
5. Only once the site is accessible should the old Rentry page be retired or redirected to the new site.

**Important:** The prepared in-app Help and `!help` links on this feature branch point to `/commands/`. GitHub Pages uses a trailing slash for a directory index; `/commands` will normally redirect to `/commands/`. They are not live until this branch is deployed through GitHub Pages or merged into the configured publishing branch.

## Keeping the catalog accurate

The browser reads these Markdown headings (including compatibility aliases for older scripts):

- `## Gameplay commands` → **Game Commands**
- `## Minigame commands` → **Minigame Commands**
- `## Utility and economy` → **Utility Commands**
- `### Streamer Commands` → **Streamer Commands**

The streamer heading stays nested under Utility in Markdown so an older cached command-browser script still shows all 56 commands, even before receiving the updated JavaScript. The current script recognizes the nested heading and places its seven commands in their own category. When changing the parser or section format, bump the script's version query in `commands/index.html` to invalidate cached assets. The parser throws an error rather than silently showing a partial list if command rows cannot be categorized.

Each command table row must contain exactly six columns: command name, syntax, default cost, targeting, permission, and description.

The descriptions use concise Rentry-style wording, adjusted where the older Rentry page no longer matches the program (for example, new-follower rewards default to 50 and `!mlg` differs by dimension). Whenever the built-in command registry changes, update `COMMANDS.md` in the same pull request. Check that every built-in command appears once and no command was removed without updating the documentation. The browser itself needs no manual updates for command additions, deletions, or changed descriptions/costs, as long as the table format stays consistent.

Public command costs are **defaults**. The dropdown uses the desktop app's preset multiplier list (currently 0×, 0.5×, 0.75×, 1×, 1.25×, 1.5×, 2×, 3×) and scales **gameplay** costs by rounding up each total. Economy transfers, gambling bets, and admin actions are not multiplied. Streamers can override the command prefix, enabled commands, token-cost multiplier, and cooldowns. Multiplayer targeting can change prices. The site is intentionally not connected to anyone's local app settings or Twitch account.

## Quick manual checks

- All 56 commands currently in `COMMANDS.md` appear: 37 game, 4 minigame (including the independent gamble command), 8 utility, and 7 streamer commands.
- Search by a partial name (`heal`), description (`lightning`), and permission (`moderators`).
- Switching categories and the **Free only** filter adjusts the results correctly, including when 0× turns gameplay commands free.
- Changing the multiplier scales gameplay prices and leaves transfers, bets, and admin costs unchanged.
- Dropdown options match `CommandCostMultiplierOptions` in `Settings.xaml.cs`, and the local preset fallback works without the raw GitHub host.
- The page at `/TwitchCraft/` redirects to `/TwitchCraft/commands/` while preserving URL search parameters.
- Copying `!givetokens` preserves `[twitch-user|all|random]` without splitting the table row.
- Syntax placeholders (`<amount>` and `[target]`) remain visible and must be replaced before sending.
- Keyboard focus, `/` search shortcut, screen-reader status, and phone-width layout work.
- With `COMMANDS.md` unavailable, an explanatory error and a GitHub fallback link appear.

The website does not execute Twitch or Minecraft commands. It only displays and copies syntax.
