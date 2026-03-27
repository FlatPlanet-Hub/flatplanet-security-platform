# Integration Test Report

**Date:** 2026-03-27
**Security API:** FlatPlanet Security Platform — `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`
**Platform API (HubApi):** FlatPlanet Platform API — `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`
**Environment:** Production (Azure App Service, Southeast Asia)
**Tester:** Claude Code (automated)
**Test Account:** `chris.moriarty@flatplanet.com` — role: `platform_owner`

---

## Summary

| Total | Passed | Failed | Warnings |
|---|---|---|---|
| 20 | 15 | 2 | 3 |

---

## Failures & Recommended Actions

| Test ID | Issue | Recommended Fix |
|---|---|---|
| **INT-007 / INT-008 / INT-020** | **HubApi rejects all Security Platform JWTs with 401** | Check HubApi JWT validation config: `Issuer` must match `flatplanet-security`, `Audience` must match `flatplanet-apps`, and the `SecretKey` must match the Security Platform's signing key. Likely misconfigured in HubApi's `appsettings` or Azure App Config for the production deployment. |
| **INT-004 / INT-014** | 400 responses for model-binding failures use ASP.NET `ProblemDetails` format instead of the documented `{ "success": false, "message": "..." }` envelope | Add a custom `InvalidModelStateResponseFactory` in both APIs' `Program.cs` to transform validation errors into the platform envelope format |
| **INT-019** | `GET /api/schema/tables` returns 404 — route path may be incorrect | Verify the correct DB Proxy schema endpoint path in the deployed HubApi (check Scalar docs at `/scalar`) |

---

## Results

### Security API — Auth

---

### INT-001 — Happy Path: Security API login
- **Status:** ✅ PASS
- **Expected:** `200`, body contains `accessToken`, `refreshToken`, `expiresIn`, `user.userId`
- **Actual:** `200` — all fields present, `expiresIn: 3600`, role `platform_owner` embedded in JWT
- **Notes:** JWT carries correct claims: `sub`, `email`, `full_name`, `company_id`, `session_id`, `role`

---

### INT-002 — Security API `GET /auth/me` (valid token)
- **Status:** ✅ PASS
- **Expected:** `200`, body contains `userId`, `email`, `platformRoles`, `appAccess`
- **Actual:** `200` — `platformRoles: ["platform_owner"]`, `appAccess: []` (no appSlug provided, correct)
- **Notes:** Response envelope matches spec (`success: true, data: {...}`)

---

### INT-003 — Security API login with wrong password
- **Status:** ✅ PASS
- **Expected:** `401`, `{ "success": false, "message": "Invalid email or password." }`
- **Actual:** `401`, body matches exactly — intentionally vague message confirmed
- **Notes:** —

---

### INT-004 — Security API login with missing `password` field
- **Status:** ⚠️ WARN — 400 returned but wrong response shape
- **Expected:** `400`, `{ "success": false, "message": "Email and password are required." }`
- **Actual:** `400`, ASP.NET ProblemDetails:
  ```json
  {
    "title": "One or more validation errors occurred.",
    "errors": { "Password": ["The Password field is required."] }
  }
  ```
- **Notes:** Validation is caught by ASP.NET model binding before the controller runs, bypassing the platform error envelope. Response shape is inconsistent with the documented contract and all other error responses.

---

### INT-005 — Security API `GET /auth/me` with no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`
- **Notes:** —

---

### INT-006 — Security API `GET /auth/me` with invalid/forged token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`
- **Notes:** Signature validation working correctly

---

### INT-009 — Security API `POST /authorize` (valid token, platform_owner)
- **Status:** ✅ PASS
- **Expected:** `200`, `{ "allowed": bool, "roles": [...], "permissions": [...] }`
- **Actual:** `200`, `allowed: false` — `platform_owner` holds `manage_apps, manage_users, manage_resources, view_audit_log, manage_roles, manage_companies`; does not hold app-level `read` on `dashboard-hub`
- **Notes:** `allowed: false` is the correct outcome — platform-level roles do not auto-grant app-level permissions. Correct behavior per spec.

---

### INT-010 — Security API token refresh
- **Status:** ✅ PASS
- **Expected:** `200`, new `accessToken` + `refreshToken`; old refresh token invalidated
- **Actual:** `200` — new tokens issued, both access and refresh rotated
- **Notes:** Single-use refresh confirmed working

---

### INT-013 — Security API `POST /authorize` missing `appSlug` (expect 400)
- **Status:** ✅ PASS
- **Expected:** `400`, `{ "success": false, "message": "appSlug is required." }`
- **Actual:** `400`, body matches exactly — uses correct platform envelope
- **Notes:** Service-layer validation produces the correct response shape (contrast with INT-004)

---

### INT-014 — Security API `POST /auth/refresh` missing `refreshToken` (expect 400)
- **Status:** ⚠️ WARN — 400 returned but wrong response shape
- **Expected:** `400`, `{ "success": false, "message": "Refresh token is required." }`
- **Actual:** `400`, ASP.NET ProblemDetails:
  ```json
  {
    "title": "One or more validation errors occurred.",
    "errors": { "RefreshToken": ["The RefreshToken field is required."] }
  }
  ```
- **Notes:** Same model-binding bypass issue as INT-004

---

### INT-015 — Security API `GET /auth/me?appSlug=dashboard-hub`
- **Status:** ✅ PASS
- **Expected:** `200`, `appAccess` populated with `dashboard-hub` role + permissions
- **Actual:** `200`
  ```json
  "appAccess": [{
    "appSlug": "dashboard-hub",
    "roleName": "platform_owner",
    "permissions": ["manage_apps","manage_users","manage_resources","view_audit_log","manage_roles","manage_companies"]
  }]
  ```
- **Notes:** App-scoped enrichment working correctly

---

### INT-016 — Security API login with `appSlug=dashboard-hub`
- **Status:** ✅ PASS
- **Expected:** `200`, tokens issued; `appSlug` does not affect auth outcome
- **Actual:** `200` — tokens issued normally; `user` object in login response does not include `appAccess` (expected per spec)
- **Notes:** —

---

### INT-017 — Security API `POST /auth/logout`
- **Status:** ✅ PASS
- **Expected:** `200`, `{ "success": true, "message": "Logged out successfully." }`
- **Actual:** `200`, body matches exactly
- **Notes:** —

---

### INT-018 — Security API `POST /auth/refresh` with bogus token (expect 401)
- **Status:** ✅ PASS
- **Expected:** `401`, `{ "success": false, "message": "Invalid or expired refresh token." }`
- **Actual:** `401`, body matches exactly
- **Notes:** —

---

### Platform API (HubApi) — Cross-API JWT Integration

---

### INT-007 — HubApi `GET /api/auth/me` with Security Platform JWT
- **Status:** ❌ FAIL
- **Expected:** `200`, user identity resolved from JWT claims
- **Actual:** `401` — `{ "title": "Unauthorized" }` (raw ASP.NET middleware rejection)
- **Notes:** **Core integration is broken.** HubApi does not accept JWTs issued by the Security Platform. All HubApi endpoints requiring a Security Platform JWT are blocked.

---

### INT-008 — HubApi `GET /api/projects` with Security Platform JWT
- **Status:** ❌ FAIL
- **Expected:** `200`, list of projects the user has access to
- **Actual:** `401`
- **Notes:** Same root cause as INT-007

---

### INT-011 — HubApi `GET /api/auth/me` with no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`
- **Notes:** —

---

### INT-012 — HubApi `GET /api/projects` with no token
- **Status:** ✅ PASS
- **Expected:** `401`
- **Actual:** `401`
- **Notes:** —

---

### INT-019 — HubApi DB Proxy `GET /api/schema/tables` with Security Platform JWT (expect 403)
- **Status:** ⚠️ WARN — `404` instead of expected `403`
- **Expected:** `403` — `ProjectScopeMiddleware` should reject Security Platform JWT before reaching controller
- **Actual:** `404`
- **Notes:** Route path `/api/schema/tables` not found. Could not confirm the `403` rejection behavior documented for DB Proxy endpoints. Also blocked by INT-007 root cause.

---

### INT-020 — HubApi `POST /api/auth/api-tokens` with Security Platform JWT
- **Status:** ❌ FAIL (blocked by INT-007)
- **Expected:** `200`, new API token created
- **Actual:** `401`
- **Notes:** Dependent on INT-007 fix. Cannot test token creation or downstream DB Proxy flow until HubApi accepts the Security Platform JWT.

---

## Root Cause Analysis: INT-007 / INT-008 / INT-020

The Security Platform JWT carries these key claims:

```
iss: flatplanet-security
aud: flatplanet-apps
role: platform_owner
```

HubApi's JWT middleware must be configured to trust tokens with exactly these `issuer` and `audience` values, signed with the matching `SecretKey`. In production on Azure, these values are injected via App Configuration or Key Vault.

The `401` response has no body (raw middleware rejection, not a controller error), which strongly indicates a **JWT signature validation failure or issuer/audience mismatch** — the token is structurally valid but HubApi cannot verify it.

**Check in HubApi's Azure App Configuration:**

| Setting | Expected Value |
|---|---|
| `Jwt:Issuer` | `flatplanet-security` |
| `Jwt:Audience` | `flatplanet-apps` |
| `Jwt:SecretKey` | Must match Security Platform's production secret |

**Priority: P0 blocker** — the entire authenticated cross-API flow (projects, members, API token creation, Claude Config) is non-functional in production.
