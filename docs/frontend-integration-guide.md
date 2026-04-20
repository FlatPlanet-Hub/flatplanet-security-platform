# FlatPlanet — Frontend Integration Guide

**Audience:** Frontend developers
**Last updated:** 2026-04-20
**Verified against:** Security Platform v1.7.0 (updated) · Platform API (HubApi) v1.0.1
**Tested by:** Integration tester (Claude Code)

---

## What's New in This Version

> These are verified changes from integration testing — update your frontend accordingly.

### Security Platform v1.7.0 (2026-04-20)

| # | Change | Impact |
|---|---|---|
| 1 | TOTP MFA (authenticator app) replaces SMS OTP | Enrollment flow changed — users now scan a QR code. See Step 1c. |
| 2 | `POST /api/v1/mfa/totp/login-verify` — verify TOTP code at login | When `requiresMfa: true` and `mfaMethod: "totp"`, call this endpoint instead of the email-OTP verify |
| 3 | `POST /api/v1/mfa/totp/request-email-fallback` — email OTP fallback for TOTP users | When user can't access authenticator app — call this, then use email-OTP login-verify. See Step 1d. |
| 4 | `POST /api/v1/mfa/email-otp/login-verify` — email OTP login path | Separate endpoint for email OTP login (was `mfa/otp/login-verify`) |
| 5 | `POST /api/v1/mfa/email-otp/resend` — resend email OTP | Expose a "Resend code" button — it is safe and will always return `200` |
| 6 | `POST /api/v1/mfa/backup-codes/generate` — generate backup codes | Show in account settings. Prompt user to save before leaving the page. |
| 7 | `POST /api/v1/mfa/backup-code/login-verify` — last-resort backup code login | New fallback option at the MFA screen |
| 8 | `GET /api/v1/mfa/status` — user's MFA status | Use in account settings to show current MFA state |
| 9 | `mfaMethod` added to login response | Use to determine whether to show TOTP or email OTP challenge screen |
| 10 | `mfaEnrolmentPending` added to login response | `true` if the user must set up MFA before proceeding — redirect to enrollment |
| 11 | `POST /api/v1/admin/users/{userId}/force-reset-password` — admin-triggered password reset | Admin panel action — sends a reset email on behalf of the user. See Admin section. |
| 12 | Admin MFA management endpoints (disable, reset, set-method) | Admin panel — manage user MFA without user involvement. See Admin section. |
| 13 | `PATCH /api/v1/auth/me` — self-service name and email change | New profile update endpoint. Name change: no re-login. Email change: `requiresReLogin: true` — all sessions revoked. See Step 13. |
| 14 | `mfaEnrolmentPending: true` now returns a restricted enrollment `accessToken` | Use it to call `begin-enrol` and `verify-enrol`. Token is 10 min, no refresh, restricted to enrollment endpoints only. See Step 1 / Step 1c. |

### Security Platform v1.6.0 (2026-04-17)

| # | Change | Impact |
|---|---|---|
| 1 | Login response now includes `requiresMfa`, `challengeId`, `idleTimeoutMinutes` | Handle MFA challenge branch at login — see v1.7.0 for updated branching logic (`mfaMethod`) |
| 2 | ~~`POST /api/v1/mfa/enroll` — phone enroll + SMS OTP~~ | ⚠️ Superseded by v1.7.0 — use `POST /api/v1/mfa/totp/begin-enrol` + `verify-enrol` instead. See Step 1c. |
| 3 | ~~`POST /api/v1/mfa/otp/verify` — complete enrollment~~ | ⚠️ Superseded by v1.7.0 — use `POST /api/v1/mfa/totp/verify-enrol` instead. See Step 1c. |
| 4 | ~~`POST /api/v1/mfa/otp/login-verify` — complete MFA-gated login~~ | ⚠️ Superseded by v1.7.0 — use `totp/login-verify` or `email-otp/login-verify` depending on `mfaMethod`. See Step 1b. |
| 5 | `GET /api/v1/identity/verification/status` — user's own verification status | Use to gate features requiring identity verification |
| 6 | `POST /api/v1/auth/change-password` now rate limited: 5 req / 15 min per user | Show appropriate message on `429` |
| 7 | `POST /api/v1/auth/forgot-password` now rate limited: 3 req / 15 min per IP | Show appropriate message on `429` |
| 8 | `POST /api/v1/authorize` now returns `roles` and `permissions` alongside `allowed` | Already documented in guide — no change needed if you're only reading `allowed` |
| 9 | New `503` error code from SMS endpoints | Handle gracefully — show retry message |

### Security Platform v1.4.0 (2026-04-13)

| # | Change | Impact |
|---|---|---|
| 1 | `POST /api/v1/auth/change-password` added (FEAT-CP) | Authenticated users can change their password without admin help; all sessions revoked on success — redirect to login |
| 2 | `POST /api/v1/auth/forgot-password` added (FEAT-FP) | Initiates email-based reset; always returns 200 to prevent enumeration |
| 3 | `POST /api/v1/auth/reset-password` added (FEAT-FP) | Consumes single-use 15-min token and sets a new password; all sessions revoked |
| 4 | Password policy now enforced on all password-setting flows | Min 8 chars, uppercase, lowercase, digit, special char — surface policy to users before they submit |

### Platform API v1.0.1 (2026-04-07)

| # | Change | Impact |
|---|---|---|
| 1 | Parameterized `@param` query binding fixed (BUG-1) | `POST /api/projects/{id}/query/read` and `/write` with `parameters` payload now work correctly for all types |
| 2 | `AlterOperationType` string enum fix (BUG-2) | `PUT /api/projects/{id}/migration/alter-table` now accepts camelCase string values (e.g. `"addColumn"`) without error |
| 3 | Schema names with digit-first suffix now accepted | Projects whose `schemaName` starts with a digit after `project_` (e.g. `project_03557ada`) no longer rejected |
| 4 | `CLAUDE-local.md` template improvements | Generated workspace file now includes session startup, project management, and SP sections by default |

### Platform API v1.0.0 (2026-04-06)

| # | Change | Impact |
|---|---|---|
| 1 | All project endpoints now include `projectType` and `authEnabled` fields | Required on `POST /api/projects` — determines enforced tech stack and SP auth integration |
| 2 | `GET /api/projects/{id}/claude-config/workspace` — new workspace endpoint | Returns `CLAUDE-local.md` content + scoped API token. Frontend downloads and saves locally — **never committed to repo** |
| 3 | Smart token management on workspace — existing token revoked before issuing new | No token accumulation; safe to call on every workspace refresh |
| 4 | `CLAUDE.md` is no longer auto-committed to the GitHub repo | The Claude brief is now `CLAUDE-local.md`, generated locally via the workspace endpoint |

### Platform API v0.9.0 (2026-04-01)

| # | Change | Impact |
|---|---|---|
| 1 | `GET /api/projects` and `GET /api/projects/{id}` now include a `github` object | Projects with a linked repo return `repoName`, `repoFullName`, `branch`, `repoLink` |
| 2 | `POST /api/projects` supports GitHub repo creation and linking | Pass a `github` object to create a new repo or link an existing one |
| 3 | `POST /api/projects/{id}/members` accepts `githubUsername` | Optionally adds the user as a GitHub collaborator at invite time |
| 4 | Valid project roles are `owner`, `developer`, `viewer` — **`admin` is not valid** | Do not use `admin` in role dropdowns — it will return `409` |
| 5 | `GET /api/projects/{id}` `ownerId`, `appSlug`, `schemaName` now return real values | These were previously silently null due to a DB mapping bug (now fixed) |
| 6 | Adding a member to a project auto-grants them `viewer` on `dashboard-hub` | New members can immediately access the dashboard without a separate grant |
| 7 | `POST /api/v1/authorize` with `view_projects` now returns `allowed: true` for `platform_owner` | The permission was previously missing from the DB |

---

## Overview

There are two backend services the frontend talks to:

| Service | What it does | Base URL |
|---|---|---|
| **Security Platform** | Login, logout, token refresh, user identity, authorization checks | `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net` |
| **Platform API (HubApi)** | Projects, members, API tokens, DB proxy | `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net` |

The frontend only logs in through the Security Platform. The JWT it issues is used directly as the bearer token for HubApi too. There is no separate HubApi login.

---

## Auth Flow

```
1.  POST  /api/v1/auth/login
      ├─ requiresMfa: false      → proceed to step 2
      ├─ mfaEnrolmentPending: true → redirect to MFA enrollment (Step 1c)
      ├─ requiresMfa: true, mfaMethod: "totp"      → POST /api/v1/mfa/totp/login-verify (userId + totpCode)
      │                                                  └─ can't access app? → request email fallback (Step 1d)
      └─ requiresMfa: true, mfaMethod: "email_otp" → POST /api/v1/mfa/email-otp/login-verify (challengeId + otpCode)
                                                          └─ 200 → tokens issued, proceed to step 2
2.  GET   /api/v1/auth/me                                 → get user profile + roles
3.  GET   /api/projects                                   → list user's projects           (HubApi)
4.  GET   /api/projects/{id}                              → get single project             (HubApi)
5.  GET   /api/projects/{id}/claude-config/workspace      → download CLAUDE-local.md       (HubApi)
6.  POST  /api/v1/auth/refresh                            → rotate tokens before expiry
7.  POST  /api/v1/auth/heartbeat                          → keep session alive (every idleTimeoutMinutes × 0.5 min)
8.  POST  /api/v1/auth/logout                             → end session, clear tokens
```

Attach to every request:
```
Authorization: Bearer <accessToken>
Content-Type: application/json
```

---

## Step 1 — Login

**Endpoint:** `POST /api/v1/auth/login` on the Security Platform

**Request:**
```json
{
  "email": "chris.moriarty@flatplanet.com",
  "password": "••••••••",
  "appSlug": "dashboard-hub"
}
```

`appSlug` is optional. Pass `"dashboard-hub"` to get app-scoped permissions included in `/auth/me`.

**Response — no MFA enrolled:**
```json
{
  "success": true,
  "data": {
    "requiresMfa": false,
    "mfaMethod": null,
    "mfaEnrolmentPending": false,
    "mfaEnrolled": false,
    "challengeId": null,
    "accessToken": "eyJhbGci...",
    "refreshToken": "TUmzgLj...",
    "expiresIn": 3600,
    "idleTimeoutMinutes": 30,
    "user": {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "email": "chris.moriarty@flatplanet.com",
      "fullName": "Chris Moriarty",
      "companyId": "a5af2cfc-2887-4e60-942d-8c29ccf012cf"
    }
  }
}
```

**Response — MFA required (`requiresMfa: true`):**
```json
{
  "success": true,
  "data": {
    "requiresMfa": true,
    "mfaMethod": "totp",
    "mfaEnrolmentPending": false,
    "mfaEnrolled": false,
    "challengeId": null,
    "accessToken": "",
    "refreshToken": "",
    "expiresIn": 0,
    "idleTimeoutMinutes": 0,
    "user": { ... }
  }
}
```

When `requiresMfa` is `true`: no tokens are issued yet.

- If `mfaMethod: "totp"` — `challengeId` is `null`. Show a TOTP input. See Step 1b Branch A.
- If `mfaMethod: "email_otp"` — `challengeId` is populated. Show an email OTP input. See Step 1b Branch B.

**Response — MFA enrollment pending (`mfaEnrolmentPending: true`):**
```json
{
  "success": true,
  "data": {
    "requiresMfa": false,
    "mfaMethod": "totp",
    "mfaEnrolmentPending": true,
    "mfaEnrolled": false,
    "challengeId": null,
    "accessToken": "eyJhbGci...",
    "refreshToken": "",
    "expiresIn": 600,
    "idleTimeoutMinutes": 10,
    "user": {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "email": "alice@acme.com",
      "fullName": "Alice Chen",
      "companyId": "a5af2cfc-2887-4e60-942d-8c29ccf012cf"
    }
  }
}
```

When `mfaEnrolmentPending` is `true`: MFA is required on this account but the user hasn't set it up yet. The `accessToken` is a **restricted enrollment-only token** — valid for 10 minutes, no refresh token.

- Use this token to call `POST /api/v1/mfa/totp/begin-enrol` and `POST /api/v1/mfa/totp/verify-enrol`. See Step 1c.
- This token is **not** a full session token — calling any other `[Authorize]` endpoint returns `403`.
- If the user wants to cancel: call `POST /api/v1/auth/logout` with this token, then redirect to login.
- Always guard against `mfaEnrolmentPending: true` with an empty `accessToken` (defensive): if `accessToken` is blank, show an error and redirect back to login — do not attempt enrollment.

**`idleTimeoutMinutes`** — use this value to schedule your heartbeat interval: fire `POST /api/v1/auth/heartbeat` every `idleTimeoutMinutes × 0.5` minutes to keep the session alive. Default is `30` minutes, so send a heartbeat every 15 minutes.

**Error cases:**

| HTTP | Meaning |
|---|---|
| `400` | Missing email or password |
| `401` | Wrong credentials |
| `403` | Account or company is suspended |
| `423` | Account temporarily locked |
| `429` | Rate limited |

---

## Step 1b — MFA Login Verify (only when `requiresMfa: true`)

Branch on `mfaMethod` from the login response.

---

### Branch A — TOTP (`mfaMethod: "totp"`)

**Endpoint:** `POST /api/v1/mfa/totp/login-verify`

**Auth required:** No

Show a code input screen. The user opens their authenticator app (Microsoft Authenticator, Google Authenticator) to get the current 6-digit code.

**Request:**
```json
{
  "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
  "totpCode": "483921"
}
```

`userId` is from `user.userId` in the login response. `totpCode` is what the user types in.

**Response `200`:** Standard `LoginResponse` with `requiresMfa: false` and tokens populated. Store and use normally.

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `422` | Invalid or expired OTP. | Show inline error — let user retry. |
| `400` | *(validation message)* | Missing field. |

**Frontend notes:**
- Show a "Can't access your authenticator app?" link that triggers the TOTP email fallback flow. See Step 1d.
- Show a "Use a backup code" link as a last resort. See Step 1e.

---

### Branch B — Email OTP (`mfaMethod: "email_otp"`)

**Endpoint:** `POST /api/v1/mfa/email-otp/login-verify`

**Auth required:** No

Show a code input screen. The user receives a one-time code by email.

**Request:**
```json
{
  "challengeId": "b3d4e5f6-0000-0000-0000-000000000001",
  "otpCode": "483921"
}
```

`challengeId` is from the login response. `otpCode` is what the user types in.

**Response `200`:** Standard `LoginResponse` with `requiresMfa: false` and tokens populated. Store and use normally.

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `422` | Invalid or expired OTP. | Show inline error — let user retry. |
| `400` | *(validation message)* | Missing field. |

**Frontend notes:**
- The challenge expires after 5 minutes. If the user takes too long, show "Code expired — please log in again" and redirect to login.
- Expose a **"Resend code"** button — call `POST /api/v1/mfa/email-otp/resend` with `{ "userId": "..." }`. On `200`, update the `challengeId` in state and show "Code resent."
- Show a "Use a backup code" link as a last resort. See Step 1e.

---

## Step 1c — TOTP Enrollment (account settings)

Place the enrollment flow in account settings. Triggered automatically if `mfaEnrolmentPending: true` at login. Two calls: begin-enrol (get QR code) → verify-enrol (confirm first code).

**Step 1 — Get QR Code**

`POST /api/v1/mfa/totp/begin-enrol` — Auth required

No request body.

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "qrCodeUri": "otpauth://totp/FlatPlanet%20Security%20Platform:alice@acme.com?secret=BASE32SECRET&issuer=FlatPlanet%20Security%20Platform"
  }
}
```

Render `qrCodeUri` as a QR code image (use a client-side QR code library). Also show the raw URI as a "Can't scan? Enter manually" option. The user opens their authenticator app, scans the code, and proceeds to Step 2.

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | *(validation message)* | User already enrolled |
| `401` | — | Session expired |

**Step 2 — Verify First Code**

`POST /api/v1/mfa/totp/verify-enrol` — Auth required

**Request:**
```json
{
  "totpCode": "483921"
}
```

**Response `200`:** Standard `LoginResponse` with `requiresMfa: false`, `mfaEnrolled: true`, and tokens populated. Store tokens and proceed — the user is now logged in.

After success, prompt the user to **generate backup codes** (Step 1e). They will need these if they lose access to their authenticator app.

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `422` | Invalid or expired OTP. | Wrong code or clock skew — show inline error, let user retry |
| `401` | — | Session expired |

---

## Step 1d — TOTP Email Fallback (can't access authenticator app)

When a TOTP user cannot access their authenticator app, they can receive a one-time code by email instead. Two calls: request-email-fallback → email-otp/login-verify.

Trigger this flow when the user clicks "Can't access your authenticator app?" on the TOTP login screen.

**Step 1 — Request fallback code**

`POST /api/v1/mfa/totp/request-email-fallback` — No auth

**Request:**
```json
{
  "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9"
}
```

`userId` from the login response.

**Response `200` (always):**
```json
{
  "success": true,
  "data": {
    "challengeId": "b3d4e5f6-0000-0000-0000-000000000001"
  }
}
```

Always show the same confirmation regardless of the userId — the response never reveals whether the user exists.

**Step 2 — Verify email OTP**

Proceed with `POST /api/v1/mfa/email-otp/login-verify` using the returned `challengeId`. See Branch B in Step 1b.

---

## Step 1e — Backup Code Login (last resort)

When the user cannot access their authenticator app and has no email access, they can use a pre-generated backup code. Each code is 10 characters and single-use.

Trigger this flow when the user clicks "Use a backup code" on the MFA login screen.

**Endpoint:** `POST /api/v1/mfa/backup-code/login-verify` — No auth

**Request:**
```json
{
  "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
  "backupCode": "A1B2C3D4E5"
}
```

**Response `200`:** Standard `LoginResponse` with tokens. After login, recommend the user generates new backup codes (Step 1f).

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `422` | Invalid or expired OTP. | Code not found or already used — show error |

---

## Step 1f — Generate Backup Codes (account settings)

Allow users to generate backup codes from their account settings. Useful after enrollment or after using all remaining codes.

**Endpoint:** `POST /api/v1/mfa/backup-codes/generate` — Auth required

No request body.

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "codes": [
      "A1B2C3D4E5",
      "F6G7H8I9J0",
      "K1L2M3N4O5",
      "P6Q7R8S9T0",
      "U1V2W3X4Y5",
      "Z6A7B8C9D0",
      "E1F2G3H4I5",
      "J6K7L8M9N0"
    ],
    "count": 8
  }
}
```

**Frontend implementation notes:**
- **Show codes once only** — display on screen and prompt the user to save them before navigating away. They cannot be retrieved again.
- Calling this endpoint **invalidates any previously generated codes** — warn the user before they confirm.
- Offer a "Copy all" button and a "Download as text file" option.
- After saving, the user can see how many codes remain via `GET /api/v1/mfa/status` (`backupCodesRemaining`).

---

## Step 2 — Check Authorization (Dashboard Gate)

Before rendering the dashboard, check if the user has access:

**Endpoint:** `POST /api/v1/authorize` on the Security Platform

**Request:**
```json
{
  "appSlug": "dashboard-hub",
  "resourceIdentifier": "/dashboard",
  "requiredPermission": "view_projects"
}
```

**Response — allowed:**
```json
{
  "success": true,
  "data": {
    "allowed": true,
    "roles": ["platform_owner"],
    "permissions": ["manage_apps", "manage_users", "view_audit_log", "view_projects", "manage_resources", "manage_roles", "manage_companies"]
  }
}
```

**Response — denied:**
```json
{
  "success": true,
  "data": {
    "allowed": false,
    "roles": [],
    "permissions": []
  }
}
```

> `allowed: false` with HTTP `200` is normal — it means the user doesn't have that permission, not an error. Redirect to a no-access page.

**Who can access the dashboard:**

| Role | `allowed` |
|---|---|
| `platform_owner` | ✅ Yes — has `view_projects` |
| Regular user (Viewer on dashboard-hub) | ✅ Yes — has `view_projects` |
| User with no dashboard-hub role | ❌ No |

---

## Step 3 — Projects

### List projects

`GET /api/projects` on HubApi

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": "f56c5da7-760b-4273-a766-6bf90089d268",
      "name": "FlatPlanet Development Hub (Frontend)",
      "description": "Hub frontend",
      "schemaName": "project_hub_fe",
      "ownerId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "appSlug": "fp-development-hub",
      "roleName": "owner",
      "techStack": "Next.js",
      "projectType": "frontend",
      "authEnabled": false,
      "isActive": true,
      "createdAt": "2026-03-27T00:55:39Z",
      "github": {
        "repoName": "fp-development-hub",
        "repoFullName": "FlatPlanet-Hub/fp-development-hub",
        "branch": "main",
        "repoLink": "https://github.com/FlatPlanet-Hub/fp-development-hub"
      },
      "members": null
    }
  ]
}
```

> `github` is `null` when no repo is linked. `members` is always `null` on list — use `GET /api/projects/{id}/members` to fetch members.

**Who sees what:**
- Regular users see only their own projects
- Users with `view_all_projects` permission on `dashboard-hub` see all projects (their `roleName` shows as `"admin"` for projects they're not explicitly a member of)

---

### Create project

`POST /api/projects` on HubApi

**Request — no GitHub:**
```json
{
  "name": "My New Project",
  "description": "Optional description",
  "techStack": "React",
  "projectType": "frontend",
  "authEnabled": false
}
```

**Request — create new GitHub repo:**
```json
{
  "name": "My New Project",
  "techStack": "React",
  "projectType": "fullstack",
  "authEnabled": true,
  "github": {
    "createRepo": true,
    "repoName": "my-new-project"
  }
}
```

**Request — link existing GitHub repo:**
```json
{
  "name": "My New Project",
  "techStack": "React",
  "projectType": "backend",
  "authEnabled": false,
  "github": {
    "createRepo": false,
    "existingRepoUrl": "https://github.com/FlatPlanet-Hub/my-new-project"
  }
}
```

**`projectType` values:**

| Value | Stack enforced in CLAUDE-local.md |
|---|---|
| `frontend` | React.js + TypeScript → Netlify |
| `backend` | .NET 10 / C# → Azure App Service |
| `database` | Supabase / PostgreSQL |
| `fullstack` | Frontend + Backend + Database |

**`authEnabled`:** When `true`, the generated `CLAUDE-local.md` includes the FlatPlanet Security Platform auth integration guide. Set to `false` until the project is ready to integrate auth.

**Response `201`:**
```json
{
  "success": true,
  "data": {
    "id": "20721609-cb5e-41e2-8d16-395d49d4cfdd",
    "name": "My New Project",
    "schemaName": "project_68996f32",
    "ownerId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
    "appSlug": "my-new-project",
    "roleName": "owner",
    "techStack": "React",
    "projectType": "frontend",
    "authEnabled": false,
    "isActive": true,
    "createdAt": "2026-04-01T01:59:27Z",
    "github": {
      "repoName": "my-new-project",
      "repoFullName": "FlatPlanet-Hub/my-new-project",
      "branch": "main",
      "repoLink": "https://github.com/FlatPlanet-Hub/my-new-project"
    }
  }
}
```

`github` is `null` in the response when no GitHub configuration was provided.

**Notes:**
- Creation takes 2–4 seconds — ~19 Security Platform calls happen in sequence
- `CLAUDE-local.md` is **not** committed to the repo — the developer generates it locally after project creation via the workspace endpoint (see Step 5 below)
- On `409` the SP error message is included in the response body — surface it to the user

**Error cases:**

| HTTP | Meaning |
|---|---|
| `400` | `name` is missing or `company_id` claim absent |
| `401` | Missing or invalid JWT |
| `409` | Slug already taken or SP error (message in body) |

---

### Get single project

`GET /api/projects/{id}` on HubApi

**Response `200`:** Same shape as list item above (including `github` object).

| HTTP | Meaning |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User doesn't have access to this project |
| `404` | Project not found |

---

## Step 4 — Project Members

### List members

`GET /api/projects/{id}/members` on HubApi

**Response `200`:**
```json
{
  "success": true,
  "data": [
    {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "fullName": "Chris Moriarty",
      "email": "chris.moriarty@flatplanet.com",
      "githubUsername": null,
      "roleName": "owner",
      "permissions": ["read", "write", "ddl", "manage_members", "delete_project"],
      "grantedAt": "2026-03-27T00:55:39Z"
    }
  ]
}
```

---

### Add member

`POST /api/projects/{id}/members` on HubApi

**Request:**
```json
{
  "userId": "f21b0f18-deb1-4d1f-a0c3-f193c3d049b2",
  "role": "developer",
  "githubUsername": "john-dev"
}
```

> `githubUsername` is optional. If provided, the user is invited as a GitHub repo collaborator (`owner` → admin, `developer` → push, `viewer` → pull). This is the **only time** GitHub access is granted — there is no endpoint to add GitHub access later without removing and re-adding the member.

**Valid roles:** `owner` | `developer` | `viewer` — do not use `admin`

**Response `200`:**
```json
{
  "success": true
}
```

**Key behaviour:**
- After granting the project role, HubApi automatically grants the user `viewer` on `dashboard-hub` if they don't already have a role there. This means new members can access the dashboard immediately.
- The user must already exist in the Security Platform. This endpoint does not create users.
- Re-inviting a previously removed user works (upsert — no `409`).

| HTTP | Meaning |
|---|---|
| `403` | Caller lacks `manage_members` on this project |
| `409` | Project not registered with Security Platform (missing `appSlug`) |

**Who can add members:**

| Role | Can add? |
|---|---|
| `platform_owner` | ✅ Yes |
| Project `owner` | ✅ Yes |
| Project `developer` / `viewer` | ❌ No — `403` |
| User with no project role | ❌ No — `403` |

---

### Update member role

`PUT /api/projects/{id}/members/{userId}/role` on HubApi

**Request:**
```json
{
  "role": "viewer"
}
```

**Response `200`:** `{ "success": true }`

> GitHub repo permissions are **not updated** on role change. Only Security Platform role changes.

---

### Remove member

`DELETE /api/projects/{id}/members/{userId}` on HubApi

**Response `200`:** `{ "success": true }`

> API tokens held by the removed user for this project are immediately revoked. GitHub collaborator access is not removed.

---

## Step 5 — Workspace File (CLAUDE-local.md)

After a project is created (or whenever the developer needs to refresh their local Claude setup), the frontend downloads the `CLAUDE-local.md` workspace file and saves it to the developer's local project folder.

**Endpoint:** `GET /api/projects/{id}/claude-config/workspace` on HubApi

**Auth:** Security Platform JWT (same bearer token used everywhere)

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "filename": "CLAUDE-local.md",
    "gitignoreEntry": "CLAUDE-local.md",
    "content": "# My New Project — Claude Code Workspace\n\n...(full file content)...",
    "tokenId": "a3f1b2c4-...",
    "expiresAt": "2026-05-06T00:00:00Z"
  }
}
```

**What `content` contains (auto-generated by HubApi):**
- Project name, type, and auth status
- Live scoped API token (valid 30 days) + expiry date
- HubApi base URL and DB proxy endpoint reference
- Enforced tech stack standards for the project's `projectType`
- Security Platform auth integration guide (only when `authEnabled = true`)
- FlatPlanet Standards URL

**Frontend implementation:**

1. Call the workspace endpoint after project creation (or on demand from a "Download Workspace File" button)
2. Save the returned `content` to the developer's local machine as `CLAUDE-local.md` (see saving options below)
3. **Never upload or store this file server-side** — it contains a live API token
4. Show the `gitignoreEntry` value and remind the developer to add `CLAUDE-local.md` to `.gitignore`
5. Show `expiresAt` so the developer knows when to regenerate

**Smart token behaviour:**
- If the project already has an active token, it is **revoked** before a new one is issued
- Safe to call on every workspace refresh — no token accumulation
- To force-regenerate: `POST /api/projects/{id}/claude-config/regenerate`

---

### How to save CLAUDE-local.md to the developer's machine

Browsers cannot write to arbitrary folders without user interaction. Use **Option B** as primary with **Option A** as fallback.

#### Option B — File System Access API (recommended, Chrome/Edge only)

Opens the native OS save dialog so the developer can navigate directly to their project folder and save the file there.

```js
async function saveWorkspaceFile(content) {
  try {
    const fileHandle = await window.showSaveFilePicker({
      suggestedName: 'CLAUDE-local.md',
      types: [{ description: 'Markdown', accept: { 'text/markdown': ['.md'] } }],
    });
    const writable = await fileHandle.createWritable();
    await writable.write(content);
    await writable.close();
  } catch (err) {
    if (err.name !== 'AbortError') throw err; // user cancelled — do nothing
  }
}
```

> Requires HTTPS (Netlify ✅). Not supported in Firefox or Safari — fall back to Option A for those.

#### Option A — Browser download (fallback, all browsers)

Triggers a standard browser download. File lands in the user's **Downloads folder** — they must move it to their project root manually.

```js
function downloadWorkspaceFile(content) {
  const blob = new Blob([content], { type: 'text/markdown' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'CLAUDE-local.md';
  a.click();
  URL.revokeObjectURL(url);
}
```

#### Combined implementation (B with A as fallback)

```js
async function handleDownloadWorkspace(projectId) {
  const res = await fetch(
    `${HUBAPI_BASE_URL}/api/projects/${projectId}/claude-config/workspace`,
    { headers: { Authorization: `Bearer ${accessToken}` } }
  );
  const { data } = await res.json();

  if ('showSaveFilePicker' in window) {
    await saveWorkspaceFile(data.content);   // Option B — native save dialog
  } else {
    downloadWorkspaceFile(data.content);     // Option A — Downloads folder fallback
  }
}
```

**Recommended UI:**
- Button label: **"Download Workspace File"**
- After save: show a reminder banner:
  > `CLAUDE-local.md` saved. Make sure `CLAUDE-local.md` is in your project's `.gitignore`. Token expires **{expiresAt}**.

---

| HTTP | Meaning |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User doesn't have access to this project |
| `404` | Project not found |

---

## Step 6 — Token Refresh

**Endpoint:** `POST /api/v1/auth/refresh` on the Security Platform

**Request:**
```json
{
  "refreshToken": "TUmzgLj..."
}
```

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci... (new)",
    "refreshToken": "9QzCQ... (new)",
    "expiresIn": 3600
  }
}
```

- Refresh token is **single-use** — store the new one immediately
- On `401` from refresh → redirect to login page

**401 handling pattern:**
```
401 received
  ├─ Try POST /api/v1/auth/refresh
  │     ├─ 200 → store new tokens, retry original request
  │     └─ 401 → redirect to login page
  └─ If no refresh token stored → redirect to login page
```

---

## Step 7 — Logout

`POST /api/v1/auth/logout` on the Security Platform

Revokes all refresh tokens and ends the session. Clear both tokens and redirect to login.

**Response `200`:** `{ "success": true, "message": "Logged out successfully." }`

---

## Step 8 — Change Password *(user profile / account settings page)*

**Endpoint:** `POST /api/v1/auth/change-password` on the Security Platform

**Auth required:** Yes — include the current access token in the `Authorization` header.

**Request:**
```json
{
  "currentPassword": "OldP@ss1!",
  "newPassword": "NewP@ss2!",
  "confirmPassword": "NewP@ss2!"
}
```

**Response `200`:**
```json
{ "success": true, "message": "Password changed. Please log in again." }
```

**Password policy** (enforce client-side before submitting):

| Rule | Requirement |
|---|---|
| Minimum length | 8 characters |
| Uppercase | At least one A–Z |
| Lowercase | At least one a–z |
| Digit | At least one 0–9 |
| Special character | At least one of `!@#$%^&*()_+-=[]{}|;':",./<>?` |

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | Current password is incorrect. | Show inline error on the current-password field |
| `400` | New password must be different from the current password. | Show inline error on the new-password field |
| `400` | Passwords do not match. | Show inline error on confirm-password field |
| `400` | *(policy message)* | Show password requirements hint |
| `401` | — | Session expired — redirect to login |
| `429` | — | Rate limited (5 per 15 min per user) — show "Too many attempts, please try again later" |

**Frontend implementation notes:**

- On `200`, immediately clear both tokens from storage and redirect to the login page. All sessions have been revoked server-side.
- Validate the password policy client-side to give the user immediate feedback before the request is sent.
- Do not expose the current password field error message verbatim to help prevent brute-force probing of the form.

---

## Step 9 — Forgot Password *(login page)* 

**Endpoint:** `POST /api/v1/auth/forgot-password` on the Security Platform

**Auth required:** No

**Request:**
```json
{
  "email": "alice@acme.com"
}
```

**Response `200` (always):**
```json
{ "success": true, "message": "If that email exists, a reset link has been sent." }
```

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | *(validation message)* | Missing or invalid email format |
| `429` | — | Rate limited (3 per 15 min per IP) — show "Too many attempts, please try again later" |

**Frontend implementation notes:**

- Always show the same confirmation message regardless of the response — do not indicate whether the email was found.
- The reset link in the email points to `{BaseUrl}/reset-password?token={rawToken}`. Your reset-password page must read the `token` query parameter and pass it to the reset endpoint.
- The token expires in **15 minutes** — surface this to the user on the confirmation screen ("Check your inbox — the link expires in 15 minutes").

---

## Step 10 — Reset Password *(email link landing page)*

**Endpoint:** `POST /api/v1/auth/reset-password` on the Security Platform

**Auth required:** No

The user lands on your `/reset-password?token=...` page from the email link. Read the `token` from the URL and submit it with the new password.

**Request:**
```json
{
  "token": "a3f1b2c4d5e6...",
  "newPassword": "NewP@ss2!",
  "confirmPassword": "NewP@ss2!"
}
```

**Response `200`:**
```json
{ "success": true, "message": "Password reset successfully. Please log in." }
```

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | Reset token is invalid or has expired. | Show error and prompt user to request a new link |
| `400` | New password must be different from the current password. | Show inline error on new-password field |
| `400` | Passwords do not match. | Show inline error on confirm-password field |
| `400` | *(policy message)* | Show password requirements hint |

**Frontend implementation notes:**

- Apply the same password policy validation client-side as in Step 8.
- On `200`, redirect to the login page with a success banner: "Password reset successfully. Please log in."
- On `400` with "invalid or expired", redirect to the forgot-password page so the user can request a fresh link. The used or expired token cannot be retried.
- The token is extracted from the URL as-is — do not decode or transform it before sending.

---

## Step 11 — Heartbeat (keep session alive)

**Endpoint:** `POST /api/v1/auth/heartbeat` on the Security Platform

**Auth required:** Yes

Fire this on an interval of `idleTimeoutMinutes × 0.5` minutes (returned at login). With the default `idleTimeoutMinutes: 30`, send a heartbeat every **15 minutes**.

**Response `200`:**
```json
{ "success": true, "data": { "sessionActive": true } }
```

**On `401`:** Session has expired. Stop the interval, clear tokens, redirect to login. Do not attempt a token refresh — the session is already gone.

---

## Step 12 — Identity Verification Status

**Endpoint:** `GET /api/v1/identity/verification/status` on the Security Platform

**Auth required:** Yes

Use this to gate features that require identity verification (e.g. certain financial operations or sensitive data access).

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "otpVerified": true,
    "videoVerified": false,
    "fullyVerified": true,
    "verifiedAt": "2026-04-17T10:20:00Z"
  }
}
```

| Field | Notes |
|---|---|
| `otpVerified` | User completed MFA enrollment (OTP verified). |
| `videoVerified` | Video verification complete. Always `false` — not yet implemented. |
| `fullyVerified` | The value to gate on. Recomputed server-side from current config on every call. |
| `verifiedAt` | When the user first reached `fullyVerified: true`. `null` if not yet verified. |

---

## Admin — MFA Management *(admin panel)*

These endpoints require `platform_owner` or `app_admin` role. Use them in the admin user management panel.

### Disable MFA for a user

`POST /api/v1/admin/mfa/{userId}/disable`

Disables MFA entirely. The user can log in with password only until they re-enroll.

**Response `200`:** `{ "success": true, "message": "MFA disabled." }`

---

### Reset MFA for a user

`POST /api/v1/admin/mfa/{userId}/reset`

Clears the TOTP secret, backup codes, and MFA method. The user must re-enroll on their next login. Use when a user loses their authenticator device and email access.

**Response `200`:** `{ "success": true, "message": "MFA reset." }`

---

### Change a user's MFA method

`POST /api/v1/admin/mfa/{userId}/set-method`

**Request:**
```json
{ "method": "email_otp" }
```

Valid values: `"email_otp"` | `"totp"`

**Response `200`:** `{ "success": true, "message": "MFA method updated." }`

---

## Admin — Force Reset Password *(admin panel)*

`POST /api/v1/admin/users/{userId}/force-reset-password`

Sends a password reset email on behalf of the user. Use this in the admin panel when a user is locked out, requests help, or their account needs a password reset without them initiating it themselves.

**Auth required:** Yes — `platform_owner` or `app_admin`

**Request:**
```json
{
  "appSlug": "dashboard-hub"
}
```

**Response `200`:**
```json
{
  "success": true,
  "data": { "message": "Password reset email sent." }
}
```

The user receives a standard password reset email. The reset link expires in 15 minutes.

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | App not found. | Check `appSlug` — must match a registered app |
| `404` | Resource not found. | User not found |

> **Note on password actions by placement:**
> - **Login page** — Forgot Password (`POST /auth/forgot-password`): user-initiated, no login required
> - **User profile / settings** — Change Password (`POST /auth/change-password`): authenticated user changes their own password  
> - **Admin panel** — Force Reset Password (`POST /admin/users/{id}/force-reset-password`): admin sends reset email on behalf of user

---

## Step 13 — Update Profile *(user profile / account settings page)*

Allows an authenticated user to change their own display name and/or email address. Both fields are optional — send only what is changing.

**Endpoint:** `PATCH /api/v1/auth/me` on the Security Platform

**Auth required:** Yes — include the current access token in the `Authorization` header.

**Rate limit:** 10 requests per 15 minutes per user.

**Request (name change only):**
```json
{
  "fullName": "Alice Chen-Park"
}
```

**Request (email change only):**
```json
{
  "email": "alice.new@acme.com"
}
```

**Request (both):**
```json
{
  "fullName": "Alice Chen-Park",
  "email": "alice.new@acme.com"
}
```

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "fullName": "Alice Chen-Park",
    "email": "alice.new@acme.com",
    "requiresReLogin": true
  }
}
```

| Field | Notes |
|---|---|
| `fullName` | Updated display name. |
| `email` | Updated email (or existing if unchanged). |
| `requiresReLogin` | `true` if email was changed — all sessions revoked. Clear tokens and redirect to login. |

**Error cases:**

| HTTP | Message | Action |
|---|---|---|
| `400` | At least one field (fullName or email) must be provided. | Empty request body. |
| `400` | *(validation message)* | Name too long, or invalid email format. |
| `401` | — | Session expired — redirect to login. |
| `409` | Email is already in use. | Show inline error on the email field. |
| `429` | — | Rate limited (10 per 15 min per user). |

**Frontend implementation notes:**

- **Name change** (`requiresReLogin: false`): update the displayed name in the UI — the session remains valid, no re-login required.
- **Email change** (`requiresReLogin: true`): immediately clear both tokens from storage and redirect to the login page. All sessions have been revoked server-side — the stored access token is now invalid.
- If both name and email are sent and the email is new, `requiresReLogin` is `true` regardless. Always check `requiresReLogin` on the response rather than inferring from the request.
- Validate email format client-side before submitting.

---

## Session Timeouts

| Timeout | Default | Triggered by |
|---|---|---|
| **Idle** | 30 minutes | No requests for 30 min → `401` |
| **Absolute** | 8 hours | Session older than 8 hours → `401` |

---

## Standard Response Envelope

**Success:**
```json
{ "success": true, "data": { ... } }
```

**Success (no data):**
```json
{ "success": true, "message": "Logged out successfully." }
```

**Error:**
```json
{ "success": false, "message": "Invalid email or password." }
```

**Validation error (400):**
```json
{
  "success": false,
  "message": "Validation failed.",
  "errors": {
    "Password": ["The Password field is required."]
  }
}
```

---

## Known Limitations

| # | Limitation | Workaround |
|---|---|---|
| 1 | `GET /api/v1/users` and `GET /api/v1/apps` require `platform_owner` or `app_admin` platform role | Project owners cannot browse the user list for invite — platform owner must do it |
| 2 | GitHub repo collaborator is not removed when a member is removed | Must be done manually on GitHub |
| 3 | GitHub repo permissions are not updated when a member's role changes | Re-add the member with the correct role if GitHub access must match |
| 4 | Legacy projects (created before GitHub integration) have empty `repoName` and `repoLink` in the `github` object even if `repoFullName` is populated | Use `repoFullName` to construct the link: `https://github.com/{repoFullName}` |

---

## Base URLs

```
Security Platform:  https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net
Platform API:       https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net
```

## Token Types

| Token | Issued by | Used for | Lifetime |
|---|---|---|---|
| **Security Platform JWT** | SP `/auth/login` | Frontend → Security Platform, Frontend → HubApi | 60 min |
| **HubApi API Token** | HubApi `/api/auth/api-tokens` | Claude Code → DB Proxy only | 30 days |

The frontend only ever uses the **Security Platform JWT**. Never use an HubApi API Token for frontend requests.
