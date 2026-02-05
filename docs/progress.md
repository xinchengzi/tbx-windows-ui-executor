# Progress Log

> Keep this as the single source of truth for development progress.
> Every entry should include: what changed, how to test, and what’s next.

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
- Consider returning `displayIndex/deviceName` in capture response for easier debugging.
- M2 input endpoints (`/input/key`).

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

---

## 2026-02-05 (Update 3)

### Completed
- Enforced **Per-Monitor DPI Aware V2** at process startup:
  - Added `app.manifest` with `dpiAwareness=PerMonitorV2,PerMonitor` and fallback `dpiAware=true/PM`
  - Updated `TbxExecutor.csproj` with `<ApplicationManifest>app.manifest</ApplicationManifest>`
- Added DPI awareness self-check in `GET /env`:
  - New field `dpiAwareness` returns current mode (e.g., `"PerMonitorV2"`)
  - Implemented via `GetThreadDpiAwarenessContext` + `AreDpiAwarenessContextsEqual` Win32 APIs
- Updated `docs/spec.md` with DPI awareness enforcement details

### How to verify DPI awareness
1) Start the tray app on Windows 10 1703+ / Windows 11.
2) Call `GET /env`:
```bash
curl -s http://100.115.92.6:17890/env -H "Authorization: Bearer $TOKEN" | jq '.data.dpiAwareness'
```
3) Expected result: `"PerMonitorV2"`
4) If result is `"SystemAware"` or `"Unaware"`, the manifest is not being applied correctly.

### Technical notes
- **Manifest vs API**: Manifest is preferred because Windows reads it before process initialization, avoiding race conditions.
- **Fallback chain**: `PerMonitorV2` → `PerMonitor` (V1) → `true/PM` (legacy)
- **Query method**: Uses `GetThreadDpiAwarenessContext()` which returns a pseudo-handle, then compares via `AreDpiAwarenessContextsEqual()`.

### Next
- M2 input endpoints (`/input/key`)
- Consider adding `displayIndex/deviceName` in capture response
