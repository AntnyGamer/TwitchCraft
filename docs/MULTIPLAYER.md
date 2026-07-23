# Multiplayer

Enabling multiplayer allows more Minecraft players and enables TwitchCraft's player targeting. It does not automatically make the local server reachable from the internet.

## Connection addresses

| Player location | Address to use |
|---|---|
| Same computer | `127.0.0.1` |
| Same local network | The server computer's private/LAN IPv4 address |
| Outside the local network | A deliberately configured public endpoint, VPN, or tunnel |

Prefer a private VPN or trusted tunnel when practical. If you expose the Minecraft game port directly, restrict access, keep Windows and Java updated, use online mode where appropriate, and share the address only with people you trust.

## Router and firewall setup

For direct home-network hosting, the usual game port is TCP `25565`:

1. Find the server computer's LAN IPv4 address.
2. Reserve that address in the router if possible.
3. Forward only the Minecraft game port to that LAN address.
4. Allow the Java server through Windows Firewall on the required network profile.
5. Test from outside the home network.

Router menus differ. Do not place the computer in a DMZ and do not expose RCON merely because the game port is exposed.

## Twitch command targeting

When multiplayer targeting is active, most gameplay commands accept a target:

```text
!command <player>
!command all
!command random
```

Some commands accept arguments before or after the target; use [COMMANDS.md](COMMANDS.md) for exact syntax. Costs for targeted paid commands normally scale by the number of resolved, targetable players. Offline, invalid, spectator-filtered, or protected players may be rejected.

## In-game features

- Twitch chat can be relayed into Minecraft when enabled.
- A player-list sidebar is shown in multiplayer.
- Player health can be displayed in the tab list.
- `/trigger locateplayers` can display player coordinates when the bundled datapack is installed and active.

## Common problems

- Friends cannot connect: verify the correct address, game port, firewall, router forwarding, ISP carrier-grade NAT, and server readiness.
- Target is not found: verify exact Minecraft username and that the player is online and not excluded by command rules.
- `all` costs more than expected: paid commands scale by the resolved player count.
- Local player cannot join: wait for the ready state and verify the Minecraft client version matches the server.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for a complete checklist.
