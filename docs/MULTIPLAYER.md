# Multiplayer

Enabling multiplayer allows more Minecraft players and enables TwitchCraft's player targeting. It does not automatically make the local server reachable from the internet.

## Connection addresses

| Player location | Address to use |
|---|---|
| Same computer | `127.0.0.1` |
| Same local network | The server computer's private/LAN IPv4 address |
| Outside the local network | A deliberately configured public endpoint, VPN, or tunnel |

Prefer a private VPN or trusted tunnel when practical. If you expose the Minecraft game port directly, restrict access, keep Windows and Java updated, use online mode where appropriate, and share the address only with people you trust.


## Advanced Bind IP

The **Server Bind IP** in Setup controls which local address or network interface the managed Minecraft server listens on. Leave it at the default unless you intentionally need a specific interface.

TwitchCraft defaults to `127.0.0.1`. When local multiplayer is enabled, TwitchCraft automatically broadens the default loopback bind to `0.0.0.0` for that session so the server can accept IPv4 connections on the computer's available interfaces. The IPv6 loopback address `::1` is similarly broadened to `::`. A deliberately configured non-loopback Bind IP is preserved instead.

Use a custom Bind IP only when you need to bind Minecraft to a particular address, such as a VPN or virtual-network adapter, a specific LAN interface, or IPv6. The value must be an IP address assigned to the computer running TwitchCraft; it is not the address that every player should necessarily type into Minecraft.

If you are using a VPN or virtual network:

1. Connect the host computer to the VPN or virtual network before starting TwitchCraft.
2. Use the IP address assigned to that adapter as the Bind IP.
3. Make sure the other players can reach the host through the same VPN or tunnel.
4. Allow Java/Minecraft through Windows Firewall for the network profile used by that adapter.
5. Follow that VPN or tunnel's connection instructions instead of assuming normal router port forwarding applies.

TwitchCraft shows the **Advanced Bind IP** warning for addresses that need extra care, including IPv6 and address ranges commonly used by VPN, virtual-network, or carrier-grade NAT setups. If you entered the address intentionally, choose **No** on the reset prompt and continue with the appropriate network setup. If you did not intend to change the Bind IP, choose **Yes** to restore the default.

For normal home multiplayer with router port forwarding, you generally do not need to enter your public IP as the Bind IP. Keep the default Bind IP and let TwitchCraft expose the local server when multiplayer is enabled. The router forwards the Minecraft port to the host computer's LAN address.

If a custom Bind IP stops working, run `ipconfig` and confirm that the address still belongs to an active adapter on the TwitchCraft computer. VPN, virtual-network, DHCP, and IPv6 addresses can change between sessions.

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
