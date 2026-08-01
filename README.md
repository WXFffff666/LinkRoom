# LinkRoom

Windows 便携式 P2P 游戏联机工具。单 exe 发布，数据存储在 exe 同目录 `LinkRoomData/`。

**当前版本：v1.16.0**

## 多语言（i18n）

- 界面文案通过 resx 提供中英双语：`src/LinkRoom.Core/Resources/Strings.resx`（默认中文）+ `Strings.en.resx`（英文）
- 设置 →「语言」可选 跟随系统 / 中文 / English（默认跟随系统：系统 UI 为 en\* 时用英文，否则中文），切换后需重启生效
- **范围说明**：本次迁移覆盖静态 XAML 文本与 MessageBox 静态文案；日志、聊天内容、诊断包保持中文；VM 动态状态文本（StatusText / StatusDetail / ConnectionQuality / ProgressText / 提示等）当前保持中文，留待后续迁移

## 功能

### 联机核心

- **创建/加入房间** — 8 位房间号 + 可选密码，支持 `linkroom://` 联机链接
- **QR 码联机** — 创建房间后自动生成二维码，好友扫码即可加入
- **短链分享** — 短码 + 完整联机链接，一键复制
- **双模式** — 轻量模式（无虚拟网卡）/ LAN 模式（虚拟网卡 + UDP 广播，MC 自动发现）
- **NAT 检测** — 并发 STUN 探测，支持自定义/远程 STUN 列表
- **共享节点中继** — 默认 `tcp://public.easytier.top:11010`
- **安全模式（E2EE）** — 默认开启：节点间独立协商加密密钥，中继也无法解密；可填写共享节点公钥锁定其身份（`peer_public_key`，填写后连接错误节点会被拒绝）。**注意：同一网络内所有节点必须一致开启安全模式（并保持相同版本），否则安全节点会拒绝未开启安全模式的节点**。公钥留空 = 仅加密、不验证共享节点身份（官方公共节点未发布固定公钥）。详见 `src/LinkRoom.Core/SPIKE-SECUREMODE.md`
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
- **房间锁定** — 房主可锁定房间（**服务端强制** + 客户端提示，EasyTier ACL `default_action=2` 拒绝非房主成员）。启用时 host 会在生成的 EasyTier 配置中追加 `[acl.acl_v1]` 段（默认拒绝 + 单一允许规则 `source_groups=["room-owner"]`），并把 256-bit `group_secret` 编码进 `linkroom://` 分享链接的 `lock=...` 参数。客人必须使用该链接加入（`EasyTierConfigBuilder` 会自动把同 secret 写进自己的 TOML），否则 host 的入站链会丢弃连接。**配置一致性要求**：所有节点必须使用同一 `group_secret`，否则 EasyTier 组校验失败。**新成员**在 host 开关锁定后需要重新生成链接并重连（secret 随每次开启轮换）。详见 `src/LinkRoom.Core/SPIKE-ACL.md`。
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
- IPv6-only、SOCKS5 代理、房间锁定（**v1.16.1 起改为服务端强制：EasyTier ACL**）、UPnP 可配置
- 首次运行向导、连接进度条、Peer 列表增强
- 配置校验、进程守护、STUN 缓存统一、设置即时保存
- 日志内存裁剪（300 条）、PublishReadyToRun 优化

### v1.16.1

- **房间锁定服务端化**：基于 EasyTier ACL（`[acl.acl_v1]` TOML 段，`default_action=2` 拒绝 + `room-owner` group 允许），spike 验证于 `src/LinkRoom.Core/SPIKE-ACL.md`。分享链接新增 `lock=...` 参数携带 256-bit `group_secret`；host 持久化 `RoomId → secret` 映射以支持断线重连
- 日志脱敏：扩展 `SecretRedactRegex` / `SettingsService.SanitizeLog` 覆盖 `group_secret`，避免 easytier-core 启动 dump 暴露密钥

### v1.16.2

- **EasyTier 安全模式**：默认开启 E2EE（`[secure_mode]` TOML 段 + 每实例持久化 X25519 密钥对），可选 `peer_public_key` 锁定共享节点公钥。spike 验证于 `src/LinkRoom.Core/SPIKE-SECUREMODE.md`（双节点 p2p 成功、错误公钥被拒、非安全节点被拒）。日志脱敏扩展覆盖 `local_private_key`

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
