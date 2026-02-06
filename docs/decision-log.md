# 决策日志

记录影响架构、安全边界、兼容性或 API 语义的决策。

## 2026-02-06 — 键盘输入修复

- **问题**：键盘输入返回 `ERROR_INVALID_PARAMETER (87)`
- **根因**：`InputUnion` 结构体只包含 `KEYBDINPUT`，导致 `Marshal.SizeOf<INPUT>()` 返回错误大小
- **决策**：在 `InputUnion` 中添加 `MOUSEINPUT`，确保 Windows API 要求的正确 union 大小

## 2026-02-06 — Exit 死锁修复

- **问题**：点击 "Exit" 后应用无响应
- **根因**：在 UI 线程调用 `StopAsync().GetAwaiter().GetResult()` 导致死锁
- **决策**：使用 `Task.Run` 包装异步调用，添加超时保护

## 2026-02-05 — 开发流程决策

- 实现由 **OpenClaw 编码代理（Codex CLI）** 完成。
- 人工/助手角色：指定需求、验收标准，审查变更。
- 原因：通过将进度持久化到文档和 git 历史，最大化速度并减少跨会话的记忆丢失。

## 2026-02-05 — 坐标系统

- 外部坐标系统为**物理像素**。
- 截图像素坐标必须与输入注入坐标匹配。

## 2026-02-05 — 网络与鉴权

- 仅绑定到 tailnet 接口 IP。
- 强制远程 IP 白名单（默认 `100.64.0.1`）。
- 所有端点需要 `Authorization: Bearer <token>`。

## 2026-02-05 — 安全边界

- 工作站锁定时拒绝 `/input/*` 和 `/macro/*`。
- 不提权；不与 UAC 安全桌面交互。
