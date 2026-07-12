# LinkRoom Release 指南

## 自动发布（推荐）

### 方式一：推送 Git Tag

```bash
# 1. 确保 csproj 中 Version 与 tag 一致
#    src/LinkRoom/LinkRoom.csproj → <Version>1.16.0</Version>

# 2. 提交并推送 main
git push origin main

# 3. 打标签并推送
git tag v1.16.0
git push origin v1.16.0
```

推送 `v*` 标签后，GitHub Actions **Release** 工作流会自动：

1. 运行 `tools/fetch-easytier.ps1` 下载 EasyTier 2.6.4 运行时
2. `dotnet publish` 构建单文件 `LinkRoom.exe`（ReadyToRun 已启用）
3. 创建 GitHub Release，上传 `LinkRoom-v{版本}-win-x64.exe`
4. 客户端 `UpdateService` 会通过 GitHub API 检测此 Release 并提供自动更新

### 方式二：手动触发

1. 打开 GitHub → **Actions** → **Release**
2. 点击 **Run workflow**
3. 输入版本号（如 `v1.16.0`）
4. 等待完成后在 [Releases](https://github.com/WXFffff666/LinkRoom/releases) 页面下载

## 本地构建

```powershell
.\tools\fetch-easytier.ps1
dotnet publish src\LinkRoom\LinkRoom.csproj -c Release -o publish
# 输出: publish\LinkRoom.exe（约 100MB 单文件）
```

## CI

每次 push / PR 到 `main` 分支会触发 **CI** 工作流，验证 `dotnet build` 是否通过。

## 版本号规范

| 项目 | 格式 | 示例 |
|------|------|------|
| Git Tag | `vMAJOR.MINOR.PATCH` | `v1.16.0` |
| csproj Version | `MAJOR.MINOR.PATCH` | `1.16.0` |
| 预发布 | `v1.17.0-beta.1` | 自动标记为 prerelease |

**注意**：Tag 前缀 `v` 与 csproj 中 Version 需对应（Tag `v1.16.0` ↔ Version `1.16.0`）。

## 更新机制说明

客户端 `UpdateService` 通过 GitHub Releases API 检测新版本：

```
GET https://api.github.com/repos/WXFffff666/LinkRoom/releases/latest
```

- 匹配资产名含 `LinkRoom` 且以 `.exe` 结尾的下载链接
- 下载到 `LinkRoomData/update/LinkRoom-{tag}-win-x64.exe`
- 写入 `manifest.json` 记录 App / EasyTier 版本
- **增量更新**：EasyTier 版本不变时仅替换 exe，保留 `runtime/` 目录
- 通过批处理脚本替换当前 exe 并重启

发布新版本时需确保 Release 资产命名与上述规则一致（CI workflow 已自动处理）。

## 发布检查清单

- [ ] `src/LinkRoom/LinkRoom.csproj` 中 `<Version>` 已更新
- [ ] `README.md` 版本历史已更新
- [ ] 本地 `dotnet build LinkRoom.sln -c Release` 通过
- [ ] 功能变更已在 README 中说明
- [ ] 推送 main 后打 tag 并 push
- [ ] 确认 Actions Release 工作流成功
- [ ] 在 Releases 页面验证 exe 可下载
- [ ] 用旧版本客户端测试自动更新提示与安装

## 版本历史

### v1.16.0（2026-07-12）

**新功能**

- GitHub 自动/手动更新（启动检查 + 设置页 + 主界面按钮）
- 增量更新：EasyTier 版本不变时保留 runtime
- QR 码联机、短链分享
- 虚拟 IP 复制、TCP 连接测速
- Windows Toast 通知（成员加入/离开、中继切换）
- P2P 路径 ASCII 可视化
- 配置导入/导出（`.linkroom.json`）
- MC Mod 目录扫描
- IPv6-only 模式、SOCKS5 代理
- 房间锁定（客户端侧）
- EasyTier 版本在线检查
- 首次运行向导

**优化**

- UPnP 设置真正生效（不再写死禁用）
- 连接前配置校验（房间号/端口/MTU/密码）
- EasyTier 进程守护（15s 健康轮询）
- STUN 缓存路径统一到 `LinkRoomData/config/`
- 设置变更即时保存
- 连接进度条（分步显示）
- Peer 列表增强（Ping 全部）
- 日志内存裁剪（最多 300 条）
- PublishReadyToRun 启用

### v1.15.0

- 统一数据目录、NAT 并发检测、自动重连
- LAN/轻量双模式、联机码、连接质量面板
- CI/CD 工作流、诊断导出、插件 API
