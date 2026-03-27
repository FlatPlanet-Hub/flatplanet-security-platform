# FlatPlanet — Frontend Integration Guide

**Audience:** Frontend developers
**Last updated:** 2026-03-27
**Verified against:** Security Platform v1.2.0 · Platform API (HubApi) v0.8.3

---

## Overview

There are two backend services the frontend talks to:

| Service | What it does | Base URL |
|---|---|---|
| **Security Platform** | Login, logout, token refresh, user identity, authorization checks | `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net` |
| **Platform API (HubApi)** | Projects, members, API tokens, DB proxy | `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net` |

**The key thing to understand:** the frontend only logs in through the Security Platform. The JWT it issues is then used directly as the bearer token for HubApi too. There is no separate HubApi login.

---

## How Auth Works End-to-End

```
┌─────────┐        POST /api/v1/auth/login         ┌──────────────────────┐
│         │ ─────────────────────────────────────► │                      │
│         │ ◄──── accessToken + refreshToken ────  │  Security Platform   │
│         │                                         │                      │
│Frontend │        Any HubApi request               └──────────────────────┘
│         │  Authorization: Bearer <accessToken>
│         │ ─────────────────────────────────────► ┌──────────────────────┐
│         │ ◄──── response ────────────────────── │                      │
└─────────┘                                         │  Platform API        │
                                                    │  (HubApi)            │
                                                    └──────────────────────┘
```

1. Frontend calls **Security Platform** `POST /api/v1/auth/login` → gets `accessToken` + `refreshToken`
2. Frontend uses that **same `accessToken`** as the `Bearer` token on every HubApi request
3. HubApi validates the JWT itself — it trusts tokens issued by the Security Platform

You never call a HubApi login endpoint. There isn't one.

---

## Step 1 — Login

**Endpoint:** `POST /api/v1/auth/login` on the Security Platform

**Request:**
```json
{
  "email": "chris.moriarty@flatplanet.com",
  "password": "••••••••"
}
```

You can optionally pass `"appSlug": "dashboard-hub"` if you want app-scoped permissions included in `/auth/me` responses. It has no effect on whether login succeeds.

**Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci...",
    "refreshToken": "TUmzgLj...",
    "expiresIn": 3600,
    "user": {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "email": "chris.moriarty@flatplanet.com",
      "fullName": "Chris Moriarty",
      "companyId": "a5af2cfc-2887-4e60-942d-8c29ccf012cf"
    }
  }
}
```

**What to store:**
- `accessToken` — attach to every API request as `Authorization: Bearer <token>`. Expires in 60 minutes.
- `refreshToken` — store securely (httpOnly cookie recommended). Use it to get a new access token when it expires. **Single-use — rotated on every refresh.**
- `user.userId` and `user.companyId` — useful for display and scoping requests.

**Error cases:**

| HTTP | Meaning |
|---|---|
| `400` | Missing email or password |
| `401` | Wrong credentials |
| `403` | Account or company is suspended |
| `423` | Account temporarily locked (too many failed attempts) |
| `429` | Rate limited |

---

## Step 2 — Attach the Token to Every Request

All protected endpoints on both services require:

```
Authorization: Bearer <accessToken>
```

This is the same token for both the Security Platform and HubApi. No separate token for HubApi.

---

## Step 3 — Get the Current User

### On the Security Platform (full profile)

`GET /api/v1/auth/me` — returns the user's platform roles and app-scoped permissions.

```json
{
  "success": true,
  "data": {
    "userId": "dc88786a-...",
    "email": "chris.moriarty@flatplanet.com",
    "fullName": "Chris Moriarty",
    "roleTitle": "Platform Owner",
    "companyId": "a5af2cfc-...",
    "status": "active",
    "lastSeenAt": "2026-03-27T01:27:54Z",
    "platformRoles": ["platform_owner"],
    "appAccess": []
  }
}
```

Add `?appSlug=dashboard-hub` to get permissions for a specific app:
```json
"appAccess": [{
  "appSlug": "dashboard-hub",
  "roleName": "platform_owner",
  "permissions": ["manage_apps", "manage_users", "view_audit_log", ...]
}]
```

### On HubApi (lightweight identity check)

`GET /api/auth/me` — reads identity directly from JWT claims, no database call. Fast.

```json
{
  "success": true,
  "data": {
    "userId": "dc88786a-...",
    "email": "chris.moriarty@flatplanet.com",
    "fullName": "Chris Moriarty",
    "companyId": "a5af2cfc-...",
    "canCreateProject": false
  }
}
```

Use this on HubApi for quick identity confirmation. Use the Security Platform version when you need roles or permissions.

> `canCreateProject` comes from the JWT's `permissions` claim — not a live DB check. If a user's role changes, they need to log out and back in to see the updated value.

---

## Step 4 — Token Refresh

The access token expires after **60 minutes**. Before it expires, swap the refresh token for a new pair.

**Endpoint:** `POST /api/v1/auth/refresh` on the Security Platform

**Request:**
```json
{
  "refreshToken": "TUmzgLj..."
}
```

**Response:**
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

**Important rules:**
- The old refresh token is immediately invalidated when you call this. Store the new one.
- If you get `401` back, the refresh token is gone — send the user to the login page.
- Never retry a refresh with the same token after a `401`.

**Recommended approach:** intercept `401` responses in your HTTP client, attempt one token refresh, retry the original request with the new access token, then redirect to login if that also fails.

---

## Step 5 — Logout

**Endpoint:** `POST /api/v1/auth/logout` on the Security Platform (requires the access token)

```
POST /api/v1/auth/logout
Authorization: Bearer <accessToken>
```

Response: `200 { "success": true, "message": "Logged out successfully." }`

This revokes **all** refresh tokens for the user and ends the session. After calling this, clear both tokens from your storage and redirect to the login page.

---

## Step 6 — Check Authorization (Optional)

If you need to gate a UI element on whether the user can perform a specific action:

**Endpoint:** `POST /api/v1/authorize` on the Security Platform

```json
{
  "appSlug": "dashboard-hub",
  "resourceIdentifier": "/admin",
  "requiredPermission": "manage_users"
}
```

Response:
```json
{
  "success": true,
  "data": {
    "allowed": true,
    "roles": ["platform_owner"],
    "permissions": ["manage_apps", "manage_users", ...]
  }
}
```

`allowed: false` with a `200` status is normal — it just means the user doesn't have that permission. It is not an error.

---

## Step 7 — Projects (HubApi)

Once you have the access token, projects come from HubApi.

### List projects

`GET /api/projects` — returns only projects the user has access to.

- Regular users see only their own projects.
- Admin users (`view_all_projects` permission on `dashboard-hub`) see all projects.

```json
{
  "success": true,
  "data": [
    {
      "id": "5802c2c2-...",
      "name": "Tala",
      "description": "Tala project",
      "schemaName": "",
      "ownerId": "...",
      "isActive": true,
      "createdAt": "2026-01-15T10:30:00Z"
    }
  ]
}
```

> Fields like `appSlug`, `roleName`, `gitHubRepo`, `techStack`, and `members` are omitted when null. Do not assume their absence means an error — treat missing fields as null.

### Get a single project

`GET /api/projects/{id}`

Same shape as list items. Returns `403` if the user doesn't have access, `404` if not found.

---

## Session Timeouts

The Security Platform enforces two session limits server-side. Both return `401` when triggered:

| Timeout | Default | What happens |
|---|---|---|
| **Idle** | 30 minutes | Session unused for 30 min → `401` |
| **Absolute** | 8 hours | Session older than 8 hours → `401` |

When you receive a `401` from any endpoint and your token refresh also returns `401`, send the user back to the login page. Do not silently loop.

---

## Standard Response Envelope

Every response from both services uses the same wrapper:

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

Always check `success` first. If `false`, show `message` to the user or log `errors` for debugging.

---

## Token Types — Don't Mix Them Up

There are two completely different JWT types in this system:

| Token | Issued by | Used for | Lifetime |
|---|---|---|---|
| **Security Platform JWT** | Security Platform `/auth/login` | Frontend → Security Platform, Frontend → HubApi | 60 min |
| **HubApi API Token** | HubApi `/api/auth/api-tokens` | Claude Code → DB Proxy only | 30 days |

The frontend only ever uses the **Security Platform JWT**. The HubApi API Token is for machine-to-machine use (Claude Code, CI/CD). Never use an API Token for frontend requests — it will be rejected on most endpoints.

---

## Quick Reference

### Auth flow (happy path)

```
1.  POST  /api/v1/auth/login          → get accessToken + refreshToken
2.  GET   /api/v1/auth/me             → get user profile + roles
3.  GET   /api/projects               → list user's projects       (HubApi)
4.  GET   /api/projects/{id}          → get single project         (HubApi)
5.  POST  /api/v1/auth/refresh        → rotate tokens before expiry
6.  POST  /api/v1/auth/logout         → end session, clear tokens
```

### When you get a 401

```
401 received
  │
  ├─ Try POST /api/v1/auth/refresh
  │     ├─ 200 → store new tokens, retry original request
  │     └─ 401 → redirect to login page
  │
  └─ If no refresh token stored → redirect to login page
```

### Base URLs

```
Security Platform:  https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net
Platform API:       https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net
```

### All requests need this header (except login and refresh)

```
Authorization: Bearer <accessToken>
Content-Type: application/json
```
