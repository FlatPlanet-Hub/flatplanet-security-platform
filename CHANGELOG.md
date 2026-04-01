# Changelog

All notable changes to the FlatPlanet Security Platform are documented here.

---

## [1.2.2] — 2026-04-01

DB constraint fixes and permission seeding for dashboard-hub.

---

### Fixes

- **Fix: re-grant upsert (BUG-01)** — `POST /api/v1/apps/{appId}/users` now upserts instead of plain INSERT; re-granting a previously revoked user reactivates the existing row instead of returning `409` (PR #29)
- **DB: V11 migration** — drops `granted_by` FK constraint and NOT NULL on `user_app_roles`; `UserAppRole.GrantedBy` updated to `Guid?`; service-token callers (sentinel GUID) no longer require a matching users row (PR #30)
- **DB: V12 migration** — seeds `view_projects` permission for `dashboard-hub`; grants to all dashboard-hub app roles and to `platform_owner` platform role; idempotent via `ON CONFLICT DO NOTHING` (PR #31)
- **DB: V13 migration** — drops `granted_by` FK constraint and NOT NULL on `role_permissions` (mirrors V11); re-runs V12 role_permissions grants which had failed due to the NOT NULL constraint (PR #33)

---

## [1.2.1] — 2026-03-27

Validation envelope fix, ServiceToken registration, and seed data cleanup.

---

### Fixes

- **Fix: `InvalidModelStateResponseFactory`** — validation errors (400) now return the platform envelope `{ success: false, message: "Validation failed.", errors: { field: ["msg"] } }` instead of ASP.NET's default `{ title, errors }` shape (PR #22)
- **Fix: `ServiceTokenAuthHandler` registered** — `ServiceTokenOptions` now configured in DI; `ServiceToken` scheme added to both `PlatformOwner` and `AdminAccess` policies so server-to-server calls work correctly
- **Fix: `appsettings.json`** — `ServiceToken` section added with placeholder value
- **Fix: `seed_test_data.sql`** — simplified to Development Hub only (`dashboard-hub`); removed `platform-api` and `tala` test apps; updated Development Hub URL to `https://fpdevelopmenthub.netlify.app`
- **Docs: `api-reference.md` → `security-api-reference.md`** — renamed for clarity; validation error shape documented

---

## [1.2.0] — 2026-03-27

Controller refactor, cold-start fix, and complete API reference documentation.

---

### Phase 11 — Controller Refactor & Performance

- **`ApiController` base class** — extracted `OkData`, `Created201`, `CreatedData`, `OkMessage`, `FailBadRequest`, `FailUnauthorized`, `GetUserId`, `TryGetUserId`, `TryGetSessionId` helpers; all 16 controllers now extend it — eliminates inline `new { success, ... }` and duplicated claim extraction across the codebase
- **`AuthService` parallelism** — `LoadConfigAsync()` extracted; 3 rate-limit checks + 4 post-login operations parallelized with `Task.WhenAll`; reduces login latency under concurrent load
- **`CompanyService` parallelism** — `UpdateStatusAsync` user-loop parallelized; inactive user processing runs concurrently
- **Fix: `company_deactivated` audit event** — `UpdateStatusAsync` was logging `company_suspended` for both `suspended` and `inactive`; now correctly fires `CompanyDeactivated` for `inactive` status
- **Fix: DB connection cold-start** — `IDbConnectionFactory` now pre-warms the pool at startup via `SELECT 1`; eliminates ~20s first-request delay after deployment
- **Fix: `UserAppAccessDto` permissions** — `GetDetailsByUserIdAsync` now joins `role_permissions` so user detail response includes permission names per app role (PR #21)
- **Docs: `docs/api-reference.md`** — complete API reference covering all 42 endpoints with accurate request/response schemas, field tables, realistic examples, error cases, and edge case notes; includes Service Token auth, `PermissionResponse.category`, `UserContextResponse` full shape, `UserAccessResponse.userFullName`, resource status values, and compliance export shape

---

## [1.1.0] — 2026-03-25

Standalone authentication release. Removes Supabase Auth dependency — platform owns the full auth stack.

---

### Phase 10 — Standalone Authentication (PR #12)

- **Removed Supabase Auth** — `ISupabaseAuthClient`, `SupabaseAuthClient`, `SupabaseOptions` deleted entirely
- **bcrypt password verification** — `LoginAsync` verifies credentials directly against `users.password_hash` (work factor 12); no external HTTP call on login
- **`POST /api/v1/users`** — admins can now create users with a hashed password (returns `201 Created`)
- **`DatabaseOptions`** replaces `SupabaseOptions` — DB config decoupled from auth provider; `appsettings.json` restructured
- **Fix: duplicate unique values return 409** — `ExceptionHandlingMiddleware` now catches `PostgresException` SqlState `23505`; covers duplicate emails, slugs, role names, and all unique constraints
- **Fix: `IPasswordHasher` registered as Singleton** — stateless, no reason to allocate per request
- **DB migration V5** — `ALTER TABLE users ADD COLUMN password_hash TEXT NOT NULL`; `id` column gets `DEFAULT gen_random_uuid()`
- 25/25 tests passing

---

## [1.0.0] — 2026-03-25

Production release. Complete authentication, authorization, admin management, audit/compliance, and security hardening.

---

### Phase 9 — Frontend Setup (PR #10)

- Added CORS policy (`AllowedOrigins` configurable via `appsettings.json`)
- Added OpenAPI documentation via Scalar UI at `/scalar/v1`
- Added `appsettings.Development.json` with local dev defaults

---

### Phase 8 — Spec Gap Hardening (PR #11)

- **Input validation** — `[Required]`, `[MaxLength]`, `[EmailAddress]`, `[RegularExpression]` added to all request DTOs (Login, Refresh, Company, App, Role, Permission, Resource, User)
- **Per-email rate limiting** — `LoginAsync` now enforces `rate_limit_login_per_email_per_minute` in addition to per-IP
- **Company status gate on login** — suspended or inactive companies block login with `403 Forbidden`
- **Enriched `/me` endpoint** — `GET /api/v1/auth/me?appSlug=X` now returns platform roles and app-scoped permissions
- **Paginated user list** — `GET /api/v1/users` supports `page`, `pageSize`, `companyId`, `status`, `search` query params
- **User detail endpoint** — `GET /api/v1/users/{id}` returns full app access details (app slug, role, permissions)
- **Company suspend cascade** — suspending a company bulk-suspends all users and revokes all their refresh tokens
- **ISO 27001 access review** — `GET /api/v1/access-review` returns all active grants with days-since-granted, filterable by company/app (admin only)
- **Session idle/absolute timeout middleware** — `SessionValidationMiddleware` enforces idle and absolute session expiry on every authenticated request
- **Anonymize user hardening** — `AnonymizeUserAsync` now ends all active sessions and revokes all refresh tokens
- **Fix: `ComplianceController.Export` self-access** — changed class-level `[Authorize(Policy="AdminAccess")]` to `[Authorize]`; non-admin users can now export their own data; `Anonymize` remains admin-only
- **Fix: Company audit log FK violation** — `UpdateStatusAsync` audit log entries now use `UserId = null` (was incorrectly storing company GUID in user FK column)
- **DB migration V4** — `ALTER TABLE sessions ADD COLUMN idle_timeout_minutes INTEGER NOT NULL DEFAULT 30`

---

### Phase 7 — Unit Tests

- 24 passing unit tests across `AuthServiceTests`, `AuthorizationServiceTests`, `CompanyServiceTests`, `SessionValidationMiddlewareTests`
- Test project references both Application and API projects
- Moq-based mocking for all repository and service dependencies

---

### Phase 6 — Audit, Compliance & Security Config

- `GET /api/v1/audit` — paginated audit log with filters (userId, eventType, dateFrom, dateTo)
- `GET /api/v1/users/{id}/export` — GDPR data export (self or admin)
- `POST /api/v1/users/{id}/anonymize` — GDPR anonymization (admin only); nulls PII fields
- `GET /api/v1/security-config` — list all security config entries
- `PUT /api/v1/security-config/{key}` — update a config value (platform owner only)
- DB migration V3 — `FORCE ROW LEVEL SECURITY` + SELECT policy on `auth_audit_log`

---

### Phase 5 — Admin CRUD

- Companies: `GET`, `POST`, `PUT`, `PATCH /status`
- Apps: `GET`, `POST`, `PUT`, `DELETE`
- Roles: `GET`, `POST`, `PUT`, `DELETE`
- Permissions: `GET`, `POST`, `DELETE`
- Resources: `GET`, `POST`, `PUT`, `DELETE`
- Users: `GET` (paged), `GET /{id}`, `POST`, `PUT`, `PATCH /status`, `DELETE`
- User app role assignment: `POST /api/v1/users/{id}/apps/{appId}/roles`
- All admin endpoints protected by `[Authorize(Policy = "AdminAccess")]` or `[Authorize(Policy = "PlatformOwner")]`

---

### Phase 4 — Authorization Check & User Context

- `POST /api/v1/authorize` — checks if a user has a permission on a resource; logs to audit
- `GET /api/v1/users/{id}/context` — returns user's app roles and permissions for a given app
- IDOR fix: `userId` derived from JWT, not from request body
- DB migration V2 — indexes on `user_app_roles`, `role_permissions` seed for platform roles, partial unique indexes, RLS on `auth_audit_log`

---

### Phase 3 — Authentication

- `POST /api/v1/auth/login` — Supabase-backed sign-in; issues JWT access token + refresh token; enforces rate limits and account lockout
- `POST /api/v1/auth/logout` — revokes session and all refresh tokens
- `POST /api/v1/auth/refresh` — rotates refresh token; issues new access token
- `GET /api/v1/auth/me` — returns authenticated user profile
- JWT contains `sub`, `email`, `session_id`, platform role claims
- Session tracking with idle/absolute timeout config
- Per-IP and per-email rate limiting; account lockout after N failures
- Fix: legacy JWTs without `session_id` claim can still log out (nullable session ID)

---

### Phase 2 — Domain Entities & Database Schema

- Domain entities: `User`, `Company`, `App`, `Role`, `Permission`, `Resource`, `UserAppRole`, `Session`, `RefreshToken`, `LoginAttempt`, `AuthAuditLog`, `SecurityConfig`
- DB migration V1 — full initial schema with FK constraints, indexes, seed data

---

### Phase 1 — Project Scaffold

- Solution structure: `FlatPlanet.Security.API`, `FlatPlanet.Security.Application`, `FlatPlanet.Security.Domain`, `FlatPlanet.Security.Infrastructure`, `FlatPlanet.Security.Tests`
- Clean Architecture with Dapper + PostgreSQL (no EF Core)
- JWT Bearer authentication, ASP.NET Core Authorization Policies
- `IDbConnectionFactory` abstraction for Dapper transactions
