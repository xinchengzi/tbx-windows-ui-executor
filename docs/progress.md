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

### Completed (baseline)
- Implemented `GET /env` to return per-monitor bounds + DPI/scale (Windows only)
- Updated `/capture` + `/capture/selfcheck` metadata so `scale`/`dpi` match the captured monitor
- `mode=screen` captures a single monitor (primary by default) and supports `displayIndex`

### How to test (baseline)
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
- Multi-monitor DPI can differ.
- Per-monitor DPI uses `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` (Win 8.1+). Fallback may return system DPI depending on process DPI awareness.

### Next (after baseline)
- Enforce/verify Per-Monitor DPI awareness at process startup.
- M2 input endpoints (`/input/mouse`, `/input/key`).

---

## 2026-02-05 (Update 2) — /input/mouse

### Completed
- Implemented `POST /input/mouse` endpoint:
  - Operations: `move`, `click`, `double`, `right`, `wheel`, `drag`
  - Coordinate system: physical pixels in virtual screen space (negative origins supported)
  - SendInput flags: `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`
  - Optional humanization: `jitterPx`, `delayMs`
  - 412 `UAC_REQUIRED` when blocked by secure desktop

### How to test
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"move","x":500,"y":300}'
```

Locked test:
- Win+L then call any `/input/*` → expect 409 `LOCKED`

Multi-monitor test:
- Use `GET /env` to find secondary monitor coordinates
- Click on secondary monitor using its physical pixel coordinates

---

## 2026-02-05 (Update 3) — /input/key

### Completed
- Implemented `POST /input/key` endpoint (`kind=press`):
  - Key down in given order; key up in reverse order
  - Supported keys: CTRL, ALT, SHIFT, WIN, ENTER, ESC, TAB, BACKSPACE, DELETE, HOME, END, PAGEUP, PAGEDOWN, UP/DOWN/LEFT/RIGHT, A-Z, 0-9, F1-F12, SPACE
  - Optional humanization: `delayMs` between events
  - 412 `UAC_REQUIRED` when blocked by secure desktop

### How to test
```bash
# Ctrl+L
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","L"]}'

# Ctrl+Shift+Esc
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","SHIFT","ESC"]}'
```

---

## 2026-02-05 (Update 4) — DPI awareness enforcement

### Completed
- Enforced **Per-Monitor DPI Aware V2** via `app.manifest` (with fallbacks)
- Added DPI awareness self-check in `GET /env`:
  - New field `dpiAwareness` (e.g., `"PerMonitorV2"`)

### How to verify DPI awareness
```bash
curl -s http://100.115.92.6:17890/env \
  -H "Authorization: Bearer $TOKEN" | jq '.data.dpiAwareness'
# expected: "PerMonitorV2"
```

---

## Next
- (Optional) Include display identity in capture response (`displayIndex/deviceName`) for easier debugging.
- M3: macros + evidence bundles.
