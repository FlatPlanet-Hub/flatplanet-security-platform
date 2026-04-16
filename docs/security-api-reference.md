# FlatPlanet Security Platform — API Reference

**Version**: 1.5.0
**Base URL**: `https://<your-host>/api/v1`
**Content-Type**: `application/json`
**Auth**: Bearer JWT or Service Token in `Authorization` header

---

## Overview

### Authentication

The platform supports two authentication methods:

**1. JWT (user authentication)**

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

Tokens are issued by `POST /api/v1/auth/login` and rotated by `POST /api/v1/auth/refresh`. The platform owns credential storage — passwords are hashed with bcrypt (work factor 12). No external auth provider is involved.

**2. Service Token (server-to-server)**

```
Authorization: Bearer <service-token>
```

Used by trusted backend services (e.g. HubApi) to call the platform without a user context. The service token is a static secret configured in `appsettings.json` under `ServiceToken.Token` (minimum 32 characters). On a valid service token, the caller is granted both `platform_owner` and `app_admin` roles — it has full access to all endpoints. Token comparison uses constant-time equality to prevent timing attacks.

**Access token lifetime**: configurable via `jwt_access_expiry_minutes` (default 60 min)
**Refresh token lifetime**: configurable via `jwt_refresh_expiry_days` (default 7 days)

### Session Enforcement

Every authenticated request is checked by `SessionValidationMiddleware`:

- **Idle timeout**: if the session has been inactive longer than `session_idle_timeout_minutes` (default 30), the request is rejected with `401`
- **Absolute timeout**: if the session has exceeded `session_absolute_timeout_minutes` (default 480 min / 8 hours), the request is rejected with `401`

When either fires, the client must re-authenticate via `POST /api/v1/auth/login`.

### Authorization Policies

| Policy | Who it applies to |
|---|---|
| `AdminAccess` | Users with `app_admin` or `platform_owner` platform role |
| `PlatformOwner` | Users with `platform_owner` platform role only |
| *(no policy)* | Any authenticated user |

---

## Standard Response Shape

Every response wraps data in a consistent envelope:

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

**Validation Error (400):**
```json
{
  "success": false,
  "message": "Validation failed.",
  "errors": {
    "email": ["The Email field is required."],
    "password": ["The Password field must not exceed 128 characters."]
  }
}
```

The `errors` object is keyed by field name. Each key holds an array of error messages for that field. This shape applies to all endpoints that use request DTO validation (`[Required]`, `[MaxLength]`, `[EmailAddress]`, `[RegularExpression]`).

---

## Error Reference

| HTTP | When it fires |
|---|---|
| `400` | Missing required field or validation failure — see validation error shape above |
| `401` | Missing/expired/invalid token, session timed out, invalid credentials |
| `403` | Authenticated but not permitted (wrong role, suspended account) |
| `404` | Resource not found |
| `409` | Unique constraint violation (duplicate email, slug, role name, etc.) |
| `422` | Business rule violation (e.g. deleting a role with active users) |
| `429` | Rate limit exceeded |
| `500` | Unhandled server error |

---

## Auth

---

### POST /api/v1/auth/login

Verifies credentials against the platform's database (bcrypt, work factor 12). Issues a JWT access token and a refresh token. Enforces per-IP and per-email rate limits, account lockout, and company status gating.

**Auth required**: No

#### Request

```json
{
  "email": "alice@acme.com",
  "password": "S3cur3P@ss!",
  "appSlug": "dashboard-hub"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `email` | string | Yes | Must be a valid email. Max 256 chars. |
| `password` | string | Yes | Max 128 chars. Never returned in any response. |
| `appSlug` | string | No | If provided, app-scoped roles and permissions are included in the response. Max 100 chars. |

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "v2.local.abc123...",
    "expiresIn": 3600,
    "idleTimeoutMinutes": 30,
    "user": {
      "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
      "email": "alice@acme.com",
      "fullName": "Alice Chen",
      "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
    }
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `accessToken` | string | JWT. Lifetime = `jwt_access_expiry_minutes` × 60 seconds. |
| `refreshToken` | string | Opaque token. Store securely. Single-use (rotated on each refresh). |
| `expiresIn` | integer | Access token lifetime in **seconds**. |
| `idleTimeoutMinutes` | integer | Session idle timeout in **minutes**. Session is invalidated if no authenticated request arrives within this window. Use this to schedule heartbeats — fire at `idleTimeoutMinutes × 0.5` minutes. |
| `user.userId` | UUID | Use this as the user identifier in downstream calls. |
| `user.companyId` | string | UUID as string. |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | Email and password are required. | Missing field caught at controller. |
| `401` | Invalid email or password. | No user found or bcrypt mismatch. Intentionally vague. |
| `403` | User account is suspended. | User status is not `active`. |
| `403` | Company account is suspended. | Company status is not `active`. |
| `429` | Too many login attempts from this IP. | Exceeded `rate_limit_login_per_ip_per_minute`. |
| `429` | Too many login attempts for this account. | Exceeded `rate_limit_login_per_email_per_minute`. |
| `423` | Account is temporarily locked. Please try again later. | Failed attempts exceed `max_failed_login_attempts` within `lockout_duration_minutes`. |

#### Notes

- On failed credential check, both a `LoginAttempt` (failed) and an audit log entry (`login_failure`) are recorded.
- On success, the oldest session is evicted if `max_concurrent_sessions` is reached.
- Refresh tokens are single-use. Using a refresh token invalidates it and issues a new one.
- The `appSlug` field does not affect authentication — it only enriches the response. A missing or invalid slug does not cause a failure on login.

#### JWT Claims Structure

The decoded access token payload includes:

| Claim | Type | Description |
|---|---|---|
| `sub` | UUID string | User ID |
| `name` | string | Full name |
| `email` | string | Email address |
| `app_slug` | string | App slug the token was issued for (if `appSlug` was provided at login) |
| `permissions` | string | Comma-separated permission list for this app (e.g. `"read,write"`) |
| `business_codes` | string or string[] | Short business code(s) the user belongs to (e.g. `"fp"` or `["fp","acme"]`). Single membership serializes as a plain string, not an array. |
| `business_ids` | string or string[] | UUID(s) of the business(es) the user belongs to, parallel-indexed with `business_codes`. Single membership serializes as a plain string. |
| `exp` | integer | Unix expiry timestamp |
| `iss` | string | Token issuer (`flatplanet-security`) |
| `aud` | string | Token audience (`flatplanet-apps`) |

> **Important — normalization**: `business_codes` and `business_ids` may be a plain string (single membership) or a string array (multiple memberships). Always normalize before use:
> ```js
> const codes = Array.isArray(jwt.business_codes)
>   ? jwt.business_codes
>   : jwt.business_codes ? [jwt.business_codes] : [];
> const ids = Array.isArray(jwt.business_ids)
>   ? jwt.business_ids
>   : jwt.business_ids ? [jwt.business_ids] : [];
> // codes[i] and ids[i] always refer to the same business.
> ```

---

### POST /api/v1/auth/logout

Ends the current session and revokes all refresh tokens for the user.

**Auth required**: Yes

#### Request

No body.

#### Success Response — 200

```json
{ "success": true, "message": "Logged out successfully." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `401` | — | Missing or invalid token. |

#### Notes

- Revokes **all** refresh tokens for the user, not just the current session's.
- If the JWT's `session_id` claim is absent (legacy token), the session is not ended but tokens are still revoked.

---

### POST /api/v1/auth/refresh

Rotates the refresh token and issues a new access token. The old refresh token is invalidated immediately.

**Auth required**: No

#### Request

```json
{
  "refreshToken": "v2.local.abc123..."
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `refreshToken` | string | Yes | Opaque token from a prior login or refresh. |

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "v2.local.xyz789...",
    "expiresIn": 3600
  }
}
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | Refresh token is required. | Empty or missing field. |
| `401` | Invalid or expired refresh token. | Token not found, already revoked, or past expiry. |
| `401` | User not found. | User was deleted after token was issued. |
| `403` | User account is suspended. | User status is not `active`. |

#### Notes

- Do **not** retry with the same refresh token after a `401`. The token is gone.
- If a `401` is returned, the user must re-authenticate via `POST /api/v1/auth/login`.
- Session `last_active_at` is updated on every successful refresh.

---

### GET /api/v1/auth/me

Returns the authenticated user's profile, platform roles, and optionally app-scoped permissions.

**Auth required**: Yes

#### Query Parameters

| Param | Type | Required | Notes |
|---|---|---|---|
| `appSlug` | string | No | If provided, returns roles and permissions scoped to that app. |

#### Request

```
GET /api/v1/auth/me?appSlug=dashboard-hub
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
    "email": "alice@acme.com",
    "fullName": "Alice Chen",
    "roleTitle": "Senior Engineer",
    "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "status": "active",
    "lastSeenAt": "2026-03-25T14:22:00Z",
    "platformRoles": ["app_admin"],
    "appAccess": [
      {
        "appSlug": "dashboard-hub",
        "roleName": "editor",
        "permissions": ["read_reports", "create_reports"]
      }
    ]
  }
}
```

#### Notes

- `appAccess` is empty (`[]`) when `appSlug` is not provided or the user has no role in that app.
- `platformRoles` reflects roles defined directly on the platform (e.g. `app_admin`, `platform_owner`).

---

### POST /api/v1/auth/change-password

Changes the authenticated user's password. On success, all sessions and refresh tokens for the user are revoked, forcing a full re-login.

**Auth required**: Yes (Bearer JWT)

#### Request

```json
{
  "currentPassword": "OldP@ss1!",
  "newPassword": "NewP@ss2!",
  "confirmPassword": "NewP@ss2!"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `currentPassword` | string | Yes | The user's existing password. Verified against the stored bcrypt hash. |
| `newPassword` | string | Yes | Must satisfy the password policy (see below). Must differ from `currentPassword`. |
| `confirmPassword` | string | Yes | Must match `newPassword` exactly. |

#### Password Policy

All new passwords (for both change-password and reset-password) must meet the following requirements:

| Rule | Requirement |
|---|---|
| Minimum length | 8 characters |
| Uppercase | At least one uppercase letter (A–Z) |
| Lowercase | At least one lowercase letter (a–z) |
| Digit | At least one numeric digit (0–9) |
| Special character | At least one character from: `!@#$%^&*()_+-=[]{}|;':",./<>?` |

#### Success Response — 200

```json
{ "success": true, "message": "Password changed. Please log in again." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | Current password is incorrect. | `currentPassword` does not match the stored hash. |
| `400` | New password must be different from the current password. | `newPassword` equals `currentPassword`. |
| `400` | Passwords do not match. | `newPassword` and `confirmPassword` differ. |
| `400` | *(policy message)* | `newPassword` fails the password policy (see above). |
| `401` | — | Missing or invalid JWT. |

#### Notes

- All sessions and all refresh tokens are revoked immediately on success. The client must redirect to the login page.
- `userId` is derived from the JWT — it is never taken from the request body.

---

### POST /api/v1/auth/forgot-password

Initiates a password reset flow by sending a time-limited reset link to the user's email address. The response is identical whether or not the email exists in the system — this prevents user enumeration.

**Auth required**: No

#### Request

```json
{
  "email": "alice@acme.com"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `email` | string | Yes | The email address to send the reset link to. Must be a valid email format. |

#### Success Response — 200

```json
{ "success": true, "message": "If that email exists, a reset link has been sent." }
```

This response is returned **regardless of whether the email is registered** to prevent account enumeration.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | *(validation message)* | Missing or malformed `email` field. |

#### Notes

- The reset link sent to the user's inbox has the form: `{BaseUrl}/reset-password?token={rawToken}`
- The token expires in **15 minutes**.
- The token is SHA-256 hashed before storage — the plaintext token is never persisted.
- `BaseUrl` is configured in Azure App Config / `appsettings.json`.
- SMTP settings must be configured under the `Smtp` section in `appsettings.json` (host, port, credentials, sender address).

---

### POST /api/v1/auth/reset-password

Completes a password reset using the token from the reset link email. On success, all sessions and refresh tokens are revoked, requiring the user to log in with the new password.

**Auth required**: No

#### Request

```json
{
  "token": "a3f1b2c4d5e6...",
  "newPassword": "NewP@ss2!",
  "confirmPassword": "NewP@ss2!"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `token` | string | Yes | The raw token from the reset link query string (`?token=`). |
| `newPassword` | string | Yes | Must satisfy the password policy. Must differ from the current password. |
| `confirmPassword` | string | Yes | Must match `newPassword` exactly. |

#### Success Response — 200

```json
{ "success": true, "message": "Password reset successfully. Please log in." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | Reset token is invalid or has expired. | Token not found, already used, or older than 15 minutes. |
| `400` | New password must be different from the current password. | `newPassword` matches the user's existing password. |
| `400` | Passwords do not match. | `newPassword` and `confirmPassword` differ. |
| `400` | *(policy message)* | `newPassword` fails the password policy (see above). |

#### Notes

- The token is **single-use**. It is invalidated immediately on success.
- Token expiry is **15 minutes** from when the forgot-password request was made.
- All sessions and all refresh tokens are revoked on success.

---

### POST /api/v1/auth/heartbeat

Resets the session idle timer. Call this endpoint periodically from long-lived clients (dashboards, SPAs) to keep the session alive while the user is active but not making other API calls.

**Auth required**: Yes

#### Request

No body.

#### Success Response — 200

```json
{ "success": true, "data": { "sessionActive": true } }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `401` | — | Missing or invalid token, or session has already expired. |

#### Notes

- Fire this request at an interval of `idleTimeoutMinutes × 0.5` minutes (returned at login). Example: if `idleTimeoutMinutes` is `30`, send a heartbeat every **15 minutes**.
- A `401` response means the session has already expired. Stop the heartbeat interval, clear tokens from storage, and redirect the user to the login page. Do not attempt a token refresh — the session row is already gone.
- `SessionValidationMiddleware` updates `last_active_at` automatically on every authenticated request, including this one.

---

## Authorization

---

### POST /api/v1/authorize

Checks whether the authenticated user has a required permission on a resource within a given app. Logs the decision to the audit log.

**Auth required**: Yes
The `userId` is derived from the JWT — do **not** include it in the request body.

#### Request

```json
{
  "appSlug": "dashboard-hub",
  "resourceIdentifier": "/reports/monthly",
  "requiredPermission": "create_reports"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `appSlug` | string | Yes | The app to check access against. |
| `resourceIdentifier` | string | Yes | The resource path or identifier (e.g. `/admin`, `/reports/monthly`). |
| `requiredPermission` | string | Yes | The permission name to verify. |

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "allowed": true,
    "roles": ["editor"],
    "permissions": ["read_reports", "create_reports"]
  }
}
```

| Field | Notes |
|---|---|
| `allowed` | `true` if the user has the required permission in the given app. |
| `roles` | All roles the user holds in the app. |
| `permissions` | All permissions the user holds in the app (across all roles). |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | appSlug is required. | Empty or missing `appSlug`. |
| `401` | Invalid token. | JWT invalid or user ID claim missing. |

#### Notes

- A `200` response with `"allowed": false` is a valid and expected outcome — do not treat it as an error.
- Both `authorize_allowed` and `authorize_denied` events are written to the audit log regardless of outcome.

---

## Users

All user endpoints require `AdminAccess` policy except where noted.

---

### GET /api/v1/users

Returns a paginated list of users with optional filtering.

**Auth required**: Yes — `AdminAccess`

#### Query Parameters

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | integer | `1` | Page number. |
| `pageSize` | integer | `20` | Items per page. |
| `companyId` | UUID | — | Filter by company. |
| `status` | string | — | Filter by status: `active`, `suspended`, `inactive`. |
| `search` | string | — | Full-text search on email / full name. |

#### Request

```
GET /api/v1/users?page=1&pageSize=20&status=active&search=alice
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        "email": "alice@acme.com",
        "fullName": "Alice Chen",
        "roleTitle": "Senior Engineer",
        "status": "active",
        "createdAt": "2026-01-10T09:00:00Z",
        "lastSeenAt": "2026-03-25T14:22:00Z"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  }
}
```

---

### GET /api/v1/users/{id}

Returns full user detail including all app role assignments.

**Auth required**: Yes — `AdminAccess`

#### Request

```
GET /api/v1/users/3f2504e0-4f89-11d3-9a0c-0305e82c3301
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
    "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "email": "alice@acme.com",
    "fullName": "Alice Chen",
    "roleTitle": "Senior Engineer",
    "status": "active",
    "createdAt": "2026-01-10T09:00:00Z",
    "lastSeenAt": "2026-03-25T14:22:00Z",
    "appAccess": [
      {
        "appId": "b1e2c3d4-1234-5678-abcd-ef0123456789",
        "appName": "Dashboard Hub",
        "appSlug": "dashboard-hub",
        "roleName": "editor",
        "status": "active",
        "grantedAt": "2026-02-01T12:00:00Z",
        "expiresAt": null
      }
    ]
  }
}
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | User not found. | No user with that ID. |

---

### POST /api/v1/users

Creates a new user with a bcrypt-hashed password. The platform owns credential storage — no external auth provider is involved.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "email": "bob@acme.com",
  "fullName": "Bob Smith",
  "roleTitle": "Product Manager",
  "password": "InitialP@ss123!"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `companyId` | UUID | Yes | The company this user belongs to. |
| `email` | string | Yes | Must be unique across the platform. Max 256 chars. |
| `fullName` | string | Yes | Display name. Max 200 chars. |
| `roleTitle` | string | No | Job title. Max 100 chars. |
| `password` | string | Yes | Min 8 characters, max 128. Stored as bcrypt hash (work factor 12). Never returned in any response. |

#### Success Response — 201

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "email": "bob@acme.com",
    "fullName": "Bob Smith",
    "roleTitle": "Product Manager",
    "status": "active",
    "createdAt": "2026-03-26T10:00:00Z",
    "lastSeenAt": null
  }
}
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | — | Missing required field or validation failure. |
| `409` | — | Email already exists (unique constraint violation). |

---

### PUT /api/v1/users/{id}

Updates a user's name and role title.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "fullName": "Alice Chen-Park",
  "roleTitle": "Staff Engineer"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `fullName` | string | Yes | Max 200 chars. |
| `roleTitle` | string | No | Max 100 chars. |

#### Success Response — 200

Returns the updated `UserResponse` (same shape as `GET /api/v1/users/{id}` without `appAccess`).

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | User not found. | No user with that ID. |

---

### PUT /api/v1/users/{id}/status

Updates a user's status.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{ "status": "suspended" }
```

#### Fields

| Field | Type | Required | Allowed values |
|---|---|---|---|
| `status` | string | Yes | `active`, `suspended`, `inactive` |

#### Success Response — 200

```json
{ "success": true, "message": "Status updated." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | — | Invalid status value. |
| `404` | User not found. | No user with that ID. |

---

### POST /api/v1/users/{id}/offboard

Offboards a user: revokes all sessions and refresh tokens, removes all app role assignments, and logs the event.

**Auth required**: Yes — `AdminAccess`

#### Request

No body.

#### Success Response — 200

```json
{ "success": true, "message": "User offboarded successfully." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | User not found. | No user with that ID. |

---

### GET /api/v1/users/{id}/export

Returns all personal data held for a user (GDPR data export). A user may export their own data; admins may export any user's data.

**Auth required**: Yes — `AdminAccess` for other users; any authenticated user for their own `id`.

#### Request

```
GET /api/v1/users/3f2504e0-4f89-11d3-9a0c-0305e82c3301/export
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "user": { "id": "...", "email": "alice@acme.com", "fullName": "Alice Chen", "..." : "..." },
    "appRoles": [
      { "id": "...", "userId": "...", "userEmail": "alice@acme.com", "userFullName": "Alice Chen", "roleId": "...", "roleName": "editor", "status": "active", "expiresAt": null }
    ],
    "sessions": [
      { "id": "...", "ipAddress": "203.0.113.1", "userAgent": "Mozilla/5.0 ...", "isActive": false, "startedAt": "2026-03-01T08:00:00Z", "lastActiveAt": "2026-03-01T09:30:00Z" }
    ],
    "auditEvents": [
      { "id": "...", "userId": "...", "appId": null, "eventType": "login_success", "ipAddress": "203.0.113.1", "userAgent": "...", "details": null, "createdAt": "2026-03-01T08:00:00Z" }
    ],
    "exportedAt": "2026-03-27T10:00:00Z"
  }
}

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `403` | — | Non-admin attempting to export another user's data. |
| `404` | User not found. | No user with that ID. |

---

### POST /api/v1/users/{id}/anonymize

Irreversibly anonymizes a user's PII (nulls email, name, IP fields). Ends all active sessions and revokes all refresh tokens. Cannot be undone.

**Auth required**: Yes — `AdminAccess`

#### Request

No body.

#### Success Response — 200

```json
{ "success": true, "message": "User data anonymized." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | User not found. | No user with that ID. |

#### Notes

- This action is irreversible. The user will not be able to log in after anonymization.

---

## Companies

All company endpoints require `PlatformOwner` policy.

---

### GET /api/v1/companies

Returns all companies.

**Auth required**: Yes — `PlatformOwner`

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "Acme Corp",
      "code": "fp",
      "countryCode": "US",
      "status": "active",
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ]
}
```

---

### GET /api/v1/companies/{id}

Returns a single company by ID.

**Auth required**: Yes — `PlatformOwner`

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "name": "Acme Corp",
    "code": "fp",
    "countryCode": "US",
    "status": "active",
    "createdAt": "2026-01-01T00:00:00Z"
  }
}
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Company not found. | No company with that ID. |

---

### POST /api/v1/companies

Creates a new company. Default status is `active`.

**Auth required**: Yes — `PlatformOwner`

#### Request

```json
{
  "name": "Acme Corp",
  "countryCode": "US",
  "code": "fp"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Max 200 chars. Must be unique. |
| `countryCode` | string | Yes | ISO country code (e.g. `US`, `GB`, `PH`). Max 10 chars. |
| `code` | string | No | Short business identifier (e.g. `"fp"`). Used in JWT `business_codes` claim and file storage paths. The corresponding UUID appears in the parallel `business_ids` claim. |

#### Success Response — 201

Returns the created `CompanyResponse`.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `409` | — | Company name already exists. |

---

### PUT /api/v1/companies/{id}

Updates a company's name, country code, and optional code.

**Auth required**: Yes — `PlatformOwner`

#### Request

```json
{
  "name": "Acme International",
  "countryCode": "GB",
  "code": "fp"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Max 200 chars. Must be unique. |
| `countryCode` | string | Yes | ISO country code. Max 10 chars. |
| `code` | string | No | Short business identifier. |

#### Success Response — 200

Returns the updated `CompanyResponse`.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Company not found. | No company with that ID. |

---

### GET /api/v1/companies/{companyId}/members

Returns all members of a company.

**Auth required**: Yes — `PlatformOwner`

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "userId": "dc88786a-...",
      "email": "chris.moriarty@flatplanet.com",
      "fullName": "Chris Moriarty",
      "role": "member",
      "status": "active",
      "joinedAt": "2026-04-10T00:07:38Z"
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `userId` | UUID | Security Platform user ID. |
| `email` | string | User's email address. |
| `fullName` | string | User's display name. |
| `role` | string | Membership role (e.g. `"member"`, `"admin"`). |
| `status` | string | Membership status — `active` or `inactive`. |
| `joinedAt` | string | ISO 8601 timestamp when the membership was created. |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Company not found. | No company with that ID. |

---

### POST /api/v1/companies/{companyId}/members

Adds a user to a company.

**Auth required**: Yes — `PlatformOwner`

#### Request

```json
{
  "userId": "dc88786a-...",
  "role": "member"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `userId` | UUID | Yes | ID of the user to add. |
| `role` | string | Yes | Membership role to assign (e.g. `"member"`, `"admin"`). |

#### Success Response — 200

```json
{ "success": true, "message": "Member added." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Company not found. | No company with that ID. |
| `404` | User not found. | No user with that ID. |
| `409` | — | User is already a member of this company. |

---

### DELETE /api/v1/companies/{companyId}/members/{userId}

Removes a user from a company.

**Auth required**: Yes — `PlatformOwner`

#### Success Response — 200

```json
{ "success": true, "message": "Member removed." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Company not found. | No company with that ID. |
| `404` | Member not found. | User is not a member of this company. |

---

### PUT /api/v1/companies/{id}/status

Updates a company's status. Cascades to users and tokens depending on the new status.

**Auth required**: Yes — `PlatformOwner`

#### Request

```json
{ "status": "suspended" }
```

#### Fields

| Field | Type | Required | Allowed values |
|---|---|---|---|
| `status` | string | Yes | `active`, `suspended`, `inactive` |

#### Success Response — 200

```json
{ "success": true, "message": "Status updated." }
```

#### Cascade Behavior

| Status | What happens |
|---|---|
| `suspended` | All users bulk-suspended. All refresh tokens revoked (`company_suspended`). Audit event: `company_suspended`. |
| `inactive` | All users set to `inactive`. All app role assignments suspended per user. Audit event: `company_deactivated`. |
| `active` | Status updated only. No automatic re-activation of users. |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `400` | — | Invalid status value. |
| `404` | Company not found. | No company with that ID. |

---

## Apps

All app endpoints require `AdminAccess` policy.

---

### GET /api/v1/apps

Returns all registered apps.

**Auth required**: Yes — `AdminAccess`

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "id": "b1e2c3d4-1234-5678-abcd-ef0123456789",
      "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "Dashboard Hub",
      "slug": "dashboard-hub",
      "baseUrl": "https://dashboard.acme.com",
      "status": "active",
      "registeredAt": "2026-01-15T08:00:00Z"
    }
  ]
}
```

---

### GET /api/v1/apps/{id}

Returns a single app by ID.

**Auth required**: Yes — `AdminAccess`

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | App not found. | No app with that ID. |

---

### POST /api/v1/apps

Registers a new app. Default status is `active`.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "companyId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "name": "Dashboard Hub",
  "slug": "dashboard-hub",
  "baseUrl": "https://dashboard.acme.com"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `companyId` | UUID | Yes | Owning company. |
| `name` | string | Yes | Display name. Max 200 chars. |
| `slug` | string | Yes | URL-safe identifier. Must be unique. Max 100 chars. |
| `baseUrl` | string | No | App's base URL. Max 500 chars. |

#### Success Response — 201

Returns the created `AppResponse` at `Location: /api/v1/apps/{id}`.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `409` | — | Slug already exists. |

---

### PUT /api/v1/apps/{id}

Updates an app's name, base URL, and status.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "name": "Dashboard Hub v2",
  "baseUrl": "https://dashboardv2.acme.com",
  "status": "active"
}
```

#### Success Response — 200

Returns the updated `AppResponse`.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | App not found. | No app with that ID. |

---

## Roles

All role endpoints require `AdminAccess` policy. Roles are scoped to an app.

---

### GET /api/v1/apps/{appId}/roles

Returns all roles for the given app.

**Auth required**: Yes — `AdminAccess`

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "id": "c1d2e3f4-0000-0000-0000-000000000001",
      "appId": "b1e2c3d4-1234-5678-abcd-ef0123456789",
      "name": "editor",
      "description": "Can create and edit content.",
      "isPlatformRole": false,
      "createdAt": "2026-01-20T10:00:00Z"
    }
  ]
}
```

---

### POST /api/v1/apps/{appId}/roles

Creates a new role for the given app.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "name": "viewer",
  "description": "Read-only access to all resources."
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Must be unique within the app. Max 100 chars. |
| `description` | string | No | Max 500 chars. |

#### Success Response — 201

Returns the created `RoleResponse`.

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `409` | — | Role name already exists in this app. |

---

### PUT /api/v1/apps/{appId}/roles/{id}

Updates a role's name and description.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "name": "senior-editor",
  "description": "Can publish and archive content."
}
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `403` | Role does not belong to this app. | Role ID exists but belongs to a different app. |
| `404` | Role not found. | No role with that ID. |

---

### DELETE /api/v1/apps/{appId}/roles/{id}

Deletes a role. Fails if any users are currently assigned to it.

**Auth required**: Yes — `AdminAccess`

#### Success Response — 200

```json
{ "success": true, "message": "Role deleted." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `403` | Role does not belong to this app. | Role ID exists but belongs to a different app. |
| `404` | Role not found. | No role with that ID. |
| `422` | Cannot delete a role that has active users assigned. | Revoke all user assignments first. |

---

### POST /api/v1/apps/{appId}/roles/{roleId}/permissions

Assigns a permission to a role.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{ "permissionId": "d1e2f3a4-0000-0000-0000-000000000001" }
```

#### Success Response — 200

```json
{ "success": true, "message": "Permission assigned." }
```

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `404` | Role not found. | No role with that ID. |
| `409` | — | Permission already assigned to this role. |

---

### DELETE /api/v1/apps/{appId}/roles/{roleId}/permissions/{permId}

Removes a permission from a role.

**Auth required**: Yes — `AdminAccess`

#### Success Response — 200

```json
{ "success": true, "message": "Permission removed." }
```

---

## Permissions

All permission endpoints require `AdminAccess` policy. Permissions are scoped to an app.

---

### GET /api/v1/apps/{appId}/permissions

Returns all permissions for the given app.

---

### POST /api/v1/apps/{appId}/permissions

Creates a permission.

#### Request

```json
{
  "name": "create_reports",
  "description": "Allows creating monthly reports.",
  "category": "reporting"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Must be unique within the app. Max 100 chars. |
| `description` | string | No | Max 500 chars. |
| `category` | string | Yes | Groups permissions for display. Max 100 chars (e.g. `reporting`, `admin`, `billing`). |

#### Success Response — 201

Returns the created `PermissionResponse` (`id`, `appId`, `name`, `description`, `category`, `createdAt`).

---

### PUT /api/v1/apps/{appId}/permissions/{id}

Updates a permission's name, description, and category.

#### Request

```json
{
  "name": "create_reports",
  "description": "Allows creating monthly reports.",
  "category": "reporting"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Max 100 chars. |
| `description` | string | No | Max 500 chars. |
| `category` | string | Yes | Max 100 chars. |

---

## Resources

All resource endpoints require `AdminAccess` policy. Resources are scoped to an app and categorized by resource type.

---

### GET /api/v1/apps/{appId}/resources

Returns all resources for the given app.

---

### POST /api/v1/apps/{appId}/resources

Creates a resource.

#### Request

```json
{
  "name": "Monthly Reports",
  "identifier": "/reports/monthly",
  "resourceTypeId": "e1f2a3b4-0000-0000-0000-000000000001",
  "description": "Monthly financial reports."
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `resourceTypeId` | UUID | Yes | Must reference an existing resource type. |
| `name` | string | Yes | Max 200 chars. |
| `identifier` | string | Yes | Path or key used in `POST /api/v1/authorize` calls. Must be unique within the app. Max 200 chars. |

#### Success Response — 201

Returns the created `ResourceResponse` (`id`, `appId`, `resourceTypeId`, `name`, `identifier`, `status`, `createdAt`).

---

### PUT /api/v1/apps/{appId}/resources/{id}

Updates a resource's name, identifier, and status.

#### Request

```json
{
  "name": "Monthly Reports",
  "identifier": "/reports/monthly",
  "status": "active"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Max 200 chars. |
| `identifier` | string | Yes | Max 200 chars. |
| `status` | string | Yes | `active` or `inactive` only. |

---

## Resource Types

Require `AdminAccess` policy.

---

### GET /api/v1/resource-types

Returns all resource types.

---

### POST /api/v1/resource-types

Creates a resource type.

#### Request

```json
{
  "name": "report",
  "description": "Financial and operational reports."
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | Yes | Must be unique. |
| `description` | string | No | — |

#### Success Response — 201

Returns the created `ResourceTypeResponse` (`id`, `name`, `description`, `createdAt`).

---

## User Access (App Role Grants)

All endpoints require `AdminAccess` policy. Manages which users have roles within a specific app.

---

### GET /api/v1/apps/{appId}/users

Returns all active user-role grants for the given app.

**Auth required**: Yes — `AdminAccess`

---

### POST /api/v1/apps/{appId}/users

Grants a user a role within an app.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{
  "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "roleId": "c1d2e3f4-0000-0000-0000-000000000001",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

#### Fields

| Field | Type | Required | Notes |
|---|---|---|---|
| `userId` | UUID | Yes | The user to grant access to. |
| `roleId` | UUID | Yes | The role to assign within the app. |
| `expiresAt` | datetime | No | ISO 8601. If omitted, the grant does not expire. |

#### Success Response — 201

Returns the created `UserAccessResponse`:

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
    "userEmail": "alice@acme.com",
    "userFullName": "Alice Chen",
    "roleId": "c1d2e3f4-0000-0000-0000-000000000001",
    "roleName": "editor",
    "status": "active",
    "expiresAt": "2026-12-31T23:59:59Z"
  }
}
```

#### Notes

- If the user was previously granted access to this app and that grant was later revoked, calling this endpoint reactivates the existing record. It does **not** return `409`.

---

### PUT /api/v1/apps/{appId}/users/{userId}/role

Changes a user's role within an app.

**Auth required**: Yes — `AdminAccess`

#### Request

```json
{ "roleId": "c1d2e3f4-0000-0000-0000-000000000002" }
```

#### Success Response — 200

```json
{ "success": true, "message": "Role updated." }
```

---

### DELETE /api/v1/apps/{appId}/users/{userId}

Revokes a user's access to an app.

**Auth required**: Yes — `AdminAccess`

#### Success Response — 200

```json
{ "success": true, "message": "Access revoked." }
```

---

## User Context

---

### GET /api/v1/apps/{appSlug}/user-context

Returns the authenticated user's roles and permissions within the specified app.

**Auth required**: Yes

#### Request

```
GET /api/v1/apps/dashboard-hub/user-context
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
    "email": "alice@acme.com",
    "fullName": "Alice Chen",
    "companyName": "Acme Corp",
    "roles": ["editor"],
    "permissions": ["read_reports", "create_reports"],
    "allowedApps": [
      {
        "appId": "b1e2c3d4-1234-5678-abcd-ef0123456789",
        "appSlug": "dashboard-hub",
        "appName": "Dashboard Hub"
      }
    ]
  }
}
```

| Field | Notes |
|---|---|
| `roles` | All roles the user holds in the specified app. |
| `permissions` | All permissions the user holds across all roles in the app. |
| `allowedApps` | All apps the user currently has an active role grant in (across the platform, not just this app). |

#### Error Responses

| HTTP | Message | Cause |
|---|---|---|
| `401` | Invalid token. | JWT invalid or user ID claim missing. |

---

## Audit Log

---

### GET /api/v1/audit

Returns a paginated audit log with optional filters.

**Auth required**: Yes — `AdminAccess`

#### Query Parameters

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | integer | `1` | — |
| `pageSize` | integer | `50` | Clamped to max `200`. |
| `userId` | UUID | — | Filter by user. |
| `appId` | UUID | — | Filter by app. |
| `eventType` | string | — | Filter by event type (see Appendix). |
| `from` | datetime | — | ISO 8601. Inclusive lower bound on `created_at`. |
| `to` | datetime | — | ISO 8601. Inclusive upper bound on `created_at`. |

#### Request

```
GET /api/v1/audit?eventType=login_failure&from=2026-03-01T00:00:00Z&page=1&pageSize=50
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "f1a2b3c4-0000-0000-0000-000000000001",
        "userId": null,
        "appId": null,
        "eventType": "login_failure",
        "ipAddress": "203.0.113.42",
        "userAgent": "Mozilla/5.0 ...",
        "details": "{\"email\":\"hacker@evil.com\"}",
        "createdAt": "2026-03-25T08:15:00Z"
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 50
  }
}
```

---

## Security Config

All endpoints require `PlatformOwner` policy.

---

### GET /api/v1/security/config

Returns all security configuration entries.

**Auth required**: Yes — `PlatformOwner`

#### Success Response — 200

```json
{
  "success": true,
  "data": [
    {
      "configKey": "jwt_access_expiry_minutes",
      "configValue": "60",
      "updatedAt": "2026-01-01T00:00:00Z",
      "updatedByUserId": null
    }
  ]
}
```

---

### PUT /api/v1/security/config/{key}

Updates a single security configuration value. All values are stored as strings.

**Auth required**: Yes — `PlatformOwner`

#### Request

```json
{ "value": "30" }
```

#### Success Response — 200

```json
{ "success": true, "message": "Config updated." }
```

#### Notes

- Changes take effect immediately on the next request that reads the config.
- Invalid numeric values for keys that expect integers will cause the platform to fall back to defaults at runtime.

---

## Access Review

---

### GET /api/v1/access-review

Returns all active app role grants for ISO 27001 periodic access review. Includes how long each grant has been active, enabling identification of stale access.

**Auth required**: Yes — `AdminAccess`

#### Query Parameters

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | integer | `1` | — |
| `pageSize` | integer | `20` | — |
| `companyId` | UUID | — | Filter by company. |
| `appId` | UUID | — | Filter by app. |

#### Request

```
GET /api/v1/access-review?companyId=7c9e6679-7425-40de-944b-e07fc1f90ae7&page=1&pageSize=20
```

#### Success Response — 200

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "grantId": "a1b2c3d4-0000-0000-0000-000000000001",
        "userId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "userEmail": "alice@acme.com",
        "companyName": "Acme Corp",
        "appId": "b1e2c3d4-1234-5678-abcd-ef0123456789",
        "appName": "Dashboard Hub",
        "roleName": "editor",
        "grantedAt": "2026-01-15T12:00:00Z",
        "expiresAt": null,
        "daysSinceGranted": 70
      }
    ],
    "totalCount": 1,
    "page": 1,
    "pageSize": 20
  }
}
```

#### Notes

- `daysSinceGranted` is calculated at query time. Grants with high values should be reviewed and revoked or renewed.
- Only returns grants with `status = active`.

---

## Appendix

### Audit Event Types

| Event | When it fires |
|---|---|
| `login_success` | Successful login |
| `login_failure` | Failed credential check |
| `logout` | Explicit logout |
| `token_refresh` | Refresh token rotated |
| `token_revoke` | Token manually revoked |
| `session_start` | New session created on login |
| `session_end` | Session ended (logout or eviction) |
| `session_idle_timeout` | Session ended by idle timeout middleware |
| `session_absolute_timeout` | Session ended by absolute timeout middleware |
| `role_granted` | Role assigned to a user |
| `role_revoked` | Role removed from a user |
| `user_created` | New user created |
| `user_deactivated` | User status set to inactive |
| `user_offboarded` | User offboarded |
| `account_locked` | Account locked after too many failures |
| `account_unlocked` | Account manually unlocked |
| `authorize_allowed` | Authorization check passed |
| `authorize_denied` | Authorization check failed |
| `user_anonymized` | User PII anonymized (GDPR) |
| `company_suspended` | Company suspended (all users suspended, tokens revoked) |
| `company_deactivated` | Company set to inactive (all users + app roles deactivated) |

---

### Security Config Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `jwt_access_expiry_minutes` | integer | `60` | Access token lifetime in minutes. |
| `jwt_refresh_expiry_days` | integer | `7` | Refresh token lifetime in days. |
| `session_idle_timeout_minutes` | integer | `30` | Max idle time before session is invalidated. |
| `session_absolute_timeout_minutes` | integer | `480` | Max session lifetime regardless of activity (8 hours). |
| `max_concurrent_sessions` | integer | `3` | Max simultaneous sessions per user. Oldest is evicted when exceeded. |
| `max_failed_login_attempts` | integer | `5` | Failed attempts before account lockout. |
| `lockout_duration_minutes` | integer | `30` | Lookback window for counting failed attempts. |
| `rate_limit_login_per_ip_per_minute` | integer | `5` | Max login attempts per IP per minute. |
| `rate_limit_login_per_email_per_minute` | integer | `10` | Max login attempts per email per minute. |

---

### Versioning

The current API version is `v1`, reflected in all endpoint paths (`/api/v1/...`). The current platform release is **1.2.1**.

| Version | Date | Summary |
|---|---|---|
| `1.2.1` | 2026-03-27 | Validation errors now return platform envelope `{ success, message, errors }`. ServiceToken auth handler registered. Seed data simplified to Development Hub only. |
| `1.2.0` | 2026-03-27 | Controller refactor (`ApiController` base). Service Token auth documented. Permission `category` field. `UserContextResponse` full shape. `UserAccessResponse` with `userFullName`. Resource status values corrected. Compliance export shape documented. |
| `1.1.0` | 2026-03-25 | Standalone bcrypt authentication. Added `POST /api/v1/users`. Removed Supabase Auth dependency. |
| `1.0.0` | 2026-03-25 | Production release. Full auth, authorization, admin CRUD, audit/compliance, spec hardening. |
