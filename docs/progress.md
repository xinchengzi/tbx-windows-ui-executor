# 进度日志

> 本文档作为开发进度的唯一真实来源。
> 每条记录应包括：变更内容、测试方法和后续计划。

## 2026-02-06（Bug 修复版本）

### 已完成

修复了代码审查中发现的多个 bug：

#### 严重问题
1. **RunGuard 并发锁未使用**：在 `/input/*` 和 `/macro/*` 端点添加了并发控制中间件，现在正确返回 `429 BUSY`
2. **TrayHost Icon 资源泄漏**：添加了 `DestroyIcon` 调用释放 GDI 句柄

#### 中等问题
3. **App.xaml.cs Mutex 生命周期**：将 Mutex 改为类字段，在 `OnExit` 中正确释放，修复单例保护失效问题
4. **RunLogger 内存泄漏**：添加过期清理机制，最多缓存 50 个 RunContext，超出时清理最久未使用的
5. **BusyIndicator 竞态条件**：重构通知逻辑，避免重复事件
6. **锁屏检测保守策略**：`OpenInputDesktop` 失败时现在返回 `true`（假定锁屏），而非 `false`

#### 轻微问题
7. **DisplaySelector 空显示器处理**：坐标点不在任何显示器边界内时，返回最近的显示器而非 null
8. **KeyInput NumLock 扩展键标志**：NumLock 不是扩展键，移除了 `KEYEVENTF_EXTENDEDKEY` 标志
9. **JsonSerializerOptions 重复创建**：改为静态只读属性，减少 GC 压力
10. **Mouse Wheel delta 符号**：使用 `unchecked((uint)dy)` 明确转换负数滚轮值
11. **配置文件原子写入**：先写入临时文件再重命名，防止写入中断导致配置损坏

#### 文档更新
12. **API 文档翻译**：将 `docs/api.md` 翻译为中文

### 测试方法

1) **并发锁测试**：
   ```bash
   # 并发发送两个请求，第二个应返回 429 BUSY
   curl -X POST http://<host>:17890/macro/run \
     -H "Authorization: Bearer $TOKEN" \
     -d '{"steps":[{"kind":"sleep","ms":2000}]}' &
   sleep 0.1
   curl -X POST http://<host>:17890/input/mouse \
     -H "Authorization: Bearer $TOKEN" \
     -d '{"kind":"click","x":100,"y":100}'
   # 预期：第二个请求返回 429 BUSY
   ```

2) **单例保护测试**：
   - 启动应用后再次尝试启动
   - 预期：第二个实例应立即退出

3) **锁屏检测测试**：
   - 锁定工作站 (Win+L)
   - 发送输入请求
   - 预期：返回 409 LOCKED

### 说明
- 所有修复均为最小化更改，未改变现有 API 行为
- 文档已同步翻译为中文

---

## 2026-02-05 (Update 4)

### Completed
- Implemented evidence bundle logging for runs and macro steps:
  - Added `RunLogger` class that writes to `%APPDATA%/TbxExecutor/runs/<runId>/`
  - `steps.jsonl`: One JSON line per step/result
  - `screenshots/`: Optional folder for captured images
  - Token redaction: Bearer tokens are never logged
- Enhanced `/capture` endpoint:
  - When `X-Run-Id` header is present, screenshots are saved to the run's screenshots folder
  - Response unchanged (still returns `imageB64`)
- Added `/macro/run` endpoint:
  - Executes a sequence of steps (capture, mouse, key, delay)
  - Each step logged to `steps.jsonl`
  - Screenshots stored as file references (no `imageB64` in logs)
  - Supports `onFailure: "stop"` (default) or `"continue"`
- Updated API documentation with `/macro/run` specification
- Updated spec.md with evidence bundle section (1.4)

### How to test (evidence bundle)
1) Start the tray app on Windows 11.
2) Call `/macro/run` with a sequence of steps:
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Run-Id: test-run-001" \
  -d '{
    "steps": [
      {"type": "capture", "capture": {"mode": "screen"}},
      {"type": "mouse", "mouse": {"kind": "click", "x": 500, "y": 300}},
      {"type": "delay", "delayMs": 100},
      {"type": "capture", "capture": {"mode": "screen"}}
    ]
  }'
```
3) Check `%APPDATA%/TbxExecutor/runs/test-run-001/`:
   - `steps.jsonl` should contain 4 lines (one per step)
   - `screenshots/` should contain 2 PNG files
4) Verify `imageB64` is NOT present in `steps.jsonl` (only `screenshotPath` references)
5) Verify Bearer token is NOT present anywhere in logs

### Notes
- Evidence bundle directory is created on first write
- Screenshots folder is created only when captures occur
- Network/auth middleware unchanged per constraints

---

## 2026-02-05

### Context / Goal
- Project: **TBX Windows UI Executor** (Win11 tray app) providing **capture/window/input primitives** over tailnet.
- Controller: mf-kvm01 (100.64.0.1)
- Executor: yc-tbx (Win11)

### Agreed constraints (already decided)
- Bind only to tailnet IP; keep IP allowlist (default `100.64.0.1`).
- Auth: `Authorization: Bearer <token>`.
- Coordinate system exposed to Controller: **physical pixels**.
- Refuse input/macro when workstation is locked.
- No elevation; don’t touch UAC secure desktop.

### Current repo state
- `main` @ `8035e4e` (2026-02-05): capture endpoint + tailnet binding helper + config endpoint.

### Completed
- Implemented `GET /env` to return per-monitor bounds + DPI/scale (Windows only):
  - Added `virtualScreenRectPx`
  - Per display: `boundsRectPx`, `workAreaRectPx`, and `dpi/scale` objects
- Updated `/capture` + `/capture/selfcheck` metadata so `scale`/`dpi` match the captured monitor:
  - window/region: monitor containing capture rect center
  - screen: respects `displayIndex` (else primary)
- `mode=screen` now captures a single monitor (primary by default) instead of the entire virtual desktop.

### How to test (2026-02-05)
1) On Windows 11 with 2 monitors at different scaling (e.g., 100% and 175%):
   - Start the tray app.
   - Call `GET /env` and note `displays[i].scale.x` / `displays[i].dpi.x`.
2) Screen capture primary:
   - `POST /capture` with `{ "mode": "screen" }`
   - Verify `data.scale/data.dpi` match the primary monitor from `/env`.
3) Screen capture by index:
   - `POST /capture` with `{ "mode": "screen", "displayIndex": 1 }`
   - Verify `data.scale/data.dpi` match `/env.displays[1]`.
4) Window capture across monitors:
   - Move Notepad between monitors and run `POST /capture` with `{ "mode": "window", "window": { "processName": "notepad" } }`
   - Verify `scale/dpi` follow the monitor containing the window center.
5) Region capture across monitors:
   - Choose a region whose center lies on each monitor and call `mode=region`.
   - Verify `scale/dpi` match the monitor containing that region center.

### Notes / Risks
- DPI correctness is the foundation; do it before input.
- Multi-monitor DPI can differ.
- Per-monitor DPI uses `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` (Win 8.1+). Fallback may return system DPI depending on process DPI awareness.

### Next
- Validate per-monitor DPI awareness is enabled at process startup.
- M2 input endpoints (`/input/key`).

---

## 2026-02-05 (Update 3)

### Completed
- Enhanced `/capture` and `/capture/selfcheck` responses to include display identity:
  - Added optional `displayIndex` and `deviceName` fields to capture metadata
  - `displayIndex`: Index of the display used for scale/dpi derivation (matches `GET /env`)
  - `deviceName`: Device name of the display (e.g., `"\\\\.\\DISPLAY1"`)
  - For `mode=screen`: reports the selected display (by `displayIndex` or primary)
  - For `mode=window`/`mode=region`: reports the display containing the capture rect center
- Updated API documentation with new response fields

### How to test (display identity in capture)
1) Call `GET /env` and note display indices and device names.
2) Screen capture primary:
   - `POST /capture` with `{ "mode": "screen" }`
   - Verify `data.displayIndex` and `data.deviceName` match the primary display from `/env`.
3) Screen capture by index:
   - `POST /capture` with `{ "mode": "screen", "displayIndex": 1 }`
   - Verify `data.displayIndex === 1` and `data.deviceName` matches `/env.displays[1].deviceName`.
4) Region capture:
   - Choose a region on each monitor and verify `displayIndex`/`deviceName` match.
5) `/capture/selfcheck`:
   - Verify response includes `displayIndex` and `deviceName` for the primary display.

---

## 2026-02-05 (Update 2)

### Completed
- Implemented `POST /input/mouse` endpoint with full mouse input support:
  - **Operations**: `move`, `click`, `double`, `right`, `wheel`, `drag`
  - **Coordinate system**: Physical pixels in virtual screen space (handles negative origins)
  - **SendInput**: Uses `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK` for cross-monitor support
  - **Humanization**: Optional `jitterPx` and `delayMs` for human-like input
  - **Error handling**: Returns 412 `UAC_REQUIRED` when blocked by secure desktop
- Updated API documentation with full `/input/mouse` specification and curl examples

### How to test (POST /input/mouse)

1) Start the tray app on Windows 11.

2) Move cursor:
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"move","x":500,"y":300}'
```

3) Left click (open Start menu area):
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":50,"y":1050}'
```

4) Right click (context menu):
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"right","x":500,"y":500}'
```

5) Scroll wheel:
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"wheel","x":500,"y":500,"dy":-360}'
```

6) Drag (draw selection box on desktop):
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"drag","x":100,"y":100,"x2":400,"y2":400}'
```

7) Test locked workstation (Win+L, then try any input):
   - Expected: 409 LOCKED response

8) Multi-monitor test (if available):
   - Use `GET /env` to find secondary monitor coordinates
   - Click on secondary monitor using its physical pixel coordinates
