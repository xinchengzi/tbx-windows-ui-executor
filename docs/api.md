# API (v1 draft)

Default port: `17890`

All requests require:

- `Authorization: Bearer <token>`
- Remote IP must be allowlisted (default: `100.64.0.1`)

## GET /health
Returns basic status.

## GET /env
Returns environment metadata (display/DPI placeholders for now).

## POST /window/list
Returns window list on Windows; returns `[]` on non-Windows.

## POST /window/focus
Accepts `{ "match": { "titleContains"?, "titleRegex"?, "processName"? } }`.
Returns 501 Not Implemented.

## POST /capture
Returns 501 Not Implemented.
