# Taskbar Banner App

Windows desktop app that shows advertiser banners docked next to the system-tray
notification area on each visible taskbar, verifies the machine is genuinely
"watched", and accrues verified active minutes for reporting to a server.

See `plan.md` for the full product/technical plan. This repository currently
implements **Milestones 1-7**: banner overlay windows, taskbar detection,
system-state + idle monitoring, the verification engine, SQLite persistence
with upload/reporting, OAuth 2.0 login-gated sessions, API-driven ad rotation,
and packaging/logging/hardening. Windows 10 and Windows 11 are both supported;
the banner is docked immediately to the **left of the "Show hidden icons"
chevron** on each notification area.

## Prerequisites

- Windows 10 1809+ (per-monitor DPI aware)
- .NET 8 SDK (`dotnet --version`); the solution also builds with newer SDKs
  (`global.json` uses `rollForward: latestMajor`)

## Build, run & test

```powershell
dotnet build .\TaskbarBanner.sln
dotnet run --project .\src\TaskbarBanner.App
dotnet test .\tests\TaskbarBanner.Core.Tests
```

The diagnostics window shows taskbars/monitors, live system state, the engine
state machine (state / prime progress / counted minutes), and a bounded event
log with one entry per epoch. Banners appear next to the notification area of
every visible bottom/top-docked taskbar. Clicking a banner raises the
diagnostics window.

## What works now (Milestones 1-7)

- `src/TaskbarBanner.Core` (library; references the Windows Desktop framework
  only for `Microsoft.Win32.SystemEvents`)
  - `TaskbarService`: per-monitor taskbar detection via `Shell_TrayWnd` /
    `Shell_SecondaryTrayWnd`, dock edge, geometry-based auto-hide visibility,
    per-monitor DPI, work area.
  - `SystemStateMonitor` (`ISystemStateMonitor`): lock/unlock, console and
    remote session connect/disconnect, logon/logoff, suspend/resume and session
    ending via `SystemEvents`; exposes a `SystemStateSnapshot`
    (powered-on / unlocked / connected).
  - `ActivityMonitor` (`IActivityMonitor`): idle time via `GetLastInputInfo`
    (tick-wrap safe), approximate last-input wall-clock time, and a monotonic
    last-input tick counter used for activity-change detection.
  - `VerificationEngine`: explicit `VerificationState` state machine
    (`Idle` -> `Priming` -> `Counting`) over UTC-anchored 60 s epochs.
    - Fully eligible epoch = powered/unlocked/connected AND every banner-host
      taskbar visible without auto-hide AND >= 1 banner overlay confirmed
      on-screen AND idle <= 60 s AND an input change inside the epoch.
    - 5 consecutive eligible epochs prime before anything counts; any failing
      tick discards the whole epoch and forces a fresh 5-minute prime.
    - Epochs are only counted when fully observed from a minute boundary;
      sleep/gap jumps invalidate and recover on the next clean epoch.
    - Raises `EpochFinalized` (per-epoch audit log) and `MinuteVerified`
      (start/end UTC for reporting) events. Conditions are injectable via
      `IVerificationConditions`, time via `IClock`.
  - `Interop/NativeMethods`: Win32 P/Invoke surface.
- `src/TaskbarBanner.App` (WPF)
  - `BannerOverlayWindow`: borderless, topmost, `WS_EX_NOACTIVATE |
    WS_EX_TOOLWINDOW`, `ShowActivated=false` - never steals focus; click raises
    the main window via `SetForegroundWindow`.
  - `OverlayManager`: 600 ms reconcile loop; one overlay per taskbar-bearing
    monitor; hides/destroys overlays as taskbars disappear; positions re-derived
    continuously; exposes the banner/taskbar confirmation checks consumed by the
    engine (`IsAnyBannerConfirmedVisible`,
    `AreShownBannerTaskbarsEligible`).
  - `LiveVerificationConditions`: adapts `SystemStateMonitor`, `ActivityMonitor`
    and `OverlayManager` to the engine; engine ticks once per second.
  - `ReportService`: opens the SQLite store, generates the per-run session id,
    enqueues each `MinuteVerified` event, and runs a background upload pump.
  - `HttpReportApiClient`: posts batches to `POST /api/v1/minutes` (base URL
    from `TASKBARBANNER_API_BASE`, default `http://127.0.0.1:5099`).
  - `SampleAdSource`: rotating placeholder ads (API-driven ad module later).
  - Diagnostics main window: banner toggle, taskbar list, system state, engine
    state machine + per-epoch log, reporting queue status (pending/uploaded/
    retry delay) and upload attempt log.
- `src/TaskbarBanner.Core` (reporting) / `Microsoft.Data.Sqlite`
  - `SqliteMinuteStore` (`IMinuteStore`): local SQLite queue + audit trail
    (`report.db` in `%LOCALAPPDATA%\TaskbarBanner`); unique idempotency key per
    minute, persistent device id, attempt tracking, uploaded rows retained.
  - `MinuteUploader`: batches pending minutes, single-flight (never sends the
    same batch twice concurrently), exponential backoff on failure (5 s -> up
    to 5 min), marks uploaded only after server confirmation.
  - `ReportPayload` / `ReportPayloadJson`: canonical batch contract
    (`minutes[]` with `idempotencyKey/sessionId/deviceId/minuteStartUtc/
    minuteEndUtc`, camelCase).
- `src/TaskbarBanner.Core` (auth) - OAuth 2.0 Authorization Code + PKCE
  - `AuthSessionManager`: builds the authorize URL (`response_type=code`,
    `scope=openid profile offline_access`, `state`, `nonce`,
    `code_challenge=S256`); verifies `state` on the loopback callback,
    exchanges the code, auto-refreshes before expiry, handles
    `invalid_grant` (clear) vs transient errors (keep token, gate off), and
    revokes on logout.
  - `OAuthTokenClient`: token exchange / refresh / revocation over HTTP
    (form-encoded, typed `OAuthProtocolException` for server errors).
  - `DpapiTokenStore` (`ITokenStore`): tokens encrypted at rest with DPAPI
    (`CurrentUser`) in `%LOCALAPPDATA%\TaskbarBanner\auth.json`.
  - `Pkce`: RFC 7636 verifier/challenge generation (S256).
- `src/TaskbarBanner.App` (auth)
  - `AppAuthController`: ties auth state to the app - banners and counting are
    **gated on a successful sign-in** (`OverlayManager.SessionEnabled`);
    log out / session loss resets the reporting session (clears pending queue,
    new session id).
  - `LoginWindow`: WebView2 sign-in that intercepts the
    `http://127.0.0.1:{port}/callback` redirect (secure random port when no
    redirect URI is configured).
  - Config via env: `TASKBARBANNER_OAUTH_CLIENT_ID/AUTH_URL/TOKEN_URL/
    REDIRECT/REVOKE_URL/SCOPE`; dev smoke test via `TASKBARBANNER_DEV_AUTH=1`
    (demo sign-in without an IdP).
  - Diagnostics window shows auth status and sign in/out buttons; token refresh
    is attempted before expiry.
- `src/TaskbarBanner.App` (ads)
  - `RemoteAdSource`: pulls the current ad from `GET /api/v1/ads/current`
    (optional `Authorization: Bearer`), refreshes on the server-provided
    rotation interval (clamped 10-300 s), keeps the last good ad when offline,
    and caches it to disk (`ad-cache.json`) so a restart offline still shows the
    cached campaign instead of a placeholder.
  - `HttpAdsApiClient` + `AdDto`: parses
    `{brandName, headline, tagline, logoText, logoImageUrl, websiteUrl,
    durationSeconds}`.
  - Overlays render logo text, or the remote logo image when available (falling
    back to text offline); clicking a banner raises the main window, whose
    "Current ad" bar shows the details and an **Open website** button that
    launches the advertiser URL in the default browser (`ShellExecute`).
  - Ad rotation is re-fetched right after a successful sign-in.
- Positioning (Windows 10 & 11)
  - `NotificationAnchorLocator` resolves the notification-area left edge (the
    "Show hidden icons" chevron is its leftmost element) by trying, in order:
    the legacy `TrayNotifyWnd` child window (Windows 10, and wherever present)
    and a UI Automation search for the chevron button (used on Windows 11 where
    the tray is XAML-rendered). The banner's right edge is docked left of that
    anchor with a small margin, so it never covers the chevron or the icons.
  - Anchors are cached and re-resolved on taskbar movement/rotation; monitors
    with no notification area (e.g. Windows 10 secondary taskbars) dock the
    banner at the far taskbar end.
  - The diagnostics window shows the detected OS/build so Windows 10 vs 11
    layout differences can be checked at a glance.
- Hardening / ops
  - Upload requests can be signed: `X-Device-Id` always, plus HMAC-SHA256
    `X-Signature` over `timestamp + method + path + body-hash` with
    `X-Timestamp` when `TASKBARBANNER_SIGNING_SECRET` is set (server-side
    validation is a future step; §8 of plan.md).
  - `AppLog` writes a file audit trail to
    `%LOCALAPPDATA%\TaskbarBanner\events.log` mirroring diagnostics events.
  - Packaging: `scripts\publish.ps1` (single-file self-contained win-x64 build
    after tests) and `installer\TaskbarBanner.iss` (Inno Setup; Windows 10+).
- `tests/TaskbarBanner.Core.Tests` (xunit, injectable fakes - no real input or
  OS events needed):
  - eligibility matrix: locked session, taskbar auto-hide/hidden, banner
    missing, idle > 60 s, missing activity change;
  - priming logic (5 full eligible epochs), interruption => full re-prime,
    sleep-style time-gap recovery, UTC epoch anchoring, one count per epoch;
  - SQLite store: queue order, duplicate idempotency key ignored, device id
    stability, upload/attempt transitions, persistence across reopen;
  - uploader: success marks uploaded, retry keeps rows pending with growing
    backoff, retries resend identical idempotency keys, single-flight prevents
    concurrent double-send;
  - JSON payload contract shape;
  - auth: PKCE generation/challenge math, DPAPI token round-trip + delete,
    token-client request/response parsing incl. `invalid_grant`, and the
    session manager (login URL params without leaking the verifier, state
    mismatch rejection, code exchange, refresh on expiry, invalid-grant vs
    transient refresh handling, `NeedsRefresh` near expiry, revoke on logout);
  - ads: remote-source cache/fallback behaviour and HTTP ad parsing incl.
    rotation clamping and the bearer header;
  - hardening: HMAC request-signing determinism and the uploader's signature +
    device headers.

## Configuration (environment variables)

| Variable | Purpose | Default |
| --- | --- | --- |
| `TASKBARBANNER_API_BASE` | Base URL for minutes + ads endpoints | `http://127.0.0.1:5099` |
| `TASKBARBANNER_OAUTH_CLIENT_ID` | OAuth client id (enables browser sign-in) | empty |
| `TASKBARBANNER_OAUTH_AUTH_URL` | Authorization endpoint | empty |
| `TASKBARBANNER_OAUTH_TOKEN_URL` | Token endpoint | empty |
| `TASKBARBANNER_OAUTH_REDIRECT` | Fixed redirect URI (else random loopback port) | empty |
| `TASKBARBANNER_OAUTH_REVOKE_URL` | Revocation endpoint | empty |
| `TASKBARBANNER_OAUTH_SCOPE` | OAuth scopes | `openid profile offline_access` |
| `TASKBARBANNER_DEV_AUTH` | `1`/`true` - demo sign-in without an IdP | empty |
| `TASKBARBANNER_SIGNING_SECRET` | HMAC secret for signed upload requests | empty |
| `TASKBARBANNER_TRAY_RIGHT_OFFSET_PX` | Force banner right-edge offset from the taskbar right (px @96 DPI); QA override when anchor detection needs tuning | auto |

## Known limitations at this milestone

- Auto-hide on the **primary** taskbar is detected (no accrual while enabled);
  auto-hide mode on secondary taskbars is not directly detectable, so an
  auto-hidden secondary that is momentarily hovered-visible can look eligible.
- Vertically docked taskbars are detected but do not yet host a banner.
- Anchor detection: `TrayNotifyWnd` covers Windows 10 (and any legacy layout);
  on Windows 11 the tray is XAML-rendered so the chevron is located through UI
  Automation, which is tuned for the English "Show hidden icons" name /
  automation ids - on a non-English Windows 11 use
  `TASKBARBANNER_TRAY_RIGHT_OFFSET_PX` if the fallback gap is not exact.
- Banners and counting are gated on a signed-in session (OAuth or dev demo);
  without one the app shows no banners and accrues nothing.
- The engine samples conditions once per second, so an idle crossing at most
  ~2 s inside the 60 s boundary may be detected one epoch late; server-side
  validation (plan §8) remains the authoritative anti-fraud layer.
- The state monitor learns lock/sleep state only from transition events, so a
  session already locked/suspended at startup is initially reported as
  unlocked/powered.
- No real backend exists yet: with the default endpoint reachable, verified
  minutes upload and remain as an audit trail; while unreachable they stay
  `Pending` in SQLite and retry with exponential backoff (never double-sent).
- OAuth sign-in needs a configured IdP (env vars) or `TASKBARBANNER_DEV_AUTH=1`
  for a demo session; WebView2 runtime must be installed for browser sign-in.
  OIDC `id_token` signature/claims are not yet validated, and API requests are
  not yet signed - both are part of the hardening milestone.
