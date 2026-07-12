# LinkRoom Release 指南

## 自动发布（推荐）

### 方式一：推送 Git Tag

```bash
git tag v1.15.0
git push origin v1.15.0
```

推送 `v*` 标签后，GitHub Actions 会自动：
1. 下载 EasyTier 2.6.4 运行时
2. 构建并发布单文件 `LinkRoom.exe`
3. 创建 GitHub Release 并上传产物

### 方式二：手动触发

1. 打开 GitHub → Actions → **Release**
2. 点击 **Run workflow**
3. 输入版本号（如 `v1.16.0`）
4. 等待完成后在 Releases 页面下载

## 本地构建

```powershell
.\tools\fetch-easytier.ps1
dotnet publish src\LinkRoom\LinkRoom.csproj -c Release -o publish
# 输出: publish\LinkRoom.exe
```

## CI

每次 push/PR 到 `main` 分支会触发 **CI** 工作流，验证构建是否通过。

## 版本号规范

- Tag 格式：`vMAJOR.MINOR.PATCH`（如 `v1.15.0`）
- 预发布：`v1.16.0-beta.1`（自动标记为 prerelease）
- csproj 中 `<Version>` 会在 Release 流程中自动同步
