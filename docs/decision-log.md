# Decision Log

Record decisions that affect architecture, safety boundaries, compatibility, or API semantics.

## 2026-02-05 — Development process decision
- Implementation will be done by **OpenClaw coding agent (Codex CLI)**.
- Human/assistant role: specify requirements, acceptance criteria, and review changes.
- Reason: maximize velocity and reduce memory loss across sessions by persisting progress to docs + git history.

## 2026-02-05 — Coordinate system
- External coordinate system is **physical pixels**.
- Capture pixel coordinates must match input injection coordinates.

## 2026-02-05 — Network & auth
- Bind only to tailnet interface IP.
- Enforce remote IP allowlist (default `100.64.0.1`).
- Require `Authorization: Bearer <token>` for all endpoints.

## 2026-02-05 — Safety boundaries
- Refuse `/input/*` and `/macro/*` when workstation is locked.
- No elevation; no interaction with UAC secure desktop.
