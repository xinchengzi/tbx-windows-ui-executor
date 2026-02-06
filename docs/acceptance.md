# Acceptance Checklist — DPI correctness & monitor selection

This checklist validates that **coordinates are physical pixels** and that captures are consistent across **100% / 125% / 175%** Windows scaling, including (when implemented) **multi-monitor selection**.

Scope: current API endpoints:
- `GET /env`
- `POST /capture`

> Why this matters: the Controller must be able to do **pixel-perfect mapping**:
> - Pick a point `(x,y)` from an image
> - Use the same `(x,y)` later for region captures and (future) input injection
> - Get consistent results across DPI scaling and monitors

---

## Definitions

- **Physical pixels**: the pixel grid of the captured image (what you see in a PNG/JPEG). This is the only coordinate system exposed to the Controller.
- **Scale**: Windows display scaling factor (e.g. `1.0`, `1.25`, `1.75`).
- **Display index**: integer identifying a monitor (API field: `displayIndex`).

---

## Preconditions

1. Executor is running on Windows 11 with **Per-Monitor DPI Aware v2** enabled (project requirement).
2. You have the base URL, token, and allowlist configured.
3. You can call endpoints from the Controller machine.

Quick smoke:
- `GET /health` returns `ok: true`.
- `GET /env` returns `ok: true` (even if displays are placeholders).

---

## Checklist A — `/env` reports DPI/monitor metadata (current + future)

> Note: `GET /env` currently returns placeholder `displays: []` (see `docs/api.md`). This section defines **acceptance criteria** for when `/env` is completed.

### A1. Coordinate system declaration
- [ ] `GET /env` returns `data.coordinateSystem == "physicalPixels"`.

### A2. Monitor list completeness (when implemented)
For each connected monitor, `/env` should include:
- [ ] Stable `displayIndex` (0..N-1)
- [ ] Bounds in **physical pixels** (x/y/w/h) in the virtual desktop space
- [ ] Per-monitor `dpi` and/or `scale`
- [ ] `isPrimary` marker

**Pass criteria**: The Controller can pick a monitor by `displayIndex` and reason about its pixel bounds and scale.

---

## Checklist B — DPI correctness at 100% / 125% / 175%

Run **all items** at each scaling setting:
- 100% (scale 1.0)
- 125% (scale 1.25)
- 175% (scale 1.75)

### B0. Test setup (manual)
1. Windows Settings → System → Display → Scale → set to target value.
2. Log out/in if required (Windows sometimes needs it for full consistency).

### B1. Full-screen capture returns self-consistent metadata
Call:
```json
{ "mode": "screen" }
```
Verify:
- [ ] Response `data.regionRectPx.w` and `.h` match the **decoded image pixel dimensions**.
- [ ] `data.scale` and `data.dpi` are present.
  - Current implementation note: `scale`/`dpi` may be system-wide placeholders (see `docs/api.md`).

**Pass criteria**: The image dimensions and reported `regionRectPx` dimensions agree exactly.

### B2. Region capture matches crop of full-screen (pixel-perfect mapping, capture-only)
This validates pixel mapping *without requiring any input endpoints*.

Procedure:
1. Capture full screen as in **B1**; decode the image.
2. Choose a region that stays well inside bounds, e.g.:
   - `x = 100`, `y = 100`, `w = 400`, `h = 300`
3. Call region capture:
```json
{ "mode": "region", "region": { "x": 100, "y": 100, "w": 400, "h": 300 } }
```
4. Decode the region image.
5. Crop the full-screen image at the same `(x,y,w,h)`.

Verify:
- [ ] Region image pixel dimensions are exactly `w × h`.
- [ ] Region image pixels equal the cropped full-screen pixels (byte-for-byte or near-exact with a strict diff threshold).

**Pass criteria**: Region capture is a perfect crop (or within a tiny tolerance if the capture pipeline introduces format artifacts; for PNG this should be exact).

### B3. Window capture geometry is consistent (sanity)
If you have a stable target window (e.g. Notepad):
```json
{ "mode": "window", "window": { "processName": "notepad" } }
```
Verify:
- [ ] `windowRectPx` is non-null.
- [ ] `windowRectPx.w/h` are plausible and match the captured image content size expectations.

**Pass criteria**: Window capture provides a usable rect in physical pixels.

---

## Checklist C — Multi-monitor selection & correctness (when implemented)

> `displayIndex` is currently documented as a placeholder in `docs/api.md`. This section defines the acceptance tests to run once implemented.

### C1. Capturing each monitor returns the expected resolution
For each `displayIndex` in `[0..N-1]`:
```json
{ "mode": "screen", "displayIndex": i }
```
Verify:
- [ ] Returned image dimensions match that monitor’s physical resolution.
- [ ] `regionRectPx` corresponds to that monitor’s bounds, not the entire virtual desktop.

### C2. Per-monitor scale/dpi matches the captured monitor
If monitors have different scaling (e.g. internal at 175%, external at 100%):
- [ ] Capturing each monitor returns `scale/dpi` matching that monitor (not system-wide).

### C3. Virtual-desktop coordinate consistency across monitors
If the API supports capturing regions in the virtual desktop space:
- [ ] Regions that cross monitor boundaries are either:
  - rejected with a clear error (`400 BAD_REQUEST`), or
  - captured correctly with well-defined semantics.

---

## Controller-side validation recipe (recommended)

The Controller can validate pixel-perfect mapping with only `/capture`:

1. `POST /capture {mode:"screen"}` → decode image `S`.
2. Pick deterministic test ROIs (multiple sizes/positions).
3. For each ROI:
   - `POST /capture {mode:"region", region: ROI}` → decode image `R`.
   - Compute `crop(S, ROI)` → image `C`.
   - Compare `R` vs `C`.

Suggested thresholds:
- PNG: exact match expected.
- JPEG: allow small per-pixel error; prefer running acceptance with PNG.

This proves:
- The coordinate system is physical pixels.
- There is no hidden DPI virtualization affecting region coordinates.

---

## Curl examples

```bash
# env
curl -sS -H "Authorization: Bearer $TBX_TOKEN" http://$TBX_HOST:17890/env | jq

# capture full screen
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"mode":"screen"}' \
  http://$TBX_HOST:17890/capture | jq

# capture region
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"mode":"region","region":{"x":100,"y":100,"w":400,"h":300}}' \
  http://$TBX_HOST:17890/capture | jq
```

---

## Checklist D — Window Capture Enhancement

### D1. Window selection scoring
- [ ] When multiple windows match the criteria, the system selects the best one based on scoring
- [ ] Foreground window gets priority (+200 score)
- [ ] Visible non-minimized windows are preferred (+100 score)

### D2. Window metadata in response
When capturing with `mode: "window"`:
- [ ] Response includes `selectedWindow` object with:
  - `hwnd` (window handle)
  - `title` (window title)
  - `processName` (process name)
  - `rectPx` (window rect in physical pixels)
  - `isVisible`, `isMinimized` (state flags)
  - `score` (selection score for debugging)

### D3. Diagnostic information on failure
When window capture fails:
- [ ] Response includes `data.reason` explaining why capture failed
- [ ] Response includes `data.candidates` with top 5 visible windows
- [ ] Each candidate includes hwnd, title, processName, score, dimensions

### D4. Window capture curl example
```bash
# Capture QQ window with metadata
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"mode":"window","window":{"processName":"QQ"}}' \
  http://$TBX_HOST:17890/capture | jq '.data.selectedWindow'
```

---

## Checklist E — Evidence Bundle HTTP Endpoints

### E1. List runs
- [ ] `GET /run/list` returns recent runs sorted by lastWriteUtc descending
- [ ] Response includes runId, lastWriteUtc, stepsCount, hasScreenshots
- [ ] `?limit=N` parameter works correctly

```bash
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  "http://$TBX_HOST:17890/run/list?limit=10" | jq
```

### E2. Get steps
- [ ] `GET /run/steps?runId=...` returns steps.jsonl content
- [ ] Response Content-Type is `application/x-ndjson`
- [ ] Each line is valid JSON

```bash
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  "http://$TBX_HOST:17890/run/steps?runId=my-test-run" 
```

### E3. Get screenshot
- [ ] `GET /run/screenshot?runId=...&stepId=...` returns image binary
- [ ] Response Content-Type is `image/png` or `image/jpeg`
- [ ] Invalid runId/stepId returns 400 error

```bash
curl -sS -H "Authorization: Bearer $TBX_TOKEN" \
  "http://$TBX_HOST:17890/run/screenshot?runId=my-test-run&stepId=abc123" \
  --output screenshot.png
```

### E4. Security validation
- [ ] `runId` with path traversal characters (`../`, `..\\`) is rejected with 400
- [ ] `stepId` with invalid characters is rejected with 400
- [ ] Only `[a-zA-Z0-9_-]` pattern is accepted for runId and stepId

---

## Checklist F — Mouse Wheel Input

### F1. Wheel via /input/mouse
- [ ] `POST /input/mouse` with `kind: "wheel"` scrolls vertically
- [ ] `dy: -120` scrolls down, `dy: 120` scrolls up
- [ ] `dx` parameter triggers horizontal scroll (HWHEEL)
- [ ] Optional `x`, `y` moves cursor before scrolling

```bash
# Scroll down at position
curl -X POST http://$TBX_HOST:17890/input/mouse \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"wheel","x":500,"y":300,"dy":-120}'
```

### F2. Wheel via macro step (input.wheel)
- [ ] `kind: "input.wheel"` step works in /macro/run
- [ ] `delta` parameter controls scroll amount
- [ ] `horizontal: true` triggers horizontal scroll
- [ ] Error response includes `lastError` for diagnostics

```bash
# Macro with wheel step
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"window.focus","match":{"processName":"QQ"}},
      {"kind":"input.wheel","x":200,"y":400,"delta":-360}
    ]
  }'
```

### F3. Wheel error diagnostics
- [ ] On failure, response includes `lastError` Win32 error code
- [ ] Error message includes operation details (e.g., "wheel dx=0 dy=-120")

---

## Checklist G — Keyboard Input Improvements

### G1. Scan code support
- [ ] `/input/key` uses scan codes (KEYEVENTF_SCANCODE)
- [ ] Extended keys (arrows, Delete, Home, etc.) include KEYEVENTF_EXTENDEDKEY
- [ ] Ctrl+F works reliably in applications

### G2. Enhanced error diagnostics
- [ ] On failure, response includes `lastError` and detailed message
- [ ] Error shows which key failed: "INPUT_FAILED: keydown CTRL (vk=0x11, scan=0x1D)"

```bash
# Test Ctrl+F
curl -X POST http://$TBX_HOST:17890/input/key \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","F"]}'
```

---

## Checklist H — Tray Busy Indicator

### H1. Macro busy state
- [ ] Tray icon changes when macro is running
- [ ] Icon reverts to normal after macro completes
- [ ] Works for both successful and failed macros

### H2. Input flash state
- [ ] Tray icon briefly flashes on `/input/mouse` request
- [ ] Tray icon briefly flashes on `/input/key` request
- [ ] Flash duration is approximately 500ms
- [ ] Multiple rapid inputs keep icon in busy state

### H3. Visual verification
Run this test sequence and observe tray icon:
```bash
# Should flash briefly
curl -X POST http://$TBX_HOST:17890/input/key \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","A"]}'

# Should stay busy during execution
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"steps":[{"kind":"sleep","ms":2000}]}'
```

---

## Manual Test Recipes

### Recipe 1: Scroll QQ chat list
1. Open QQ and ensure it's visible
2. Run macro to scroll left panel:
```bash
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"window.focus","match":{"processName":"QQ"}},
      {"kind":"sleep","ms":200},
      {"kind":"input.wheel","x":150,"y":400,"delta":-360},
      {"kind":"sleep","ms":500},
      {"kind":"input.wheel","x":150,"y":400,"delta":360}
    ]
  }'
```
3. Verify: QQ list scrolls down then back up

### Recipe 2: Test Ctrl+F in Notepad
1. Open Notepad with some text
2. Run:
```bash
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "steps": [
      {"kind":"window.focus","match":{"processName":"notepad"}},
      {"kind":"sleep","ms":100},
      {"kind":"input.key","keys":["CTRL","F"]}
    ]
  }'
```
3. Verify: Find dialog opens

### Recipe 3: Verify tray busy indicator
1. Watch tray icon
2. Run a long macro:
```bash
curl -X POST http://$TBX_HOST:17890/macro/run \
  -H "Authorization: Bearer $TBX_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"steps":[{"kind":"sleep","ms":3000}]}'
```
3. Verify: Tray icon shows busy state for 3 seconds

