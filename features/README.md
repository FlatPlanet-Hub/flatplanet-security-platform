# Features — ISO 27001 Compliance Plan

## Architecture Principle

**MFA is enforced in SP, not in HubApi or any other app.**

SP is the single source of truth for identity. All MFA checks happen at login time —
by the time any app receives a JWT, SP has already guaranteed the user passed every
required check. No app (HubApi, future apps) needs to know about MFA internals.

---

## Sequence

```
FEAT-02 (migrations) ──┬──► FEAT-03 (SP admin audit log)      SP coder
                        ├──► FEAT-05 (MFA SMS OTP + login gate) SP coder
                        │         └──► FEAT-06 (verification)  SP coder
                        └──► FEAT-04 (HubApi audit log)        HubApi coder (parallel)

FEAT-01 (refresh token config) ── DevOps / Frontend — independent, no deploy needed
FEAT-08 (video verification)   ── FUTURE, plug in when business decides
```

---

## Feature Index

| Feature | File | Repo | Status |
|---|---|---|---|
| FEAT-01 | *(no file — Azure config only)* | Azure + Frontend | ⏳ Pending |
| FEAT-02 | [feat-02-schema-foundation.md](feat-02-schema-foundation.md) | SP | ⏳ Pending |
| FEAT-03 | [feat-03-admin-audit-log.md](feat-03-admin-audit-log.md) | SP | ⏳ Pending |
| FEAT-04 | *(in HubApi Agents/features/)* | HubApi | ⏳ Pending |
| FEAT-05 | [feat-05-mfa-sms-otp.md](feat-05-mfa-sms-otp.md) | SP | ⏳ Pending |
| FEAT-06 | [feat-06-identity-verification-status.md](feat-06-identity-verification-status.md) | SP | ⏳ Pending |
| FEAT-08 | *(not written — future)* | SP | 🔮 Future |

---

## FEAT-01 Instructions (no code needed)

**DevOps:**
- Azure Portal → SP App Service → Environment Variables
- Set `Jwt__RefreshTokenExpiryDays = 90`
- Restart SP App Service

**Frontend (after backend is stable):**
- Add `setInterval` every 50 minutes → `POST /api/v1/auth/refresh` → store new tokens
- This keeps the CEO dashboard session alive without re-login

---

## What HubApi Does and Does Not Do

| Concern | Who handles it | Notes |
|---|---|---|
| MFA enrollment | SP only | `POST /api/v1/mfa/enroll` |
| MFA at login | SP only | Login returns `requiresMfa: true` if enrolled |
| JWT validation | HubApi (and all apps) | Same as today — no changes needed |
| Admin audit log | HubApi (FEAT-04) | HubApi logs its own write operations |
| Identity verification status | SP only | `GET /api/v1/identity/verification/status` |

---

## SP Connection Pool — Apply Before Deploying Any Feature

SP's connection string must be tuned for Supabase PgBouncer (transaction mode) the same way HubApi was fixed. Without this, SP will hit the same stale-connection 500s under real traffic.

In `appsettings.json` (SP), ensure the connection string includes:
```
No Reset On Close=true;Minimum Pool Size=0;Maximum Pool Size=5
```

Do this before deploying FEAT-02. It is a config change only — no code changes needed.

---

## Key Decisions

| Decision | Value |
|---|---|
| Video required now? | No — `require_video_verification = false` in security_config |
| When to require video | Flip config to `true` when FEAT-08 is deployed |
| fully_verified logic | Computed in service layer (not DB generated column) |
| SMS provider (dev) | ConsoleSmsSender — prints to logs |
| SMS provider (prod) | TwilioSmsSender — configure via Azure env vars |
| MFA enforced where? | SP login gate only — no app-level enforcement |
