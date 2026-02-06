# API 文档 (v1)

默认端口：`17890`

所有请求需要：

- `Authorization: Bearer <token>`
- 远程 IP 必须在白名单中（默认：`100.64.0.1`）
- 建议：绑定 tailnet IP 并启用白名单

另请参阅：
- `docs/acceptance.md` — DPI 正确性和显示器选择的验收清单

---

## GET /health

返回基本状态。
工作站锁定时包含 `locked` 字段。

---

## GET /env

返回环境元数据，包括每个显示器的边界和 DPI。

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "os": "Microsoft Windows NT 10.0.22631.0",
    "coordinateSystem": "physicalPixels",
    "virtualScreenRectPx": { "x": -1920, "y": 0, "w": 3840, "h": 2160 },
    "displays": [
      {
        "index": 0,
        "deviceName": "\\\\.\\DISPLAY1",
        "isPrimary": true,
        "boundsRectPx": { "x": 0, "y": 0, "w": 1920, "h": 1080 },
        "workAreaRectPx": { "x": 0, "y": 0, "w": 1920, "h": 1040 },
        "dpi": { "x": 168, "y": 168 },
        "scale": { "x": 1.75, "y": 1.75 }
      }
    ]
  }
}
```

### 说明/限制

- 所有矩形均为**虚拟屏幕**坐标空间中的**物理像素**
- 每个显示器的 DPI 通过 `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` 获取（Windows 8.1+）
  - 回退使用 `CreateDC("DISPLAY", deviceName, ...)` + `GetDeviceCaps(LOGPIXELS*)`
  - 回退可能返回**系统 DPI**，取决于进程 DPI 感知设置

---

## GET /config

返回有效的 `listenHost`、`listenPort` 和 `allowlistIps`。
Token 已脱敏，永不返回。

---

## GET /status

返回当前忙碌状态和计数器。

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "busy": false,
    "counters": { "macroCount": 0, "isFlashing": false }
  }
}
```

---

## POST /window/list

返回 Windows 上的窗口列表；非 Windows 返回 `[]`。

---

## POST /window/focus

接受 `{ "match": { "titleContains"?, "titleRegex"?, "processName"? } }`。

在 Windows 上，聚焦最匹配的窗口（优先选择可见且未最小化的窗口），
返回已聚焦的 `WindowInfo`。无匹配时返回 `404 WINDOW_NOT_FOUND`。
非 Windows 返回 `501 NOT_IMPLEMENTED`。
工作站锁定时返回 `409 LOCKED`。

---

## POST /input/*

工作站锁定时拒绝，返回 `409 LOCKED`。
另一个宏或输入序列正在运行时拒绝，返回 `429 BUSY`。

---

## POST /input/mouse

使用 SendInput 执行鼠标输入操作。坐标为虚拟屏幕空间中的物理像素。

### 请求

```json
{
  "kind": "move" | "click" | "double" | "right" | "wheel" | "drag",
  "x": 500,
  "y": 300,
  "button": "left" | "right" | "middle",
  "dx": 0,
  "dy": -120,
  "x2": 800,
  "y2": 600,
  "humanize": {
    "jitterPx": 2,
    "delayMs": [10, 50]
  }
}
```

### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `kind` | string | **是** | 操作类型：`move`、`click`、`double`、`right`、`wheel`、`drag` |
| `x` | int | 视情况 | X 坐标（物理像素）。`move`、`click`、`double`、`right`、`drag` 必需。`wheel` 可选。 |
| `y` | int | 视情况 | Y 坐标（物理像素）。`move`、`click`、`double`、`right`、`drag` 必需。`wheel` 可选。 |
| `button` | string | 否 | 鼠标按钮：`left`（默认）、`right`、`middle`。用于 `click`/`double`。 |
| `dx` | int | 否 | 水平滚轮增量。仅用于 `wheel`。默认：0。 |
| `dy` | int | 否 | 垂直滚轮增量。仅用于 `wheel`。默认：-120（向下滚动）。 |
| `x2` | int | drag 时必需 | 拖拽操作的终点 X 坐标。 |
| `y2` | int | drag 时必需 | 拖拽操作的终点 Y 坐标。 |
| `humanize` | object | 否 | 添加类人随机性。 |
| `humanize.jitterPx` | int | 否 | 应用于坐标的随机像素偏移。 |
| `humanize.delayMs` | [int, int] | 否 | 操作间随机延迟范围 [最小, 最大] 毫秒。 |

### 操作类型

| Kind | 描述 |
|------|------|
| `move` | 移动光标到 (x, y) |
| `click` | 移动到 (x, y)，然后左键点击（或指定按钮） |
| `double` | 移动到 (x, y)，然后双击 |
| `right` | 移动到 (x, y)，然后右键点击 |
| `wheel` | 在当前位置滚动滚轮（如果提供了 x,y 则先移动）。dy=-120 向下滚动，dy=120 向上滚动。 |
| `drag` | 在 (x, y) 按下鼠标，移动到 (x2, y2)，释放鼠标 |

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "success": true,
    "cursorX": 500,
    "cursorY": 300
  }
}
```

### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `BAD_REQUEST` | 无效 JSON、缺少必需字段或未知 kind |
| 409 | `LOCKED` | 工作站已锁定 |
| 412 | `UAC_REQUIRED` | 输入被 UAC/安全桌面阻止 |
| 429 | `BUSY` | 另一个宏或输入序列正在运行 |
| 500 | `INPUT_FAILED` | SendInput 失败 |
| 501 | `NOT_IMPLEMENTED` | 非 Windows 平台 |

### 坐标系

- 坐标为**虚拟屏幕**空间中的**物理像素**
- 虚拟屏幕原点可以为负（如副屏在主屏左侧）
- 使用 `GET /env` 获取虚拟屏幕边界和每个显示器的坐标

### 示例

#### 移动光标
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"move","x":500,"y":300}'
```

#### 左键点击
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":500,"y":300}'
```

#### 右键点击
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"right","x":500,"y":300}'
```

#### 双击
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"double","x":500,"y":300}'
```

#### 在指定位置向下滚动
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"wheel","x":500,"y":300,"dy":-120}'
```

#### 从 (100,100) 拖拽到 (500,500)
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"drag","x":100,"y":100,"x2":500,"y2":500}'
```

#### 带类人化的点击
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":500,"y":300,"humanize":{"jitterPx":2,"delayMs":[10,50]}}'
```

---

## POST /input/key

使用 SendInput 执行键盘输入操作。

### 请求

```json
{
  "kind": "press",
  "keys": ["CTRL", "L"],
  "humanize": {
    "delayMs": [10, 50]
  }
}
```

### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `kind` | string | **是** | 操作类型：`press` |
| `keys` | string[] | **是** | 要按下的按键名称数组 |
| `humanize` | object | 否 | 添加类人随机性 |
| `humanize.delayMs` | [int, int] | 否 | 按键事件间随机延迟范围 [最小, 最大] 毫秒 |

### 操作类型

| Kind | 描述 |
|------|------|
| `press` | 按顺序按下，然后逆序释放（和弦行为） |

### 支持的按键

| 类别 | 按键 |
|------|------|
| 修饰键 | `CTRL`、`ALT`、`SHIFT`、`WIN` |
| 特殊键 | `ENTER`、`ESC`、`TAB`、`BACKSPACE`、`DELETE`、`SPACE` |
| 导航键 | `HOME`、`END`、`PAGEUP`、`PAGEDOWN`、`UP`、`DOWN`、`LEFT`、`RIGHT` |
| 字母键 | `A`-`Z` |
| 数字键 | `0`-`9` |
| 功能键 | `F1`-`F12` |

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "success": true
  }
}
```

### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `BAD_REQUEST` | 无效 JSON、缺少必需字段或未知 key/kind |
| 409 | `LOCKED` | 工作站已锁定 |
| 412 | `UAC_REQUIRED` | 输入被 UAC/安全桌面阻止 |
| 429 | `BUSY` | 另一个宏或输入序列正在运行 |
| 500 | `INPUT_FAILED` | SendInput 失败 |
| 501 | `NOT_IMPLEMENTED` | 非 Windows 平台 |

#### 增强错误响应 (v0.2+)

当 `INPUT_FAILED` 发生时，响应包含诊断信息：

```json
{
  "ok": false,
  "status": 500,
  "error": "INPUT_FAILED: keydown CTRL (vk=0x11, scan=0x1D)",
  "data": {
    "lastError": 5,
    "message": "INPUT_FAILED: keydown CTRL (vk=0x11, scan=0x1D)"
  }
}
```

常见 `lastError` 值：
- `5` (ERROR_ACCESS_DENIED)：UIPI 阻止输入 - 目标窗口具有更高权限级别
- `0`：未记录错误（检查窗口是否获得焦点）

### 示例

#### 按 Ctrl+L（在浏览器中聚焦地址栏）
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","L"]}'
```

#### 按 Ctrl+C（复制）
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","C"]}'
```

#### 按 Enter
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["ENTER"]}'
```

#### 按 F5（刷新）
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["F5"]}'
```

---

## GET /input/cursor

返回当前光标位置、虚拟屏幕边界和前台窗口句柄。用于调试鼠标移动问题。

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "cursorX": 500,
    "cursorY": 300,
    "virtualScreen": {
      "x": -1920,
      "y": 0,
      "w": 3840,
      "h": 2160
    },
    "foregroundHwnd": 12345678
  }
}
```

### 响应字段

| 字段 | 类型 | 描述 |
|------|------|------|
| `cursorX` | int | 当前光标 X 位置（物理像素） |
| `cursorY` | int | 当前光标 Y 位置（物理像素） |
| `virtualScreen.x` | int | 虚拟屏幕原点 X（多显示器时可能为负） |
| `virtualScreen.y` | int | 虚拟屏幕原点 Y |
| `virtualScreen.w` | int | 虚拟屏幕宽度（物理像素） |
| `virtualScreen.h` | int | 虚拟屏幕高度（物理像素） |
| `foregroundHwnd` | long | 当前前台窗口的句柄 |

---

## POST /macro/*

工作站锁定时拒绝，返回 `409 LOCKED`。
另一个宏或输入序列正在运行时拒绝，返回 `429 BUSY`。

---

## POST /macro/run

原子执行宏步骤序列。仅限 Windows。

### 请求

```json
{
  "steps": [
    {"kind":"window.focus", "match": {"titleContains":"Notepad"}},
    {"kind":"capture", "mode":"window", "window": {"processName":"notepad"}},
    {"kind":"input.mouse", "x": 500, "y": 300},
    {"kind":"input.key", "keys": ["CTRL", "S"]},
    {"kind":"sleep", "ms": 100}
  ],
  "defaults": {
    "humanize": {"delayMs": [10, 50], "jitterPx": 1}
  },
  "failFast": true
}
```

### 步骤类型

| Kind | 描述 | 必需字段 |
|------|------|----------|
| `window.focus` | 聚焦窗口 | `match`（同 `/window/focus`） |
| `capture` | 截取屏幕/窗口/区域 | 同 `/capture` 字段 |
| `input.mouse` | 鼠标输入（默认点击） | 同 `/input/mouse`。使用 `input.mouse.move`、`input.mouse.click` 等指定动作。 |
| `input.mouse.wheel` | 通过 input.mouse 滚轮 | `dy`（垂直增量）或 `dx`（水平增量）。可选：`x`、`y` 先移动光标。 |
| `input.wheel` | 专用滚轮步骤 | `delta`（滚轮量，如每格 120）、`horizontal`（布尔，默认 false）。可选：`x`、`y`。 |
| `input.key` | 键盘输入 | 同 `/input/key`。使用 `input.key.press` 指定动作。 |
| `sleep` | 延迟执行 | `ms`（毫秒） |

#### `input.wheel` 步骤详情

```json
{
  "kind": "input.wheel",
  "x": 500,
  "y": 300,
  "delta": -120,
  "horizontal": false
}
```

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `x` | int | 否 | 滚动前光标移动到的 X 坐标 |
| `y` | int | 否 | 滚动前光标移动到的 Y 坐标 |
| `delta` | int | 否 | 滚轮增量（默认：-120）。正数=向上/右滚动，负数=向下/左滚动。典型：每格 120。 |
| `horizontal` | bool | 否 | 如果为 true，发送水平滚轮 (HWHEEL)。默认：false（垂直）。 |

### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `steps` | array | **是** | 要执行的步骤对象数组 |
| `defaults` | object | 否 | 应用于所有步骤的默认设置 |
| `defaults.humanize` | object | 否 | 默认类人化设置 |
| `defaults.humanize.delayMs` | [int, int] | 否 | 随机延迟范围 [最小, 最大] 毫秒 |
| `defaults.humanize.jitterPx` | int | 否 | 鼠标操作的随机像素偏移 |
| `failFast` | bool | 否 | 首次失败时停止（默认：true） |

### 响应

```json
{
  "runId": "abc123def456",
  "ok": true,
  "steps": [
    {"stepId": "step1", "ok": true, "data": {...}},
    {"stepId": "step2", "ok": true, "data": {...}},
    {"stepId": "step3", "ok": false, "status": 412, "error": "UAC_REQUIRED"}
  ]
}
```

### 步骤结果字段

| 字段 | 类型 | 描述 |
|------|------|------|
| `stepId` | string | 此步骤的唯一标识符 |
| `ok` | bool | 步骤是否成功 |
| `status` | int? | 失败时的 HTTP 状态码 |
| `error` | string? | 失败时的错误码 |
| `data` | object? | 成功时的步骤特定结果数据 |

### 行为

- 步骤按顺序执行
- 每个步骤返回自己的结果和唯一 `stepId`
- 如果 `failFast` 为 true（默认），首次失败时停止执行
- 如果提供了 `X-Run-Id` 请求头则使用，否则生成新 runId
- `capture` 步骤返回与 `/capture` 相同的格式
- `defaults.humanize` 的类人化设置应用于所有步骤，除非单步骤覆盖

### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `BAD_REQUEST` | 无效 JSON、缺少必需字段或未知步骤类型 |
| 404 | `WINDOW_NOT_FOUND` | 窗口聚焦步骤：无匹配窗口 |
| 404 | `CAPTURE_FAILED` | 截图步骤：目标未找到或截图失败 |
| 409 | `LOCKED` | 工作站已锁定 |
| 412 | `UAC_REQUIRED` | 输入被 UAC/安全桌面阻止 |
| 429 | `BUSY` | 另一个宏或输入序列正在运行 |
| 500 | `INPUT_FAILED` | SendInput 失败 |
| 501 | `NOT_IMPLEMENTED` | 非 Windows 平台 |

### 示例

#### 聚焦窗口并截图
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"window.focus", "match": {"processName":"notepad"}},
      {"kind":"sleep", "ms": 100},
      {"kind":"capture", "mode":"window", "window": {"processName":"notepad"}}
    ]
  }'
```

#### 带类人化的点击和输入
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"input.mouse", "x": 500, "y": 300},
      {"kind":"input.key", "keys": ["H", "E", "L", "L", "O"]}
    ],
    "defaults": {"humanize": {"delayMs": [20, 80], "jitterPx": 2}}
  }'
```

---

## POST /capture

截取屏幕、窗口或区域。返回带元数据的 PNG 或 JPEG 图像。

### 请求

```json
{
  "mode": "screen" | "window" | "region",
  "window": {
    "titleContains": "string (可选)",
    "titleRegex": "string (可选)",
    "processName": "string (可选)"
  },
  "region": {
    "x": 100,
    "y": 200,
    "w": 800,
    "h": 600
  },
  "format": "png" | "jpeg",
  "quality": 90,
  "displayIndex": 0
}
```

#### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `mode` | string | 否 | `"screen"`（默认）、`"window"` 或 `"region"` |
| `window` | object | mode=window 时 | 窗口匹配条件 |
| `window.titleContains` | string | 否 | 匹配包含此文本的窗口（不区分大小写） |
| `window.titleRegex` | string | 否 | 按正则表达式匹配窗口 |
| `window.processName` | string | 否 | 按进程名匹配窗口（如 `"notepad"`） |
| `region` | object | mode=region 时 | 物理像素坐标 |
| `region.x` | int | 是 | X 坐标（物理像素） |
| `region.y` | int | 是 | Y 坐标（物理像素） |
| `region.w` | int | 是 | 宽度（物理像素） |
| `region.h` | int | 是 | 高度（物理像素） |
| `format` | string | 否 | `"png"`（默认）或 `"jpeg"`/`"jpg"` |
| `quality` | int | 否 | JPEG 质量 1-100（默认：90）。PNG 忽略此项。 |
| `displayIndex` | int | 否 | 当 `mode=screen` 时：按索引截取特定显示器（来自 `GET /env`）。默认：主显示器。 |

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "imageB64": "<base64 编码图像>",
    "format": "png",
    "regionRectPx": { "x": 0, "y": 0, "w": 1920, "h": 1080 },
    "windowRectPx": { "x": 100, "y": 100, "w": 800, "h": 600 },
    "ts": 1707123456789,
    "scale": 1.75,
    "dpi": 168,
    "displayIndex": 0,
    "deviceName": "\\\\.\\DISPLAY1"
  }
}
```

#### 响应字段

| 字段 | 类型 | 描述 |
|------|------|------|
| `imageB64` | string | Base64 编码的图像数据 |
| `format` | string | `"png"` 或 `"jpeg"` |
| `regionRectPx` | object | 实际截取区域的物理屏幕像素坐标 |
| `windowRectPx` | object? | 如果 mode=window，窗口矩形；否则 null |
| `ts` | long | Unix 时间戳（毫秒） |
| `scale` | double | 包含截取矩形中心的显示器缩放因子（如 175% 缩放为 1.75） |
| `dpi` | int | 包含截取矩形中心的显示器 DPI（通常为 96 * scale） |
| `displayIndex` | int? | 用于 scale/dpi 派生的显示器索引（来自 `GET /env`）。未知时为 null。 |
| `deviceName` | string? | 显示器设备名（如 `"\\\\.\\DISPLAY1"`）。未知时为 null。 |

### DPI 说明

- 所有坐标为**物理像素**（DPI 感知）
- 进程假定已启用 **Per-Monitor DPI Aware V2**
- `scale`/`dpi` 报告的是**包含截取矩形中心的显示器**
  - `mode=window`：使用窗口矩形中心
  - `mode=region`：使用区域矩形中心
  - `mode=screen`：使用选定的显示器（通过 `displayIndex`）或主显示器

### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `BAD_REQUEST` | 无效 JSON 或缺少必需字段 |
| 404 | `CAPTURE_FAILED` | 窗口未找到或截图失败 |
| 501 | `NOT_IMPLEMENTED` | 非 Windows 平台 |

### 示例

#### 截取全屏（PNG）
```json
{ "mode": "screen" }
```

#### 截取特定窗口（JPEG）
```json
{ "mode": "window", "window": { "processName": "notepad" }, "format": "jpeg", "quality": 85 }
```

#### 截取区域
```json
{ "mode": "region", "region": { "x": 100, "y": 100, "w": 500, "h": 400 } }
```

---

## GET /capture/selfcheck

快速自检以验证截图功能是否正常工作。

### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456", 
  "ok": true,
  "data": {
    "ok": true,
    "captureAvailable": true,
    "testImageSize": 1234567,
    "testRegionPx": { "w": 1920, "h": 1080 },
    "scale": 1.75,
    "dpi": 168,
    "displayIndex": 0,
    "deviceName": "\\\\.\\DISPLAY1"
  }
}
```

非 Windows 或截图失败时：
```json
{
  "data": {
    "ok": false,
    "reason": "NOT_WINDOWS" | "CAPTURE_FAILED",
    "captureAvailable": false
  }
}
```

---

## 窗口截图增强

使用 `mode: "window"` 截图时，系统现在使用智能窗口选择：

### 窗口选择评分

窗口根据以下因素评分和排名：

| 因素 | 分数 |
|------|------|
| 前台窗口 | +200 |
| 可见且未最小化 | +100 |
| 可见但已最小化 | +50 |
| 有效矩形 (w/h > 0) | +50 |
| ProcessName 精确匹配 | +30 |
| TitleContains 匹配 | +20 |
| 窗口面积（归一化） | +0~30 |

选择得分最高的窗口。

### 带窗口元数据的响应

截取窗口时，响应包含 `selectedWindow` 审计信息：

```json
{
  "data": {
    "imageB64": "...",
    "format": "png",
    "regionRectPx": { "x": 100, "y": 100, "w": 800, "h": 600 },
    "windowRectPx": { "x": 100, "y": 100, "w": 800, "h": 600 },
    "ts": 1707123456789,
    "scale": 1.75,
    "dpi": 168,
    "displayIndex": 0,
    "deviceName": "\\\\.\\DISPLAY1",
    "selectedWindow": {
      "hwnd": 12345678,
      "title": "QQ",
      "processName": "QQ",
      "rectPx": { "x": 100, "y": 100, "w": 800, "h": 600 },
      "isVisible": true,
      "isMinimized": false,
      "score": 280
    }
  }
}
```

### 截图失败诊断

窗口截图失败时，错误响应包含诊断信息：

```json
{
  "ok": false,
  "status": 404,
  "error": "CAPTURE_FAILED",
  "data": {
    "reason": "NO_MATCHING_WINDOWS",
    "candidates": [
      {
        "hwnd": 12345678,
        "title": "记事本 - 无标题",
        "processName": "notepad",
        "score": 0,
        "isVisible": true,
        "isMinimized": false,
        "width": 800,
        "height": 600
      }
    ]
  }
}
```

`candidates` 数组显示前 5 个可见窗口，帮助诊断为何无匹配。

---

## 证据包端点

这些端点允许通过 HTTP 读取证据包数据（runs、steps、screenshots）。

### GET /run/list

列出最近的执行记录。

#### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `limit` | int (query) | 否 | 返回的最大记录数（默认：20，最大：100） |

#### 响应

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": [
    {
      "runId": "my-test-run-001",
      "lastWriteUtc": "2024-02-05T10:30:00Z",
      "stepsCount": 5,
      "hasScreenshots": true
    },
    {
      "runId": "another-run-002",
      "lastWriteUtc": "2024-02-04T15:20:00Z",
      "stepsCount": 3,
      "hasScreenshots": false
    }
  ]
}
```

#### 示例

```bash
curl -X GET "http://100.115.92.6:17890/run/list?limit=10" \
  -H "Authorization: Bearer $TOKEN"
```

### GET /run/steps

返回特定 run 的 steps.jsonl 内容。

#### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `runId` | string (query) | **是** | Run ID（必须匹配 `[a-zA-Z0-9_-]+`） |

#### 响应

返回 NDJSON（换行分隔的 JSON），`Content-Type: application/x-ndjson`。

每行是一个表示步骤的 JSON 对象：

```json
{"stepId":"abc123","endpoint":"/capture","tsMs":1707123456789,"ok":true,...}
{"stepId":"def456","endpoint":"/input/mouse","tsMs":1707123456800,"ok":true,...}
```

#### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `INVALID_RUN_ID` | runId 包含无效字符 |
| 404 | `RUN_NOT_FOUND` | Run 不存在或无步骤 |

#### 示例

```bash
curl -X GET "http://100.115.92.6:17890/run/steps?runId=my-test-run-001" \
  -H "Authorization: Bearer $TOKEN"
```

### GET /run/screenshot

返回特定步骤的截图文件。

#### 参数

| 字段 | 类型 | 必需 | 描述 |
|------|------|------|------|
| `runId` | string (query) | **是** | Run ID（必须匹配 `[a-zA-Z0-9_-]+`） |
| `stepId` | string (query) | **是** | Step ID（必须匹配 `[a-zA-Z0-9_-]+`） |

#### 响应

返回带适当 `Content-Type`（`image/png` 或 `image/jpeg`）的截图图像。

#### 错误

| 状态码 | 错误码 | 描述 |
|--------|--------|------|
| 400 | `INVALID_RUN_ID` | runId 包含无效字符 |
| 400 | `INVALID_STEP_ID` | stepId 包含无效字符 |
| 404 | `SCREENSHOT_NOT_FOUND` | 截图文件不存在 |

#### 示例

```bash
curl -X GET "http://100.115.92.6:17890/run/screenshot?runId=my-test-run-001&stepId=abc123def456" \
  -H "Authorization: Bearer $TOKEN" \
  --output screenshot.png
```

#### 安全性

- `runId` 和 `stepId` 验证 `[a-zA-Z0-9_-]+` 模式
- 严格验证防止路径遍历攻击
- 所有文件访问限制在 `%APPDATA%\TbxExecutor\runs\` 目录内
