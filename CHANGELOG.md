# Changelog

All notable changes to the FlatPlanet Security Platform are documented here.

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
