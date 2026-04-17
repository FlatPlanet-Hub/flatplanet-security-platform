# FEAT-03 — Admin Audit Log

**Repo:** flatplanet-security-platform
**Branch:** `feature/feat-03-admin-audit-log`
**Depends on:** FEAT-02 merged and migrations run
**Coder:** SP coder

---

## Goal

Every admin write operation (create/update/suspend user, grant/revoke role, register/update/deactivate app)
must be logged to `admin_audit_log` with before/after state.
ISO 27001 A.12.4.1 / A.12.4.3.

---

## Conventions

- Follow existing `AuditLogRepository` pattern — Dapper, `IDbConnectionFactory`
- Action strings use dot notation: `"user.create"`, `"role.grant"`, `"app.deactivate"`
- `before_state` / `after_state` are serialized with `System.Text.Json.JsonSerializer.Serialize(obj)` — store as string, Dapper maps to JSONB
- Actor (who did it) is resolved from `IHttpContextAccessor` or passed as parameter from controller
- Register everything in `Program.cs` as `AddScoped`

---

## Files to Create

### Domain

**No new entity needed** — `admin_audit_log` is write-only from the service layer.

Add action constants class:

`src/FlatPlanet.Security.Domain/Enums/AdminAction.cs`
```csharp
namespace FlatPlanet.Security.Domain.Enums;

public static class AdminAction
{
    public const string UserCreate     = "user.create";
    public const string UserUpdate     = "user.update";
    public const string UserSuspend    = "user.suspend";
    public const string UserDeactivate = "user.deactivate";

    public const string RoleGrant  = "role.grant";
    public const string RoleRevoke = "role.revoke";

    public const string AppRegister   = "app.register";
    public const string AppUpdate     = "app.update";
    public const string AppDeactivate = "app.deactivate";
}
```

---

### Application Layer

`src/FlatPlanet.Security.Application/Interfaces/Repositories/IAdminAuditLogRepository.cs`
```csharp
public interface IAdminAuditLogRepository
{
    Task LogAsync(Guid actorId, string actorEmail, string action,
                  string targetType, Guid? targetId,
                  object? before, object? after, string? ipAddress);
    Task DeleteExpiredAsync(int retentionDays);
    Task<IEnumerable<AdminAuditLogDto>> GetPagedAsync(AdminAuditLogQueryParams query);
    Task<AdminAuditLogDetailDto?> GetByIdAsync(Guid id);
}
```

`src/FlatPlanet.Security.Application/DTOs/Admin/AdminAuditLogDto.cs`
```csharp
public class AdminAuditLogDto
{
    public Guid     Id         { get; set; }
    public string   ActorEmail { get; set; } = string.Empty;
    public string   Action     { get; set; } = string.Empty;
    public string   TargetType { get; set; } = string.Empty;
    public Guid?    TargetId   { get; set; }
    public DateTime CreatedAt  { get; set; }
}

public class AdminAuditLogDetailDto : AdminAuditLogDto
{
    public string? BeforeState { get; set; }   // raw JSON string
    public string? AfterState  { get; set; }   // raw JSON string
    public string? IpAddress   { get; set; }
}
```

`src/FlatPlanet.Security.Application/DTOs/Admin/AdminAuditLogQueryParams.cs`
```csharp
public class AdminAuditLogQueryParams
{
    public int       Page       { get; set; } = 1;
    public int       PageSize   { get; set; } = 50;
    public string?   Action     { get; set; }
    public string?   TargetType { get; set; }
    public Guid?     ActorId    { get; set; }
    public DateTime? From       { get; set; }
    public DateTime? To         { get; set; }
}
```

---

### Infrastructure

`src/FlatPlanet.Security.Infrastructure/Repositories/AdminAuditLogRepository.cs`

- Constructor: `IDbConnectionFactory db, ILogger<AdminAuditLogRepository> logger`
- `LogAsync`: INSERT into `admin_audit_log`. Serialize `before`/`after` to JSON string via `JsonSerializer.Serialize()`.
- Do NOT throw on failure — wrap in try/catch. On catch:
  - Write to `_logger.LogError(ex, "AUDIT FAILURE: {Action} on {TargetType} {TargetId}", action, targetType, targetId)`
  - This surfaces in Azure Application Insights / Log Stream — auditors can detect record loss
  - Never rethrow — audit failure must not break the main request flow

---

## Files to Modify

### Inject into UserService

`src/FlatPlanet.Security.Application/Services/UserService.cs`

Add `IAdminAuditLogRepository _adminAudit` to constructor.

Call `_adminAudit.LogAsync(...)` **after** each write succeeds:

| Method | Action | Before | After |
|---|---|---|---|
| `CreateAsync` | `user.create` | null | new user (minus password hash) |
| `UpdateAsync` | `user.update` | old user | new user |
| `SuspendAsync` | `user.suspend` | old status | new status |
| `DeactivateAsync` | `user.deactivate` | old status | new status |

---

### Inject into RoleService / UserAppRoleService

Add `IAdminAuditLogRepository _adminAudit` to constructor.

| Method | Action | Before | After |
|---|---|---|---|
| `GrantRoleAsync` | `role.grant` | null | `{ userId, appId, roleId }` |
| `RevokeRoleAsync` | `role.revoke` | `{ userId, appId, roleId }` | null |

---

### Inject into AppService

Add `IAdminAuditLogRepository _adminAudit` to constructor.

| Method | Action | Before | After |
|---|---|---|---|
| `RegisterAsync` | `app.register` | null | new app |
| `UpdateAsync` | `app.update` | old app | new app |
| `DeactivateAsync` | `app.deactivate` | old status | new status |

---

## New Endpoint

`src/FlatPlanet.Security.API/Controllers/AdminAuditLogController.cs`

- Route: `api/v1/admin/audit-log`
- Auth: `[Authorize(Policy = "PlatformOwner")]`
- `GET /` — accepts `AdminAuditLogQueryParams` as query params, returns paginated `PagedResult<AdminAuditLogDto>`
- Return shape: `{ id, actorEmail, action, targetType, targetId, createdAt }` — do NOT return `before_state`/`after_state` in list view (too heavy). Add a separate `GET /{id}` that returns full detail including before/after.

---

## Wire in Program.cs

```csharp
// Repositories
builder.Services.AddScoped<IAdminAuditLogRepository, AdminAuditLogRepository>();
```

No new options/config needed.

---

## Audit Log Retention

`audit_log_retention_days` is seeded in `security_config` (FEAT-02, V10) with a default of 1095 days (3 years).

`DeleteExpiredAsync` SQL (already in interface above):
```sql
DELETE FROM admin_audit_log
WHERE created_at < now() - make_interval(days => @RetentionDays)
```

Register a hosted service `AuditLogCleanupService` that runs once daily:

```csharp
public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogCleanupService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<ISecurityConfigRepository>();
            var audit  = scope.ServiceProvider.GetRequiredService<IAdminAuditLogRepository>();

            var raw           = await config.GetAsync("audit_log_retention_days", "1095");
            var retentionDays = int.TryParse(raw, out var days) ? days : 1095;
            await audit.DeleteExpiredAsync(retentionDays);

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }
}
```

> Note: Use `IServiceScopeFactory` — `BackgroundService` is singleton, scoped services must be resolved per iteration.
> `ISecurityConfigRepository.GetAsync` returns a string; parse it manually. No `GetIntAsync` method exists.
> MFA challenge cleanup (FEAT-05) will add `IMfaChallengeRepository.DeleteExpiredAsync()` call here as a modification step in that feature.

Register in `Program.cs`:
```csharp
builder.Services.AddHostedService<AuditLogCleanupService>();
```

---

## Testing

After deploy:
1. Create a user via admin endpoint → check `admin_audit_log` has one row with `action = 'user.create'`
2. Suspend the user → check `action = 'user.suspend'`, `before_state` has old status
3. Grant a role → check `action = 'role.grant'`
4. `GET /api/v1/admin/audit-log` → paginated list returns those rows
5. Confirm `UPDATE`/`DELETE` on `admin_audit_log` are denied (run in Supabase SQL editor)
