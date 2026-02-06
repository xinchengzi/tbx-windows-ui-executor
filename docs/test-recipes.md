# Test Recipes (Reusable)

> Goal: make tests repeatable, auditable, and easy to rerun after each build.
> All recipes assume `TBX_HOST`, `TBX_TOKEN` are set.

## Environment

```bash
export TBX_HOST=100.64.0.3
export TBX_TOKEN=...
```

Helper headers:

```bash
H_AUTH=(-H "Authorization: Bearer $TBX_TOKEN")
H_JSON=(-H "Authorization: Bearer $TBX_TOKEN" -H "Content-Type: application/json")
BASE="http://$TBX_HOST:17890"
```

---

## Recipe A — Verify physical cursor movement (must be visible + readable)

1) Read cursor:
```bash
curl -sS "$BASE/input/cursor" "${H_AUTH[@]}" | jq .
```

2) Move cursor:
```bash
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d kind:move | jq .
```

3) Read cursor again (must match target):
```bash
curl -sS "$BASE/input/cursor" "${H_AUTH[@]}" | jq .
```

Expected:
- `cursorX/cursorY` changes and equals target.

---

## Recipe B — Focus QQ window (correct request schema)

```bash
curl -sS "$BASE/window/focus" "${H_JSON[@]}" \
  -d {match:{processName:QQ}} | jq .
```

Notes:
- **Wrong**: `{"processName":"QQ"}` → returns `BAD_MATCH`.
- QQ title may be `\u0000\u0000`, so prefer `processName`.

---

## Recipe C — Window scroll test on QQ (no manual coordinate needed)

This recipe:
- captures QQ window (before)
- computes a point in the chat area from `windowRectPx`
- click to ensure focus
- wheel twice with large delta
- captures QQ window (after)

```bash
RESP=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  -d mode:window)

X=$(echo "$RESP" | jq -r .data.windowRectPx.x)
Y=$(echo "$RESP" | jq -r .data.windowRectPx.y)
W=$(echo "$RESP" | jq -r .data.windowRectPx.w)
H=$(echo "$RESP" | jq -r .data.windowRectPx.h)

# heuristic point: inside chat area, not left conversation list
CX=$(python3 -c "x=$X;w=$W;print(int(x+w*0.60))")
CY=$(python3 -c "y=$Y;h=$H;print(int(y+h*0.55))")

echo "$RESP" | jq -r .data.imageB64 | base64 -d > qq_before.png

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"click\",\"x\":$CX,\"y\":$CY,\"button\":\"left\",\"clicks\":1}" >/dev/null

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.2
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.7

RESP2=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  -d mode:window)

echo "$RESP2" | jq -r .data.imageB64 | base64 -d > qq_after.png

sha256sum qq_before.png qq_after.png
```

Expected:
- `qq_before.png` and `qq_after.png` differ visually (scroll).

---

## Recipe D — Keyboard Ctrl+F self-test (currently failing)

```bash
curl -sS "$BASE/input/key" "${H_JSON[@]}" \
  -d kind:press | jq .
```

Expected:
- `ok:true`

Current known issue:
- May still return `lastError=87` (`ERROR_INVALID_PARAMETER`).

---

## Recipe E — Evidence pack (run/steps/screenshot) (currently not closed)

Target chain:
- `/macro/run` → runId
- `/run/steps?runId=...` → not null
- `/run/screenshot?runId=...&stepId=...` → image

Known issue:
- Some runs appear in `/run/list` with `hasScreenshots:true` but `/run/steps` returns `RUN_NOT_FOUND`.

