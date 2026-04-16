# Missing Specs — FlatPlanet Security Platform

**Compared against:** `docs/Feature.md`
**Date: 2026-03-25**

---

## Summary

15 gaps found between the spec and the implementation. Some are missing features, some are spec omissions that the implementation exposed. Grouped by severity.

---

## Critical Gaps (spec defined it, code doesn't do it)

### 1. Company status check at login — not implemented

**Spec (Login Flow, step 9):**
> Check company status = 'active' → if not → reject with 403

**Reality:** `AuthService.LoginAsync` checks `user.Status` but never fetches the company or checks its status. A suspended company's users can still log in.

**Fix:** After the user lookup, fetch the company and check `company.Status != "active"`.

---

### 2. Per-email-per-minute rate limit — config seeded, code missing

**Spec (A.12, item 17):**
> Rate limiting: 5 attempts per minute per IP, **10 per minute per email**

**Spec also seeds this in `security_config`:**
```sql
('rate_limit_login_per_email_per_minute', '10', ...)
```

**Reality:** `AuthService.LoginAsync` only enforces the per-IP limit. The per-email-per-minute check is absent. The config key is in the DB but never read.

**Fix:** Add a `CountRecentByEmailAsync(email, since)` to `ILoginAttemptRepository` and enforce it in `AuthService` before the lockout check.

---

### 3. Session idle timeout not enforced

**Spec (A.9, item 6):**
> Session idle timeout — configurable, default 30 min (check `last_active_at` on every request)

**Reality:** `idleTimeout` is fetched from `security_config` during login but the value is never stored on the session and never checked on any subsequent request. There is no middleware or filter that validates `session.last_active_at`. Idle timeout silently doesn't exist.

**Fix:** The session entity needs an `IdleTimeoutMinutes` or a computed `IdleExpiresAt` stored at creation. Middleware (or a filter) on authenticated requests must read the session, compare `last_active_at + idle_timeout` to `now`, end the session if expired, and return 401.

---

### 4. `GET /api/v1/auth/me` returns profile only — spec requires roles + permissions

**Spec:**
> `GET /api/v1/auth/me` — Current user profile **+ roles + permissions**

**Reality:** `AuthService.GetProfileAsync` returns a `UserProfileResponse` with user fields only. No roles or permissions are included.

**Fix:** `GET /api/v1/auth/me` should accept an optional `appSlug` query parameter and delegate to `UserContextService`, or at minimum return all platform-level roles.

---

### 5. `GET /api/v1/users` — no pagination, search, or filter

**Spec:**
> `GET /api/v1/users` — List all users **(search, filter, pagination)**

**Reality:** `IUserRepository.GetAllAsync()` returns the full table with no parameters. No pagination, no search, no filter by status or company.

**Fix:** Add a query params DTO with `page`, `pageSize`, `companyId?`, `status?`, `search?`. Implement in the repository with SQL `LIMIT/OFFSET` and `WHERE` clauses.

---

### 6. `GET /api/v1/users/{id}` — missing app access in response

**Spec:**
> `GET /api/v1/users/{id}` — Get user detail **+ all app access**

**Reality:** `UserController.GetById` returns a `UserResponse` — just the user record. No app roles are included.

**Fix:** `IUserService.GetByIdAsync` should aggregate the user's `UserAppRole` records and return them alongside the user details.

---

### 7. CORS not configured

**Spec (A.13, item 22):**
> CORS — strict allowed origins, configured per app (from `apps.base_url`)

**Reality:** `Program.cs` has no `AddCors` or `UseCors`. The API will use the default CORS policy (which in ASP.NET Core is restrictive in some hosts and open in others depending on environment). The spec-defined per-app origin configuration from `apps.base_url` is completely absent.

**Fix:** Add `AddCors` with a named policy. At startup, load all `apps.base_url` values and register them as allowed origins. Add `UseCors` to the middleware pipeline before authentication.

---

### 8. Company suspension does not cascade to users

**Spec (A.7, item 35):**
> Suspended company cascades — suspending a company suspends all users in that company

**Reality:** `CompanyService.UpdateStatusAsync` updates the company's status. Nothing in the implementation suspends or deactivates the company's users. Suspended company users can still log in (especially since check #1 above is also missing).

**Fix:** When company status is set to `suspended`, call `IUserRepository.UpdateStatusAsync` for all users in that company, or add a bulk `SuspendByCompanyIdAsync` method.

---

### 9. Access review endpoint — missing entirely

**Spec (A.9, item 9):**
> Periodic access review — endpoint to list all active grants with age for admin review

**Reality:** No such endpoint exists. The `UserAccessController` lists users per app, but there is no platform-wide endpoint that shows all active `user_app_roles` sorted by grant age for compliance review.

**Fix:** Add `GET /api/v1/access-review` that returns all active, non-expired `user_app_roles` across all apps with `granted_at`, `expires_at`, and days-since-granted — paginated and filterable by company/app.

---

## Important Gaps (implementation diverges from spec)

### 10. `GET /api/v1/users/{id}/export` — no "self" access check

**Spec:**
> Export all data for a user **(admin or self)**

**Reality:** `ComplianceController.Export` has no check — any authenticated user can export any other user's full data including all sessions, IP addresses, and audit events. Only an admin or the user themselves should be able to call this.

**Fix:** Compare the JWT `sub` claim against the `id` route param. Permit if they match. Otherwise require the caller to have the `manage_users` or `view_audit_log` permission.

---

### 11. `AnonymizeUserAsync` does not deactivate the user

**Spec:**
> `POST /api/v1/users/{id}/anonymize` — Anonymize user PII. **Preserves audit trail with anonymized references.**

**Reality:** `ComplianceService.AnonymizeUserAsync` replaces PII fields but leaves the user's status as-is. An anonymized user can still log in (if their Supabase Auth account is not also removed). Anonymization without deactivation is incomplete — the anonymized email is formatted as `anonymized_{id}@deleted.invalid` which Supabase Auth won't recognize, so login would fail at the Supabase step but not cleanly at the platform level.

**Fix:** Set `user.Status = "inactive"` as part of anonymization. Also revoke all sessions and refresh tokens.

---

### 12. `user_anonymized` audit event type not in `AuditEventType` constants

**Spec (A.18):**
> Audit log covers all auth events

**Reality:** `ComplianceService.cs:120` uses a raw string literal `"user_anonymized"`. `AuditEventType` has no constant for it. Also missing: `"authorize_allowed"` and `"authorize_denied"` (used in `AuthorizationService`).

**Fix:** Add to `AuditEventType`:
```csharp
public const string UserAnonymized = "user_anonymized";
public const string AuthorizeAllowed = "authorize_allowed";
public const string AuthorizeDenied = "authorize_denied";
```

---

### 13. Input validation not implemented

**Spec (A.14, item 25):**
> Input validation on all endpoints — email format, UUID format, string length limits

**Reality:** Only null/empty checks exist on a few endpoints. No email format validation (`LoginRequest.Email`), no string length limits on any DTO (names, slugs, descriptions), no enum validation on status fields.

**Fix:** Add `[EmailAddress]`, `[MaxLength]`, `[RegularExpression]` data annotations to DTOs, or add FluentValidation. At minimum validate email format at login and enforce max lengths on free-text fields.

---

## Spec Omissions (spec doesn't define it, but implementation needs it)

### 14. JWT token structure in spec doesn't include `session_id`

**Spec (JWT Token Structure section):**
```json
{ "sub", "email", "full_name", "company_id", "iss", "aud", "iat", "exp" }
```

**Reality needed:** Logout requires the session ID to terminate the right session. Without `session_id` in the JWT, logout is non-functional (see `review.md` Critical Issue #1). The spec explicitly states the JWT fields but omits `session_id`.

**Fix needed in spec:** Add `session_id` to the documented JWT structure. Update `JwtService.IssueAccessToken` accordingly.

---

### 15. `AppSlug` in `LoginRequest` is unused dead code

**Spec:** The login flow doesn't describe using `appSlug` during authentication. The spec's login endpoint only shows `{ "email", "password" }`.

**Reality:** `LoginRequest` has an `AppSlug` property that is never read in `AuthService.LoginAsync`. Its intent is unclear — possibly intended to scope the user context returned at login, but nothing uses it.

**Fix needed in spec:** Either define the intended behavior (e.g., "if appSlug is provided, include user context for that app in the login response") or remove the field.

---

## Gap Summary Table

| # | Gap | Type | Severity |
|---|-----|------|----------|
| 1 | Company status not checked at login | Missing feature | Critical |
| 2 | Per-email-per-minute rate limit | Missing feature | Critical |
| 3 | Session idle timeout not enforced | Missing feature | Critical |
| 4 | `GET /auth/me` missing roles + permissions | Missing feature | High |
| 5 | `GET /users` no pagination/search/filter | Missing feature | High |
| 6 | `GET /users/{id}` missing app access | Missing feature | High |
| 7 | CORS not configured | Missing feature | High |
| 8 | Company suspend cascade missing | Missing feature | High |
| 9 | Access review endpoint absent | Missing feature | Medium |
| 10 | Export has no admin-or-self check | Security gap | High |
| 11 | Anonymize doesn't deactivate user | Implementation divergence | Medium |
| 12 | `AuditEventType` missing 3 constants | Implementation divergence | Low |
| 13 | Input validation absent | Missing feature | Medium |
| 14 | JWT spec missing `session_id` | Spec omission | Critical |
| 15 | `AppSlug` in LoginRequest unused | Spec omission | Low |
