# FEAT-05 — MFA SMS OTP

**Repo:** flatplanet-security-platform
**Branch:** `feature/feat-05-mfa-sms-otp`
**Depends on:** FEAT-02 merged (`mfa_challenges` table must exist)
**Coder:** SP coder

---

## Goal

Users enroll a phone number and verify their identity via a 6-digit SMS OTP.
OTP is hashed (SHA-256) before storage — never stored in plaintext.
MFA is enforced **at login time in SP** — HubApi and all other apps require zero changes.
ISO 27001 A.9.4.2.

---

## Why MFA lives in SP, not HubApi

SP is the single source of truth for identity. If a user must pass MFA, SP enforces it
before issuing any JWT. By the time HubApi (or any other app) sees a token, SP has already
guaranteed the user passed all required checks. No app-level enforcement needed anywhere.

---

## Flow

### Enrollment (one-time)
```
POST /api/v1/mfa/enroll     { phoneNumber }  → saves phone, sends OTP, creates mfa_challenge row
POST /api/v1/mfa/otp/verify { code }         → hashes code, checks mfa_challenges, marks verified
                                             → sets users.mfa_enabled = true
                                             → calls IdentityVerificationService.SyncStatusAsync (FEAT-06)
```

### Login (every login after enrollment)
```
POST /api/v1/auth/login  { email, password }
    │
    ├── mfa_enabled = false → issue JWT immediately (current behavior, unchanged)
    │
    └── mfa_enabled = true  → do NOT issue JWT yet
                             → create mfa_challenge, send OTP
                             → return { requiresMfa: true, challengeId: "..." }

POST /api/v1/mfa/otp/login-verify  { challengeId, code }
    └── verify OTP → issue JWT + refresh token (same shape as normal login)
```

---

## Conventions

- Follow `AuthService` / `UserRepository` patterns exactly
- Hash OTP using existing `JwtService.HashToken(string)` — SHA-256, returns lowercase hex
- Read OTP config from `security_config` via `ISecurityConfigRepository` (already injected pattern — see AuthService.LoadConfigAsync)
- **Cache `security_config` reads** — do not hit the DB on every request. Use `IMemoryCache` with a 5-minute TTL. The config values change rarely; stale reads for 5 minutes is acceptable.
- Throw typed exceptions: `TooManyRequestsException`, `ArgumentException`, `KeyNotFoundException`, `ServiceUnavailableException`
- Add `ServiceUnavailableException` if it doesn't exist — maps to HTTP 503
- `ExceptionHandlingMiddleware` already maps these — add `ServiceUnavailableException → 503` mapping if not present

## Breaking Change Warning — Frontend Must Update First

`LoginResponse` shape changes when a user has MFA enabled. Before deploying FEAT-05 to production:
- Frontend must handle both shapes: `{ requiresMfa: false, accessToken, refreshToken }` and `{ requiresMfa: true, challengeId }`
- Deploy frontend change first, then deploy SP
- Test with a non-MFA user first — their flow is unchanged

---

## Files to Create

### Options

`src/FlatPlanet.Security.Application/Common/Options/SmsOptions.cs`
```csharp
namespace FlatPlanet.Security.Application.Common.Options;

public class SmsOptions
{
    public const string Section = "Sms";

    // Provider is not needed here — Program.cs uses IsDevelopment() to switch implementations
    public string AccountSid  { get; set; } = string.Empty;
    public string AuthToken   { get; set; } = string.Empty;
    public string FromNumber  { get; set; } = string.Empty;
}
```

---

### Domain Entity

`src/FlatPlanet.Security.Domain/Entities/MfaChallenge.cs`
```csharp
namespace FlatPlanet.Security.Domain.Entities;

public class MfaChallenge
{
    public Guid      Id          { get; set; }
    public Guid      UserId      { get; set; }
    public string    PhoneNumber { get; set; } = string.Empty;
    public string    OtpHash     { get; set; } = string.Empty;
    public DateTime  ExpiresAt   { get; set; }
    public DateTime? VerifiedAt  { get; set; }
    public int       Attempts    { get; set; }
    public DateTime  CreatedAt   { get; set; }
}
```

---

### Repository Interface

`src/FlatPlanet.Security.Application/Interfaces/Repositories/IMfaChallengeRepository.cs`
```csharp
public interface IMfaChallengeRepository
{
    Task CreateAsync(MfaChallenge challenge);
    Task<MfaChallenge?> GetActiveByUserIdAsync(Guid userId);
    Task<MfaChallenge?> GetByIdAsync(Guid id);
    Task MarkVerifiedAsync(Guid id);
    Task IncrementAttemptsAsync(Guid id);
    Task InvalidateActiveAsync(Guid userId);      // expire all active challenges for a user
    Task<bool> HasVerifiedChallengeAsync(Guid userId); // used by FEAT-06
    Task DeleteExpiredAsync();                    // called by AuditLogCleanupService (FEAT-03)
}
```

---

### SMS Sender Interface

`src/FlatPlanet.Security.Application/Interfaces/Services/ISmsSender.cs`
```csharp
public interface ISmsSender
{
    Task SendAsync(string toPhoneNumber, string body);
}
```

---

### DTOs

`src/FlatPlanet.Security.Application/DTOs/Mfa/EnrollPhoneRequest.cs`
```csharp
public class EnrollPhoneRequest
{
    [Required, Phone]
    public string PhoneNumber { get; set; } = string.Empty;
}
```

`src/FlatPlanet.Security.Application/DTOs/Mfa/VerifyOtpRequest.cs`
```csharp
public class VerifyOtpRequest
{
    [Required, StringLength(8, MinimumLength = 4)]
    public string Code { get; set; } = string.Empty;
}
```

`src/FlatPlanet.Security.Application/DTOs/Mfa/EnrollPhoneResponse.cs`
```csharp
public class EnrollPhoneResponse
{
    public string MaskedPhone { get; set; } = string.Empty; // e.g. "+63*****1234"
    public DateTime ExpiresAt { get; set; }
}
```

---

### Service Interface

`src/FlatPlanet.Security.Application/Interfaces/Services/IMfaService.cs`
```csharp
public interface IMfaService
{
    Task<EnrollPhoneResponse> EnrollAndSendOtpAsync(Guid userId, string phoneNumber);
    Task VerifyOtpAsync(Guid userId, string code);
    Task<MfaChallenge> SendLoginOtpAsync(Guid userId, string phoneNumber); // called by AuthService
    Task<LoginResponse> VerifyLoginOtpAsync(Guid challengeId, string code); // called by MfaController
}
```

---

### Service Implementation

`src/FlatPlanet.Security.Application/Services/MfaService.cs`

Constructor injects: `IMfaChallengeRepository`, `IUserRepository`, `ISmsSender`,
`ISecurityConfigRepository`, `IJwtService`, `IAuditLogRepository`

**EnrollAndSendOtpAsync:**
1. Load config: `mfa_otp_expiry_minutes` (default 10), `mfa_otp_length` (default 6) — both return as strings from `GetAsync`; parse with `int.Parse` before use
2. **Rate limit check**: if an active unexpired challenge already exists for this user → do NOT create a new one → return the existing expiry with masked phone (prevents OTP spam and Twilio cost burn)
3. Generate OTP: `string otp = string.Join("", RandomNumberGenerator.GetBytes(otpLength).Select(b => (b % 10).ToString()))`
   where `otpLength` is the parsed int from config
4. Hash: `_jwt.HashToken(otp)`
5. Save to `mfa_challenges` with `ExpiresAt = now + expiry`
6. Update `users.phone_number` via `IUserRepository`
7. Call `_smsSender.SendAsync(phoneNumber, $"Your FlatPlanet verification code is: {otp}")` — wrap in try/catch:
   - On success: continue
   - On failure: `_logger.LogError(ex, "SMS send failed for user {UserId}", userId)` → throw `ServiceUnavailableException("SMS service unavailable. Please try again later.")` — ExceptionHandlingMiddleware maps to 503
8. Return `EnrollPhoneResponse` with masked phone and expiry
9. Log to `auth_audit_log` event type `MfaOtpIssued`

**VerifyOtpAsync:**
1. Load config: `mfa_otp_max_attempts` (default 3)
2. `GetActiveByUserIdAsync` — throw `KeyNotFoundException("No active MFA challenge.")` if null
3. Check `challenge.ExpiresAt > now` — throw `ArgumentException("OTP has expired.")` if not
4. Check `challenge.Attempts < maxAttempts` — throw `TooManyRequestsException(...)` if exceeded
5. Hash submitted code and compare with `challenge.OtpHash`
6. If mismatch: `IncrementAttemptsAsync`, throw `ArgumentException("Invalid OTP.")`
7. If match: `MarkVerifiedAsync`, update `users.mfa_enabled = true`
8. Log `MfaVerified` to `auth_audit_log`
9. **Call `IIdentityVerificationService.SyncStatusAsync(userId)`** — inject this (FEAT-06 adds it; for now, create a stub interface so code compiles)

> **FEAT-06 stub:** Create `IIdentityVerificationService` with just `Task SyncStatusAsync(Guid userId)` returning `Task.CompletedTask`. FEAT-06 will implement it properly.

---

### Repository Implementation

`src/FlatPlanet.Security.Infrastructure/Repositories/MfaChallengeRepository.cs`

Follow `UserRepository` pattern: `IDbConnectionFactory`, open connection per method.

```sql
-- CreateAsync
INSERT INTO mfa_challenges (user_id, phone_number, otp_hash, expires_at)
VALUES (@UserId, @PhoneNumber, @OtpHash, @ExpiresAt)

-- GetActiveByUserIdAsync
SELECT * FROM mfa_challenges
WHERE user_id = @UserId AND verified_at IS NULL
ORDER BY created_at DESC LIMIT 1

-- MarkVerifiedAsync
UPDATE mfa_challenges SET verified_at = now() WHERE id = @Id

-- IncrementAttemptsAsync
UPDATE mfa_challenges SET attempts = attempts + 1 WHERE id = @Id
```

---

### SMS Sender Implementations

`src/FlatPlanet.Security.Infrastructure/ExternalServices/ConsoleSmsSender.cs`
```csharp
// Dev only — prints to Console instead of sending SMS
public class ConsoleSmsSender : ISmsSender
{
    public Task SendAsync(string to, string body)
    {
        Console.WriteLine($"[SMS to {to}]: {body}");
        return Task.CompletedTask;
    }
}
```

`src/FlatPlanet.Security.Infrastructure/ExternalServices/TwilioSmsSender.cs`
- Inject `IOptions<SmsOptions>` and `IHttpClientFactory`
- Call Twilio REST API: `POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json`
- Basic auth: `AccountSid:AuthToken`
- Form body: `From`, `To`, `Body`

---

### DTOs — Login Gate

`src/FlatPlanet.Security.Application/DTOs/Mfa/MfaLoginVerifyRequest.cs`
```csharp
public class MfaLoginVerifyRequest
{
    [Required]
    public string ChallengeId { get; set; } = string.Empty;

    [Required, StringLength(8, MinimumLength = 4)]
    public string Code { get; set; } = string.Empty;
}
```

Update existing `LoginResponse` DTO to support both login shapes:
```csharp
public class LoginResponse
{
    public bool    RequiresMfa   { get; set; }       // true when MFA challenge was issued
    public string? ChallengeId  { get; set; }        // set when RequiresMfa = true
    public string? AccessToken  { get; set; }        // set when RequiresMfa = false
    public string? RefreshToken { get; set; }        // set when RequiresMfa = false
    public int?    ExpiresIn    { get; set; }        // set when RequiresMfa = false
}
```

---

### Login Gate — Changes to AuthService

`src/FlatPlanet.Security.Application/Services/AuthService.cs` — `LoginAsync`

After credentials pass, before issuing JWT, add:

```csharp
if (user.MfaEnabled)
{
    // Don't issue JWT — send OTP instead
    var challenge = await _mfaService.SendLoginOtpAsync(user.Id, user.PhoneNumber);
    return new LoginResponse { RequiresMfa = true, ChallengeId = challenge.Id.ToString() };
}
// else: existing JWT issuance code unchanged
```

Add `SendLoginOtpAsync(Guid userId, string phoneNumber)` to `IMfaService`:
- Does NOT update `phone_number` on the user (already enrolled)
- **FIX — Race condition**: before creating a new challenge, invalidate all existing active challenges for this user:
  ```csharp
  await _mfaChallenges.InvalidateActiveAsync(userId); // marks all active as expired
  ```
  Then create the new challenge. This ensures only the latest challenge is valid — prevents concurrent login attempts from cross-contaminating OTP codes.
- Wrap `_smsSender.SendAsync` in try/catch same as `EnrollAndSendOtpAsync` — throw 503 on failure
- Returns the new `MfaChallenge` so `AuthService` can get its `Id`

Add to `IMfaChallengeRepository`:
```csharp
Task InvalidateActiveAsync(Guid userId);
```
```sql
UPDATE mfa_challenges
SET expires_at = now()
WHERE user_id = @UserId AND verified_at IS NULL AND expires_at > now()
```

---

### Login Verify — Changes to MfaService

Add `VerifyLoginOtpAsync(Guid challengeId, string code)` to `IMfaService`:
1. `GetByIdAsync(challengeId)` — throw `KeyNotFoundException` if not found
2. Check not expired, not over max attempts (same as `VerifyOtpAsync`)
3. Hash and compare — increment attempts on mismatch
4. On match: `MarkVerifiedAsync`, then issue JWT via `IJwtService` for `challenge.UserId`
5. Create session, return full `LoginResponse` with `AccessToken`, `RefreshToken`, `ExpiresIn`
6. Log `MfaLoginVerified` to `auth_audit_log`

Add to `IMfaChallengeRepository`:
```csharp
Task<MfaChallenge?> GetByIdAsync(Guid id);
```
```sql
SELECT * FROM mfa_challenges WHERE id = @Id
```

---

### Controller

`src/FlatPlanet.Security.API/Controllers/MfaController.cs`

- Route: `api/v1/mfa`

```
POST /api/v1/mfa/enroll           [Authorize]  → EnrollAndSendOtpAsync(GetUserId(), phoneNumber)
POST /api/v1/mfa/otp/verify       [Authorize]  → VerifyOtpAsync(GetUserId(), code)
POST /api/v1/mfa/otp/login-verify [AllowAnonymous] → VerifyLoginOtpAsync(challengeId, code)
                                                    → returns LoginResponse with tokens
```

`login-verify` is `[AllowAnonymous]` — the user has no JWT yet at this point.

Use `GetUserId()` from `ApiController` base class for the two `[Authorize]` endpoints.

---

## Wire in Program.cs

```csharp
// Options
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.Section));

// Repositories
builder.Services.AddScoped<IMfaChallengeRepository, MfaChallengeRepository>();

// SMS sender — use ConsoleSmsSender in Development, TwilioSmsSender in Production
if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();
else
    builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();

// Services
builder.Services.AddScoped<IMfaService, MfaService>();

// Stub for FEAT-06 — replace when FEAT-06 is merged
builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationServiceStub>();
```

---

## appsettings.json Addition

```json
"Sms": {
  "AccountSid": "",
  "AuthToken": "",
  "FromNumber": ""
}
```

In Azure App Configuration (Production):
- `Sms__AccountSid = <from Twilio>`
- `Sms__AuthToken = <from Twilio>`
- `Sms__FromNumber = <verified Twilio number>`

> `Sms__Provider` is NOT needed — the Production vs Development switch is done in `Program.cs` via `IsDevelopment()`.

---

## MFA Challenge Cleanup

Expired and verified challenge rows accumulate indefinitely. Add a cleanup method:

Add to `IMfaChallengeRepository`:
```csharp
Task DeleteExpiredAsync();
```
```sql
DELETE FROM mfa_challenges
WHERE expires_at < now() - INTERVAL '24 hours'
```

Register in the same `AuditLogCleanupService` (FEAT-03) background service — add a call to `DeleteExpiredAsync()` in its daily loop. No separate hosted service needed.

---

## Add to AuditEventType.cs

```csharp
public const string MfaOtpIssued    = "mfa_otp_issued";
public const string MfaVerified     = "mfa_verified";
public const string MfaFailed       = "mfa_failed";
public const string MfaLoginVerified = "mfa_login_verified";
```

---

## Unit Tests

`tests/FlatPlanet.Security.Tests/MfaServiceTests.cs`

| Test | Scenario |
|---|---|
| `EnrollAndSendOtp_StoresHashedOtp_NotPlaintext` | OTP in DB must not equal submitted code |
| `VerifyOtp_Expired_ThrowsArgumentException` | ExpiresAt in past |
| `VerifyOtp_MaxAttempts_ThrowsTooManyRequests` | Attempts >= max |
| `VerifyOtp_WrongCode_IncrementsAttempts` | Wrong code increments attempts |
| `VerifyOtp_CorrectCode_MarksVerified` | VerifiedAt set, mfa_enabled updated |

---

## Testing After Deploy

**Enrollment flow:**
1. Login with JWT → `POST /api/v1/mfa/enroll` with `{ "phoneNumber": "+639XXXXXXXXX" }`
   - Dev: check Console output for OTP
   - Verify `mfa_challenges` row created in Supabase
2. `POST /api/v1/mfa/otp/verify` with wrong code → `400 Invalid OTP`
3. `POST /api/v1/mfa/otp/verify` with correct code → `200`
   - Verify `mfa_challenges.verified_at` is set
   - Verify `users.mfa_enabled = true`

**Login gate flow (test AFTER enrollment above):**
4. `POST /api/v1/auth/login` → response must have `{ "requiresMfa": true, "challengeId": "..." }` — no accessToken
5. Copy the `challengeId`
6. `POST /api/v1/mfa/otp/login-verify` with `{ "challengeId": "...", "code": "WRONG" }` → `400`
7. `POST /api/v1/mfa/otp/login-verify` with correct code from Console → `200` with `accessToken`, `refreshToken`
8. Use the returned `accessToken` — must work on HubApi

**Rate limit test:**
9. `POST /api/v1/mfa/enroll` twice in quick succession → second call returns same expiry, no new SMS sent

**Concurrent login test:**
10. Simulate two logins for same user → only the LATEST OTP code works, the earlier one returns `400`
