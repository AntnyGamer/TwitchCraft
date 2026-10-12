# GitHub Pages command browser

This folder contains a small, static command browser for TwitchCraft viewers.

## Files

- `index.html`: public command catalog, search and navigation
- `command-browser.css`: responsive styling
- `command-browser.js`: parses command documentation, handles filtering and copying syntax
- `.nojekyll`: tells GitHub Pages to serve `COMMANDS.md` as the original Markdown text rather than converting it to HTML
- `COMMANDS.md`: **the only maintained source of built-in command information**

The browser fetches `./COMMANDS.md` from the same origin. No duplicated JSON command list, frontend framework, build step, database, external JavaScript, or tracking is needed.

## Preview locally

From the repository root, run:

```sh
python -m http.server 8000
```

Then open `http://127.0.0.1:8000/docs/`.

Opening the HTML directly from `file://` is not supported because browsers generally block local `fetch` access to `COMMANDS.md`.

## Publish after reviewing this branch

This feature was developed on `feature/command-browser`; creating the branch **does not publish** the website or change the current `main` branch.

1. Review and merge the branch into `main` when ready.
2. In GitHub repository **Settings → Pages**, choose **Deploy from a branch**.
3. Select branch `main` and folder `/docs`, then save.
4. Verify `https://antnygamer.github.io/TwitchCraft/` displays commands and that search, category filters, and clipboard copying work on desktop and mobile.
5. Only once the site is accessible should the old Rentry page be retired or redirected to the new site.

**Important:** The prepared in-app Help and `!help` links on this feature branch point to the future GitHub Pages address. They are not live until GitHub Pages is enabled and deployed.

## Keeping the catalog accurate

The browser reads these Markdown headings:

- `## Game Commands`
- `## Minigame Commands`
- `## Utility Commands`
- `## Streamer Commands`

Each command table row must contain exactly six columns: command name, syntax, default cost, targeting, permission, and description.

The descriptions use concise Rentry-style wording, adjusted where the older Rentry page no longer matches the program (for example, new-follower rewards default to 50 and `!mlg` differs by dimension). Whenever the built-in command registry changes, update `COMMANDS.md` in the same pull request. Check that every built-in command appears once and no command was removed without updating the documentation. The browser itself needs no manual updates for command additions, deletions, or changed descriptions/costs, as long as the table format stays consistent.

Public command costs are **defaults**. Streamers can override the command prefix, enabled commands, token-cost multiplier, and cooldowns. Multiplayer targeting can change prices. The site is intentionally not connected to anyone's local app settings or Twitch account.

## Quick manual checks

- All 56 commands currently in `COMMANDS.md` appear: 37 game, 4 minigame (including the independent gamble command), 8 utility, and 7 streamer commands.
- Search by a partial name (`heal`), description (`lightning`), and permission (`moderators`).
- Switching categories and the **Free only** filter adjusts the results correctly.
- Copying `!givetokens` preserves `[twitch-user|all|random]` without splitting the table row.
- Syntax placeholders (`<amount>` and `[target]`) remain visible and must be replaced before sending.
- Keyboard focus, `/` search shortcut, screen-reader status, and phone-width layout work.
- With `COMMANDS.md` unavailable, an explanatory error and a GitHub fallback link appear.

The website does not execute Twitch or Minecraft commands. It only displays and copies syntax.
