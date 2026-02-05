# Progress Log

> Keep this as the single source of truth for development progress.
> Every entry should include: what changed, how to test, and what's next.

## 2026-02-05 (Update 4)

### Completed
- Implemented `POST /macro/run` endpoint for batch macro execution:
  - **Step types**: `window.focus`, `capture`, `input.mouse`, `input.key`, `sleep`
  - **Sequential execution**: Steps run in order with individual results
  - **Fail-fast mode**: Optional early termination on first failure (default: true)
  - **Humanization defaults**: Apply `delayMs`/`jitterPx` to all steps via `defaults.humanize`
  - **Run ID**: Uses `X-Run-Id` header if present, otherwise generates new ID
  - **UAC handling**: Returns 412 `UAC_REQUIRED` if blocked by secure desktop
  - **Capture schema**: `capture` step returns same schema as `/capture` endpoint
- Created `MacroRunner.cs` with step execution logic reusing existing providers
- Updated API documentation with full `/macro/run` specification

### How to test (POST /macro/run)

1) Focus window and capture:
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

2) Mouse click and keyboard input:
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"input.mouse", "x": 500, "y": 300},
      {"kind":"input.key", "keys": ["CTRL", "A"]}
    ],
    "defaults": {"humanize": {"delayMs": [10, 50], "jitterPx": 1}}
  }'
```

3) Test fail-fast behavior:
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"window.focus", "match": {"titleContains":"NonExistentWindow"}},
      {"kind":"capture", "mode":"screen"}
    ],
    "failFast": true
  }'
# Expected: First step fails with WINDOW_NOT_FOUND, second step not executed
```

4) Test locked workstation (Win+L, then try any macro):
   - Expected: 409 LOCKED response

5) Test UAC blocking (trigger UAC prompt, then try input macro):
   - Expected: 412 UAC_REQUIRED in step result

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
