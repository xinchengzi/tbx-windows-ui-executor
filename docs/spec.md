# TBX Windows UI Executor（托盘被控端）— 规格与设计文档

> 目标：在 **yc-tbx（Win11）** 上提供一个长期可用、易维护、扩展能力强的“桌面执行端”。
> 
> - OpenClaw/智能/识别在服务器（**mf-kvm01: 100.64.0.1**）
> - tbx 只提供：**截图 + 系统级键鼠 + 窗口管理**（通用能力）
> - 只允许 **tailnet** 访问 + **Bearer Token**
> - **不允许锁屏执行**、**不提权/UAC 不碰**

---

## 0. 名词

- **Controller**：服务器侧控制端（OpenClaw 专用 agent，运行在 mf-kvm01）。负责策略、视觉识别/OCR、任务编排、总结。
- **Executor**：Windows 执行端（本项目），运行在 yc-tbx。负责屏幕/窗口/输入等原子能力。
- **Run / Step**：一次远程任务（runId）由多个动作步骤（stepId）组成，用于审计与回放。

---

## 1. 约束与安全边界（硬性要求）

### 1.1 网络与鉴权
- 监听地址：**仅绑定 Headscale/Tailscale 虚拟网卡 IP**（tailnet）。
- 控制端：**单一控制端**，只允许来自 `mf-kvm01 (100.64.0.1)`。
- 鉴权：`Authorization: Bearer <token>`（单 token，支持一键轮换）。
- 传输：HTTP（内网），**不需要 HTTPS**。

### 1.2 行为限制
- **不允许锁屏执行**：锁屏/会话不可交互时，所有输入类动作（鼠标/键盘/宏）必须拒绝。
- **不提权**：不尝试绕过 UAC，不对安全桌面进行输入。
- 默认不开放 `type(text)`（文本输入），仅提供按键（hotkey/press）。后续如需文本输入，必须在设置里显式开启。

### 1.3 可观测性
- 所有请求生成 `runId`；动作生成 `stepId`。
- 默认记录结构化日志；关键动作（输入/聚焦/宏）可配置“前后截图”。
- 支持导出某个 run 的“证据包”（JSONL + 截图序列）。

---

## 2. 目标能力（通用，不绑定 QQ/微信/浏览器）

### 2.1 Screen（截图）
- 全屏/指定显示器截图
- 指定窗口截图（优先）
- 指定区域截图（Controller 计算 ROI 后下发）
- 返回元数据：捕获区域 rect、窗口 rect、时间戳、DPI/scale、坐标系说明

### 2.2 Window（窗口管理）
- 枚举窗口：title、processName、hwnd、是否可见/最小化、rect
- 聚焦窗口：按 title 匹配/正则或 processName
- 获取窗口 rect（物理像素）
- 可选：移动/调整大小（推荐用于“固定布局”提高鲁棒性）

### 2.3 Input（键鼠注入）
- 鼠标：move/click/double/right/drag/wheel
- 键盘：press（支持组合键 Ctrl/Alt/Shift/Win）
- 人类化参数（可选）：延迟范围、轨迹曲线、轻微抖动、滚轮步长随机

### 2.4 Macro（宏执行）
- 允许 Controller 一次下发动作序列，减少网络往返、提高一致性。
- 每步可配置：delay、是否截图、失败策略（stop/continue/retry）。

---

## 3. 坐标与 DPI 规范（关键：支持 175% 缩放）

### 3.1 坐标系选择
- Executor 对外统一使用：**物理像素坐标（physical pixels）**。
- 截图返回的图像像素坐标 == 输入注入使用的坐标。

### 3.2 DPI-aware 要求
- 进程必须启用 **Per-Monitor DPI Aware V2**。
- API 响应必须携带：
  - `dpiX`, `dpiY` 或 `scale`（如 1.75）
  - `windowRectPx` / `regionRectPx`（物理像素）
  - `screenId`（单屏也保留字段，便于未来扩展）

### 3.3 验收用例
- 在 100%/125%/175% 缩放下，点选测试点（如屏幕四角+中心），点击坐标与截图标注完全一致（误差<=1~2px）。

---

## 4. 锁屏与 UAC 拦截

### 4.1 锁屏检测
- 判定标准：当前没有可交互桌面会话 / 会话被锁 / 前台桌面不可输入。
- 行为：
  - `/input/*` 与 `/macro/*` 返回 `409 LOCKED`（或自定义错误码）
  - `/capture` 允许返回“锁屏截图”或返回明确状态（推荐两者都可配置）

### 4.2 UAC/安全桌面
- 判定标准：检测到安全桌面或输入注入失败（系统拒绝）。
- 行为：拒绝动作并返回 `412 UAC_REQUIRED`（建议），同时写入审计日志。

---

## 5. API（v1 草案）

> 端口：建议 `17890`（可配置）。
> 绑定：仅 tailnet IP。
> 默认 IP allowlist：`100.64.0.1`。

### 5.1 公共字段
- Header：`Authorization: Bearer <token>`
- Response：
  - `runId`: string
  - `stepId`: string
  - `ts`: unix ms

### 5.2 端点

#### GET /health
返回：版本、运行状态、是否锁屏、是否暂停远控、allowlist 生效情况。

#### GET /env
返回：
- 显示器：分辨率、scale、DPI、单/多屏布局
- 坐标系说明（physical pixels）

#### POST /window/list
返回窗口列表（可按可见/进程筛选）。

#### POST /window/focus
请求：`{ match: { titleContains?: string, titleRegex?: string, processName?: string } }`
返回：聚焦后的窗口信息与 rect。

#### POST /capture
请求（示例）：
```json
{ "mode": "window", "window": {"processName":"QQ.exe"}, "format":"png", "region": null }
```
或区域：
```json
{ "mode": "region", "region": {"x": 100, "y": 200, "w": 800, "h": 600}, "format":"jpeg", "quality": 80 }
```
返回：
- `imageB64`（或二进制流 + header 元数据，二选一实现）
- `regionRectPx`, `windowRectPx`, `scale`, `dpi`, `ts`

#### POST /input/mouse
请求（示例）：
```json
{ "kind": "click", "x": 1200, "y": 220, "button": "left", "humanize": {"jitterPx": 2, "delayMs": [40,120]} }
```
支持：move/click/double/right/drag/wheel。

#### POST /input/key
请求（示例）：
```json
{ "kind": "press", "keys": ["CTRL","L"] }
```

#### POST /macro/run
请求：动作数组，Executor 原子执行并逐步回传结果。

---

## 6. 配置与托盘 UI

### 6.1 配置项
- `listenHost`: tailnet IP（或自动选择 tailnet 网卡）
- `listenPort`: 默认 17890
- `token`: 随机生成，可轮换
- `allowlistIps`: 默认 `["100.64.0.1"]`
- `paused`: 是否暂停远控
- `logging`: 日志级别、截图归档策略、证据包目录

### 6.2 托盘交互
- 状态：Online / Locked / Paused / Error / Busy
- 菜单：
  - Copy endpoint URL
  - Copy token（可选：只显示一次，避免泄露）
  - Rotate token
  - Pause/Resume
  - Export last run bundle
  - Open logs folder

---

## 7. 开发里程碑

### M1（底座与 DPI）
- 托盘 + 配置持久化
- /health /env /window/list /window/focus /capture
- DPI-aware V2 + 175% 验收

### M2（输入与审计）
- /input/mouse /input/key
- runId/stepId + 关键动作前后截图
- 锁屏与 UAC 拦截完善

### M3（宏与导出）
- /macro/run
- 证据包导出（runId.zip）
- 限流、超时、并发控制（单控制端下仍建议做）

### M4（扩展能力，可选）
- 剪贴板模块
- 窗口固定布局（move/resize + profile）
- 动作录制/回放

---

## 8. 与 QQ 实验场景的衔接（示例流程）

Controller（服务器 OpenClaw agent）调用 Executor：
1) focus QQ 窗口
2) capture 会话列表区域 → 识别目标会话坐标 → click
3) capture 聊天区域右上角 → 识别“x条新消息”按钮 → click
4) 循环：capture chat region → OCR → wheel down → 直到连续 N 次画面指纹不变（划不动）
5) 去重拼接 → 总结输出

> Executor 不包含任何“识别 QQ 按钮”的逻辑；只执行坐标动作并提供截图。

---

## 9. 风险与注意事项
- Windows 输入注入在某些前台限制/安全桌面会失败：必须显式返回错误并停止。
- 部分应用对后台窗口输入无效：必须先 focus。
- UI 自动化鲁棒性依赖“固定窗口大小/位置/主题/缩放”：建议提供窗口布局 profile。

---

## 10. 端口与默认值（建议）
- listenPort：`17890`
- allowlistIps：`["100.64.0.1"]`
- image 默认：PNG（区域截图可用 JPEG 省带宽）

