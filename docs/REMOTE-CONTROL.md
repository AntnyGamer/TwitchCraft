# Remote control mode

Remote Control Mode connects TwitchCraft to an already-running Minecraft Java server through RCON. TwitchCraft does not start or stop that remote server.

## Security first

RCON grants powerful server control and does not replace normal player authentication. Prefer a private VPN, private network, firewall allow-list, or authenticated tunnel between TwitchCraft and the server.

Avoid exposing the RCON port directly to the public internet. Never reuse the RCON password, display it on stream, paste it into chat, include it in a support issue, or upload `server.properties` without redacting it.

## Configure the remote server

In the remote server's `server.properties`:

```properties
enable-rcon=true
rcon.port=25575
rcon.password=REPLACE_WITH_YOUR_PASSWORD
```

Restart the server after changing these values. Restrict the firewall so only the TwitchCraft machine or private network can reach the RCON port.

The Minecraft game port and RCON port serve different purposes. Players do not need access to the RCON port.

## Connect TwitchCraft

1. Start the remote Minecraft server first.
2. Open TwitchCraft's Start screen.
3. Press **Ctrl + Alt + R** to show Remote Control Mode.
4. Enter the remote hostname or IP address.
5. Enter the RCON port, normally `25575`.
6. Enter the exact RCON password.
7. Enter the streamer's Minecraft username if requested.
8. Select **Start**.

Use `127.0.0.1` only when TwitchCraft and the remote-controlled server run on the same computer. Use a private address when both machines share a trusted network or VPN.

## Compatibility and behavior

- The configured Minecraft version must match the remote server closely enough for TwitchCraft's version-specific command syntax.
- Command delivery uses RCON rather than local Java standard input.
- Remote mode cannot manage the remote Java process or local server files.
- Local-only administrator operations such as chat-driven ban/unban may be unavailable by design.
- Token refunds occur when a paid command cannot be dispatched successfully; a successful dispatch is not refunded merely because the in-game outcome was not visible.

## Troubleshooting

- Authentication failure: check `enable-rcon`, port, password, and server restart.
- Connection timeout: check host, routing, VPN/tunnel, firewall, and whether the port is listening.
- Commands fail on one server version: verify the configured Minecraft version and consult the log for sanitized diagnostics.
- Queries work intermittently: check latency, packet loss, host limits, and other RCON clients.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) before sharing diagnostics.
