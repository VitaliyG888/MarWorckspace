# Development Plan — Taskbar Banner App for Windows

## 1. Overview
A Windows desktop app that displays advertiser banners (logo + short text) docked next to the system-tray icons in the taskbar. The app verifies that the computer is genuinely "watched" (on, unlocked, user active, banner visible) and accrues **verified active minutes**. Verified minutes are reported to a server. Clicking a banner opens the app; from the app the user can open the advertised website. Auth is OAuth 2.0.

## 2. Tech Stack
- **Language/Platform:** C# (.NET 8), **WPF** (main app + overlay banner windows).
- **WebView2** for OAuth login and for showing the advertiser page inside the app.
- **Backend contract:** HTTPS REST API + OAuth 2.0 (Authorization Code + PKCE).
- Persistence: local SQLite (accrued minutes queue, session state, tokens).
- Build: single MSIX/Inno Setup installer, auto-update optional.

## 3. System Architecture / Modules
1. **Tray/Banner Overlay Module** — borderless, topmost, non-activating windows (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`). One overlay window per taskbar (one per display that shows a taskbar, incl. **secondary monitors**), positioned above its notification-area cluster. Shows rotating ads (logo image + short text). Click = raise app window. Overlays are shown **only after successful OAuth 2.0 login**.
2. **Taskbar State Module** — via `SHAppBarMessage(ABM_GETTASKBARPOS)` and `SystemParametersInfo`: taskbar rect, visibility, auto-hide mode, multi-monitor position.
3. **System State Module** — `SystemEvents.SessionSwitch` (lock/unlock), `SystemEvents.PowerModeChanged` (suspend/resume), session connect/disconnect.
4. **Activity Module** — `GetLastInputInfo` to compute idle time since last keyboard/mouse input.
5. **Counter/Verification Engine** — 1-minute tick; central state machine (see §4).
6. **Reporting Module** — persists verified minutes locally and uploads them (with retry/backoff) to the server.
7. **OAuth/Auth Module** — OAuth 2.0 with PKCE via WebView2; token refresh; DPAPI-protected token storage.
8. **Ad Content Module** — fetches ad rotation from API (or offline cache), renders banner.

## 4. Active-Minute Verification Rules (core logic)
Define an **epoch** of 60 s. A minute is **eligible** while ALL conditions hold continuously:
- Machine **powered on** and session **unlocked** (not lock screen, not disconnected).
- Taskbar **present and visible** on every monitor where the app shows a banner (auto-hide = NOT allowed). If a taskbar is auto-hidden → its banner "cannot be confirmed visible" → no accrual.
- At least **one** banner overlay is actually **rendered and on-screen** (window handle visible, topmost, fully inside its taskbar's work area) — self-check each epoch. On a multi-monitor setup the check passes while ≥ 1 of the overlays is confirmed visible.
- **User activity confirmed**: `GetLastInputInfo` shows input within the last ≤ 60 s *and* a change of activity occurred within the epoch.

**Counting rules:**
- **Cooldown/priming period:** no minute counts until the session has been continuously eligible for **5 full minutes** (this encodes "user interacted ≥ 5 minutes before the first counted minute").
- After priming, each fully eligible epoch increments the counter.
- If **any** condition fails mid-epoch (lock, sleep, banner hidden, taskbar hidden, idle > 60 s): the current epoch is discarded, counting **stops**, and **re-priming (5 full eligible minutes) is required again after every interruption** before the next minute can be counted.
- Idle threshold is fixed at **60 s**: user input must be detected at least once every 60 s for an epoch to stay eligible.
- Epoch start times are anchored to real wall-clock (UTC) boundaries for server-side validation.

Pseudo-state machine per session:
```
IDLE ──(eligible 60s)──▶ PRIMING (counts 0..4 valid epochs)
PRIMING ──(5 valid epochs)──▶ COUNTING
any state ──(condition fail)──▶ IDLE / RESET
```
Agent should implement this as an explicit `enum` state machine with an event log per epoch for debugging.

## 5. Banner Behavior
- Shown **only after successful OAuth 2.0 login** (session active); hidden/logged-out users see no banners and no counting happens.
- Rendered next to the system tray on each taskbar (incl. secondary monitors), floats above the taskbar, small fixed size (e.g. ~180×60) with logo + text.
- Never steals focus; clicking it focuses the **main app window** (no direct browser navigation).
- Main app shows the ad details + a clear **"Open website"** button that launches the advertised URL in the default browser.
- Must self-recover if a taskbar moves / resolution changes / monitor count changes (reposition on display-events); overlay on a removed/unavailable monitor is destroyed and the others remain active.

## 6. Reporting to Server
- After each verified minute, enqueue an event `{sessionId, minuteStartUtc, minuteEndUtc, deviceId}` in SQLite.
- Upload batches to `POST /api/v1/minutes` with signed heartbeats; retry with exponential backoff when offline; never double-send (idempotency key per event).
- Keep local audit trail for dispute resolution.

## 7. OAuth 2.0
- Flow: **Authorization Code + PKCE**, scopes `offline_access` (refresh token) + `openid profile`.
- Login via WebView2 against the IdP; callback through a private `http://127.0.0.1:{port}/callback` loopback (secure random port).
- Store tokens encrypted with **DPAPI (CurrentUser)**; auto-refresh before expiry; if refresh fails → re-login silently, then interactively.
- On logout: revoke tokens and clear local counter/session data.

## 8. Security / Anti-cheat
- Signed requests (token + device attestation id); server validates minute timestamps, session continuity, and plausibility (no >24 h of minutes/day without gaps).
- Server-side rules mirror client rules (lock/sleep gaps must break counting).
- Defense-in-depth only — the plan must state that absolute anti-fraud requires additional server-side heuristics later.

## 9. Testing / Acceptance Criteria
- **Unit tests:** epoch eligibility matrix (all combos of lock/sleep/taskbar-hidden/idle), priming logic, state machine, reporting queue.
- **Simulated hooks:** injectable system-event sources (no real input needed in tests).
- **Manual smoke tests:** minimize/restore taskbar, enable auto-hide, Win+L lock, sleep/resume, idle 60+ s, run on multi-monitor and different DPI.
- Acceptance: after 5 min priming + N minutes, exactly N events appear in the local store and match the server after sync.

## 10. Delivery Milestones (order for the agent)
1. Project scaffold + window overlays + taskbar detection (visible banner next to tray).
2. System state module + lock/sleep/idle detection with diagnostics UI.
3. Verification engine + state machine + unit tests.
4. Local persistence + reporting queue + mock server tests.
5. OAuth 2.0 + session/token management.
6. Ad rotation via API + banner click → app → website flow.
7. Packaging, installer, logging/telemetry, hardening pass.

## Confirmed Decisions
- **Idle threshold:** 60 s (input required at least once per 60 s for an epoch to stay eligible).
- **Re-priming:** yes, a full 5-minute re-prime is required after every interruption before minutes count again.
- **Monitors:** secondary monitors are supported — one banner overlay per taskbar; ≥ 1 confirmed-visible overlay keeps an epoch eligible.
- **Banner visibility gating:** banners are shown **only after login**; not logged in = no banners, no counting.
