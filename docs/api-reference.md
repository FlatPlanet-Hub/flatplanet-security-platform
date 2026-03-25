# FlatPlanet Security Platform — API Reference

**Version**: 1.1.0
**Base URL**: `https://<your-host>/api/v1`
**Interactive Docs (Scalar UI)**: `/scalar/v1`
**Changelog**: [CHANGELOG.md](../CHANGELOG.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Authentication](#authentication)
3. [Common Patterns](#common-patterns)
4. [Rate Limiting](#rate-limiting)
5. [Session Behavior](#session-behavior)
6. [Security Headers](#security-headers)
7. [Endpoints](#endpoints)
   - [Auth](#auth)
   - [Authorization Check](#authorization-check)
   - [Users](#users)
   - [Companies](#companies)
   - [Apps](#apps)
   - [Roles](#roles)
   - [Permissions](#permissions)
   - [Resources](#resources)
   - [Resource Types](#resource-types)
   - [User App Access](#user-app-access)
   - [User Context](#user-context)
   - [Audit Log](#audit-log)
   - [Compliance (GDPR)](#compliance-gdpr)
   - [Access Review](#access-review)
   - [Security Config](#security-config)

---

## Overview

FlatPlanet Security Platform is a centralized Identity & Access Management (IAM) service. It handles authentication, session management, role-based authorization, and compliance features (GDPR export/anonymize, ISO 27001 access review) for all FlatPlanet applications.

**Stack**: .NET 10 · PostgreSQL · Dapper · BCrypt.Net

---

## Authentication

All protected endpoints require a `Bearer` token in the `Authorization` header.

```
Authorization: Bearer <accessToken>
```

Tokens are issued by `POST /api/v1/auth/login` and rotated via `POST /api/v1/auth/refresh`.

### Token Properties

| Property | Value |
| -------- | ----- |
| Algorithm | HS256 |
| Issuer | `flatplanet-security` |
| Audience | `flatplanet-apps` |
| Access token expiry | 60 minutes (configurable) |
| Refresh token expiry | 7 days (configurable) |
| Clock skew tolerance | None — zero tolerance |

### JWT Claims

| Claim | Description |
| ----- | ----------- |
| `sub` | User ID (UUID) |
| `email` | User email address |
| `session_id` | Current session ID (UUID) |
| `platform_owner` | Present if user has platform owner role |
| `app_admin` | Present if user has app admin role |

### Authorization Policies

| Policy | Required Claim | Used By |
| ------ | -------------- | ------- |
| `PlatformOwner` | `platform_owner` | Companies, security config |
| `AdminAccess` | `platform_owner` OR `app_admin` | Most admin endpoints |

---

## Common Patterns

### Response Envelope

All responses follow this structure:

```json
{
  "success": true,
  "data": { ... }
}
```

Error responses:

```json
{
  "success": false,
  "error": "Descriptive error message"
}
```

### Pagination

Endpoints that return lists support pagination via query params.

**Request params**: `?page=1&pageSize=20`

**Response shape**:

```json
{
  "items": [...],
  "totalCount": 150,
  "page": 1,
  "pageSize": 20,
  "totalPages": 8
}
```

---

## Rate Limiting

Applied on `POST /api/v1/auth/login` only. Limits are configurable via `GET /api/v1/security/config`.

| Limit | Scope | Behavior |
| ----- | ----- | -------- |
| `rate_limit_login_per_ip_per_minute` | Per client IP | 429 Too Many Requests |
| `rate_limit_login_per_email_per_minute` | Per email address | 429 Too Many Requests |
| `max_failed_login_attempts` | Per account | 423 Account Locked |

Retry-After is not currently returned in the response header. Do not blindly retry on 429 — implement exponential backoff.

---

## Session Behavior

Sessions are tracked in the database. Every authenticated request updates `last_active_at`.

| Timeout Type | Default | Behavior |
| ------------ | ------- | -------- |
| Idle timeout | 30 minutes | Returns 401 if no activity within window |
| Absolute timeout | Based on session expiry | Returns 401 regardless of activity |

**On 401 from an authenticated endpoint**: attempt a token refresh via `POST /api/v1/auth/refresh`. If that also returns 401, the session is fully expired — redirect to login.

**Do NOT retry the original request immediately** on 401 without first refreshing. Double-requests will both fail.

---

## Security Headers

Every response includes:

| Header | Value |
| ------ | ----- |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains` |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Content-Security-Policy` | `default-src 'self'` |
| `X-XSS-Protection` | `0` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |

---

## Endpoints

---

### Auth

---

#### POST /api/v1/auth/login

Verifies credentials directly against the platform's database (bcrypt, work factor 12) and issues a JWT access token + refresh token. No external auth provider is involved.

**Auth required**: No

---

**Request**

```json
{
  "email": "jane.doe@acme.com",
  "password": "S3cur3P@ssw0rd!",
  "appSlug": "acme-portal"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `email` | string | Yes | User email. Max 256 characters. Must be valid email format. |
| `password` | string | Yes | User password. Max 128 characters. |
| `appSlug` | string | No | Application slug. When provided, restricts login to users with access to that app. Max 100 characters. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "rt_9f3a21b84c2e7d1045af...",
    "expiresIn": 3600,
    "user": {
      "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "email": "jane.doe@acme.com",
      "fullName": "Jane Doe",
      "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210"
    }
  }
}
```

**`expiresIn`** is in seconds. Store the access token and schedule a refresh before it expires.

---

**Error Responses**

| Status | Condition | Error |
| ------ | --------- | ----- |
| 400 | Invalid email format or missing required field | `"Invalid request"` |
| 401 | Wrong credentials | `"Invalid email or password"` |
| 403 | Company is suspended or inactive | `"Company account is suspended"` |
| 423 | Account locked after too many failed attempts | `"Account is locked"` |
| 429 | Rate limit exceeded (IP or email) | `"Too many login attempts"` |

---

**Notes**

- Each failed login attempt is recorded. After `max_failed_login_attempts` failures, the account locks at the session level. An admin must unlock it.
- If `appSlug` is provided and the user has no access to that app, login still succeeds — app-level access is enforced at the authorization check (`POST /api/v1/authorize`), not at login.
- A suspended company blocks all its users from logging in regardless of individual user status.

---

#### POST /api/v1/auth/logout

Ends the current session and revokes all active refresh tokens for that session.

**Auth required**: Yes (Bearer token)

---

**Request**

Empty body.

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Logged out successfully."
}
```

---

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 401 | Token is invalid, expired, or session already ended |

---

**Notes**

- After logout, the access token becomes invalid on the next request (session is marked ended in DB).
- All refresh tokens tied to the session are revoked. A new login is required to obtain a new token pair.
- Safe to call even if the session has already expired — returns 200.

---

#### POST /api/v1/auth/refresh

Issues a new access token and rotates the refresh token.

**Auth required**: No

---

**Request**

```json
{
  "refreshToken": "rt_9f3a21b84c2e7d1045af..."
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `refreshToken` | string | Yes | The refresh token received from login or previous refresh. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "rt_7c1d4f92ae308b56712e...",
    "expiresIn": 3600
  }
}
```

---

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 401 | Refresh token not found, expired, or already revoked |

---

**Notes**

- **Token rotation**: each successful refresh invalidates the old refresh token and issues a new one. Store the new token immediately.
- **Do NOT retry** a failed refresh — if the token is revoked or expired, the user must log in again.
- The client IP is recorded on each refresh for audit purposes.

---

#### GET /api/v1/auth/me

Returns the authenticated user's profile. Optionally scoped to a specific app.

**Auth required**: Yes (Bearer token)

---

**Query Params**

| Param | Required | Description |
| ----- | -------- | ----------- |
| `appSlug` | No | If provided, response includes the user's roles and permissions for that app. |

---

**Success Response** — `200 OK`

Without `appSlug`:

```json
{
  "success": true,
  "data": {
    "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "email": "jane.doe@acme.com",
    "fullName": "Jane Doe",
    "roleTitle": "Senior Engineer",
    "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
    "status": "active",
    "lastSeenAt": "2026-03-25T14:30:00Z",
    "platformRoles": [],
    "appAccess": []
  }
}
```

With `appSlug=acme-portal`:

```json
{
  "success": true,
  "data": {
    "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "email": "jane.doe@acme.com",
    "fullName": "Jane Doe",
    "roleTitle": "Senior Engineer",
    "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
    "status": "active",
    "lastSeenAt": "2026-03-25T14:30:00Z",
    "platformRoles": ["app_admin"],
    "appAccess": [
      {
        "appSlug": "acme-portal",
        "roleName": "Editor",
        "permissions": ["document:read", "document:write"]
      }
    ]
  }
}
```

---

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 401 | Token invalid or session expired |

---

---

### Authorization Check

---

#### POST /api/v1/authorize

Checks whether the authenticated user has a specific permission on a resource within an app. Every call is recorded to the audit log regardless of outcome.

**Auth required**: Yes (Bearer token)

---

**Request**

```json
{
  "appSlug": "acme-portal",
  "resourceIdentifier": "document:invoice-2026-001",
  "requiredPermission": "document:write"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `appSlug` | string | Yes | Slug of the app to check access for. |
| `resourceIdentifier` | string | Yes | The identifier of the resource as registered in the platform. |
| `requiredPermission` | string | Yes | The permission name to verify. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "granted": true
  }
}
```

---

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 401 | Token invalid or session expired |
| 403 | `granted: false` is not returned as a 403 — the body will contain `"granted": false` with a 200 |

---

**Notes**

- A 200 response does not mean access was granted. Always read the `granted` field.
- Both `granted: true` and `granted: false` are logged to the audit log with event types `authorize_allowed` and `authorize_denied`.
- Do not cache authorization responses client-side — permissions can change at any time.

---

### Users

All user management endpoints require `AdminAccess` policy unless noted.

---

#### POST /api/v1/users

Creates a new user with a bcrypt-hashed password. The platform owns credential storage — no external auth provider is involved.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
  "email": "jane.doe@acme.com",
  "fullName": "Jane Doe",
  "roleTitle": "Senior Engineer",
  "password": "S3cur3P@ssw0rd!"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `companyId` | UUID | Yes | Company the user belongs to. |
| `email` | string | Yes | Must be a valid email. Max 256 characters. Must be unique — duplicate returns 409. |
| `fullName` | string | Yes | Display name. Max 128 characters. |
| `roleTitle` | string | No | Job title or role label. Max 100 characters. |
| `password` | string | Yes | Plaintext password. Min 8 characters, max 128. Stored as bcrypt hash (work factor 12). Never returned in any response. |

---

**Success Response** — `201 Created`

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
    "email": "jane.doe@acme.com",
    "fullName": "Jane Doe",
    "roleTitle": "Senior Engineer",
    "status": "active",
    "createdAt": "2026-03-25T15:00:00Z",
    "lastSeenAt": null
  }
}
```

---

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 400 | Missing required field or validation failure (e.g., invalid email, password too short) |
| 404 | `companyId` does not exist |
| 409 | Email already registered |

---

**Notes**

- The `password` field is hashed server-side before storage. Do not pre-hash on the client.
- A newly created user is set to `active` status by default.
- Creating a user does not grant them access to any app. Use `POST /api/v1/apps/{appId}/users` to assign app access.

---

#### GET /api/v1/users

Returns a paginated list of users. Supports filtering.

**Auth required**: AdminAccess

---

**Query Params**

| Param | Required | Description |
| ----- | -------- | ----------- |
| `page` | No | Page number. Default: `1`. |
| `pageSize` | No | Items per page. Default: `20`. |
| `companyId` | No | Filter by company UUID. |
| `status` | No | Filter by status: `active`, `suspended`, or `inactive`. |
| `search` | No | Partial match on email or full name. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
        "email": "jane.doe@acme.com",
        "fullName": "Jane Doe",
        "roleTitle": "Senior Engineer",
        "status": "active",
        "createdAt": "2026-01-15T09:00:00Z",
        "lastSeenAt": "2026-03-25T14:30:00Z"
      }
    ],
    "totalCount": 42,
    "page": 1,
    "pageSize": 20
  }
}
```

---

#### GET /api/v1/users/{id}

Returns a single user with their app access details.

**Auth required**: AdminAccess

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
    "email": "jane.doe@acme.com",
    "fullName": "Jane Doe",
    "roleTitle": "Senior Engineer",
    "status": "active",
    "createdAt": "2026-01-15T09:00:00Z",
    "lastSeenAt": "2026-03-25T14:30:00Z",
    "appAccess": [
      {
        "appId": "f1e2d3c4-b5a6-7890-abcd-ef1234567890",
        "appName": "Acme Portal",
        "appSlug": "acme-portal",
        "roleName": "Editor",
        "status": "active",
        "grantedAt": "2026-01-15T09:00:00Z",
        "expiresAt": null
      }
    ]
  }
}
```

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 404 | User not found |

---

#### PUT /api/v1/users/{id}

Updates a user's profile fields.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "fullName": "Jane A. Doe",
  "roleTitle": "Lead Engineer"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `fullName` | string | Yes | User's display name. Max 200 characters. |
| `roleTitle` | string | No | Job title or role label. Max 200 characters. |

---

**Success Response** — `200 OK`

Returns the updated `UserResponse` object.

---

#### PUT /api/v1/users/{id}/status

Changes a user's status.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "status": "suspended"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `status` | string | Yes | One of: `active`, `suspended`, `inactive`. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Status updated."
}
```

---

**Notes**

- Suspending a user does not immediately end their active sessions — the session idle/absolute timeout will terminate them. To force immediate session termination, use the offboard endpoint.

---

#### POST /api/v1/users/{id}/offboard

Immediately ends all sessions, revokes all refresh tokens, and marks the user as inactive.

**Auth required**: AdminAccess

---

**Request**

Empty body.

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "User offboarded successfully."
}
```

---

**Notes**

- This is irreversible in the sense that the user must be re-activated by an admin to log in again.
- All active sessions are terminated immediately — ongoing requests with the user's token will return 401 on the next authenticated request.

---

### Companies

All company endpoints require `PlatformOwner` policy.

---

#### GET /api/v1/companies

Returns all companies.

**Auth required**: PlatformOwner

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
      "name": "Acme Corp",
      "countryCode": "US",
      "status": "active",
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ]
}
```

---

#### GET /api/v1/companies/{id}

Returns a single company.

**Auth required**: PlatformOwner

**Error Responses**: 404 if not found.

---

#### POST /api/v1/companies

Creates a new company.

**Auth required**: PlatformOwner

---

**Request**

```json
{
  "name": "Acme Corp",
  "countryCode": "US"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Company name. Max 200 characters. |
| `countryCode` | string | Yes | ISO country code. Max 10 characters. |

---

**Success Response** — `201 Created`

Returns the created `CompanyResponse`.

---

#### PUT /api/v1/companies/{id}

Updates company name and country code.

**Auth required**: PlatformOwner

**Request**: Same shape as `POST /api/v1/companies`.

**Success Response** — `200 OK`. Returns updated `CompanyResponse`.

---

#### PUT /api/v1/companies/{id}/status

Changes a company's status.

**Auth required**: PlatformOwner

---

**Request**

```json
{
  "status": "suspended"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `status` | string | Yes | One of: `active`, `suspended`, `inactive`. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Status updated."
}
```

---

**Notes — Cascade Behavior**

Suspending a company triggers a cascading operation:
1. All users under that company are bulk-suspended.
2. All active refresh tokens for those users are revoked.
3. Active sessions will return 403 on the next authenticated request (company status gate).

This is not reversible in a single step — reactivating the company does not automatically reactivate users. Each user must be re-activated individually.

---

### Apps

All app endpoints require `AdminAccess` policy.

---

#### GET /api/v1/apps

Returns all registered apps.

**Auth required**: AdminAccess

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
      "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
      "name": "Acme Portal",
      "slug": "acme-portal",
      "baseUrl": "https://portal.acme.com",
      "status": "active",
      "registeredAt": "2026-01-10T08:00:00Z"
    }
  ]
}
```

---

#### GET /api/v1/apps/{id}

Returns a single app.

**Error Responses**: 404 if not found.

---

#### POST /api/v1/apps

Registers a new app.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "companyId": "c9d8e7f6-a5b4-3210-fedc-ba9876543210",
  "name": "Acme Portal",
  "slug": "acme-portal",
  "baseUrl": "https://portal.acme.com"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `companyId` | UUID | Yes | Company that owns this app. |
| `name` | string | Yes | Display name. Max 200 characters. |
| `slug` | string | Yes | URL-safe identifier. Lowercase letters, digits, and hyphens only. Max 100 characters. Used in all app-scoped API calls. |
| `baseUrl` | string | Yes | App's base URL. Max 500 characters. |

---

**Success Response** — `201 Created`. Returns `AppResponse`.

---

**Notes**

- `slug` must be globally unique. A duplicate slug returns 409 — this is enforced at the database level via a unique constraint.
- Once set, the slug should not be changed — it is used as the identifier in authorization checks and user access grants.

---

#### PUT /api/v1/apps/{id}

Updates app details.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "name": "Acme Portal v2",
  "baseUrl": "https://portal-v2.acme.com",
  "status": "active"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Max 200 characters. |
| `baseUrl` | string | Yes | Max 500 characters. |
| `status` | string | Yes | One of: `active`, `suspended`, `inactive`. |

---

**Success Response** — `200 OK`. Returns updated `AppResponse`.

---

### Roles

All role endpoints require `AdminAccess` policy.

---

#### GET /api/v1/apps/{appId}/roles

Returns all roles for an app.

**Auth required**: AdminAccess

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "d4e5f6a7-b8c9-0123-cdef-012345678901",
      "appId": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
      "name": "Editor",
      "description": "Can read and write documents",
      "isPlatformRole": false,
      "createdAt": "2026-01-10T08:00:00Z"
    }
  ]
}
```

---

#### POST /api/v1/apps/{appId}/roles

Creates a role for an app.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "name": "Editor",
  "description": "Can read and write documents"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Role name. Max 100 characters. |
| `description` | string | No | Max 500 characters. |

---

**Success Response** — `201 Created`. Returns `RoleResponse`.

---

#### PUT /api/v1/apps/{appId}/roles/{id}

Updates a role's name and description.

**Request**: Same shape as POST.
**Success Response** — `200 OK`. Returns updated `RoleResponse`.

---

#### DELETE /api/v1/apps/{appId}/roles/{id}

Deletes a role.

**Auth required**: AdminAccess

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Role deleted."
}
```

**Notes**: Deleting a role that has active user grants will affect those users' access. Verify no users are assigned the role before deleting.

---

#### POST /api/v1/apps/{appId}/roles/{roleId}/permissions

Assigns a permission to a role.

**Auth required**: AdminAccess

---

**Request**

```json
{
  "permissionId": "e5f6a7b8-c9d0-1234-defa-123456789012"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `permissionId` | UUID | Yes | ID of the permission to assign. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Permission assigned."
}
```

---

#### DELETE /api/v1/apps/{appId}/roles/{roleId}/permissions/{permId}

Removes a permission from a role.

**Auth required**: AdminAccess

**Success Response** — `200 OK` `{ "success": true, "message": "Permission removed." }`

---

### Permissions

All permission endpoints require `AdminAccess` policy.

---

#### GET /api/v1/apps/{appId}/permissions

Returns all permissions for an app.

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "e5f6a7b8-c9d0-1234-defa-123456789012",
      "appId": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
      "name": "document:write",
      "description": "Allows creating and editing documents",
      "category": "document",
      "createdAt": "2026-01-10T08:00:00Z"
    }
  ]
}
```

---

#### POST /api/v1/apps/{appId}/permissions

Creates a permission for an app.

---

**Request**

```json
{
  "name": "document:write",
  "description": "Allows creating and editing documents",
  "category": "document"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Permission identifier. Max 100 characters. Convention: `resource:action`. |
| `description` | string | No | Max 500 characters. |
| `category` | string | Yes | Logical group for the permission. Max 100 characters. |

---

**Success Response** — `201 Created`. Returns `PermissionResponse`.

---

#### PUT /api/v1/apps/{appId}/permissions/{id}

Updates a permission.

**Request**: Same shape as POST.
**Success Response** — `200 OK`. Returns updated `PermissionResponse`.

---

### Resources

All resource endpoints require `AdminAccess` policy.

---

#### GET /api/v1/apps/{appId}/resources

Returns all resources registered for an app.

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "f6a7b8c9-d0e1-2345-efab-234567890123",
      "appId": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
      "resourceTypeId": "rt-001",
      "name": "Invoice 2026-001",
      "identifier": "document:invoice-2026-001",
      "status": "active",
      "createdAt": "2026-02-01T00:00:00Z"
    }
  ]
}
```

---

#### POST /api/v1/apps/{appId}/resources

Registers a resource.

---

**Request**

```json
{
  "resourceTypeId": "rt-001",
  "name": "Invoice 2026-001",
  "identifier": "document:invoice-2026-001"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `resourceTypeId` | string | Yes | ID of the resource type. |
| `name` | string | Yes | Human-readable label. Max 200 characters. |
| `identifier` | string | Yes | Machine-readable identifier used in `POST /api/v1/authorize`. Max 200 characters. Must be unique per app. |

---

**Success Response** — `201 Created`. Returns `ResourceResponse`.

---

**Notes**

- The `identifier` value is what you pass as `resourceIdentifier` in authorization checks. Use a consistent naming convention (e.g., `type:id`).

---

#### PUT /api/v1/apps/{appId}/resources/{id}

Updates a resource.

---

**Request**

```json
{
  "name": "Invoice 2026-001 (Revised)",
  "identifier": "document:invoice-2026-001-rev",
  "status": "active"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Max 200 characters. |
| `identifier` | string | Yes | Max 200 characters. |
| `status` | string | Yes | One of: `active`, `inactive`. |

---

**Success Response** — `200 OK`. Returns updated `ResourceResponse`.

---

### Resource Types

All resource type endpoints require `AdminAccess` policy.

Seed data provides four built-in resource types: `page`, `section`, `panel`, `api_endpoint`.

---

#### GET /api/v1/resource-types

Returns all resource types.

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    { "id": "rt-001", "name": "page", "description": "A full application page" },
    { "id": "rt-002", "name": "section", "description": "A page section" },
    { "id": "rt-003", "name": "panel", "description": "A dashboard panel" },
    { "id": "rt-004", "name": "api_endpoint", "description": "An API route" }
  ]
}
```

---

#### POST /api/v1/resource-types

Creates a custom resource type.

---

**Request**

```json
{
  "name": "report",
  "description": "A scheduled or ad-hoc report"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `name` | string | Yes | Type name. Should be lowercase. |
| `description` | string | No | Max characters not specified; keep it brief. |

---

**Success Response** — `201 Created`. Returns `ResourceTypeDto`.

---

### User App Access

Manages which users have access to which apps and under what role.

All endpoints require `AdminAccess` policy.

---

#### GET /api/v1/apps/{appId}/users

Returns all users with access to the app.

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "grant-uuid",
      "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "userEmail": "jane.doe@acme.com",
      "userFullName": "Jane Doe",
      "roleId": "d4e5f6a7-b8c9-0123-cdef-012345678901",
      "roleName": "Editor",
      "status": "active",
      "expiresAt": null
    }
  ]
}
```

---

#### POST /api/v1/apps/{appId}/users

Grants a user access to an app with a specific role.

---

**Request**

```json
{
  "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "roleId": "d4e5f6a7-b8c9-0123-cdef-012345678901",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `userId` | UUID | Yes | ID of the user to grant access to. |
| `roleId` | UUID | Yes | ID of the role to assign. Role must belong to this app. |
| `expiresAt` | datetime (ISO 8601) | No | Optional expiry for time-limited access. Null means no expiry. |

---

**Success Response** — `201 Created`. Returns `UserAccessResponse`.

---

#### PUT /api/v1/apps/{appId}/users/{userId}/role

Changes a user's role within an app.

---

**Request**

```json
{
  "roleId": "new-role-uuid-here"
}
```

**Success Response** — `200 OK` `{ "success": true, "message": "Role updated." }`

---

#### DELETE /api/v1/apps/{appId}/users/{userId}

Revokes a user's access to an app.

**Success Response** — `200 OK` `{ "success": true, "message": "Access revoked." }`

---

### User Context

---

#### GET /api/v1/apps/{appSlug}/user-context

Returns the authenticated user's roles and permissions for a specific app. Intended for frontend apps to load their own access context on startup.

**Auth required**: Yes (Bearer token)

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "appSlug": "acme-portal",
    "roleName": "Editor",
    "permissions": ["document:read", "document:write"]
  }
}
```

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 401 | Token invalid or session expired |
| 404 | App with that slug does not exist |

---

**Notes**

- Returns only the calling user's own context — not other users'.
- If the user has no access to the app, the response will return with an empty role/permissions rather than a 403. Check `permissions` before rendering gated UI.

---

### Audit Log

---

#### GET /api/v1/audit

Returns a paginated audit log. Supports filtering by user, app, event type, and date range.

**Auth required**: AdminAccess

---

**Query Params**

| Param | Required | Description |
| ----- | -------- | ----------- |
| `userId` | No | Filter by user UUID. |
| `appId` | No | Filter by app UUID. |
| `eventType` | No | Filter by event type (see list below). |
| `from` | No | Start datetime (ISO 8601). |
| `to` | No | End datetime (ISO 8601). |
| `page` | No | Default: `1`. |
| `pageSize` | No | Default: `50`. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "log-uuid",
        "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "appId": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
        "eventType": "login_success",
        "ipAddress": "203.0.113.42",
        "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)...",
        "details": "{}",
        "createdAt": "2026-03-25T14:30:00Z"
      }
    ],
    "totalCount": 2841,
    "page": 1,
    "pageSize": 50,
    "totalPages": 57
  }
}
```

---

**Event Types**

| Category | Event Types |
| -------- | ----------- |
| Authentication | `login_success`, `login_failure`, `logout`, `token_refresh`, `token_revoke` |
| Session | `session_start`, `session_end`, `session_idle_timeout`, `session_absolute_timeout` |
| Access | `role_granted`, `role_revoked`, `authorize_allowed`, `authorize_denied` |
| User | `user_created`, `user_deactivated`, `user_offboarded`, `user_anonymized` |
| Security | `account_locked`, `account_unlocked`, `company_suspended` |

---

**Notes**

- The `auth_audit_log` table has Row Level Security (RLS) enforced at the database level. Queries outside the application context will not return data without an appropriate PostgreSQL RLS policy.
- `details` is a JSON string with event-specific metadata. Schema varies by event type.

---

### Compliance (GDPR)

---

#### GET /api/v1/users/{id}/export

Exports all personal data for a user. Accessible by the user themselves or any admin.

**Auth required**: Yes (Bearer token — self-access OR AdminAccess)

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "user": {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "email": "jane.doe@acme.com",
      "fullName": "Jane Doe",
      "status": "active"
    },
    "appRoles": [
      { "appSlug": "acme-portal", "roleName": "Editor" }
    ],
    "sessions": [
      {
        "id": "session-uuid",
        "startedAt": "2026-03-20T09:00:00Z",
        "lastActiveAt": "2026-03-25T14:30:00Z",
        "ipAddress": "203.0.113.42"
      }
    ],
    "auditEvents": [
      {
        "eventType": "login_success",
        "createdAt": "2026-03-25T14:00:00Z"
      }
    ],
    "exportedAt": "2026-03-25T15:00:00Z"
  }
}
```

**Error Responses**

| Status | Condition |
| ------ | --------- |
| 403 | Non-admin user attempting to export another user's data |
| 404 | User not found |

---

#### POST /api/v1/users/{id}/anonymize

Anonymizes all PII for a user. Nulls name, email, and other personal fields. Ends all sessions and revokes all tokens.

**Auth required**: AdminAccess

---

**Request**

Empty body.

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "User data anonymized."
}
```

---

**Notes**

- **Irreversible.** The user's PII cannot be recovered after anonymization.
- All active sessions are ended immediately. Any in-flight requests using the user's token will return 401 on the next request.
- All refresh tokens are revoked.
- The user record is retained for audit integrity — only PII fields are nulled.
- Audit event `user_anonymized` is recorded.

---

### Access Review

---

#### GET /api/v1/access-review

Returns all active user access grants for ISO 27001 compliance review. Includes how long each grant has been active.

**Auth required**: AdminAccess

---

**Query Params**

| Param | Required | Description |
| ----- | -------- | ----------- |
| `companyId` | No | Filter by company UUID. |
| `appId` | No | Filter by app UUID. |
| `page` | No | Default: `1`. |
| `pageSize` | No | Default: `20`. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "grantId": "grant-uuid",
        "userId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "userEmail": "jane.doe@acme.com",
        "companyName": "Acme Corp",
        "appId": "b3c4d5e6-f7a8-9012-bcde-f12345678901",
        "appName": "Acme Portal",
        "roleName": "Editor",
        "grantedAt": "2026-01-15T09:00:00Z",
        "expiresAt": null,
        "daysSinceGranted": 69
      }
    ],
    "totalCount": 18,
    "page": 1,
    "pageSize": 20,
    "totalPages": 1
  }
}
```

---

**Notes**

- `daysSinceGranted` is calculated at query time. Use this to flag stale access grants for review.
- `expiresAt: null` means the grant has no expiry — these should be reviewed periodically.

---

### Security Config

All security config endpoints require `PlatformOwner` policy.

---

#### GET /api/v1/security/config

Returns all security configuration entries.

**Auth required**: PlatformOwner

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "data": [
    { "key": "rate_limit_login_per_ip_per_minute", "value": "10" },
    { "key": "rate_limit_login_per_email_per_minute", "value": "5" },
    { "key": "max_failed_login_attempts", "value": "5" },
    { "key": "session_idle_timeout_minutes", "value": "30" }
  ]
}
```

---

#### PUT /api/v1/security/config/{key}

Updates a security configuration value.

**Auth required**: PlatformOwner

---

**Request**

```json
{
  "value": "10"
}
```

**Fields**

| Field | Type | Required | Description |
| ----- | ---- | -------- | ----------- |
| `value` | string | Yes | New value for the config key. All values are stored as strings. |

---

**Success Response** — `200 OK`

```json
{
  "success": true,
  "message": "Config updated."
}
```

---

**Notes**

- Changes to rate limiting and lockout config take effect immediately on the next request.
- Changes to `session_idle_timeout_minutes` apply to **new sessions only** — existing active sessions retain their original idle timeout value stored in the `sessions` table.
- There is no input validation on `value` for config keys beyond "string required" — passing a non-numeric value for a numeric config key (e.g., `"abc"` for a rate limit) will not error at write time but will cause runtime failures.

---

## Appendix

### Error Format Reference

All error responses follow this structure:

```json
{
  "success": false,
  "error": "Human-readable error message"
}
```

### HTTP Status Code Reference

| Code | Meaning in this API |
| ---- | ------------------- |
| 200 | Success |
| 201 | Resource created |
| 400 | Invalid input (missing field, failed validation) |
| 401 | Token invalid, expired, or session ended |
| 403 | Authenticated but not authorized for this action |
| 404 | Resource not found |
| 409 | Unique constraint violation — duplicate email, slug, role name, or any other unique field |
| 422 | Business rule violation |
| 423 | Account locked |
| 429 | Rate limit exceeded |
| 500 | Server error |

### CORS

Allowed origins are configured per environment in `appsettings.json` under `Cors:AllowedOrigins`.

Default development origins:
- `http://localhost:3000`
- `http://localhost:5173`
- `http://localhost:4200`

### Versioning

The current API version is `v1`, reflected in all endpoint paths (`/api/v1/...`). The current platform release is `1.1.0`.

When a breaking change is introduced, a new version path (`/api/v2/...`) will be added. `v1` will remain available during a documented deprecation window. Track changes in [CHANGELOG.md](../CHANGELOG.md).
