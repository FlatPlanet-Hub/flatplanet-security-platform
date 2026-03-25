# Phase 8 — Spec Gaps

## Goal

Close the 12 gaps between the Feature.md spec and the current build. These are not bugs — they are features that were specified but not implemented. The core RBAC identity service is correct; this phase makes it production-ready.

---

## 1. Company status check at login

**Why:** A suspended company's users can currently log in. The spec (Login Flow, step 9) requires a company status check before issuing tokens.

**Files:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/ICompanyRepository.cs`
  - Confirm `GetByIdAsync(Guid id)` exists — no change needed if it does
- `src/FlatPlanet.Security.Application/Services/AuthService.cs` — `LoginAsync`
  - After user lookup, fetch company and check status:
    ```csharp
    var company = await _companies.GetByIdAsync(user.CompanyId)
        ?? throw new UnauthorizedAccessException("Company not found.");
    if (company.Status != "active")
        throw new ForbiddenException($"Company account is {company.Status}.");
    ```
  - Inject `ICompanyRepository _companies` in constructor

**Tests:**
- `AuthServiceTests` — add `Login_ShouldReturn403_WhenCompanySuspended`

---

## 2. Per-email-per-minute rate limit

**Why:** The spec (A.12, item 17) requires 10 attempts per minute per email. The config key `rate_limit_login_per_email_per_minute` is seeded in `security_config` but never read or enforced.

**Files:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/ILoginAttemptRepository.cs`
  - Add: `Task<int> CountRecentByEmailAsync(string email, DateTime since);`
- `src/FlatPlanet.Security.Infrastructure/Repositories/LoginAttemptRepository.cs`
  - Implement: count all attempts (success or failure) for the email within the window
    ```sql
    SELECT COUNT(*) FROM login_attempts
    WHERE email = @Email AND attempted_at >= @Since
    ```
- `src/FlatPlanet.Security.Application/Services/AuthService.cs` — `LoginAsync`
  - After the per-IP check, add the per-email check:
    ```csharp
    var rateLimitPerEmail = Cfg("rate_limit_login_per_email_per_minute", 10);
    var emailAttempts = await _loginAttempts.CountRecentByEmailAsync(request.Email, now.AddMinutes(-1));
    if (emailAttempts >= rateLimitPerEmail)
        throw new TooManyRequestsException("Too many login attempts for this account.");
    ```

**Tests:**
- `AuthServiceTests` — add `Login_ShouldReturn429_WhenEmailRateLimitExceeded`

---

## 3. Session idle timeout enforcement

**Why:** The spec (A.9, item 6) requires idle timeout checked on every authenticated request. The config is loaded at login, but the value is never stored on the session and never checked.

**Files:**
- `src/FlatPlanet.Security.Domain/Entities/Session.cs`
  - Add: `public int IdleTimeoutMinutes { get; set; }`
- `db/` — add `V4__session_idle_timeout.sql`
  ```sql
  ALTER TABLE sessions ADD COLUMN idle_timeout_minutes INTEGER NOT NULL DEFAULT 30;
  ```
- `src/FlatPlanet.Security.Application/Services/AuthService.cs` — `LoginAsync`
  - Pass `IdleTimeoutMinutes` when creating the session:
    ```csharp
    var idleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30);
    session = await _sessions.CreateAsync(new Session
    {
        ...,
        IdleTimeoutMinutes = idleTimeoutMinutes
    }, conn, tx);
    ```
- `src/FlatPlanet.Security.Infrastructure/Repositories/SessionRepository.cs`
  - Include `idle_timeout_minutes` in INSERT and SELECT queries
- `src/FlatPlanet.Security.API/Middleware/SessionValidationMiddleware.cs` — **new file**
  - On every authenticated request:
    1. Extract `session_id` claim from JWT (skip if missing — legacy token)
    2. Load session from DB
    3. If `session.LastActiveAt + session.IdleTimeoutMinutes < now` → call `EndSessionAsync`, return 401
    4. If `session.ExpiresAt < now` → call `EndSessionAsync`, return 401
    5. Otherwise call `UpdateLastActiveAtAsync` and continue
- `src/FlatPlanet.Security.API/Program.cs`
  - Register middleware after `UseAuthentication()` and before `UseAuthorization()`:
    ```csharp
    app.UseMiddleware<SessionValidationMiddleware>();
    ```

**Tests:**
- `SessionValidationMiddlewareTests` — `Request_ShouldReturn401_WhenSessionIdleExpired`
- `SessionValidationMiddlewareTests` — `Request_ShouldReturn401_WhenSessionAbsoluteExpired`
- `SessionValidationMiddlewareTests` — `Request_ShouldPass_WhenSessionActive`

---

## 4. `GET /api/v1/auth/me` — return roles and permissions

**Why:** The spec defines `GET /auth/me` as returning "current user profile + roles + permissions." Currently it returns profile fields only.

**Files:**
- `src/FlatPlanet.Security.Application/DTOs/Auth/UserProfileResponse.cs`
  - Add:
    ```csharp
    public IEnumerable<string> PlatformRoles { get; set; } = [];
    public IEnumerable<AppAccessDto> AppAccess { get; set; } = [];
    ```
  - Add `AppAccessDto`:
    ```csharp
    public class AppAccessDto
    {
        public string AppSlug { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public IEnumerable<string> Permissions { get; set; } = [];
    }
    ```
- `src/FlatPlanet.Security.API/Controllers/AuthController.cs` — `GetMe`
  - Accept optional query param `?appSlug=`
  - Pass to service
- `src/FlatPlanet.Security.Application/Interfaces/Services/IAuthService.cs`
  - Change: `Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug);`
- `src/FlatPlanet.Security.Application/Services/AuthService.cs` — `GetProfileAsync`
  - Always populate `PlatformRoles` via `_roles.GetPlatformRoleNamesForUserAsync`
  - If `appSlug` provided, load app, then fetch user's roles + permissions for that app via `IUserContextService`

---

## 5. `GET /api/v1/users` — pagination, search, and filter

**Why:** The spec defines "list all users (search, filter, pagination)." Current implementation returns the full table with no parameters.

**Files:**
- `src/FlatPlanet.Security.Application/DTOs/Users/UserQueryParams.cs` — **new file**
  ```csharp
  public class UserQueryParams
  {
      public int Page { get; set; } = 1;
      public int PageSize { get; set; } = 20;
      public Guid? CompanyId { get; set; }
      public string? Status { get; set; }
      public string? Search { get; set; } // matches email or full_name (ILIKE)
  }
  ```
- `src/FlatPlanet.Security.Application/DTOs/Users/PagedResult.cs` — **new file**
  ```csharp
  public class PagedResult<T>
  {
      public IEnumerable<T> Items { get; set; } = [];
      public int TotalCount { get; set; }
      public int Page { get; set; }
      public int PageSize { get; set; }
  }
  ```
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserRepository.cs`
  - Add: `Task<PagedResult<User>> GetPagedAsync(UserQueryParams query);`
- `src/FlatPlanet.Security.Infrastructure/Repositories/UserRepository.cs`
  - Implement with dynamic SQL: `WHERE` clauses for `company_id`, `status`, `ILIKE` on `email`/`full_name`, `LIMIT`/`OFFSET`
- `src/FlatPlanet.Security.Application/Interfaces/Services/IUserService.cs`
  - Add: `Task<PagedResult<UserResponse>> GetPagedAsync(UserQueryParams query);`
- `src/FlatPlanet.Security.Application/Services/UserService.cs`
  - Implement — delegate to repository
- `src/FlatPlanet.Security.API/Controllers/UserController.cs` — `GetAll`
  - Accept `[FromQuery] UserQueryParams query`, return `PagedResult<UserResponse>`

---

## 6. `GET /api/v1/users/{id}` — include app access

**Why:** The spec says "Get user detail + all app access." Currently only user fields are returned.

**Files:**
- `src/FlatPlanet.Security.Application/DTOs/Users/UserDetailResponse.cs` — **new file** (or extend `UserResponse`)
  ```csharp
  public class UserDetailResponse : UserResponse
  {
      public IEnumerable<UserAppAccessDto> AppAccess { get; set; } = [];
  }

  public class UserAppAccessDto
  {
      public Guid AppId { get; set; }
      public string AppName { get; set; } = string.Empty;
      public string AppSlug { get; set; } = string.Empty;
      public string RoleName { get; set; } = string.Empty;
      public string Status { get; set; } = string.Empty;
      public DateTime GrantedAt { get; set; }
      public DateTime? ExpiresAt { get; set; }
  }
  ```
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserAppRoleRepository.cs`
  - Add: `Task<IEnumerable<UserAppRoleDetail>> GetDetailsByUserIdAsync(Guid userId);`
  - `UserAppRoleDetail` joins `user_app_roles` → `apps` → `roles`
- `src/FlatPlanet.Security.Infrastructure/Repositories/UserAppRoleRepository.cs`
  - Implement the join query
- `src/FlatPlanet.Security.Application/Services/UserService.cs` — `GetByIdAsync`
  - Populate `AppAccess` from `_userAppRoles.GetDetailsByUserIdAsync`
- `src/FlatPlanet.Security.API/Controllers/UserController.cs` — `GetById`
  - Return `UserDetailResponse`

---

## 7. CORS configuration

**Why:** The spec (A.13, item 22) requires strict CORS with allowed origins from `apps.base_url`. Currently `Program.cs` has no CORS configuration at all.

**Files:**
- `src/FlatPlanet.Security.API/Program.cs`
  - Load all active apps at startup and build the allowed origins list:
    ```csharp
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AppOrigins", policy =>
        {
            // Load dynamically at startup — see startup extension below
        });
    });
    ```
  - Add `app.UseCors("AppOrigins")` before `app.UseAuthentication()`
- `src/FlatPlanet.Security.API/Extensions/CorsStartupExtension.cs` — **new file**
  - At startup, query `SELECT base_url FROM apps WHERE status = 'active' AND base_url IS NOT NULL`
  - Register those URLs as allowed origins with `AllowAnyMethod().AllowAnyHeader()`
  - Always include a configurable `PLATFORM_CORS_ORIGIN` env var for the admin frontend
  - Note: apps registered after startup require an app restart (acceptable for now — dynamic CORS is Phase 9+)

---

## 8. Company suspension cascades to users and sessions

**Why:** The spec (A.7, item 35) says suspending a company suspends all users in that company. Currently `CompanyService.UpdateStatusAsync` updates only the company row.

**Files:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserRepository.cs`
  - Add: `Task SuspendByCompanyIdAsync(Guid companyId);`
- `src/FlatPlanet.Security.Infrastructure/Repositories/UserRepository.cs`
  - Implement:
    ```sql
    UPDATE users SET status = 'suspended', updated_at = now()
    WHERE company_id = @CompanyId AND status = 'active'
    ```
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IRefreshTokenRepository.cs`
  - Add: `Task RevokeAllByCompanyIdAsync(Guid companyId, string reason);`
- `src/FlatPlanet.Security.Infrastructure/Repositories/RefreshTokenRepository.cs`
  - Implement via join: revoke tokens for all users in the company
- `src/FlatPlanet.Security.Application/Services/CompanyService.cs` — `UpdateStatusAsync`
  - When new status is `"suspended"`:
    ```csharp
    await _users.SuspendByCompanyIdAsync(company.Id);
    await _refreshTokens.RevokeAllByCompanyIdAsync(company.Id, "company_suspended");
    ```
  - Log a `CompanySuspended` audit event

**Tests:**
- `CompanyServiceTests` — `SuspendCompany_ShouldSuspendAllUsers`
- `CompanyServiceTests` — `SuspendCompany_ShouldRevokeAllRefreshTokens`

---

## 9. Access review endpoint

**Why:** The spec (A.9, item 9) requires a periodic access review endpoint — all active grants with age for admin audit. Needed for ISO 27001 compliance.

**Files:**
- `src/FlatPlanet.Security.Application/DTOs/Access/AccessReviewItemDto.cs` — **new file**
  ```csharp
  public class AccessReviewItemDto
  {
      public Guid GrantId { get; set; }
      public Guid UserId { get; set; }
      public string UserEmail { get; set; } = string.Empty;
      public string CompanyName { get; set; } = string.Empty;
      public Guid AppId { get; set; }
      public string AppName { get; set; } = string.Empty;
      public string RoleName { get; set; } = string.Empty;
      public DateTime GrantedAt { get; set; }
      public DateTime? ExpiresAt { get; set; }
      public int DaysSinceGranted { get; set; }
  }
  ```
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserAppRoleRepository.cs`
  - Add: `Task<PagedResult<AccessReviewItemDto>> GetAccessReviewAsync(int page, int pageSize, Guid? companyId, Guid? appId);`
- `src/FlatPlanet.Security.Infrastructure/Repositories/UserAppRoleRepository.cs`
  - Implement: join `user_app_roles` → `users` → `companies` → `apps` → `roles`, filter `status = 'active'`, paginate, order by `granted_at ASC`
- `src/FlatPlanet.Security.Application/Interfaces/Services/IAccessReviewService.cs` — **new file**
- `src/FlatPlanet.Security.Application/Services/AccessReviewService.cs` — **new file**
- `src/FlatPlanet.Security.API/Controllers/AccessReviewController.cs` — **new file**
  - `GET /api/v1/access-review` — `[Authorize(Policy = "AdminAccess")]`
  - Query params: `page`, `pageSize`, `companyId?`, `appId?`

---

## 10. `GET /users/{id}/export` — admin-or-self check

**Why:** Any authenticated user can currently export any other user's full data (sessions, IPs, audit events). Must be admin or the user themselves.

**Files:**
- `src/FlatPlanet.Security.API/Controllers/ComplianceController.cs` — `Export` action
  - Add check before calling service:
    ```csharp
    var callerId = GetUserId();
    var isAdmin = User.IsInRole("platform_owner") || User.IsInRole("app_admin");
    if (callerId != id && !isAdmin)
        return Forbid();
    ```

---

## 11. Anonymize user — deactivate and revoke sessions

**Why:** `ComplianceService.AnonymizeUserAsync` replaces PII but leaves the user active with live sessions and refresh tokens. An anonymized user should be immediately deactivated.

**Files:**
- `src/FlatPlanet.Security.Application/Services/ComplianceService.cs` — `AnonymizeUserAsync`
  - After replacing PII fields, add:
    ```csharp
    await _users.UpdateStatusAsync(userId, "inactive");
    await _refreshTokens.RevokeAllByUserAsync(userId, "anonymized");
    ```

---

## 12. Input validation on all DTOs

**Why:** The spec (A.14, item 25) requires input validation on all endpoints. Currently only null/empty checks exist.

**Files:**
- `src/FlatPlanet.Security.Application/DTOs/Auth/LoginRequest.cs`
  - Add `[EmailAddress]`, `[Required]`, `[MaxLength(256)]` on `Email`
  - Add `[Required]`, `[MaxLength(128)]` on `Password`
- `src/FlatPlanet.Security.Application/DTOs/Auth/RefreshRequest.cs`
  - Add `[Required]` on `RefreshToken`
- All DTOs with name/slug/description fields — add `[MaxLength]`:
  - Company: `Name` → `[MaxLength(200)]`, `Status` → `[RegularExpression("active|suspended|inactive")]`
  - App: `Name` → `[MaxLength(200)]`, `Slug` → `[MaxLength(100), RegularExpression("^[a-z0-9-]+$")]`
  - Role/Permission: `Name` → `[MaxLength(100)]`
  - Resource: `Identifier` → `[MaxLength(200)]`
- `src/FlatPlanet.Security.API/Program.cs`
  - Confirm `builder.Services.AddControllers()` uses default model validation (returns 400 on annotation failures)
  - If not: add `.ConfigureApiBehaviorOptions` to return consistent `ValidationProblemDetails`

---

## New Migration Required

**`db/V4__session_idle_timeout.sql`**
```sql
ALTER TABLE sessions ADD COLUMN idle_timeout_minutes INTEGER NOT NULL DEFAULT 30;
```

---

## New Unit Tests Required

| Test Class | Test Name |
|-----------|-----------|
| `AuthServiceTests` | `Login_ShouldReturn403_WhenCompanySuspended` |
| `AuthServiceTests` | `Login_ShouldReturn429_WhenEmailRateLimitExceeded` |
| `SessionValidationMiddlewareTests` | `Request_ShouldReturn401_WhenSessionIdleExpired` |
| `SessionValidationMiddlewareTests` | `Request_ShouldReturn401_WhenSessionAbsoluteExpired` |
| `SessionValidationMiddlewareTests` | `Request_ShouldPass_WhenSessionActive` |
| `CompanyServiceTests` | `SuspendCompany_ShouldSuspendAllUsers` |
| `CompanyServiceTests` | `SuspendCompany_ShouldRevokeAllRefreshTokens` |

---

## Commit Convention

```
fix: check company status at login — suspended companies cannot authenticate
fix: enforce per-email rate limit on login — reads rate_limit_login_per_email_per_minute config
feat: enforce session idle timeout via middleware — checks last_active_at on every request
feat: GET /auth/me returns platform roles and app access
feat: GET /users supports pagination, search, and filter
feat: GET /users/{id} includes app access in response
feat: configure CORS from apps.base_url on startup
fix: cascade company suspension to users and revoke refresh tokens
feat: GET /api/v1/access-review — paginated active grants for ISO 27001 access review
fix: export endpoint requires admin or self — prevents unauthorized PII access
fix: anonymize deactivates user and revokes all sessions
fix: add input validation annotations to all DTOs
db: V4 migration — add idle_timeout_minutes to sessions
```

---

## Status: COMPLETE
