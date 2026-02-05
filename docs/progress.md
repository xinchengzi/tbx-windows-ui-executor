# Progress Log

> Keep this as the single source of truth for development progress.
> Every entry should include: what changed, how to test, and what's next.

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
