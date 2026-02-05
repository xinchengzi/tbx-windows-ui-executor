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

## POST /macro/*
Refused with `409 LOCKED` when the workstation is locked.

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
