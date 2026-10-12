# Twitch chat commands

This file documents the command registry in the current source. Commands are case-insensitive. The tables use the default `!` prefix; the primary and optional secondary prefixes can be changed in Settings.

## Targeting, pricing, and refunds

In singleplayer, targeted commands use the configured streamer Minecraft account. In multiplayer, most gameplay commands accept a Minecraft username, `all`, or `random`:

```text
!heal PlayerName
!heal all
!heal random
```

For paid targeted commands, one target pays the listed base cost. Multiple targets use the existing reduced scaling formula `base cost × (player count + 1) ÷ 2`, rounded down to a whole token. The command-cost multiplier is then applied and rounded up. Invalid, offline, protected, or unavailable targets are rejected before dispatch where applicable. Spectators may be filtered from gameplay targeting.

Paid gameplay commands refund when delivery cannot be confirmed. Remote Control Mode uses matching RCON command-response packets rather than response text. Any confirmed command in a multi-command action keeps the charge; if none are confirmed, it refunds. This avoids language/version-dependent parsing while still rejecting authentication, connection, pre-confirmation timeout, and malformed-protocol failures.

The optional global gameplay-command cooldown applies to normal gameplay commands. By default, `!lightning`, `!tiny`, and `!giant` each use an independent five-minute cooldown shared by all viewers. `!addheart` and `!removeheart` share one five-minute cooldown, while `!gambletokens` uses a five-minute per-viewer cooldown. Per-command cooldown settings can override these defaults.

## Gameplay commands

`[target]` can be a Minecraft username, `all`, or `random` in multiplayer. If no target is given, it defaults to the streamer. Costs and cooldowns are the streamer's defaults and may be customized.

| Command | Syntax | Base cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!effect` | `!effect [count 1-25] [target]` | 1 per effect | Player(s) | Everyone | Gives the player 1–25 random effects, depending on the amount specified. |
| `!givelight` | `!givelight [target]` | 3 | Player(s) | Everyone | Spawns a light block near the player. |
| `!heal` | `!heal [target]` | 3 | Player(s) | Everyone | Heals the player by 4 hearts. |
| `!anvil` | `!anvil [target]` | 5 | Player(s) | Everyone | Drops an anvil above the player after clearing a short vertical column. |
| `!insult` | `!insult [target]` | 5 | Player(s) | Everyone | Insults the player with an on-screen title. |
| `!loot` | `!loot [target]` | 5 | Player(s) | Everyone | Gives the player random loot. |
| `!troll` | `!troll [target]` | 5 | Player(s) | Everyone | Plays a creeper noise for the player without notifying them. |
| `!turnaround` | `!turnaround [target]` | 5 | Player(s) | Everyone | Turns the player around 180 degrees. |
| `!xp` | `!xp [target]` | 5 | Player(s) | Everyone | Removes 1 level of XP from the player. |
| `!switchmilk` | `!switchmilk [target]` | 6 | Player(s) | Everyone | Turns one of the player's milk buckets into a random bucket type (requires a milk bucket). |
| `!fireworks` | `!fireworks [target]` | 10 | Player(s) | Everyone | Spawns 10 non-exploding fireworks over the player. |
| `!mob` | `!mob [target]` | 10 | Player(s) | Everyone | Summons a random mob near the player. |
| `!rename` | `!rename [target]` | 10 | Player(s) | Everyone | Renames the player's held item after the Twitch viewer (requires a renameable item). |
| `!weather` | `!weather` | 10 | World | Everyone | Changes the weather to rain or a thunderstorm. |
| `!explode` | `!explode [target]` | 15 | Player(s) | Everyone | Spawns active TNT on the player. |
| `!invincible` | `!invincible [target]` | 15 | Player(s) | Everyone | Makes the player invincible for 15 seconds. |
| `!lava` | `!lava [target]` | 15 | Player(s) | Everyone | Spawns lava above the player. |
| `!night` | `!night` | 15 | World | Everyone | Sets the time to night. |
| `!scared` | `!scared [target]` | 15 | Player(s) | Everyone | Spawns 20 cats above the player and calls them a scaredy cat. |
| `!water` | `!water [target]` | 15 | Player(s) | Everyone | Spawns water above the player. |
| `!enchant` | `!enchant [target]` | 20 | Player(s) | Everyone | Applies a random enchantment to the player's held item, including normally incompatible combinations. Still costs tokens if the hand is empty. |
| `!giant` | `!giant [target]` | 20 | Player(s) | Everyone | Makes the player twice their normal size for 30 seconds (5-minute global cooldown). |
| `!tiny` | `!tiny [target]` | 20 | Player(s) | Everyone | Makes the player half their normal size for 30 seconds (5-minute global cooldown). |
| `!clearhand` | `!clearhand [target]` | 25 | Player(s) | Everyone | Clears the item in the player's main hand. |
| `!freeze` | `!freeze [target]` | 30 | Player(s) | Everyone | Prevents the player from walking or sprinting for 15 seconds. |
| `!slaughter` | `!slaughter [target]` | 30 | Player(s) | Everyone | Kills nearby mobs within 30 blocks (does not kill players, the Ender Dragon, or the Wither). |
| `!johnny` | `!johnny [target]` | 40 | Player(s) | Everyone | Spawns an aggressive vindicator named Johnny roughly 15 blocks away from the player. |
| `!chargedcreeper` | `!chargedcreeper [target]` | 45 | Player(s) | Everyone | Spawns a glowing, charged creeper roughly 15 blocks away that pursues the player. |
| `!swarm` | `!swarm [target]` | 45 | Player(s) | Everyone | Summons 5 random mobs around the player. |
| `!addheart` | `!addheart <1-5> [target]` | 50 per heart | Player(s) | Everyone | Adds 1-5 maximum hearts for 10 minutes, up to 20 effective hearts. |
| `!lightning` | `!lightning [target]` | 50 | Player(s) | Everyone | Strikes the player with lightning (5-minute global cooldown). |
| `!removeblock` | `!removeblock [target]` | 50 | Player(s) | Everyone | Removes the block below the player (protects bedrock and selected container blocks). |
| `!removeheart` | `!removeheart <1-5> [target]` | 50 per heart | Player(s) | Everyone | Removes 1-5 maximum hearts for 10 minutes, down to 5 effective hearts. |
| `!teleport` | `!teleport [target]` | 70 | Player(s) | Everyone | Teleports the player to a random location, up to approximately 2,000 blocks away. |
| `!totem` | `!totem [target]` | 100 | Player(s) | Everyone | Gives the player a Totem of Undying in their off-hand. |
| `!clear` | `!clear [target]` | 125 | Player(s) | Everyone | Clears the player's entire inventory. |
| `!mlg` | `!mlg [target]` | 150 | Player(s) | Everyone | Launches the player 200 blocks into the air with a water bucket, or 50 blocks with a cobweb in the Nether. |

## Minigame commands

`!chickenbet`, `!guess`, and `!damagewither` only work during their matching minigame. `!gambletokens` is a separate token gamble that does **not** require a minigame to be active. Paid bets are rejected or refunded if a minigame round closes during an update.

| Command | Syntax | Cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!chickenbet` | `!chickenbet <token-amount> <seconds>` | Bet amount | Active Chicken Run | Everyone | Bets tokens on the chicken's finishing time during Chicken Run. |
| `!damagewither` | `!damagewither <token-amount>` | Bet amount | Active Wither Battle | Everyone | Spends tokens to damage the Wither during Wither Battle. |
| `!gambletokens` | `!gambletokens <amount> [risk 1-10]` | 5–150 token bet | Self | Everyone | Gambles 5–150 tokens with optional risk level 1–10 (5-minute per-viewer cooldown). |
| `!guess` | `!guess <1-100>` | Free | Active number round | Everyone | Guesses a number from 1 to 100 during Number Guess (correct guesses earn 10 tokens). |

## Utility and economy

| Command | Syntax | Cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!commandstats` | `!commandstats` | Free | Current session | Everyone | Shows command statistics for the current session. |
| `!followreward` | `!followreward` | Free | None | Everyone | Explains the automatic one-time follower reward and its configured amount (50 tokens by default). |
| `!help` | `!help` | Free | None | Everyone | Links to the full TwitchCraft command list. |
| `!playerlist` | `!playerlist` | Free | Online players | Everyone | Gives a list of players currently in Minecraft. |
| `!tokenleaderboard` | `!tokenleaderboard` | Free | Twitch viewers | Everyone | Shows the 5 viewers with the highest token balances. |
| `!tokenrank` | `!tokenrank [twitch-user]` | Free | Twitch user | Everyone | Checks your token leaderboard rank or another viewer's rank. |
| `!tokens` | `!tokens [twitch-user]` | Free | Twitch user | Everyone | Checks how many tokens you or another viewer has. |
| `!tradetokens` | `!tradetokens <twitch-user> <amount>` | Entered amount | Twitch user | Everyone | Gives another viewer half of the specified tokens, rounded down, while removing the full amount from you (minimum 2 tokens). |

### Streamer Commands

These commands are available to the broadcaster and authorized bot; moderators may also use them if enabled. Minecraft player administration requires local multiplayer mode.

| Command | Syntax | Cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!ban` | `!ban <minecraft-user> [reason]` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Permanently bans a Minecraft player from the local multiplayer server (streamer/bot; moderators if enabled). |
| `!givetokens` | `!givetokens [twitch-user\|all\|random] <amount>` | Free admin action | Twitch viewers | Broadcaster/bot; moderators if enabled | Gives tokens to a viewer, all viewers, or a random viewer (streamer/bot; moderators if enabled). |
| `!kick` | `!kick <minecraft-user> [reason]` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Kicks a Minecraft player from the local multiplayer server (streamer/bot; moderators if enabled). |
| `!removetokens` | `!removetokens [twitch-user\|all\|random] <amount>` | Free admin action | Twitch viewers | Broadcaster/bot; moderators if enabled | Removes tokens from a viewer, all viewers, or a random viewer (streamer/bot; moderators if enabled). |
| `!unban` | `!unban <minecraft-user>` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Unbans a Minecraft player from the local multiplayer server (streamer/bot; moderators if enabled). |
| `!whitelistadd` | `!whitelistadd <minecraft-user>` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Adds a Minecraft player to the local server whitelist (streamer/bot; moderators if enabled). |
| `!whitelistremove` | `!whitelistremove <minecraft-user>` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Removes a Minecraft player from the local server whitelist (streamer/bot; moderators if enabled). |

## Viewer token notes

- When automatic Bit rewards are enabled by the streamer, **1 Bit earns 1 token** (for example, 100 Bits earns 100 tokens).
- New followers receive a **one-time reward of 50 tokens by default**, unless the streamer changes or disables it.
- Streamers can disable commands or change prefixes, costs, and cooldowns. The listed prices reflect default settings and may be different in a particular channel.
## Compatibility and failure cases

Gameplay commands are intended for the Minecraft versions listed in [INSTALLATION.md](INSTALLATION.md) and use the application's version-support layer for syntax differences. Local stdin and remote RCON are supported unless a row says local-only.

Heart commands read the target's current effective maximum health before applying their own temporary modifier, so other commands, mods, or plugins that change maximum health are included in the 5-20-heart limit. TwitchCraft removes only its own modifier when the effect expires.

A command can be rejected when:

- Minecraft is still starting or RCON is unavailable
- The command or minigame is on cooldown
- The sender lacks tokens or broadcaster/moderator permission
- The target is invalid, offline, a spectator, protected, or not resolvable
- A required held item or game condition is absent
- The selected Minecraft version cannot perform the requested syntax
- A minigame round is not active or its bet limits are exceeded
