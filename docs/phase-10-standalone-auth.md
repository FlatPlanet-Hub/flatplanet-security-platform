# Phase 10 — Standalone Authentication (Remove Supabase Auth)

## Goal

Replace Supabase Auth with direct credential verification against our own `users` table. The platform owns the full authentication stack: password hashing, credential verification, user creation. No external auth provider.

---

## Why

The original design delegated password verification to Supabase Auth and used Supabase UIDs as user IDs. This creates an external dependency for the core login flow and prevents us from owning the full auth stack as intended.

---

## What Changes

### Layer: Database — `db/V5__standalone_auth.sql`

```sql
-- 1. Give users a generated UUID by default (was relying on Supabase to supply the id)
ALTER TABLE users ALTER COLUMN id SET DEFAULT gen_random_uuid();

-- 2. Add password_hash — bcrypt hash stored here, never plaintext
ALTER TABLE users ADD COLUMN password_hash TEXT NOT NULL DEFAULT '';

-- Remove the placeholder default after migration (no real data yet)
ALTER TABLE users ALTER COLUMN password_hash DROP DEFAULT;
```

Comment on V1 is now incorrect — remove "matches Supabase Auth uid" from schema comments but do not edit V1. The V5 migration is sufficient.

---

### Layer: Application — New `IPasswordHasher` interface

`src/FlatPlanet.Security.Application/Interfaces/Services/IPasswordHasher.cs`

```csharp
namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

---

### Layer: Infrastructure — `BCryptPasswordHasher` implementation

**Add NuGet package to Infrastructure project:**
```
BCrypt.Net-Next
```

`src/FlatPlanet.Security.Infrastructure/Security/BCryptPasswordHasher.cs`

```csharp
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Infrastructure.Security;

public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
```

---

### Layer: Domain — Add `PasswordHash` to `User` entity

`src/FlatPlanet.Security.Domain/Entities/User.cs`
- Add: `public string PasswordHash { get; set; } = string.Empty;`

---

### Layer: Application — Update `AuthService.LoginAsync`

`src/FlatPlanet.Security.Application/Services/AuthService.cs`

**Remove:**
- `ISupabaseAuthClient _supabaseAuth` field and constructor parameter
- All `_supabaseAuth.SignInAsync(...)` call
- The `authResult` variable

**Add:**
- `IPasswordHasher _passwordHasher` field and constructor parameter

**Replace the Supabase credential check (steps 4 and 5 in LoginAsync) with:**

```csharp
// 4. Look up user by email and verify password
var user = await _users.GetByEmailAsync(request.Email);

if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
{
    await _loginAttempts.RecordAsync(new LoginAttempt
    {
        Email = request.Email,
        IpAddress = ipAddress,
        Success = false,
        AttemptedAt = now
    });
    await _auditLog.LogAsync(new AuthAuditLog
    {
        UserId = null,
        EventType = AuditEventType.LoginFailure,
        IpAddress = ipAddress,
        UserAgent = userAgent,
        Details = JsonSerializer.Serialize(new { email = request.Email })
    });
    throw new UnauthorizedAccessException("Invalid email or password.");
}
```

Remove the separate user lookup that followed the Supabase call (`await _users.GetByIdAsync(authResult.UserId)`) — `user` is now fetched directly above.

---

### Layer: Application — Add user creation with password hashing

`src/FlatPlanet.Security.Application/DTOs/Admin/UserDtos.cs`
- Add `CreateUserRequest` DTO:
  ```csharp
  public class CreateUserRequest
  {
      [Required]
      public Guid CompanyId { get; set; }

      [Required]
      [EmailAddress]
      [MaxLength(256)]
      public string Email { get; set; } = string.Empty;

      [Required]
      [MaxLength(128)]
      public string FullName { get; set; } = string.Empty;

      [MaxLength(100)]
      public string? RoleTitle { get; set; }

      [Required]
      [MinLength(8)]
      [MaxLength(128)]
      public string Password { get; set; } = string.Empty;
  }
  ```

`src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserRepository.cs`
- Add: `Task<User> CreateAsync(User user);`

`src/FlatPlanet.Security.Infrastructure/Repositories/UserRepository.cs`
- Implement `CreateAsync`:
  ```sql
  INSERT INTO users (company_id, email, full_name, role_title, password_hash)
  VALUES (@CompanyId, @Email, @FullName, @RoleTitle, @PasswordHash)
  RETURNING id
  ```

`src/FlatPlanet.Security.Application/Interfaces/Services/IUserService.cs`
- Add: `Task<UserResponse> CreateAsync(CreateUserRequest request);`

`src/FlatPlanet.Security.Application/Services/UserService.cs`
- Inject `IPasswordHasher _passwordHasher` (add to constructor)
- Implement `CreateAsync`:
  ```csharp
  public async Task<UserResponse> CreateAsync(CreateUserRequest request)
  {
      var user = new User
      {
          CompanyId = request.CompanyId,
          Email = request.Email,
          FullName = request.FullName,
          RoleTitle = request.RoleTitle,
          PasswordHash = _passwordHasher.Hash(request.Password),
          Status = "active"
      };
      var created = await _users.CreateAsync(user);
      return MapToResponse(created);
  }
  ```

`src/FlatPlanet.Security.API/Controllers/UserController.cs`
- Add `POST /api/v1/users`:
  ```csharp
  [HttpPost]
  public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
  {
      var result = await _users.CreateAsync(request);
      return Ok(new { success = true, data = result });
  }
  ```

---

### Layer: Configuration — Separate DB connection from Supabase options

`src/FlatPlanet.Security.Application/Common/Options/DatabaseOptions.cs` — **new file**
```csharp
namespace FlatPlanet.Security.Application.Common.Options;

public class DatabaseOptions
{
    public const string Section = "Database";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string BuildConnectionString() =>
        $"Host={Host};Port={Port};Database={Name};Username={User};Password={Password}";
}
```

`appsettings.json` — rename `Supabase` section, keep only what's needed:
```json
"Database": {
  "Host": "",
  "Port": 5432,
  "Name": "",
  "User": "",
  "Password": ""
},
"Jwt": {
  "SecretKey": "",
  "Issuer": "flatplanet-security",
  "Audience": "flatplanet-apps"
}
```

Remove `SupabaseOptions` entirely — `SupabaseOptions.cs` file deleted.

`src/FlatPlanet.Security.API/Program.cs`
- Replace `SupabaseOptions` with `DatabaseOptions`:
  ```csharp
  var dbOptions = builder.Configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>()!;
  builder.Services.AddSingleton<IDbConnectionFactory>(
      new NpgsqlConnectionFactory(dbOptions.BuildConnectionString()));
  ```
- Remove: `builder.Services.Configure<SupabaseOptions>(...)`
- Remove: `builder.Services.AddHttpClient<ISupabaseAuthClient, SupabaseAuthClient>()`
- Add: `builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>()`

---

### Layer: Delete — Remove Supabase auth files entirely

Delete these files:
- `src/FlatPlanet.Security.Application/Interfaces/Services/ISupabaseAuthClient.cs`
- `src/FlatPlanet.Security.Infrastructure/ExternalServices/SupabaseAuthClient.cs`
- `src/FlatPlanet.Security.Application/Common/Options/SupabaseOptions.cs`

---

### Layer: Tests — Update `AuthServiceTests`

`tests/FlatPlanet.Security.Tests/AuthServiceTests.cs`

**Remove:**
- `Mock<ISupabaseAuthClient> _supabaseAuth`
- All `_supabaseAuth.Setup(...)` calls
- `_supabaseAuth.Object` from `CreateService()`

**Add:**
- `Mock<IPasswordHasher> _passwordHasher`
- `_passwordHasher.Object` in `CreateService()`

**Update `Login_ShouldReturnTokens_WhenCredentialsValid`:**
- Remove Supabase mock
- Mock `_users.GetByEmailAsync("user@test.com")` returning user with `PasswordHash = "hashed"`
- Mock `_passwordHasher.Verify("pass123", "hashed")` returning `true`

**Rename and update `Login_ShouldReturn401_WhenSupabaseAuthFails`:**
- Rename to `Login_ShouldReturn401_WhenPasswordInvalid`
- Mock `_users.GetByEmailAsync` returning user
- Mock `_passwordHasher.Verify(...)` returning `false`
- Assert throws `UnauthorizedAccessException`

**Add `Login_ShouldReturn401_WhenUserNotFound`:**
- Mock `_users.GetByEmailAsync` returning `null`
- Assert throws `UnauthorizedAccessException`

Update all other login tests — add `_loginAttempts.Setup(l => l.CountRecentByEmailAsync(...))` and `_users.GetByEmailAsync(...)` wherever needed. Remove any remaining `_supabaseAuth` references.

---

## New Unit Tests Required

| Test Class | Test Name |
|-----------|-----------|
| `AuthServiceTests` | `Login_ShouldReturn401_WhenPasswordInvalid` (replaces Supabase fail test) |
| `AuthServiceTests` | `Login_ShouldReturn401_WhenUserNotFound` |

---

## Packages

| Project | Package | Action |
|---------|---------|--------|
| `FlatPlanet.Security.Infrastructure` | `BCrypt.Net-Next` | Add |

No packages to remove — `SupabaseAuthClient` only used `HttpClient` which is a framework type.

---

## Commit Convention

```
feat: standalone auth — replace Supabase Auth with bcrypt password verification
feat: POST /api/v1/users — admin creates users with hashed password
db: V5 migration — add password_hash to users, set id default
refactor: replace SupabaseOptions with DatabaseOptions — decouple DB config from auth provider
```

---

## Status: PENDING
