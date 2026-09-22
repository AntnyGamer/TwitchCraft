# TwitchCraft Bind IP Support Guide

In order to use different Bind IPs on TwitchCraft, it is important you know what each one does and how to use it properly.

Most users should not change the Bind IP. If normal multiplayer works for you, follow the normal [multiplayer guide](MULTIPLAYER.md) instead.

## What Is Bind IP?

The Bind IP controls what network address the Minecraft server listens on.

The Bind IP is not always the same address your friends use to join.

Example:

Bind IP: **0.0.0.0**  
Friend joins with: your public IP, LAN IP, or VPN IP

`0.0.0.0` means TwitchCraft listens on all normal IPv4 network adapters.

## Recommended Defaults

Singleplayer or local only: **127.0.0.1**  
Normal multiplayer: **0.0.0.0** (TwitchCraft will set this for you, do not set the Bind IP to this)

Only change Bind IP if you know you need a VPN IP or an IPv6 address.

## VPN IP Setup

VPN multiplayer can be useful if you do not want to port forward.

Common VPN programs:

- Hamachi
- Tailscale
- Radmin VPN
- ZeroTier

### Recommended VPN Method

Use this Bind IP:

**0.0.0.0**

Then have your friends join using your VPN IP.

Example:

Your VPN IP: **25.50.100.20**  
Friend joins: **25.50.100.20**

This works because `0.0.0.0` listens on all IPv4 adapters, including most VPN adapters. If you did this, however, then you probably would not be on this page.

### Advanced VPN Method

You can also put your actual VPN IP as the Bind IP.

Example:

Bind IP: **25.50.100.20**  
Friend joins: **25.50.100.20**

This makes TwitchCraft listen only on the VPN adapter.

Only use this if you specifically want VPN-only multiplayer.

### VPN Notes

- Your friends must be connected to the same VPN network.
- Router port forwarding is usually not needed for VPN multiplayer.
- Windows Firewall may still need to allow Java or Minecraft.
- If your VPN IP changes, update the Bind IP.

## IPv6 Setup

IPv6 is for advanced users.

IPv6 can be useful if your internet supports IPv6 or if normal IPv4 port forwarding does not work.

### IPv6 Bind Options

`::1` means IPv6 local-only. Only your own computer can connect.

`::` means IPv6 all adapters. This is like `0.0.0.0`, but for IPv6.

A specific IPv6 address makes TwitchCraft listen only on that IPv6 address.

### Recommended IPv6 Method

Use this Bind IP:

**::**

Then have friends join using your IPv6 address.

If they add a port, they may need brackets around the IPv6 address.

Example:

`[2600:abcd:1234:5678::1]:25565`

### IPv6 Notes

IPv6 only works if:

- Your internet supports IPv6.
- Your friend also has IPv6.
- Your router allows incoming IPv6 traffic.
- Windows Firewall allows Java or Minecraft.
- Your IPv6 address is reachable.

If IPv6 does not work, use normal IPv4 multiplayer or VPN multiplayer instead.

## localhost Setup

`localhost` means your own computer.

It is similar to `127.0.0.1`.

`localhost` is supported as a formality, as TwitchCraft will just convert the Bind IP to `127.0.0.1`.

Friends cannot join by typing `localhost`.

If your friend types `localhost`, they will connect to their own computer, not yours.

Recommended local-only Bind IP: **127.0.0.1**

## What Should Friends Join With?

### Normal Public Multiplayer

Bind IP: **0.0.0.0**  
Friends join with: your public IP

This usually needs port forwarding and Windows Firewall access.

### Same Wi-Fi or LAN Multiplayer

Bind IP: **0.0.0.0**  
Friends join with: your LAN IP

Example:

**192.168.1.25**

### VPN Multiplayer

Bind IP: **0.0.0.0**  
Friends join with: your VPN IP

Advanced VPN-only option:

Bind IP: your VPN IP  
Friends join with: your VPN IP

### IPv6 Multiplayer

Bind IP: **::**  
Friends join with: your IPv6 address

With a port, they may need this format:

`[YOUR_IPV6_ADDRESS]:25565`

## Quick Guide

Local only: **127.0.0.1**  
Normal multiplayer: **0.0.0.0** (set by TwitchCraft)  
VPN multiplayer: **0.0.0.0**, then friends join your VPN IP  
VPN-only multiplayer: your VPN IP  
IPv6 multiplayer: **::**, then friends join your IPv6 address  
`localhost`: same as **127.0.0.1**

## Resetting Bind IP

If you accidentally changed the Bind IP, reset it to **127.0.0.1**.

If you see a Bind IP warning and you did not mean to use an advanced Bind IP, press **Yes** to reset it.
