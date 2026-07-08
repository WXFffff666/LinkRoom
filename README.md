# LinkRoom

Windows P2P LAN gaming tool. Create room, share room ID, friends join, direct P2P connection.

## Features

- Create/Join rooms with 8-char room ID + optional password
- Auto NAT detection via Stun.Net RFC 5780
- Game port scanner (Minecraft 25565, CS 27015, etc.)
- UPnP port mapping (IGD→NAT-PMP fallback, from EasyTier)
- Real-time peer list (IP, NAT type, latency)
- Log viewer with sanitization
- Self-check diagnostic tool
- Single-file exe (103 MB), auto-extracts EasyTier core

## Quick Start

1. Download `LinkRoom.exe`
2. Run as Administrator (first run only, for Wintun driver)
3. Create room: click Create, set optional password, share room ID
4. Join room: enter room ID + password, click Join

## Credits

| Project | Use | License |
|---------|-----|---------|
| [EasyTier](https://github.com/EasyTier/EasyTier) | P2P core (subprocess) | LGPL-3.0 |
| [NatTypeTester](https://github.com/HMBSbige/NatTypeTester) | NAT detection reference | MIT |
| [OPL-WpfApp](https://github.com/Guailoudou/OPL-WpfApp) | WPF UI reference | - |
| [Tailscale](https://github.com/tailscale/tailscale) | P2P architecture reference | BSD-3 |
| [iNKORE.UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) | Mica theme | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM framework | MIT |
| [Stun.Net](https://github.com/HMBSbige/Stun.Net) | STUN client (RFC 5389/5780) | MIT |
| [Wintun](https://www.wintun.net/) | Virtual NIC driver | Proprietary |

## License

LinkRoom wrapper: MIT | EasyTier core: LGPL-3.0 (subprocess) | Wintun: bundled license
