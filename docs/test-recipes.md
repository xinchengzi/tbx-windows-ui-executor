# 测试配方（可复用）

> 目标：使测试可重复、可审计、每次构建后易于重新运行。
> 所有配方假定已设置 `TBX_HOST`、`TBX_TOKEN`。

## 环境

```bash
export TBX_HOST=100.64.0.3
export TBX_TOKEN=...
```

辅助变量：

```bash
H_AUTH=(-H "Authorization: Bearer $TBX_TOKEN")
H_JSON=(-H "Authorization: Bearer $TBX_TOKEN" -H "Content-Type: application/json")
BASE="http://$TBX_HOST:17890"
```

---

## 配方 A — 验证物理光标移动

1) 读取光标：
```bash
curl -sS "$BASE/input/cursor" "${H_AUTH[@]}" | jq .
```

2) 移动光标：
```bash
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d '{"kind":"move","x":500,"y":500}' | jq .
```

3) 再次读取光标（必须匹配目标）：
```bash
curl -sS "$BASE/input/cursor" "${H_AUTH[@]}" | jq .
```

预期：
- `cursorX/cursorY` 变化并等于目标值。

---

## 配方 B — 聚焦 QQ 窗口

```bash
curl -sS "$BASE/window/focus" "${H_JSON[@]}" \
  -d '{"match":{"processName":"QQ"}}' | jq .
```

注意：
- **错误写法**：`{"processName":"QQ"}` → 返回 `BAD_MATCH`。
- **正确写法**：`{"match":{"processName":"QQ"}}`
- QQ 标题可能是 `\u0000\u0000`，所以推荐使用 `processName`。

---

## 配方 C — 窗口滚动测试

此配方：
- 截取窗口（前）
- 从 `windowRectPx` 计算聊天区域的点
- 点击确保焦点
- 滚轮两次
- 截取窗口（后）

```bash
RESP=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  -d '{"mode":"window","window":{"processName":"QQ"}}')

X=$(echo "$RESP" | jq -r .data.windowRectPx.x)
Y=$(echo "$RESP" | jq -r .data.windowRectPx.y)
W=$(echo "$RESP" | jq -r .data.windowRectPx.w)
H=$(echo "$RESP" | jq -r .data.windowRectPx.h)

# 启发式点：在聊天区域内，不是左侧对话列表
CX=$(python3 -c "x=$X;w=$W;print(int(x+w*0.60))")
CY=$(python3 -c "y=$Y;h=$H;print(int(y+h*0.55))")

echo "$RESP" | jq -r .data.imageB64 | base64 -d > qq_before.png

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"click\",\"x\":$CX,\"y\":$CY}" >/dev/null

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.2
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.7

RESP2=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  -d '{"mode":"window","window":{"processName":"QQ"}}')

echo "$RESP2" | jq -r .data.imageB64 | base64 -d > qq_after.png

sha256sum qq_before.png qq_after.png
```

预期：
- `qq_before.png` 和 `qq_after.png` 视觉上有差异（滚动效果）。

---

## 配方 D — 键盘 Ctrl+F 测试

```bash
curl -sS "$BASE/input/key" "${H_JSON[@]}" \
  -d '{"kind":"press","keys":["CTRL","F"]}' | jq .
```

预期：
- `ok:true`

---

## 配方 E — 证据包（run/steps/screenshot）

目标链：
- `/macro/run` → 获取 runId
- `/run/steps?runId=...` → 返回步骤数据
- `/run/screenshot?runId=...&stepId=...` → 返回图像

```bash
# 1. 运行宏获取 runId
RESP=$(curl -sS "$BASE/macro/run" "${H_JSON[@]}" \
  -d '{"steps":[{"kind":"capture","mode":"screen"}]}')
RUN_ID=$(echo "$RESP" | jq -r .data.runId)

# 2. 获取步骤
curl -sS "$BASE/run/steps?runId=$RUN_ID" "${H_AUTH[@]}"

# 3. 获取截图（需要 stepId）
STEP_ID=$(echo "$RESP" | jq -r '.data.steps[0].stepId')
curl -sS "$BASE/run/screenshot?runId=$RUN_ID&stepId=$STEP_ID" "${H_AUTH[@]}" \
  --output screenshot.png
```

---

## 配方 F — 并发锁测试

验证 `429 BUSY` 响应：

```bash
# 在后台运行一个长宏
curl -sS "$BASE/macro/run" "${H_JSON[@]}" \
  -d '{"steps":[{"kind":"sleep","ms":3000}]}' &

sleep 0.5

# 尝试另一个输入操作（应返回 429）
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d '{"kind":"move","x":100,"y":100}' | jq .
```

预期：
- 第二个请求返回 `ok:false, status:429, error:"BUSY"`

---

## 配方 G — 锁屏检测测试

锁定工作站后测试：

```bash
curl -sS "$BASE/health" "${H_AUTH[@]}" | jq .data.locked
# 应返回 true

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  -d '{"kind":"move","x":100,"y":100}' | jq .
# 应返回 409 LOCKED
```
