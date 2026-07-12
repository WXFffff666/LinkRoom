# LinkRoom

Windows 便携式 P2P 游戏联机工具。单 exe 发布，数据存储在 exe 同目录 `LinkRoomData/`。

## 功能

- **创建/加入房间** — 8 位房间号 + 可选密码，支持 `linkroom://` 联机链接
- **双模式** — 轻量模式（无虚拟网卡）/ LAN 模式（虚拟网卡 + UDP 广播，MC 自动发现）
- **NAT 检测** — 并发 STUN 探测，支持自定义/远程 STUN 列表
- **共享节点中继** — 默认 `tcp://public.easytier.top:11010`
- **连接质量面板** — P2P/中继、延迟、丢包、虚拟 IP
- **自动重连** — 指数退避，可配置次数
- **房间历史** — 最近 5 个房间快速重连
- **CLI 模式** — `LinkRoom.exe --join ROOM --pass xxx --headless`
- **诊断导出 / Web 管理面板 / 插件 API**

## 数据目录

```
LinkRoom.exe
LinkRoomData/
├── runtime/2.6.4/    EasyTier 核心
├── config/           设置
├── logs/             日志
├── temp/             临时配置
├── diagnostics/      诊断包
└── plugins/          游戏插件 JSON
```

## 构建

```powershell
dotnet publish src\LinkRoom\LinkRoom.csproj -c Release
```

## 自动发布

推送 `v*` 标签即可触发 GitHub Actions 自动构建并发布 Release：

```bash
git tag v1.15.0 && git push origin v1.15.0
```

详见 [docs/RELEASE.md](docs/RELEASE.md)

## 引用项目

| 项目 | 用途 |
|------|------|
| [EasyTier](https://github.com/EasyTier/EasyTier) | P2P 核心 |
| [NatTypeTester](https://github.com/HMBSbige/NatTypeTester) | NAT 检测参考 |
| [OPL-WpfApp](https://github.com/Guailoudou/OPL-WpfApp) | 游戏联机 UI 参考 |
| [Stun.Net](https://github.com/HMBSbige/Stun.Net) | STUN 协议 |

## 许可

LinkRoom: MIT | EasyTier: LGPL-3.0
