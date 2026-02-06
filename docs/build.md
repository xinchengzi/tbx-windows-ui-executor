# 构建与运行 (Windows)

本项目设计运行在 **Windows 11 x64** 上。

## 要求
- .NET SDK 10.x（或更新版本）

## 构建

从仓库根目录：

```powershell
cd src/TbxExecutor
# 还原依赖并构建
dotnet build -c Release
```

## 运行

```powershell
cd src/TbxExecutor
dotnet run
```

应用启动后会在**系统托盘**中显示，并托管一个绑定到 tailnet 接口的 HTTP API（可配置）。

## 配置

首次运行时，会在以下位置创建配置文件：

- `%APPDATA%\TbxExecutor\config.json`

包含：
- `listenPort`（默认 17890）
- `allowlistIps`（默认 `["100.64.0.1"]`）
- `token`（首次运行时随机生成）

调用 API 时必须提供此 token：

```http
Authorization: Bearer <token>
```

## 安全性

- 会话锁定时，输入端点会拒绝请求。
- 不支持 UAC / 安全桌面（请求会快速失败）。

## GitHub Actions 自动构建

推送到 `main` 或 `feat/**` 分支时，会自动触发 Windows 构建：

- 工作流文件：`.github/workflows/build-windows.yml`
- 构建产物：`tbx-executor-portable-net10-<run_number>`
