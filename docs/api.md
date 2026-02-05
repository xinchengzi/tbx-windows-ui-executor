# API (v1 draft)

Default port: `17890`

All requests require:

- `Authorization: Bearer <token>`
- Remote IP must be allowlisted (default: `100.64.0.1`)

## GET /health
Returns basic status.
Includes `locked` when the workstation is locked.

## GET /env
Returns environment metadata (display/DPI placeholders for now).

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
Returns 501 Not Implemented.
