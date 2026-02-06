# 验收清单 — DPI 正确性与显示器选择

本清单验证**坐标为物理像素**，且截图在 **100% / 125% / 175%** Windows 缩放设置下一致，包括（实现后的）**多显示器选择**。

范围：当前 API 端点：
- `GET /env`
- `POST /capture`

> 为什么重要：Controller 必须能够进行**像素级精确映射**：
> - 从图像中选取点 `(x,y)`
> - 稍后使用相同的 `(x,y)` 进行区域截图和输入注入
> - 在不同 DPI 缩放和显示器之间获得一致结果

---

## 定义

- **物理像素**：截取图像的像素网格（PNG/JPEG 中看到的内容）。这是暴露给 Controller 的唯一坐标系统。
- **缩放**：Windows 显示缩放因子（如 `1.0`、`1.25`、`1.75`）。
- **显示器索引**：标识显示器的整数（API 字段：`displayIndex`）。

---

## 前置条件

1. Executor 在 Windows 11 上运行，启用 **Per-Monitor DPI Aware v2**（项目要求）。
2. 已配置基础 URL、token 和白名单。
3. 可以从 Controller 机器调用端点。

快速冒烟测试：
- `GET /health` 返回 `ok: true`。
- `GET /env` 返回 `ok: true`。

---

## 清单 A — `/env` 报告 DPI/显示器元数据

### A1. 坐标系统声明
- [x] `GET /env` 返回 `data.coordinateSystem == "physicalPixels"`。

### A2. 显示器列表完整性
对于每个连接的显示器，`/env` 应包括：
- [x] 稳定的 `displayIndex` (0..N-1)
- [x] 虚拟桌面空间中的**物理像素**边界 (x/y/w/h)
- [x] 每显示器的 `dpi` 和/或 `scale`
- [x] `isPrimary` 标记

**通过标准**：Controller 可以通过 `displayIndex` 选择显示器并推断其像素边界和缩放。

---

## 清单 B — 100% / 125% / 175% 下的 DPI 正确性

在每个缩放设置下运行**所有项目**：
- 100% (scale 1.0)
- 125% (scale 1.25)
- 175% (scale 1.75)

### B0. 测试设置（手动）
1. Windows 设置 → 系统 → 显示 → 缩放 → 设置目标值。
2. 如需要，注销/登录（Windows 有时需要此步骤以保持完全一致性）。

### B1. 全屏截图返回自洽元数据
调用：
```json
{ "mode": "screen" }
```
验证：
- [x] 响应 `data.regionRectPx.w` 和 `.h` 与**解码图像像素尺寸**匹配。
- [x] 存在 `data.scale` 和 `data.dpi`。

**通过标准**：图像尺寸与报告的 `regionRectPx` 尺寸完全一致。

### B2. 区域截图与全屏裁剪匹配（像素级精确映射）
验证像素映射*无需任何输入端点*。

步骤：
1. 按 **B1** 截取全屏；解码图像。
2. 选择一个完全在边界内的区域，例如：
   - `x = 100`, `y = 100`, `w = 400`, `h = 300`
3. 调用区域截图：
```json
{ "mode": "region", "region": { "x": 100, "y": 100, "w": 400, "h": 300 } }
```
4. 解码区域图像。
5. 在相同 `(x,y,w,h)` 处裁剪全屏图像。

验证：
- [x] 区域图像像素尺寸正好是 `w × h`。
- [x] 区域图像像素与裁剪的全屏像素相等（逐字节或在严格差异阈值内接近）。

**通过标准**：区域截图是完美裁剪（或在截图管道引入格式伪影时允许微小容差；PNG 应精确匹配）。

### B3. 窗口截图几何一致性（健全性检查）
如果有稳定的目标窗口（如记事本）：
```json
{ "mode": "window", "window": { "processName": "notepad" } }
```
验证：
- [x] `windowRectPx` 非空。
- [x] `windowRectPx.w/h` 合理且与截取图像内容大小预期匹配。

**通过标准**：窗口截图提供物理像素的可用矩形。

---

## 清单 C — 多显示器选择与正确性

### C1. 截取每个显示器返回预期分辨率
对于每个 `displayIndex` 在 `[0..N-1]`：
```json
{ "mode": "screen", "displayIndex": i }
```
验证：
- [x] 返回的图像尺寸与该显示器的物理分辨率匹配。
- [x] `regionRectPx` 对应该显示器的边界，而非整个虚拟桌面。

### C2. 每显示器 scale/dpi 与截取的显示器匹配
如果显示器有不同的缩放（如内置 175%，外接 100%）：
- [x] 截取每个显示器返回与该显示器匹配的 `scale/dpi`（非系统级）。

### C3. 跨显示器的虚拟桌面坐标一致性
如果 API 支持在虚拟桌面空间截取区域：
- [x] 跨显示器边界的区域要么：
  - 以明确错误拒绝（`400 BAD_REQUEST`），要么
  - 以定义良好的语义正确截取。

---

## Controller 端验证配方（推荐）

Controller 仅使用 `/capture` 即可验证像素级精确映射：

1. `POST /capture {mode:"screen"}` → 解码图像 `S`。
2. 选择确定性测试 ROI（多个大小/位置）。
3. 对于每个 ROI：
   - `POST /capture {mode:"region", region: ROI}` → 解码图像 `R`。
   - 计算 `crop(S, ROI)` → 图像 `C`。
   - 比较 `R` vs `C`。

建议阈值：
- PNG：应精确匹配。
- JPEG：允许小的每像素误差；推荐使用 PNG 进行验收测试。

这证明了：
- 坐标系统是物理像素。
- 没有隐藏的 DPI 虚拟化影响区域坐标。

---

## 清单 D — 窗口截图增强

### D1. 窗口选择评分
- [x] 多个窗口匹配时，系统根据评分选择最佳窗口
- [x] 前台窗口获得优先（+200 分）
- [x] 可见且未最小化的窗口优先（+100 分）

### D2. 响应中的窗口元数据
使用 `mode: "window"` 截图时：
- [x] 响应包含 `selectedWindow` 对象：
  - `hwnd`（窗口句柄）
  - `title`（窗口标题）
  - `processName`（进程名）
  - `rectPx`（物理像素的窗口矩形）
  - `isVisible`、`isMinimized`（状态标志）
  - `score`（调试用选择分数）

### D3. 失败时的诊断信息
窗口截图失败时：
- [x] 响应包含 `data.reason` 解释失败原因
- [x] 响应包含 `data.candidates`，前 5 个可见窗口
- [x] 每个候选包含 hwnd、title、processName、score、dimensions

---

## 清单 E — 证据包 HTTP 端点

### E1. 列出 runs
- [x] `GET /run/list` 返回按 lastWriteUtc 降序排列的最近 runs
- [x] 响应包含 runId、lastWriteUtc、stepsCount、hasScreenshots
- [x] `?limit=N` 参数正常工作

### E2. 获取 steps
- [x] `GET /run/steps?runId=...` 返回 steps.jsonl 内容
- [x] 响应 Content-Type 是 `application/x-ndjson`
- [x] 每行是有效 JSON

### E3. 获取截图
- [x] `GET /run/screenshot?runId=...&stepId=...` 返回图像二进制
- [x] 响应 Content-Type 是 `image/png` 或 `image/jpeg`
- [x] 无效 runId/stepId 返回 400 错误

### E4. 安全验证
- [x] 包含路径遍历字符（`../`、`..\\`）的 `runId` 被拒绝并返回 400
- [x] 包含无效字符的 `stepId` 被拒绝并返回 400
- [x] runId 和 stepId 只接受 `[a-zA-Z0-9_-]` 模式

---

## 清单 F — 鼠标滚轮输入

### F1. 通过 /input/mouse 滚轮
- [x] `POST /input/mouse` 的 `kind: "wheel"` 垂直滚动
- [x] `dy: -120` 向下滚动，`dy: 120` 向上滚动
- [x] `dx` 参数触发水平滚动 (HWHEEL)
- [x] 可选 `x`、`y` 在滚动前移动光标

### F2. 通过宏步骤滚轮 (input.wheel)
- [x] `kind: "input.wheel"` 步骤在 /macro/run 中工作
- [x] `delta` 参数控制滚动量
- [x] `horizontal: true` 触发水平滚动
- [x] 错误响应包含 `lastError` 用于诊断

### F3. 滚轮错误诊断
- [x] 失败时，响应包含 `lastError` Win32 错误码
- [x] 错误消息包含操作详情（如 "wheel dx=0 dy=-120"）

---

## 清单 G — 键盘输入改进

### G1. 虚拟键码支持
- [x] `/input/key` 使用纯虚拟键码模式
- [x] 扩展键（箭头、Delete、Home 等）包含 KEYEVENTF_EXTENDEDKEY
- [x] Ctrl+F 在应用程序中可靠工作

### G2. 增强错误诊断
- [x] 失败时，响应包含 `lastError` 和详细消息
- [x] 错误显示哪个键失败："INPUT_FAILED: keydown CTRL (vk=0x11)"

---

## 清单 H — 托盘忙碌指示器

### H1. 宏忙碌状态
- [x] 宏运行时托盘图标变化
- [x] 宏完成后图标恢复正常
- [x] 成功和失败的宏都能正常工作

### H2. 输入闪烁状态
- [x] `/input/mouse` 请求时托盘图标短暂闪烁
- [x] `/input/key` 请求时托盘图标短暂闪烁
- [x] 闪烁持续时间约 500ms
- [x] 多次快速输入保持图标在忙碌状态

### H3. 视觉验证
运行此测试序列并观察托盘图标：
```bash
# 应短暂闪烁
curl -X POST http://$TBX_HOST:17890/input/key \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","A"]}'

# 执行期间应保持忙碌
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"steps":[{"kind":"sleep","ms":2000}]}'
```

---

## 清单 I — 光标移动验证

### I1. 物理光标移动
- [x] `/input/mouse` 的 `kind: "move"` 物理移动系统光标
- [x] 光标位置变化在屏幕上可见
- [x] 响应包含 `cursorX` 和 `cursorY` 的实际位置

### I2. 调试光标端点
- [x] `GET /input/cursor` 返回当前光标位置
- [x] 响应包含虚拟屏幕边界
- [x] 响应包含前台窗口句柄

### I3. 指定位置滚轮
- [x] `kind: "wheel"` 带 `x`、`y` 在滚动前移动光标
- [x] 滚动效果在指定位置可见

---

## 清单 J — 托盘图标状态

### J1. 图标状态
- [x] 空闲状态显示标准应用程序图标（非警告/感叹号）
- [x] 忙碌状态显示蓝色圆形指示器（非黄色警告）
- [x] 正常操作中永远不出现感叹号图标

### J2. 状态转换
- [x] 宏开始时图标变为忙碌
- [x] 宏完成时图标恢复空闲
- [x] 单次输入操作短暂闪烁（约 500ms）
