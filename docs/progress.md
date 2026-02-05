# Progress Log

> Keep this as the single source of truth for development progress.
> Every entry should include: what changed, how to test, and what’s next.

## 2026-02-05

### Context / Goal
- Project: **TBX Windows UI Executor** (Win11 tray app) providing **capture/window/input primitives** over tailnet.
- Controller: mf-kvm01 (100.64.0.1)
- Executor: yc-tbx (Win11)

### Agreed constraints (already decided)
- Bind only to tailnet IP; keep IP allowlist (default `100.64.0.1`).
- Auth: `Authorization: Bearer <token>`.
- Coordinate system exposed to Controller: **physical pixels**.
- Refuse input/macro when workstation is locked.
- No elevation; don’t touch UAC secure desktop.

### Current repo state
- `main` @ `8035e4e` (2026-02-05): capture endpoint + tailnet binding helper + config endpoint.

### Plan (next milestones)
1) **Finish M1 properly**: DPI + multi-monitor metadata
   - Implement `/env` with real monitor list, per-monitor DPI/scale (as available)
   - Ensure `/capture` returns `scale/dpi` matching the captured monitor
   - Provide repeatable validation steps for 100%/125%/175%
2) **M2 input endpoints**
   - `/input/mouse`, `/input/key`
   - Locked → 409; UAC/safe desktop → explicit error (e.g., 412)
   - Optional: before/after screenshots behind config flags
3) **M3 macro + evidence bundle**
   - `/macro/run`
   - run bundle export (JSONL + images, optional zip)

### How to test (today)
- Build/run on Windows:
  - `dotnet build`
  - run tray app, then call:
    - `GET /health`
    - `GET /config`
    - `POST /window/list`
    - `POST /capture` (screen/window/region)

### Notes / Risks
- DPI correctness is the foundation; do it before input.
- Multi-monitor DPI can differ; per-monitor DPI awareness must be verified on Win11.
