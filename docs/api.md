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
Returns environment metadata (display/DPI placeholders for now).

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
| `displayIndex` | int | No | Display index (placeholder, not yet implemented) |

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
    "dpi": 168
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
| `scale` | double | Display scale factor (e.g., 1.75 for 175% scaling). **Placeholder**: currently returns system-wide DPI. |
| `dpi` | int | Display DPI (e.g., 168 for 175% at 96 base). **Placeholder**: currently returns system-wide DPI. |

### DPI Notes

- All coordinates are **physical pixels** (DPI-aware).
- The process assumes **Per-Monitor DPI Aware V2** is enabled.
- `scale` and `dpi` are currently placeholders returning system-wide values. Per-monitor values will be added in future versions.

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
    "dpi": 168
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
