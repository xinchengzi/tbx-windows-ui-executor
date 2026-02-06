# API (v1 draft)

Default port: `17890`

All requests require:

- `Authorization: Bearer <token>`
- Remote IP must be allowlisted (default: `100.64.0.1`)
- Recommended: bind to a tailnet IP and keep the allowlist enabled.

See also:
- `docs/acceptance.md` — acceptance checklist for DPI correctness and (future) monitor selection

## GET /health
Returns basic status.
Includes `locked` when the workstation is locked.

## GET /env

Returns environment metadata, including per-monitor bounds and DPI.

### Response

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

### Notes / limitations

- All rectangles are in **physical pixels** in the **virtual screen** coordinate space.
- Per-monitor DPI is retrieved via `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` (Win 8.1+).
  - Fallback uses `CreateDC("DISPLAY", deviceName, ...)` + `GetDeviceCaps(LOGPIXELS*)`.
  - The fallback may return **system DPI** depending on process DPI awareness.

## GET /config
Returns effective `listenHost`, `listenPort`, and `allowlistIps`.
Token is redacted and never returned.

## POST /window/list
Returns window list on Windows; returns `[]` on non-Windows.

## POST /window/focus
Accepts `{ "match": { "titleContains"?, "titleRegex"?, "processName"? } }`.
On Windows, focuses the best matching window (first visible non-minimized match if possible)
and returns the focused `WindowInfo`. Returns `404 WINDOW_NOT_FOUND` when nothing matches.
On non-Windows returns `501 NOT_IMPLEMENTED`.
When the workstation is locked, returns `409 LOCKED`.

## POST /input/*
Refused with `409 LOCKED` when the workstation is locked.
Refused with `429 BUSY` when another macro or input sequence is running.

## POST /input/mouse

Performs mouse input operations using SendInput. Coordinates are physical pixels in virtual screen space.

### Request

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

### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `kind` | string | **Yes** | Operation type: `move`, `click`, `double`, `right`, `wheel`, `drag` |
| `x` | int | Depends | X coordinate (physical pixels). Required for `move`, `click`, `double`, `right`, `drag`. Optional for `wheel`. |
| `y` | int | Depends | Y coordinate (physical pixels). Required for `move`, `click`, `double`, `right`, `drag`. Optional for `wheel`. |
| `button` | string | No | Mouse button: `left` (default), `right`, `middle`. Used by `click`/`double`. |
| `dx` | int | No | Horizontal wheel delta. For `wheel` only. Default: 0. |
| `dy` | int | No | Vertical wheel delta. For `wheel` only. Default: -120 (scroll down). |
| `x2` | int | When kind=drag | End X coordinate for drag operation. |
| `y2` | int | When kind=drag | End Y coordinate for drag operation. |
| `humanize` | object | No | Adds human-like randomness to input. |
| `humanize.jitterPx` | int | No | Random offset in pixels applied to coordinates. |
| `humanize.delayMs` | [int, int] | No | Random delay range [min, max] in milliseconds between actions. |

### Operation Types

| Kind | Description |
|------|-------------|
| `move` | Move cursor to (x, y) |
| `click` | Move to (x, y), then left-click (or button specified) |
| `double` | Move to (x, y), then double-click |
| `right` | Move to (x, y), then right-click |
| `wheel` | Scroll wheel at current position (or move to x,y first if provided). dy=-120 scrolls down, dy=120 scrolls up. |
| `drag` | Mouse down at (x, y), move to (x2, y2), mouse up |

### Response

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

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON, missing required fields, or unknown kind |
| 409 | `LOCKED` | Workstation is locked |
| 412 | `UAC_REQUIRED` | Input blocked by UAC/secure desktop |
| 429 | `BUSY` | Another macro or input sequence is running |
| 500 | `INPUT_FAILED` | SendInput failed |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

### Coordinate System

- Coordinates are **physical pixels** in **virtual screen** space.
- Virtual screen origin can be negative (e.g., secondary monitor to the left of primary).
- Use `GET /env` to retrieve virtual screen bounds and per-monitor coordinates.

### Examples

#### Move cursor
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"move","x":500,"y":300}'
```

#### Left click
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":500,"y":300}'
```

#### Right click
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"right","x":500,"y":300}'
```

#### Double click
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"double","x":500,"y":300}'
```

#### Scroll down at position
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"wheel","x":500,"y":300,"dy":-120}'
```

#### Drag from (100,100) to (500,500)
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"drag","x":100,"y":100,"x2":500,"y2":500}'
```

#### Click with humanization
```bash
curl -X POST http://100.115.92.6:17890/input/mouse \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"click","x":500,"y":300,"humanize":{"jitterPx":2,"delayMs":[10,50]}}'
```

## POST /input/key

Performs keyboard input operations using SendInput.

### Request

```json
{
  "kind": "press",
  "keys": ["CTRL", "L"],
  "humanize": {
    "delayMs": [10, 50]
  }
}
```

### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `kind` | string | **Yes** | Operation type: `press` |
| `keys` | string[] | **Yes** | Array of key names to press |
| `humanize` | object | No | Adds human-like randomness to input |
| `humanize.delayMs` | [int, int] | No | Random delay range [min, max] in milliseconds between key events |

### Operation Types

| Kind | Description |
|------|-------------|
| `press` | Key down in order, then key up in reverse order (chord behavior) |

### Supported Keys

| Category | Keys |
|----------|------|
| Modifiers | `CTRL`, `ALT`, `SHIFT`, `WIN` |
| Special | `ENTER`, `ESC`, `TAB`, `BACKSPACE`, `DELETE`, `SPACE` |
| Navigation | `HOME`, `END`, `PAGEUP`, `PAGEDOWN`, `UP`, `DOWN`, `LEFT`, `RIGHT` |
| Letters | `A`-`Z` |
| Numbers | `0`-`9` |
| Function | `F1`-`F12` |

### Response

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

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON, missing required fields, or unknown key/kind |
| 409 | `LOCKED` | Workstation is locked |
| 412 | `UAC_REQUIRED` | Input blocked by UAC/secure desktop |
| 429 | `BUSY` | Another macro or input sequence is running |
| 500 | `INPUT_FAILED` | SendInput failed |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

#### Enhanced Error Response (v0.2+)

When `INPUT_FAILED` occurs, the response now includes diagnostic information:

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

Common `lastError` values:
- `5` (ERROR_ACCESS_DENIED): UIPI blocked input - target window has higher privilege level
- `0`: No error recorded (check if window has focus)

### Implementation Notes

Keyboard input now uses **scan codes** with `KEYEVENTF_SCANCODE` flag for improved reliability:
- Extended keys (arrows, Home, End, Delete, etc.) include `KEYEVENTF_EXTENDEDKEY` flag
- Both virtual key code (`wVk`) and scan code (`wScan`) are sent
- This improves compatibility with applications that rely on scan codes

### Examples

#### Press Ctrl+L (focus address bar in browser)
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","L"]}'
```

#### Press Ctrl+C (copy)
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","C"]}'
```

#### Press Ctrl+Shift+Esc (open Task Manager)
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","SHIFT","ESC"]}'
```

#### Press Enter
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["ENTER"]}'
```

#### Press F5 (refresh)
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["F5"]}'
```

#### Press with humanization
```bash
curl -X POST http://100.115.92.6:17890/input/key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"kind":"press","keys":["CTRL","V"],"humanize":{"delayMs":[10,30]}}'
```

## GET /input/cursor

Returns the current cursor position, virtual screen bounds, and foreground window handle. Useful for debugging mouse movement issues.

### Response

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

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `cursorX` | int | Current cursor X position in physical pixels |
| `cursorY` | int | Current cursor Y position in physical pixels |
| `virtualScreen.x` | int | Virtual screen origin X (can be negative for multi-monitor) |
| `virtualScreen.y` | int | Virtual screen origin Y |
| `virtualScreen.w` | int | Virtual screen width in physical pixels |
| `virtualScreen.h` | int | Virtual screen height in physical pixels |
| `foregroundHwnd` | long | Handle of the current foreground window |

### Example

```bash
curl -X GET http://100.115.92.6:17890/input/cursor \
  -H "Authorization: Bearer $TOKEN"
```

## POST /macro/*
Refused with `409 LOCKED` when the workstation is locked.
Refused with `429 BUSY` when another macro or input sequence is running.

## POST /macro/run

Executes a sequence of input steps (mouse/keyboard) as a macro. Only one macro or input sequence can run at a time.

### Request

```json
{
  "steps": [
    { "delayMs": 100, "mouse": { "kind": "click", "x": 500, "y": 300 } },
    { "delayMs": 50, "key": { "kind": "press", "keys": ["CTRL", "V"] } }
  ]
}
```

### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `steps` | array | **Yes** | Array of macro steps to execute |
| `steps[].delayMs` | int | No | Delay in milliseconds before executing this step (default: 0) |
| `steps[].mouse` | object | No | Mouse input request (same format as `/input/mouse`) |
| `steps[].key` | object | No | Key input request (same format as `/input/key`) |

### Response

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "success": true,
    "stepsExecuted": 2
  }
}
```

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON or missing/empty steps array |
| 409 | `LOCKED` | Workstation is locked |
| 429 | `BUSY` | Another macro or input sequence is running |
| 500 | `MOUSE_FAILED` / `KEY_FAILED` | Input step failed |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

### Examples

#### Execute click then paste
```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"steps":[{"delayMs":0,"mouse":{"kind":"click","x":500,"y":300}},{"delayMs":100,"key":{"kind":"press","keys":["CTRL","V"]}}]}'
```

## POST /macro/run

Executes a sequence of macro steps atomically. Windows only.

### Request

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

### Step Kinds

| Kind | Description | Required Fields |
|------|-------------|-----------------|
| `window.focus` | Focus a window | `match` (same as `/window/focus`) |
| `capture` | Capture screen/window/region | Same fields as `/capture` |
| `input.mouse` | Mouse input (defaults to click) | Same as `/input/mouse`. Use `input.mouse.move`, `input.mouse.click`, etc. for explicit action. |
| `input.mouse.wheel` | Mouse wheel via input.mouse | `dy` (vertical delta) or `dx` (horizontal delta). Optional: `x`, `y` to move cursor first. |
| `input.wheel` | Dedicated wheel step | `delta` (wheel amount, e.g. 120 per notch), `horizontal` (bool, default false). Optional: `x`, `y`. |
| `input.key` | Keyboard input | Same as `/input/key`. Use `input.key.press` for explicit action. |
| `sleep` | Delay execution | `ms` (milliseconds) |

#### `input.wheel` Step Details

The `input.wheel` step provides a clean interface for mouse wheel operations:

```json
{
  "kind": "input.wheel",
  "x": 500,
  "y": 300,
  "delta": -120,
  "horizontal": false
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `x` | int | No | X coordinate to move cursor to before scrolling |
| `y` | int | No | Y coordinate to move cursor to before scrolling |
| `delta` | int | No | Wheel delta (default: -120). Positive = scroll up/right, negative = scroll down/left. Typical: 120 per notch. |
| `horizontal` | bool | No | If true, sends horizontal wheel (HWHEEL). Default: false (vertical). |

### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `steps` | array | **Yes** | Array of step objects to execute |
| `defaults` | object | No | Default settings applied to all steps |
| `defaults.humanize` | object | No | Default humanization settings |
| `defaults.humanize.delayMs` | [int, int] | No | Random delay range [min, max] in milliseconds |
| `defaults.humanize.jitterPx` | int | No | Random pixel offset for mouse operations |
| `failFast` | bool | No | Stop at first failure (default: true) |

### Response

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

### Step Result Fields

| Field | Type | Description |
|-------|------|-------------|
| `stepId` | string | Unique identifier for this step |
| `ok` | bool | Whether the step succeeded |
| `status` | int? | HTTP status code on failure |
| `error` | string? | Error code on failure |
| `data` | object? | Step-specific result data on success |

### Behavior

- Steps execute sequentially in order.
- Each step returns its own result with a unique `stepId`.
- If `failFast` is true (default), execution stops at the first failure.
- Uses `X-Run-Id` header if provided, otherwise generates a new runId.
- The `capture` step returns the same schema as `/capture`.
- Humanization from `defaults.humanize` is applied unless overridden per-step.

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON, missing required fields, or unknown step kind |
| 404 | `WINDOW_NOT_FOUND` | Window focus step: no matching window |
| 404 | `CAPTURE_FAILED` | Capture step: target not found or capture failed |
| 409 | `LOCKED` | Workstation is locked |
| 412 | `UAC_REQUIRED` | Input blocked by UAC/secure desktop |
| 500 | `INPUT_FAILED` | SendInput failed |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

### Examples

#### Focus window and capture
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

#### Click and type with humanization
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

## POST /capture

Captures screen, window, or region. Returns PNG or JPEG image with metadata.

### Request

```json
{
  "mode": "screen" | "window" | "region",
  "window": {
    "titleContains": "string (optional)",
    "titleRegex": "string (optional)",
    "processName": "string (optional)"
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

#### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mode` | string | No | `"screen"` (default), `"window"`, or `"region"` |
| `window` | object | When mode=window | Window match criteria |
| `window.titleContains` | string | No | Match windows containing this text (case-insensitive) |
| `window.titleRegex` | string | No | Match windows by regex pattern |
| `window.processName` | string | No | Match windows by process name (e.g., `"notepad"`) |
| `region` | object | When mode=region | Physical pixel coordinates |
| `region.x` | int | Yes | X coordinate (physical pixels) |
| `region.y` | int | Yes | Y coordinate (physical pixels) |
| `region.w` | int | Yes | Width (physical pixels) |
| `region.h` | int | Yes | Height (physical pixels) |
| `format` | string | No | `"png"` (default) or `"jpeg"`/`"jpg"` |
| `quality` | int | No | JPEG quality 1-100 (default: 90). Ignored for PNG. |
| `displayIndex` | int | No | When `mode=screen`: capture a specific display by index (from `GET /env`). Default: primary display. |

### Response

```json
{
  "runId": "abc123",
  "stepId": "def456",
  "ok": true,
  "data": {
    "imageB64": "<base64 encoded image>",
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

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `imageB64` | string | Base64-encoded image data |
| `format` | string | `"png"` or `"jpeg"` |
| `regionRectPx` | object | Actual captured region in physical screen pixels |
| `windowRectPx` | object? | Window rect if mode=window; null otherwise |
| `ts` | long | Unix timestamp in milliseconds |
| `scale` | double | Display scale factor for the monitor containing the capture rect center (e.g., 1.75 for 175% scaling). |
| `dpi` | int | Display DPI for the monitor containing the capture rect center (typically 96 * scale). |
| `displayIndex` | int? | Index of the display used for scale/dpi derivation (from `GET /env`). Null if unknown. |
| `deviceName` | string? | Device name of the display (e.g., `"\\\\.\\DISPLAY1"`). Null if unknown. |

### DPI Notes

- All coordinates are **physical pixels** (DPI-aware).
- The process assumes **Per-Monitor DPI Aware V2** is enabled.
- `scale`/`dpi` are reported for the **monitor containing the capture rectangle center**.
  - `mode=window`: uses the window rect center.
  - `mode=region`: uses the region rect center.
  - `mode=screen`: uses the selected display (by `displayIndex`) or the primary display.

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON or missing required fields |
| 404 | `CAPTURE_FAILED` | Window not found or capture failed |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

### Examples

#### Capture full screen (PNG)
```json
{ "mode": "screen" }
```

#### Capture specific window (JPEG)
```json
{ "mode": "window", "window": { "processName": "notepad" }, "format": "jpeg", "quality": 85 }
```

#### Capture region
```json
{ "mode": "region", "region": { "x": 100, "y": 100, "w": 500, "h": 400 } }
```

## GET /capture/selfcheck

Quick self-check to verify capture functionality is working.

### Response

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

On non-Windows or if capture fails:
```json
{
  "data": {
    "ok": false,
    "reason": "NOT_WINDOWS" | "CAPTURE_FAILED",
    "captureAvailable": false
  }
}
```

## POST /macro/run

Executes a sequence of steps (macro) atomically. Each step is logged to the evidence bundle.

### Request

```json
{
  "steps": [
    {
      "type": "capture",
      "capture": {
        "mode": "screen",
        "format": "png"
      }
    },
    {
      "type": "mouse",
      "mouse": {
        "kind": "click",
        "x": 500,
        "y": 300
      }
    },
    {
      "type": "key",
      "key": {
        "kind": "press",
        "keys": ["CTRL", "C"]
      }
    },
    {
      "type": "delay",
      "delayMs": 100,
      "onFailure": "stop"
    }
  ]
}
```

### Step Types

| Type | Description |
|------|-------------|
| `capture` | Takes a screenshot. Image saved to `screenshots/` folder, not returned as base64 in logs. |
| `mouse` | Performs mouse input (move, click, drag, wheel, etc.) |
| `key` | Performs keyboard input |
| `delay` | Waits for specified milliseconds |

### Step Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | **Yes** | `capture`, `mouse`, `key`, or `delay` |
| `capture` | object | When type=capture | Capture options (same as `/capture` request) |
| `mouse` | object | When type=mouse | Mouse input options (same as `/input/mouse` request) |
| `key` | object | When type=key | Key input options (same as `/input/key` request) |
| `delayMs` | int | When type=delay | Milliseconds to wait |
| `onFailure` | string | No | `stop` (default) or `continue`. If `stop`, macro halts on step failure. |

### Response

```json
{
  "runId": "abc123def456",
  "ok": true,
  "steps": [
    {
      "stepId": "step001",
      "type": "capture",
      "ok": true,
      "durationMs": 150,
      "response": {
        "screenshotPath": "screenshots/step_step001.png",
        "format": "png",
        "regionRectPx": { "x": 0, "y": 0, "w": 1920, "h": 1080 },
        "ts": 1707123456789,
        "scale": 1.75,
        "dpi": 168
      },
      "screenshotPath": "screenshots/step_step001.png"
    },
    {
      "stepId": "step002",
      "type": "mouse",
      "ok": true,
      "durationMs": 50,
      "response": { "success": true }
    }
  ]
}
```

### Evidence Bundle Logging

- All steps are automatically logged to `%APPDATA%/TbxExecutor/runs/<runId>/steps.jsonl`
- Screenshots are saved to `screenshots/step_<stepId>.png` (or `.jpg`)
- **imageB64 is NOT logged** - only the file reference path is stored
- **Bearer tokens are NEVER logged**

### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `BAD_REQUEST` | Invalid JSON or missing required fields |
| 409 | `LOCKED` | Workstation is locked |
| 501 | `NOT_IMPLEMENTED` | Non-Windows platform |

### Example

```bash
curl -X POST http://100.115.92.6:17890/macro/run \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Run-Id: my-test-run-001" \
  -d '{
    "steps": [
      {"type": "capture", "capture": {"mode": "screen"}},
      {"type": "mouse", "mouse": {"kind": "click", "x": 500, "y": 300}},
      {"type": "delay", "delayMs": 100},
      {"type": "capture", "capture": {"mode": "screen"}}
    ]
  }'
```

## Evidence Bundle Endpoints

These endpoints allow reading evidence bundle data (runs, steps, screenshots) via HTTP.

### GET /run/list

Lists recent execution runs.

#### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `limit` | int (query) | No | Maximum runs to return (default: 20, max: 100) |

#### Response

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

#### Example

```bash
curl -X GET "http://100.115.92.6:17890/run/list?limit=10" \
  -H "Authorization: Bearer $TOKEN"
```

### GET /run/steps

Returns the steps.jsonl content for a specific run.

#### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `runId` | string (query) | **Yes** | Run ID (must match `[a-zA-Z0-9_-]+`) |

#### Response

Returns NDJSON (newline-delimited JSON) with `Content-Type: application/x-ndjson`.

Each line is a JSON object representing a step:

```json
{"stepId":"abc123","endpoint":"/capture","tsMs":1707123456789,"ok":true,...}
{"stepId":"def456","endpoint":"/input/mouse","tsMs":1707123456800,"ok":true,...}
```

#### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `INVALID_RUN_ID` | runId contains invalid characters |
| 404 | `RUN_NOT_FOUND` | Run does not exist or has no steps |

#### Example

```bash
curl -X GET "http://100.115.92.6:17890/run/steps?runId=my-test-run-001" \
  -H "Authorization: Bearer $TOKEN"
```

### GET /run/screenshot

Returns a screenshot file for a specific step.

#### Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `runId` | string (query) | **Yes** | Run ID (must match `[a-zA-Z0-9_-]+`) |
| `stepId` | string (query) | **Yes** | Step ID (must match `[a-zA-Z0-9_-]+`) |

#### Response

Returns the screenshot image with appropriate `Content-Type` (`image/png` or `image/jpeg`).

#### Errors

| Status | Error Code | Description |
|--------|------------|-------------|
| 400 | `INVALID_RUN_ID` | runId contains invalid characters |
| 400 | `INVALID_STEP_ID` | stepId contains invalid characters |
| 404 | `SCREENSHOT_NOT_FOUND` | Screenshot file does not exist |

#### Example

```bash
curl -X GET "http://100.115.92.6:17890/run/screenshot?runId=my-test-run-001&stepId=abc123def456" \
  -H "Authorization: Bearer $TOKEN" \
  --output screenshot.png
```

#### Security

- `runId` and `stepId` are validated against `[a-zA-Z0-9_-]+` pattern
- Path traversal attacks are prevented by strict validation
- All file access is constrained to `%APPDATA%\TbxExecutor\runs\` directory

## Window Capture Enhancement

When using `mode: "window"` for capture, the system now uses intelligent window selection:

### Window Selection Scoring

Windows are scored and ranked based on:

| Factor | Score |
|--------|-------|
| Foreground window | +200 |
| Visible and non-minimized | +100 |
| Visible but minimized | +50 |
| Valid rect (w/h > 0) | +50 |
| ProcessName exact match | +30 |
| TitleContains match | +20 |
| Window area (normalized) | +0~30 |

The highest-scoring window is selected.

### Response with Window Metadata

When capturing a window, the response includes `selectedWindow` with audit information:

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

### Capture Failure Diagnostics

When window capture fails, the error response includes diagnostic information:

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
        "title": "Notepad - Untitled",
        "processName": "notepad",
        "score": 0,
        "isVisible": true,
        "isMinimized": false,
        "width": 800,
        "height": 600
      },
      ...
    ]
  }
}
```

The `candidates` array shows the top 5 visible windows to help diagnose why no match was found.
