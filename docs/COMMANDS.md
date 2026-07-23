# Twitch chat commands

This file documents the command registry in the current source. Commands are case-insensitive.

## Targeting, pricing, and refunds

In singleplayer, targeted commands use the configured streamer Minecraft account. In multiplayer, most gameplay commands accept a Minecraft username, `all`, or `random`:

```text
!heal PlayerName
!heal all
!heal random
```

For paid targeted commands, one target pays the listed base cost. Multiple targets use the existing reduced scaling formula `base cost × (player count + 1) ÷ 2`, rounded down to a whole token. Invalid, offline, protected, or unavailable targets are rejected before dispatch where applicable. Spectators may be filtered from gameplay targeting.

Paid gameplay commands refund the charged tokens when TwitchCraft cannot dispatch the Minecraft command. A successfully dispatched command is not refunded merely because its effect was not visible in game.

The optional global gameplay-command cooldown applies to normal gameplay commands. `!lightning` uses its own five-minute global cooldown. `!gambletokens` uses a five-minute per-viewer cooldown.

## Utility and economy

| Command | Syntax | Cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!help` | `!help` | Free | None | Everyone | Shows a short introduction and command-list link. |
| `!playerlist` | `!playerlist` | Free | Online players | Everyone | Lists active Minecraft players. |
| `!tokens` | `!tokens [twitch-user]` | Free | Twitch user | Everyone | Shows your balance or another viewer's balance. |
| `!tradetokens` | `!tradetokens <twitch-user> <amount>` | Entered amount | Twitch user | Everyone | Spends the sender's tokens and gives the recipient 50% of the amount. |
| `!gambletokens` | `!gambletokens <amount> [risk 1-10]` | 5–150 token bet | Self | Everyone | Gambles tokens; risk defaults to 5 and is clamped to 1–10. Five-minute cooldown. |
| `!givetokens` | `!givetokens [twitch-user\|all\|random] <amount>` | Free admin action | Twitch viewers | Broadcaster/bot; moderators if enabled | Adds tokens. A one-argument form targets the sender. |
| `!removetokens` | `!removetokens [twitch-user\|all\|random] <amount>` | Free admin action | Twitch viewers | Broadcaster/bot; moderators if enabled | Removes tokens. A one-argument form targets the sender. |
| `!ban` | `!ban <minecraft-user> [reason]` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Bans a player. Local multiplayer only; the streamer account is protected. |
| `!unban` | `!unban <minecraft-user>` | Free admin action | Minecraft user | Broadcaster/bot; moderators if enabled | Pardons a player. Local multiplayer only. |

## Gameplay commands

`[target]` means a player name, `all`, or `random` in multiplayer; it may be omitted in singleplayer.

| Command | Syntax | Base cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!anvil` | `!anvil [target]` | 5 | Player(s) | Everyone | Clears a short vertical column and drops an anvil. |
| `!clear` | `!clear [target]` | 125 | Player(s) | Everyone | Clears the target inventory. |
| `!clearhand` | `!clearhand [target]` | 25 | Player(s) | Everyone | Clears the main-hand item. |
| `!effect` | `!effect [count 1-25] [target]` | 1 per effect | Player(s) | Everyone | Gives one or more random effects; cost scales by effect count and targets. |
| `!explode` | `!explode [target]` | 15 | Player(s) | Everyone | Summons primed TNT. |
| `!fireworks` | `!fireworks [target]` | 10 | Player(s) | Everyone | Launches a short series of fireworks. |
| `!freeze` | `!freeze [target]` | 30 | Player(s) | Everyone | Applies extreme slowness for 15 seconds. |
| `!givelight` | `!givelight [target]` | 3 | Player(s) | Everyone | Places a light source near the target. |
| `!heal` | `!heal [target]` | 3 | Player(s) | Everyone | Gives instant health. |
| `!insult` | `!insult [target]` | 5 | Player(s) | Everyone | Shows an insulting title to the target. |
| `!invincible` | `!invincible [target]` | 15 | Player(s) | Everyone | Gives maximum resistance for 15 seconds. |
| `!johnny` | `!johnny [target]` | 40 | Player(s) | Everyone | Spawns in a "Johnny" vindicator, very strong. |
| `!lava` | `!lava [target]` | 15 | Player(s) | Everyone | Places lava above the target. |
| `!lightning` | `!lightning [target]` | 50 | Player(s) | Everyone | Strikes lightning; separate five-minute global cooldown. |
| `!loot` | `!loot [target]` | 5 | Player(s) | Everyone | Spawns several random loot-table drops. |
| `!mlg` | `!mlg [target]` | 150 | Player(s) | Everyone | Launches the target and provides a recovery item appropriate to the dimension. |
| `!mob` | `!mob [target]` | 10 | Player(s) | Everyone | Summons a random mob at the target. |
| `!night` | `!night` | 15 | World | Everyone | Sets the world time to night. |
| `!removeblock` | `!removeblock [target]` | 50 | Player(s) | Everyone | Removes the block below the target while protecting selected container/bedrock blocks. |
| `!rename` | `!rename [target]` | 10 | Player(s) | Everyone | Renames the target's held renameable item after the Twitch sender. |
| `!scared` | `!scared [target]` | 15 | Player(s) | Everyone | Spawns a bunch of cats and calls the target a scaredy cat. |
| `!slaughter` | `!slaughter [target]` | 30 | Player(s) | Everyone | Removes nearby mobs around the target. |
| `!swarm` | `!swarm [target]` | 45 | Player(s) | Everyone | Summons five distinct random mobs at the target. |
| `!switchmilk` | `!switchmilk [target]` | 6 | Player(s) | Everyone | Changes one milk bucket into an empty, water, or lava bucket if present. |
| `!teleport` | `!teleport [target]` | 70 | Player(s) | Everyone | Spreads the target to a random location with dimension-aware limits. |
| `!totem` | `!totem [target]` | 100 | Player(s) | Everyone | Places a Totem of Undying in the off hand. |
| `!troll` | `!troll [target]` | 5 | Player(s) | Everyone | Plays a creeper priming sound. |
| `!water` | `!water [target]` | 15 | Player(s) | Everyone | Places water above the target. |
| `!weather` | `!weather` | 10 | World | Everyone | Randomly starts rain or a thunderstorm. |
| `!xp` | `!xp [target]` | 5 | Player(s) | Everyone | Removes one experience level. |

## Minigame commands

These commands are meaningful only while their matching minigame is active. Paid bets are rejected or refunded if a round closes during an attempted update.

| Command | Syntax | Cost | Targeting | Permission | Description |
|---|---|---:|---|---|---|
| `!chickenbet` | `!chickenbet <token-amount> <seconds>` | Bet amount | Active Chicken Run | Everyone | Bets on the chicken's finishing time. The handler also recognizes the legacy reversed argument order when unambiguous. |
| `!guess` | `!guess <1-100>` | Free | Active number round | Everyone | Guesses the current number; correct guesses win 10 tokens. Five-second per-viewer guess cooldown. |
| `!damagewither` | `!damagewither <token-amount>` | Bet amount | Active Wither Battle | Everyone | Spends tokens as Wither damage and records the viewer's contribution. |

## Compatibility and failure cases

Gameplay commands are intended for the Minecraft versions listed in [INSTALLATION.md](INSTALLATION.md) and use the application's version-support layer for syntax differences. Local stdin and remote RCON are supported unless a row says local-only.

A command can be rejected when:

- Minecraft is still starting or RCON is unavailable
- The command or minigame is on cooldown
- The sender lacks tokens or broadcaster/moderator permission
- The target is invalid, offline, a spectator, protected, or not resolvable
- A required held item or game condition is absent
- The selected Minecraft version cannot perform the requested syntax
- A minigame round is not active or its bet limits are exceeded
