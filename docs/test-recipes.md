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

## 重要原则（必读）

### 1) 坐标系
- TbxExecutor 的输入坐标均为**虚拟屏幕空间中的物理像素**。
- `capture mode=window` 返回的 `windowRectPx` 可用于把“窗口内相对坐标”换算为“绝对屏幕坐标”。

换算：
- `absX = windowRectPx.x + relX`
- `absY = windowRectPx.y + relY`

### 2) 标准闭环（视觉 + 键鼠 + 验收）
> 任何“点中了/切换成功/输入生效”的结论，必须来自**视觉验收**（高亮/标题/输入框文本），不能仅靠 `ok:true` 或“截图变了”。

标准循环：
1. `focus` → `capture(window)`（基准帧）
2. 视觉模型定位目标元素 `box/click`（窗口内坐标）
3. 计算 `absX/absY` → 执行真实点击/按键
4. 立刻 `capture(window)` → 视觉模型判定是否达成目标
5. 不通过就重试：再次定位（不要复用旧坐标）

### 3) curl JSON 发送（防止请求体截断）
- **推荐**：`--data-binary @file.json` 或 `--data-binary '{...}'`
- 调试时加 `-v` 检查 `Content-Length` 是否合理。

---

## 配方 A — 验证物理光标移动

1) 读取光标：
```bash
curl -sS "$BASE/input/cursor" "${H_AUTH[@]}" | jq .
```

2) 移动光标：
```bash
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary '{"kind":"move","x":500,"y":500}' | jq .
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
  --data-binary '{"match":{"processName":"QQ"}}' | jq .
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
  --data-binary '{"mode":"window","window":{"processName":"QQ"}}')

X=$(echo "$RESP" | jq -r .data.windowRectPx.x)
Y=$(echo "$RESP" | jq -r .data.windowRectPx.y)
W=$(echo "$RESP" | jq -r .data.windowRectPx.w)
H=$(echo "$RESP" | jq -r .data.windowRectPx.h)

# 启发式点：在聊天区域内，不是左侧对话列表
CX=$(python3 -c "x=$X;w=$W;print(int(x+w*0.60))")
CY=$(python3 -c "y=$Y;h=$H;print(int(y+h*0.55))")

echo "$RESP" | jq -r .data.imageB64 | base64 -d > qq_before.png

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary "{\"kind\":\"click\",\"x\":$CX,\"y\":$CY}" >/dev/null

curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.2
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary "{\"kind\":\"wheel\",\"x\":$CX,\"y\":$CY,\"dy\":-1440}" >/dev/null
sleep 0.7

RESP2=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  --data-binary '{"mode":"window","window":{"processName":"QQ"}}')

echo "$RESP2" | jq -r .data.imageB64 | base64 -d > qq_after.png

sha256sum qq_before.png qq_after.png
```

预期：
- `qq_before.png` 和 `qq_after.png` 视觉上有差异（滚动效果）。

---

## 配方 D — 键盘 Ctrl+F 测试

```bash
curl -sS "$BASE/input/key" "${H_JSON[@]}" \
  --data-binary '{"kind":"press","keys":["CTRL","F"]}' | jq .
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
  --data-binary '{"steps":[{"kind":"capture","mode":"screen"}]}')
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
  --data-binary '{"steps":[{"kind":"sleep","ms":3000}]}' &

sleep 0.5

# 尝试另一个输入操作（应返回 429）
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary '{"kind":"move","x":100,"y":100}' | jq .
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
  --data-binary '{"kind":"move","x":100,"y":100}' | jq .
# 应返回 409 LOCKED
```

---

## 配方 H — QQ：视觉闭环点选“我的手机”并在输入框出现 test（标准验收）

> 目标：严格按“视觉定位 → 真实键鼠 → 视觉验收”的闭环，完成：
> 1) 选中“我的手机”会话（左侧条目高亮/右侧进入该聊天）
> 2) 点击输入框、`Ctrl+A`、输入 `test`
> 3) 视觉确认输入框出现 `test`
>
> 说明：这里的“视觉模型调用”在不同编排系统里实现不同；本仓库仅提供**接口与验收门槛**。

### H.1 基准帧：聚焦 + 截 QQ 窗口

```bash
RESP=$(curl -sS "$BASE/macro/run" "${H_JSON[@]}" \
  --data-binary '{"steps":[
    {"kind":"window.focus","match":{"processName":"QQ"}},
    {"kind":"sleep","ms":150},
    {"kind":"capture","mode":"window","window":{"processName":"QQ"},"format":"png"}
  ]}')

# 取 windowRectPx（用于 rel→abs 换算）
WIN_X=$(echo "$RESP" | jq -r '.data.steps[-1].data.windowRectPx.x')
WIN_Y=$(echo "$RESP" | jq -r '.data.steps[-1].data.windowRectPx.y')

echo "$RESP" | jq -r '.data.steps[-1].data.imageB64' | base64 -d > H0_window.png
```

### H.2 视觉模型定位“我的手机”（得到 rel 坐标）

- 输入：`H0_window.png`
- 输出：`myPhone.box=[x1,y1,x2,y2]`、`myPhone.click=[relX,relY]`
- **验收门槛**：必须能在图中找到“我的手机”。

换算：
- `ABS_X = WIN_X + relX`
- `ABS_Y = WIN_Y + relY`

> 点击点建议：条目中部偏右，避免点到头像。

### H.3 点击并立刻视觉验收（循环直到选中）

```bash
# 单次尝试（失败就回到“重新截图+重新定位”，不要复用旧坐标）
curl -sS "$BASE/input/mouse" "${H_JSON[@]}" \
  --data-binary "{\"kind\":\"click\",\"x\":$ABS_X,\"y\":$ABS_Y}" >/dev/null

sleep 0.25

RESP2=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  --data-binary '{"mode":"window","window":{"processName":"QQ"},"format":"png"}')
echo "$RESP2" | jq -r .data.imageB64 | base64 -d > H1_after_click_myphone.png
```

- 视觉验收输入：`H1_after_click_myphone.png`
- 视觉验收输出：`selected=true/false`
- **硬门槛**：
  - `selected=true`（“我的手机”条目高亮或右侧标题/聊天区进入“我的手机”）才允许进入下一阶段。

### H.4 输入框：定位 → 点击 → 输入 test → 视觉验收

重复与 H.2/H.3 相同的闭环：
- 对“输入框”做视觉定位（rel 坐标）→ 换算 abs → 点击 → 截图验收“已获得焦点”
- 再执行：

```bash
curl -sS "$BASE/input/key" "${H_JSON[@]}" \
  --data-binary '{"kind":"press","keys":["CTRL","A"]}' >/dev/null

for k in T E S T; do
  curl -sS "$BASE/input/key" "${H_JSON[@]}" \
    --data-binary "{\"kind\":\"press\",\"keys\":[\"$k\"]}" >/dev/null
done

sleep 0.3
RESP3=$(curl -sS "$BASE/capture" "${H_JSON[@]}" \
  --data-binary '{"mode":"window","window":{"processName":"QQ"},"format":"png"}')
echo "$RESP3" | jq -r .data.imageB64 | base64 -d > H2_after_type_test.png
```

- 视觉验收门槛：`H2_after_type_test.png` 中输入框可见 `test`。

### H.5 可审计补充（建议）
- 每次点击后调用 `GET /input/cursor` 记录 `cursorX/cursorY/foregroundHwnd`，用于证明“真实移动到位”。
- 如需更强诊断：额外 `capture mode=screen`，记录 QQ 在全屏中的位置与上下文。
