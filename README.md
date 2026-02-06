# tbx-windows-ui-executor

Windows 11 **托盘**执行器：通过 **tailnet**（HTTP + Bearer token）暴露**截图 + 窗口 + 输入**原语。

- 控制端：`mf-kvm01 (100.64.0.1)`
- 执行端：运行在 `yc-tbx` (Win11)

## 快速开始

### 1. 安装运行

```powershell
# 从 GitHub Actions 下载最新构建
# 或本地构建：
cd src/TbxExecutor
dotnet build -c Release
dotnet run
```

应用启动后在系统托盘显示图标。

### 2. 获取配置

首次运行自动生成配置文件：`%APPDATA%\TbxExecutor\config.json`

```json
{
  "listenPort": 17890,
  "allowlistIps": ["100.64.0.1"],
  "token": "<自动生成>"
}
```

**重要**：
- `allowlistIps`：只有列表中的 IP 才能调用 API
- `token`：可通过托盘菜单 "Copy token" 复制，或 "Rotate token" 轮换

### 3. 测试连接

```bash
export TBX_HOST=100.64.0.3
export TBX_TOKEN="<你的token>"

# 健康检查
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  "http://$TBX_HOST:17890/health" | jq .
```

### 4. 基本操作

```bash
# 键盘输入 (按 A 键)
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["A"]}' \
  "http://$TBX_HOST:17890/input/key"

# 鼠标移动
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"move","x":600,"y":400}' \
  "http://$TBX_HOST:17890/input/mouse"

# 鼠标点击
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":600,"y":400}' \
  "http://$TBX_HOST:17890/input/mouse"

# 滚轮滚动
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"wheel","x":500,"y":500,"dy":-120}' \
  "http://$TBX_HOST:17890/input/mouse"

# 截图
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"mode":"screen"}' \
  "http://$TBX_HOST:17890/capture" | jq '.data.regionRectPx'

# 聚焦窗口
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"match":{"processName":"notepad"}}' \
  "http://$TBX_HOST:17890/window/focus"
```

## 常见问题

### 收到 403 IP_NOT_ALLOWED

你的 IP 不在白名单中。编辑 `%APPDATA%\TbxExecutor\config.json`：

```json
{
  "allowlistIps": ["100.64.0.1", "你的IP"]
}
```

重启应用生效。

### 收到 409 LOCKED

工作站已锁定。解锁后重试。

### 收到 429 BUSY

另一个宏或输入正在执行。等待完成后重试。

### 收到 401 BAD_TOKEN

Token 错误。通过托盘菜单 "Copy token" 获取正确 token。

## 文档

| 文档 | 描述 |
|------|------|
| `docs/api.md` | 完整 API 参考 |
| `docs/build.md` | 构建说明 |
| `docs/spec.md` | 设计规格 |
| `docs/acceptance.md` | 验收清单 |
| `docs/test-recipes.md` | 测试配方 |
| `docs/progress.md` | 开发进度 |

## 状态

M3 宏执行与证据包已完成。
