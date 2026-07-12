# LinkRoom

Windows 便携式 P2P 游戏联机工具。单 exe 发布，数据存储在 exe 同目录 `LinkRoomData/`。

**当前版本：v1.16.0**

## 功能

### 联机核心

- **创建/加入房间** — 8 位房间号 + 可选密码，支持 `linkroom://` 联机链接
- **QR 码联机** — 创建房间后自动生成二维码，好友扫码即可加入
- **短链分享** — 短码 + 完整联机链接，一键复制
- **双模式** — 轻量模式（无虚拟网卡）/ LAN 模式（虚拟网卡 + UDP 广播，MC 自动发现）
- **NAT 检测** — 并发 STUN 探测，支持自定义/远程 STUN 列表
- **共享节点中继** — 默认 `tcp://public.easytier.top:11010`
- **UPnP** — 对称型 NAT 可启用端口映射（设置中可配置）
- **IPv6-only / SOCKS5** — 高级网络选项（设置页）

### 连接与质量

- **连接质量面板** — P2P/中继、延迟、丢包、虚拟 IP
- **P2P 路径可视化** — ASCII 路径图，展示 NAT → 策略 → 对端
- **虚拟 IP 复制 / 连接测速** — 一键复制虚拟 IP，TCP 测速到游戏端口
- **连接进度** — NAT 检测 → 路径选择 → 启动 EasyTier 分步进度条
- **自动重连** — 指数退避，可配置次数
- **进程守护** — 15 秒轮询 EasyTier 健康，异常自动恢复
- **Windows 通知** — 成员加入/离开、切换中继模式时 Toast 提醒

### 房间与管理

- **房间历史** — 最近 5 个房间快速重连
- **房间锁定** — 房主可锁定房间（客户端侧拦截非房主加入）
- **Peer 列表** — 显示 NAT / 延迟 / cost，支持 Ping 全部
- **密码强度提示** — 实时评估密码安全性
- **配置导入/导出** — `.linkroom.json` 格式备份与恢复设置
- **MC Mod 检测** — 扫描 `.minecraft/mods` 目录
- **EasyTier 版本检查** — 对比 GitHub 最新 EasyTier 发布

### 工具与其他

- **GitHub 自动更新** — 启动时自动检查 / 手动检查，支持增量更新与一键重启安装
- **首次运行向导** — 模式选择、自动更新、便携模式引导
- **CLI 模式** — `LinkRoom.exe --join ROOM --pass xxx --headless`
- **诊断导出 / Web 管理面板 / 插件 API**

## 自动更新

LinkRoom 会从 GitHub Releases 拉取最新版本：

| 触发方式 | 说明 |
|----------|------|
| 启动时自动检查 | 设置 →「启动时自动检查 GitHub 更新」（默认开启） |
| 手动检查 | 主界面「🔄 更新」/ 底部版本标签 / 设置页「立即检查更新」 |

更新对话框选项：

- **是** — 下载并自动重启安装
- **否** — 跳过此版本（不再提示）
- **取消** — 稍后提醒

**增量更新**：若 EasyTier 运行时版本未变，仅替换 exe，保留 `LinkRoomData/runtime/`，无需重新解压 EasyTier。

更新文件缓存位置：`LinkRoomData/update/`

## 快速开始

1. 从 [Releases](https://github.com/WXFffff666/LinkRoom/releases) 下载 `LinkRoom-v*-win-x64.exe`
2. 双击运行，首次启动会弹出向导
3. **创建房间**：点击「创建房间」→ 分享房间号 / 二维码 / 联机链接
4. **加入房间**：输入房间号或粘贴 `linkroom://` 链接 →「加入房间」
5. 连接成功后，复制虚拟 IP 给好友，或在 MC 等游戏中直接 LAN 发现（LAN 模式）

> LAN 模式需要**管理员权限**（右键 exe → 以管理员运行）。

## CLI 用法

```powershell
# 创建房间（无界面）
LinkRoom.exe --create --headless

# 加入房间
LinkRoom.exe --join ABCD1234 --pass mypass --headless

# LAN 模式 + 共享节点
LinkRoom.exe --create --lan-mode --shared-node --headless

# 最小化启动
LinkRoom.exe --minimized
```

## 数据目录

```
LinkRoom.exe
LinkRoomData/
├── runtime/2.6.4/    EasyTier 核心（wintun.dll 等）
├── config/           设置、STUN 缓存
├── logs/             滚动日志
├── temp/             临时 EasyTier 配置
├── update/           更新下载缓存与 manifest
├── diagnostics/      诊断包
└── plugins/          游戏插件 JSON
```

便携模式下以上目录位于 exe 同目录；关闭便携模式则使用 `%LocalAppData%\LinkRoom\`。

## 构建

```powershell
# 自动下载 EasyTier（构建前若缺失）
.\tools\fetch-easytier.ps1

dotnet publish src\LinkRoom\LinkRoom.csproj -c Release
# 输出: src\LinkRoom\bin\Release\net8.0-windows\win-x64\publish\LinkRoom.exe
```

## 自动发布

推送 `v*` 标签即可触发 GitHub Actions 自动构建并发布 Release：

```bash
git tag v1.16.0 && git push origin v1.16.0
```

详见 [docs/RELEASE.md](docs/RELEASE.md)

## 版本历史

### v1.16.0

- GitHub 自动/手动更新，增量更新，下载进度与重启安装
- QR 码联机、短链分享、虚拟 IP 复制、连接测速
- Windows Toast 通知（成员变动、中继切换）
- P2P 路径可视化、配置导入导出、MC Mod 检测
- IPv6-only、SOCKS5 代理、房间锁定、UPnP 可配置
- 首次运行向导、连接进度条、Peer 列表增强
- 配置校验、进程守护、STUN 缓存统一、设置即时保存
- 日志内存裁剪（300 条）、PublishReadyToRun 优化

### v1.15.0

- 统一数据目录 `LinkRoomData/`、NAT 并发检测、自动重连
- LAN/轻量双模式、联机码、连接质量面板、房间历史
- 诊断导出、插件 API、密码强度、共享节点预填

## 引用项目

| 项目 | 用途 |
|------|------|
| [EasyTier](https://github.com/EasyTier/EasyTier) | P2P 核心 |
| [NatTypeTester](https://github.com/HMBSbige/NatTypeTester) | NAT 检测参考 |
| [OPL-WpfApp](https://github.com/Guailoudou/OPL-WpfApp) | 游戏联机 UI 参考 |
| [Stun.Net](https://github.com/HMBSbige/Stun.Net) | STUN 协议 |
| [QRCoder](https://github.com/codebude/QRCoder) | QR 码生成 |

## 许可

LinkRoom: MIT | EasyTier: LGPL-3.0
