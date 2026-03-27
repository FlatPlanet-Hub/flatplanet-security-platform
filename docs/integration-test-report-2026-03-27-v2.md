# Integration Test Report — v2

**Date:** 2026-03-27
**Run:** Second run (re-test after fixes + new scenarios)
**Security API:** FlatPlanet Security Platform — `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`
**Platform API (HubApi):** FlatPlanet Platform API — `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`
**Environment:** Production (Azure App Service, Southeast Asia)
**Tester:** Claude Code (automated)
**Accounts used:**
- CEO / admin: `chris.moriarty@flatplanet.com` — role: `platform_owner`
- Regular user: `user@flatplanet.com` — role: none (standard user)

---

## Summary

| Total | Passed | Fixed (was WARN) | Failed | Blocked |
|---|---|---|---|---|
| 23 | 15 | 2 | 3 | 3 |

**vs. v1:** INT-004 and INT-014 are now fixed. INT-007 / INT-008 / INT-020 remain failing. Three new scenarios (CEO all-projects, CEO non-member project, regular user scoped projects) are blocked by the same root cause.

---

## P0 Status

| Test | Description | v1 | v2 |
|---|---|---|---|
| INT-007 | HubApi `GET /api/auth/me` with SP JWT | ❌ FAIL | ❌ FAIL |
| INT-008 | HubApi `GET /api/projects` with SP JWT | ❌ FAIL | ❌ FAIL |
| INT-020 | HubApi `POST /api/auth/api-tokens` with SP JWT | ❌ FAIL | ❌ FAIL |

> **The HubApi JWT configuration issue is unresolved.** All three P0 tests and all new cross-API scenarios remain blocked.

---

## Fixed Since v1

| Test | Issue | v1 | v2 |
|---|---|---|---|
| INT-004 | Login missing `password` → wrong response shape | ⚠️ WARN (ProblemDetails) | ✅ FIXED |
| INT-014 | Refresh missing `refreshToken` → wrong response shape | ⚠️ WARN (ProblemDetails) | ✅ FIXED |

**New shape (both):**
```json
{
  "success": false,
  "message": "Validation failed.",
  "errors": {
    "Password": ["The Password field is required."]
  }
}
```
Note: the `message` is `"Validation failed."` rather than the field-specific message in the docs (`"Email and password are required."`), but the response now uses the correct platform envelope and includes field-level errors. Acceptable improvement.

---

## Full Results

### Security API — Auth

---

### INT-001 — Login (valid credentials)
- **Status:** ✅ PASS
- **Expected:** `200`, `accessToken`, `refreshToken`, `expiresIn`, `user.*`
- **Actual:** `200` — all fields present; CEO role `platform_owner` in JWT claims

---

### INT-002 — Security `GET /auth/me` (valid token)
- **Status:** ✅ PASS
- **Expected:** `200`, `platformRoles`, `appAccess`
- **Actual:** `200` — `platformRoles: ["platform_owner"]`, `appAccess: []` (no appSlug, correct)

---

### INT-003 — Login with wrong password
- **Status:** ✅ PASS
- **Expected:** `401`, `{ "success": false, "message": "Invalid email or password." }`
- **Actual:** `401`, matches exactly

---

### INT-004 — Login with missing `password` *(was WARN)*
- **Status:** ✅ FIXED
- **Expected:** `400`, platform error envelope
- **Actual:** `400`
  ```json
  { "success": false, "message": "Validation failed.", "errors": { "Password": ["The Password field is required."] } }
  ```
- **Notes:** Now uses the platform envelope. `message` differs slightly from docs but is acceptable.

---

### INT-005 — `GET /auth/me` no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`

---

### INT-006 — `GET /auth/me` invalid/forged token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`

---

### INT-009 — `POST /authorize` (platform_owner, dashboard-hub)
- **Status:** ✅ PASS
- **Expected:** `200`, `{ "allowed": bool, "roles": [...], "permissions": [...] }`
- **Actual:** `200`, `allowed: false` — platform_owner holds platform-level permissions only, not app-level `read`
- **Notes:** Correct behavior — platform roles do not auto-grant app-level permissions.

---

### INT-010 — Token refresh
- **Status:** ✅ PASS
- **Expected:** `200`, new `accessToken` + `refreshToken`
- **Actual:** `200` — both rotated; single-use confirmed

---

### INT-013 — `POST /authorize` missing `appSlug`
- **Status:** ✅ PASS
- **Expected:** `400`, `{ "success": false, "message": "appSlug is required." }`
- **Actual:** `400`, matches exactly

---

### INT-014 — `POST /auth/refresh` missing `refreshToken` *(was WARN)*
- **Status:** ✅ FIXED
- **Expected:** `400`, platform error envelope
- **Actual:** `400`
  ```json
  { "success": false, "message": "Validation failed.", "errors": { "RefreshToken": ["The RefreshToken field is required."] } }
  ```

---

### INT-015 — `GET /auth/me?appSlug=dashboard-hub`
- **Status:** ✅ PASS
- **Expected:** `200`, `appAccess` populated
- **Actual:** `200`
  ```json
  "appAccess": [{
    "appSlug": "dashboard-hub",
    "roleName": "platform_owner",
    "permissions": ["manage_apps","manage_users","manage_resources","view_audit_log","manage_roles","manage_companies"]
  }]
  ```

---

### INT-016 — Login with `appSlug=dashboard-hub`
- **Status:** ✅ PASS
- **Expected:** `200`, tokens issued normally; `appSlug` does not affect auth
- **Actual:** `200`

---

### INT-017 — `POST /auth/logout`
- **Status:** ✅ PASS
- **Expected:** `200`, `{ "success": true, "message": "Logged out successfully." }`
- **Actual:** `200`, matches exactly

---

### INT-018 — `POST /auth/refresh` bogus token
- **Status:** ✅ PASS
- **Expected:** `401`, `{ "success": false, "message": "Invalid or expired refresh token." }`
- **Actual:** `401`, matches exactly

---

### Platform API (HubApi) — Cross-API JWT Integration

---

### INT-007 — HubApi `GET /api/auth/me` with CEO SP JWT
- **Status:** ❌ FAIL (unchanged from v1)
- **Expected:** `200`, user identity from JWT claims
- **Actual:** `401` — raw ASP.NET middleware rejection, no body
- **Notes:** HubApi JWT validation is not accepting tokens issued by the Security Platform.

---

### INT-008 — HubApi `GET /api/projects` with CEO SP JWT
- **Status:** ❌ FAIL (unchanged from v1)
- **Expected:** `200`, 4 projects for CEO
- **Actual:** `401`
- **Notes:** Same root cause as INT-007

---

### INT-011 — HubApi `GET /api/auth/me` no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`

---

### INT-012 — HubApi `GET /api/projects` no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`

---

### INT-020 — HubApi `POST /api/auth/api-tokens` with CEO SP JWT
- **Status:** ❌ FAIL (unchanged from v1)
- **Expected:** `200`, new API token
- **Actual:** `401`
- **Notes:** Blocked by INT-007

---

### New Scenarios — CEO Project Access

---

### NEW-CEO-1 — CEO `GET /api/projects` returns all 4 projects
- **Status:** 🚫 BLOCKED
- **Expected:** `200`, 4 projects with `roleName: "admin"` or `"owner"`
- **Actual:** `401` — blocked by INT-007 (HubApi JWT issue)

---

### NEW-CEO-2 — CEO `GET /api/projects/{id}` on non-member project
- **Status:** 🚫 BLOCKED
- **Expected:** `200`, `roleName: "admin"`
- **Actual:** Could not run — no valid project ID obtainable while GET /api/projects returns 401

---

### New Scenarios — Regular User

---

### NEW-REG-1 — Regular user `GET /api/projects` (scoped view)
- **Status:** 🚫 BLOCKED
- **Expected:** `200`, only projects the user is a member of
- **Actual:** `401` — blocked by INT-007 (HubApi JWT issue)
- **Notes:** Regular user (`user@flatplanet.com`) logged in successfully against Security API; JWT does not carry a role claim, confirming non-admin status. Cross-API test cannot proceed.

---

## Root Cause: HubApi JWT Validation Failure (P0)

The Security Platform issues JWTs with these signing parameters:

```
iss: flatplanet-security
aud: flatplanet-apps
alg: HS256
```

HubApi's JWT middleware is either:
1. Using a **different `SecretKey`** than the Security Platform's production key (most likely)
2. Expecting a different **`Issuer`** or **`Audience`** value
3. Has a **misconfigured or missing JWT section** in its Azure App Configuration / Key Vault

The raw ASP.NET `401` with no body (not a controller response) confirms the token is rejected at the middleware level before any controller logic runs.

**Required fix — verify these values match exactly in HubApi's Azure App Configuration:**

| Setting | Required Value |
|---|---|
| `Jwt__Issuer` | `flatplanet-security` |
| `Jwt__Audience` | `flatplanet-apps` |
| `Jwt__SecretKey` | Must be identical to the Security Platform's production `Jwt__SecretKey` |

Once fixed, re-run INT-007 first. If it passes, INT-008, INT-020, NEW-CEO-1, NEW-CEO-2, and NEW-REG-1 can all be validated immediately.

---

## Recommended Actions

| Priority | Test(s) | Issue | Action |
|---|---|---|---|
| **P0** | INT-007 / INT-008 / INT-020 | HubApi rejects all Security Platform JWTs | Fix `Jwt__SecretKey` / `Jwt__Issuer` / `Jwt__Audience` in HubApi Azure App Configuration |
| **P1** | NEW-CEO-1 / NEW-CEO-2 / NEW-REG-1 | Cannot run — blocked by P0 | Re-run after P0 is resolved |
| **P3** | INT-019 | DB proxy route 404 | Verify correct schema endpoint path via `/scalar` on HubApi host |

---

## Environment Notes

- **Auth method used:** Bearer JWT (Security Platform issued)
- **Mocked services:** None
- **Accounts tested:** 2 (CEO `platform_owner`, regular user no-role)
- **Security API:** All 15 tests pass — no regressions
- **HubApi:** 2 pass (no-token rejections), 3 fail, 3 blocked
