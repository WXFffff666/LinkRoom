# LinkRoom

Windows 便携式 P2P 联机工具。创建房间 → 分享房间号 → 好友加入 → 直连游戏。

## 功能

- **创建/加入房间** — 一键生成 8 位房间号，好友输入即可加入
- **自动 NAT 检测** — 多 STUN 服务器并发检测 NAT 类型，选择最优 P2P 路径
- **游戏端口扫描** — 自动检测 Minecraft(25565)、CS(27015) 等 12 种常见游戏端口
- **UPnP 端口映射** — 借鉴 EasyTier 的 IGD→NAT-PMP 回退策略
- **对等节点列表** — 实时显示已连接节点的 IP、NAT 类型、延迟
- **日志查看器** — 实时运行日志，支持清空/复制
- **自检工具** — 一键检测管理员权限、EasyTier、Wintun、网络、STUN
- **设置持久化** — 房间号、端口号自动保存
- **单文件发布** — 一个 exe，双击运行，exe 同目录解压 EasyTier 核心

## 系统要求

- Windows 10 / 11 x64
- 首次运行需管理员权限（安装 Wintun 虚拟网卡驱动）

## 快速开始

1. 下载 `LinkRoom.exe`
2. 双击运行（首次需管理员权限安装驱动）
3. **创建房间**: 点击「创建房间」→ 设置可选密码 → 分享房间号
4. **加入房间**: 输入好友分享的房间号 + 密码 → 点击「加入房间」

## 高级设置

| 选项 | 说明 |
|------|------|
| 监听端口 | P2P 通信端口（默认 11010） |
| 游戏端口扫描 | 自动检测本地运行的游戏端口 |
| 共享节点 | 通过中继节点连接 |
| UPnP | 自动配置路由器端口映射 |
| NAT 检测 | 手动触发 NAT 类型检测 |
| 自定义 STUN 服务器 | NAT 检测服务器地址 |
| MTU | 虚拟网卡最大传输单元 |
| IPv6 优先 | 优先使用 IPv6 |
| 便携模式 | 数据存储在 exe 同目录 |
| 暗色模式 | 暗色主题切换 |
| 最大重连次数 | 掉线后自动重试 |
| 静态虚拟 IP | 指定固定虚拟 IP |

## 安全

- 房间密码**不保存**到磁盘
- 密码**不出现在**进程命令行中（通过临时配置文件传递）
- 日志自动脱敏密码和公网 IP
- EasyTier 二进制嵌入单文件，运行时解压

## 引用项目

| 项目 | 用途 | 许可 |
|------|------|------|
| [EasyTier](https://github.com/EasyTier/EasyTier) | P2P 网络核心 | LGPL-3.0 |
| [NatTypeTester](https://github.com/HMBSbige/NatTypeTester) | NAT 类型检测方案参考 | MIT |
| [OPL-WpfApp](https://github.com/Guailoudou/OPL-WpfApp) | WPF UI 布局参考 | - |
| [Tailscale](https://github.com/tailscale/tailscale) | P2P 架构参考 | BSD-3 |
| [iNKORE.UI.WPF.Modern](https://github.com/iNKORE-NET/UI.WPF.Modern) | Mica 主题控件 | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM 框架 | MIT |
| [Stun.Net](https://github.com/HMBSbige/Stun.Net) | STUN 协议实现 | MIT |
| [Wintun](https://www.wintun.net/) | 虚拟网卡驱动 | 专属许可 |

## 卸载

1. 关闭 LinkRoom
2. 删除 exe 同目录下的 `runtime/` 文件夹
3. 删除 `%LOCALAPPDATA%\LinkRoom\`

## 许可

LinkRoom 包装层：MIT | EasyTier 核心：LGPL-3.0 | Wintun：随发布附带许可
