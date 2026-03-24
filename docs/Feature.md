# FlatPlanet.Security — Central Security Platform (Core Build)

## What This Is

The core authentication and authorization service for all Flat Planet applications. Handles:

1. User login via Supabase Auth
2. Retrieve user roles and permissions from the database
3. Authorization checks — can this user access this resource?
4. Connect user to their allowed apps

This is the foundation. Policy layer, verification, attendance, compliance — all come later.

## Architecture

```
User → Login (Supabase Auth)
         ↓
FlatPlanet.Security API (.NET 10)
    ├── Verify credentials via Supabase Auth
    ├── Get user roles + permissions from DB
    ├── Issue JWT
    └── Return user context to the calling app

Connected apps:
├── Dashboard Hub → login, get roles, get allowed projects, check GitHub org (app's own concern)
├── Tala → login, get roles, check resource access
└── Future apps → same pattern
```

## Tech Stack

* .NET 10 Web API
* Supabase Auth (identity — password hashing, email verification, OAuth)
* Supabase Postgres (roles, permissions, access)
* Npgsql + Dapper
* JWT Bearer authentication

## Design Principles (from Schema v0.1)

* Everything that might vary — varies through data, not structure
* Roles are defined per app — the platform does not prescribe role names
* Employment relationship (company) is separate from access relationship (app roles)
* granted\_by is always recorded
* No row in user\_app\_roles = no access. Default is always denial.

\---

## DATABASE SCHEMA (Core Only)

### companies (AGREED — from schema doc)

```sql
CREATE TABLE companies (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    name TEXT NOT NULL,
    country\_code TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',
    created\_at TIMESTAMPTZ DEFAULT now()
);
```

### users (AGREED — from schema doc)

```sql
CREATE TABLE users (
    id UUID PRIMARY KEY,                  -- matches Supabase Auth uid
    company\_id UUID NOT NULL REFERENCES companies(id),
    email TEXT UNIQUE NOT NULL,           -- matches Supabase Auth email
    full\_name TEXT NOT NULL,
    role\_title TEXT,                       -- job title, not platform role
    status TEXT NOT NULL DEFAULT 'active', -- active / inactive / suspended
    created\_at TIMESTAMPTZ DEFAULT now(),
    last\_seen\_at TIMESTAMPTZ              -- updated on every successful auth
);
```

### apps (AGREED — from schema doc)

```sql
CREATE TABLE apps (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    company\_id UUID NOT NULL REFERENCES companies(id),
    name TEXT NOT NULL,
    slug TEXT UNIQUE NOT NULL,
    base\_url TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',
    registered\_at TIMESTAMPTZ DEFAULT now(),
    registered\_by UUID NOT NULL
);
```

### resource\_types (AGREED — from schema doc)

```sql
CREATE TABLE resource\_types (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    name TEXT UNIQUE NOT NULL,
    description TEXT
);

INSERT INTO resource\_types (name, description) VALUES
    ('page', 'A full HTML page or route'),
    ('section', 'A named section within a page'),
    ('panel', 'A UI panel — may be visible to some roles and hidden from others'),
    ('api\_endpoint', 'A serverless function or API route');
```

### resources (AGREED — from schema doc)

```sql
CREATE TABLE resources (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    app\_id UUID NOT NULL REFERENCES apps(id),
    resource\_type\_id UUID NOT NULL REFERENCES resource\_types(id),
    name TEXT NOT NULL,
    identifier TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',
    created\_at TIMESTAMPTZ DEFAULT now()
);
```

### roles (AGREED — from schema doc)

```sql
CREATE TABLE roles (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    app\_id UUID REFERENCES apps(id),      -- null for platform-level roles
    name TEXT NOT NULL,
    description TEXT,
    is\_platform\_role BOOLEAN DEFAULT false,
    created\_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app\_id, name)
);

-- Platform-level roles
INSERT INTO roles (name, description, is\_platform\_role) VALUES
    ('platform\_owner', 'Full platform access across all apps and companies', true),
    ('app\_admin', 'Admin access within a specific app', true);
```

### user\_app\_roles (AGREED — from schema doc)

```sql
CREATE TABLE user\_app\_roles (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    user\_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app\_id UUID NOT NULL REFERENCES apps(id),
    role\_id UUID NOT NULL REFERENCES roles(id),
    granted\_at TIMESTAMPTZ DEFAULT now(),
    granted\_by UUID NOT NULL REFERENCES users(id),
    expires\_at TIMESTAMPTZ,               -- nullable, for temporary access
    status TEXT NOT NULL DEFAULT 'active', -- active / suspended / expired
    UNIQUE(user\_id, app\_id, role\_id)
);
```

### permissions (NEW — needed for granular access control)

```sql
CREATE TABLE permissions (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    app\_id UUID REFERENCES apps(id),      -- null for platform-level permissions
    name TEXT NOT NULL,
    description TEXT,
    category TEXT NOT NULL,               -- e.g. 'data', 'schema', 'admin', 'ui'
    created\_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app\_id, name)
);

-- Platform-level permissions
INSERT INTO permissions (name, description, category) VALUES
    ('manage\_companies', 'Create and edit company records', 'admin'),
    ('manage\_users', 'Create, edit, and deactivate users', 'admin'),
    ('manage\_apps', 'Register and configure apps', 'admin'),
    ('manage\_roles', 'Create and edit roles and permissions', 'admin'),
    ('view\_audit\_log', 'View auth and access audit logs', 'admin'),
    ('manage\_resources', 'Register and configure protected resources', 'admin');
```

### role\_permissions (NEW — maps roles to permissions)

```sql
CREATE TABLE role\_permissions (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    role\_id UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission\_id UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    granted\_at TIMESTAMPTZ DEFAULT now(),
    granted\_by UUID REFERENCES users(id),
    UNIQUE(role\_id, permission\_id)
);
```

### sessions (NEW — needed for login tracking)

```sql
CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    user\_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app\_id UUID REFERENCES apps(id),
    ip\_address TEXT,
    user\_agent TEXT,
    started\_at TIMESTAMPTZ DEFAULT now(),
    last\_active\_at TIMESTAMPTZ DEFAULT now(),
    expires\_at TIMESTAMPTZ NOT NULL,
    is\_active BOOLEAN DEFAULT true,
    ended\_reason TEXT                      -- logout / idle\_timeout / absolute\_timeout / revoked
);

CREATE INDEX idx\_sessions\_user ON sessions(user\_id);
CREATE INDEX idx\_sessions\_active ON sessions(is\_active) WHERE is\_active = true;
```

### refresh\_tokens (NEW — needed for JWT refresh)

```sql
CREATE TABLE refresh\_tokens (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    user\_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session\_id UUID REFERENCES sessions(id) ON DELETE CASCADE,
    token\_hash TEXT UNIQUE NOT NULL,       -- SHA256 hash, never plaintext
    expires\_at TIMESTAMPTZ NOT NULL,
    revoked BOOLEAN DEFAULT false,
    revoked\_at TIMESTAMPTZ,
    revoked\_reason TEXT,
    created\_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx\_refresh\_tokens\_user ON refresh\_tokens(user\_id);
```

### auth\_audit\_log (NEW — essential from day one)

```sql
CREATE TABLE auth\_audit\_log (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    user\_id UUID REFERENCES users(id),
    app\_id UUID REFERENCES apps(id),
    event\_type TEXT NOT NULL,
    ip\_address TEXT,
    user\_agent TEXT,
    details JSONB,
    created\_at TIMESTAMPTZ DEFAULT now()
);

-- Core event types:
-- login\_success, login\_failure, logout,
-- token\_refresh, token\_revoke,
-- session\_start, session\_end, session\_idle\_timeout, session\_absolute\_timeout,
-- role\_granted, role\_revoked,
-- user\_created, user\_deactivated, user\_offboarded,
-- account\_locked, account\_unlocked

CREATE INDEX idx\_auth\_audit\_user ON auth\_audit\_log(user\_id);
CREATE INDEX idx\_auth\_audit\_type ON auth\_audit\_log(event\_type);
CREATE INDEX idx\_auth\_audit\_created ON auth\_audit\_log(created\_at);

-- ISO 27001: Immutable — no UPDATE or DELETE allowed.
-- Enforce via Postgres policy or application-level restriction:
REVOKE UPDATE, DELETE ON auth\_audit\_log FROM PUBLIC;
```

### login\_attempts (NEW — needed for account lockout / ISO 27001)

```sql
CREATE TABLE login\_attempts (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    email TEXT NOT NULL,
    ip\_address TEXT,
    success BOOLEAN NOT NULL,
    attempted\_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx\_login\_attempts\_email ON login\_attempts(email);
CREATE INDEX idx\_login\_attempts\_ip ON login\_attempts(ip\_address);
CREATE INDEX idx\_login\_attempts\_time ON login\_attempts(attempted\_at);

-- Used to enforce:
-- - 5 failed attempts per email → lock account for 30 min
-- - 5 failed attempts per IP per minute → rate limit
-- Cleanup: delete rows older than 24 hours via scheduled job
```

### security\_config (NEW — configurable security parameters / ISO 27001)

```sql
CREATE TABLE security\_config (
    id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),
    config\_key TEXT UNIQUE NOT NULL,
    config\_value TEXT NOT NULL,
    description TEXT,
    updated\_at TIMESTAMPTZ DEFAULT now(),
    updated\_by UUID REFERENCES users(id)
);

INSERT INTO security\_config (config\_key, config\_value, description) VALUES
    ('session\_idle\_timeout\_minutes', '30', 'Session idle timeout in minutes'),
    ('session\_absolute\_timeout\_minutes', '480', 'Max session duration (8 hours)'),
    ('max\_concurrent\_sessions', '3', 'Max active sessions per user'),
    ('max\_failed\_login\_attempts', '5', 'Lock account after N failed logins'),
    ('lockout\_duration\_minutes', '30', 'Account lockout duration'),
    ('jwt\_access\_expiry\_minutes', '60', 'JWT access token expiry'),
    ('jwt\_refresh\_expiry\_days', '7', 'Refresh token expiry'),
    ('audit\_log\_retention\_days', '365', 'Minimum audit log retention'),
    ('rate\_limit\_login\_per\_ip\_per\_minute', '5', 'Max login attempts per IP per minute'),
    ('rate\_limit\_login\_per\_email\_per\_minute', '10', 'Max login attempts per email per minute');
```

\---

## API ENDPOINTS (Core Only)

### Authentication

* `POST /api/v1/auth/login` — Login via Supabase Auth

```json
  { "email": "user@example.com", "password": "password" }
  ```

  Backend:

  1. Call Supabase Auth to verify credentials
  2. Lookup user in `users` table by Supabase Auth uid
  3. Check user status is 'active'
  4. Create session in `sessions`
  5. Issue JWT + refresh token
  6. Update `last\_seen\_at`
  7. Log `login\_success` in `auth\_audit\_log`
  8. Return JWT + refresh token + user context
* `POST /api/v1/auth/logout` — End session
* `POST /api/v1/auth/refresh` — Rotate refresh token, extend session
* `GET /api/v1/auth/me` — Current user profile + roles + permissions

  ### Authorization Check (THE CORE ENDPOINT)

* `POST /api/v1/authorize` — Check if a user can access a resource

  ```json
  {
    "userId": "user-uuid",
    "appSlug": "dashboard-hub",
    "resourceIdentifier": "/admin",
    "requiredPermission": "manage\_users"
  }
  ```

  Backend:

  1. Lookup `user\_app\_roles` for user + app
  2. Check role status is 'active' and not expired
  3. Lookup `role\_permissions` for the user's role(s)
  4. Check if required permission is in the user's permissions
  5. Log the check in `auth\_audit\_log`
  6. Return:

  ```json
  {
    "allowed": true,
    "roles": \["app\_admin"],
    "permissions": \["manage\_users", "view\_audit\_log"]
  }
  ```

  ### User Context (called by connected apps after login)

* `GET /api/v1/apps/{appSlug}/user-context` — Get user's roles, permissions, and allowed apps

  ```json
  {
    "success": true,
    "data": {
      "userId": "user-uuid",
      "email": "chris@example.com",
      "fullName": "Chris Moriarty",
      "companyName": "Flat Planet Australia",
      "roles": \["developer"],
      "permissions": \["read", "write", "ddl"],
      "allowedApps": \[
        { "appId": "app-uuid", "appSlug": "dashboard-hub", "appName": "Dashboard Hub" },
        { "appId": "app-uuid-2", "appSlug": "tala", "appName": "Tala" }
      ]
    }
  }
  ```

  Backend:

  1. Get user from JWT `sub` claim
  2. Query all `user\_app\_roles` for the user where status = 'active' and not expired
  3. For the requested app, resolve `role\_permissions` → `permissions`
  4. Get list of all apps the user has active roles in (= `allowedApps`)
  5. Return everything

  ### Companies (platform\_owner only)

* `POST /api/v1/companies` — Create company
* `GET /api/v1/companies` — List companies
* `GET /api/v1/companies/{id}` — Get company detail
* `PUT /api/v1/companies/{id}` — Update company
* `PUT /api/v1/companies/{id}/status` — Suspend/activate (cascades to users if suspended)

  ### Apps (platform\_owner or app\_admin)

* `POST /api/v1/apps` — Register new app
* `GET /api/v1/apps` — List apps
* `GET /api/v1/apps/{id}` — Get app detail
* `PUT /api/v1/apps/{id}` — Update app

  ### Resources (app\_admin)

* `POST /api/v1/apps/{appId}/resources` — Register protected resource
* `GET /api/v1/apps/{appId}/resources` — List resources
* `PUT /api/v1/apps/{appId}/resources/{id}` — Update resource

  ### Resource Types

* `GET /api/v1/resource-types` — List all types
* `POST /api/v1/resource-types` — Add new type (platform\_owner only)

  ### Roles (app\_admin for app roles, platform\_owner for platform roles)

* `POST /api/v1/apps/{appId}/roles` — Create role
* `GET /api/v1/apps/{appId}/roles` — List roles
* `PUT /api/v1/apps/{appId}/roles/{id}` — Update role
* `DELETE /api/v1/apps/{appId}/roles/{id}` — Delete role (only if no users assigned)

  ### Permissions (app\_admin)

* `POST /api/v1/apps/{appId}/permissions` — Create permission
* `GET /api/v1/apps/{appId}/permissions` — List permissions
* `PUT /api/v1/apps/{appId}/permissions/{id}` — Update permission
* `POST /api/v1/apps/{appId}/roles/{roleId}/permissions` — Assign permissions to role
* `DELETE /api/v1/apps/{appId}/roles/{roleId}/permissions/{permId}` — Remove permission from role

  ### User Access Management (app\_admin or manage\_users permission)

* `POST /api/v1/apps/{appId}/users` — Grant user access with role
* `GET /api/v1/apps/{appId}/users` — List users with access to app
* `PUT /api/v1/apps/{appId}/users/{userId}/role` — Change user's role
* `DELETE /api/v1/apps/{appId}/users/{userId}` — Revoke access

  ### User Management (platform\_owner or manage\_users)

* `GET /api/v1/users` — List all users (search, filter, pagination)
* `GET /api/v1/users/{id}` — Get user detail + all app access
* `PUT /api/v1/users/{id}` — Update user details
* `PUT /api/v1/users/{id}/status` — Activate/deactivate/suspend

  ### Audit Log (view\_audit\_log permission)

* `GET /api/v1/audit` — Query auth audit log
Query params: `?userId={uuid}\&appId={uuid}\&eventType={type}\&from={date}\&to={date}\&page=1\&pageSize=50`

  ### User Data (GDPR / ISO 27001)

* `GET /api/v1/users/{id}/export` — Export all data for a user (admin or self)
Returns: user record, all app roles, all sessions, all audit events for that user
* `POST /api/v1/users/{id}/anonymize` — Anonymize user PII (platform\_owner only)
Replaces email, full\_name, role\_title with anonymized values. Preserves audit trail with anonymized references.

  ### Offboarding (manage\_users permission)

* `POST /api/v1/users/{id}/offboard` — Full user offboarding
Backend:

  1. Set user status = 'inactive'
  2. Revoke all active sessions
  3. Revoke all refresh tokens
  4. Suspend all `user\_app\_roles`
  5. Log `user\_offboarded` in audit log

  ### Health + Security Config

* `GET /health` — Service health check (no auth required)
* `GET /api/v1/security/config` — List all security config values (platform\_owner only)
* `PUT /api/v1/security/config/{key}` — Update security config value (platform\_owner only)

  \---

  ## JWT TOKEN STRUCTURE

  ```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "full\_name": "Chris Moriarty",
  "company\_id": "company-uuid",
  "iss": "flatplanet-security",
  "aud": "flatplanet-apps",
  "iat": 1234567890,
  "exp": 1234571490
}
```

  App-specific roles and permissions are NOT in the JWT — they change too often. Apps call `GET /api/apps/{appSlug}/user-context` or `POST /api/authorize` to get them at runtime. This keeps the JWT small and avoids stale permissions.

  \---

  ## LOGIN FLOW (end to end — with ISO 27001 controls)

1. User enters email + password in the app
2. App sends credentials to `POST /api/v1/auth/login`
3. **Rate limit check** — if IP or email exceeded limit → reject with 429
4. **Account lockout check** — query `login\_attempts` for recent failures → if locked → reject with 423
5. Call Supabase Auth to verify credentials
6. If invalid → log `login\_failure` in audit + record in `login\_attempts` → return 401
7. If valid → lookup user in `users` table
8. Check user status = 'active' → if not → reject with 403
9. Check company status = 'active' → if not → reject with 403
10. **Session limit check** — count active sessions → if >= max → end oldest or reject
11. Create session in `sessions` with idle + absolute timeout from `security\_config`
12. Issue JWT + refresh token (hash stored in `refresh\_tokens`)
13. Log `login\_success` + `session\_start` in `auth\_audit\_log`
14. Update `last\_seen\_at` on user
15. Record success in `login\_attempts`
16. App calls `GET /api/v1/apps/{appSlug}/user-context` with JWT
17. Security API returns roles, permissions, allowed apps
18. App renders based on permissions

    \---

    ## SECURITY REQUIREMENTS (Core — ISO 27001 Aligned)

    ### A.5 / A.9 — Access Control

1. Default deny — no access without explicit `user\_app\_roles` grant
2. RBAC with granular permissions per app
3. Temporary access with expiry dates on `user\_app\_roles`
4. All grants tracked with `granted\_by`
5. User deactivation cascades — revoke all sessions, refresh tokens, active grants
6. Session idle timeout — configurable, default 30 min (check `last\_active\_at` on every request)
7. Session absolute timeout — configurable, default 8 hours
8. Max concurrent sessions per user — configurable, default 3 (reject new login if exceeded, or end oldest)
9. Periodic access review — endpoint to list all active grants with age for admin review

   ### A.10 — Cryptography

10. JWT tokens: 60 minute expiry, signed with HMAC-SHA256
11. Refresh tokens: 7 day expiry, stored as SHA256 hash only — never plaintext
12. TLS required (HTTPS only) — reject HTTP, set HSTS header
13. Supabase Auth handles password hashing (bcrypt)
14. GitHub org check token stored server-side in config — never exposed to clients
15. JWT secret key rotation policy — rotate every 90 days, support dual-key validation during rollover

    ### A.12 — Operations Security

16. Immutable append-only audit log — no UPDATE or DELETE on `auth\_audit\_log`
17. Rate limiting: login endpoint — 5 attempts per minute per IP, 10 per minute per email
18. Account lockout — lock after 5 consecutive failed logins, auto-unlock after 30 min
19. Audit log retention — minimum 365 days, configurable
20. Request/response logging on all API endpoints — mask sensitive fields (passwords, tokens)
21. Health check endpoint at `GET /health` — returns service status without auth

    ### A.13 — Communications Security

22. CORS — strict allowed origins, configured per app (from `apps.base\_url`)
23. Security headers on all responses:

    * `Strict-Transport-Security: max-age=31536000; includeSubDomains`
    * `X-Content-Type-Options: nosniff`
    * `X-Frame-Options: DENY`
    * `Content-Security-Policy: default-src 'self'`
    * `X-XSS-Protection: 0` (rely on CSP instead)
    * `Referrer-Policy: strict-origin-when-cross-origin`
24. API versioning — prefix all routes with `/api/v1/` for backward compatibility

    ### A.14 — System Acquisition \& Development

25. Input validation on all endpoints — email format, UUID format, string length limits
26. Parameterized queries — Dapper parameterization, never string concatenation
27. SQL injection prevention — validate all identifiers
28. Dependency scanning — run `dotnet list package --vulnerable` in CI/CD
29. Global exception handler — never expose stack traces or internal errors to clients

    ### A.18 — Compliance (minimum for core)

30. Audit log covers all auth events — login, logout, failure, role grant/revoke, user create/deactivate
31. All timestamps in UTC (timestamptz)
32. User data export endpoint — `GET /api/users/{id}/export` returns all data for that user (GDPR right-of-access)
33. User data anonymization endpoint — `POST /api/users/{id}/anonymize` replaces PII with anonymized values (GDPR right-to-erasure, preserves audit trail)

    ### A.7 — Human Resource Security

34. User offboarding — deactivate user → revoke all sessions → revoke all refresh tokens → suspend all `user\_app\_roles` → log event
35. Suspended company cascades — suspending a company suspends all users in that company

    \---

    ## appsettings.json

    ```json
{
  "Supabase": {
    "Url": "https://your-project.supabase.co",
    "ServiceRoleKey": "YOUR\_SERVICE\_ROLE\_KEY",
    "JwtSecret": "YOUR\_SUPABASE\_JWT\_SECRET",
    "DbHost": "aws-0-us-east-1.pooler.supabase.com",
    "DbPort": 6543,
    "DbName": "postgres",
    "DbUser": "postgres.YOUR\_PROJECT\_REF",
    "DbPassword": "YOUR\_DB\_PASSWORD"
  },
  "Jwt": {
    "Issuer": "flatplanet-security",
    "Audience": "flatplanet-apps",
    "SecretKey": "CHANGE\_ME\_MIN\_32\_CHARACTERS\_LONG!!",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 7
  }
}
```

    \---

    ## TABLES NOT IN THIS BUILD (future)

* oauth\_providers + user\_oauth\_links (OAuth social login)
* user\_mfa (2FA)
* api\_tokens (Claude/service tokens)
* resource\_policies (session config per resource, 2FA requirements, allowed hours)
* verification\_events (identity verification)
* attendance\_events (payroll tracking)
* data\_classification, consent\_records, incident\_log (extended compliance)

  ## TABLES IN THIS BUILD (14 total)

  ### From Schema Doc v0.1 (agreed):

1. companies
2. users
3. apps
4. resource\_types
5. resources
6. roles
7. user\_app\_roles

   ### New (required for core + ISO 27001):

8. permissions
9. role\_permissions
10. sessions
11. refresh\_tokens
12. auth\_audit\_log
13. login\_attempts
14. security\_config

