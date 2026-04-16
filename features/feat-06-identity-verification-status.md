# FEAT-06 — Identity Verification Status

**Repo:** flatplanet-security-platform
**Branch:** `feature/feat-06-identity-verification-status`
**Depends on:** FEAT-02 (table), FEAT-05 (MFA OTP must be merged first)
**Coder:** SP coder

---

## Goal

Maintain a single `identity_verification_status` row per user that tracks
whether they have passed OTP verification (and later, video verification).
Expose a service-token endpoint so HubApi can query it.
ISO 27001 A.9.4.2.

---

## How It Works

```
MfaService.VerifyOtpAsync completes
    └── calls IIdentityVerificationService.SyncStatusAsync(userId)
            └── checks: any mfa_challenges row with verified_at IS NOT NULL for this user?
            └── upserts identity_verification_status:
                    otp_verified = true
                    video_verified = false (until FEAT-08)
                    verified_at   = now() (first time fully_verified becomes true)
```

`fully_verified` is a **plain BOOLEAN column** (not a generated column — already correct in V9 from FEAT-02).
The service layer computes it using the `require_video_verification` config flag:

```csharp
var requireVideo  = config == "true";
var fullyVerified = otpVerified && (!requireVideo || videoVerified);
```

This means: when `require_video_verification = false` (current), `fullyVerified = otpVerified`. When video is enabled later, flip the config and both checks are required automatically.

---

## Files to Create

### Domain Entity

`src/FlatPlanet.Security.Domain/Entities/IdentityVerificationStatus.cs`
```csharp
public class IdentityVerificationStatus
{
    public Guid      Id            { get; set; }
    public Guid      UserId        { get; set; }
    public bool      OtpVerified   { get; set; }
    public bool      VideoVerified { get; set; }
    public bool      FullyVerified { get; set; }
    public DateTime? VerifiedAt    { get; set; }
    public DateTime  UpdatedAt     { get; set; }
}
```

---

### Repository Interface

`src/FlatPlanet.Security.Application/Interfaces/Repositories/IIdentityVerificationRepository.cs`
```csharp
public interface IIdentityVerificationRepository
{
    Task<IdentityVerificationStatus?> GetByUserIdAsync(Guid userId);
    Task UpsertAsync(Guid userId, bool otpVerified, bool videoVerified,
                     bool fullyVerified, DateTime? verifiedAt);
}
```

---

### DTO

`src/FlatPlanet.Security.Application/DTOs/Identity/IdentityVerificationStatusDto.cs`
```csharp
public class IdentityVerificationStatusDto
{
    public bool      OtpVerified   { get; set; }
    public bool      VideoVerified { get; set; }
    public bool      FullyVerified { get; set; }
    public DateTime? VerifiedAt    { get; set; }
}
```

---

### Service Interface

`src/FlatPlanet.Security.Application/Interfaces/Services/IIdentityVerificationService.cs`
```csharp
public interface IIdentityVerificationService
{
    Task SyncStatusAsync(Guid userId);
    Task<IdentityVerificationStatusDto> GetStatusAsync(Guid userId);
}
```

---

### Service Implementation

`src/FlatPlanet.Security.Application/Services/IdentityVerificationService.cs`

Constructor injects: `IIdentityVerificationRepository`, `IMfaChallengeRepository`,
`ISecurityConfigRepository`, `IAuditLogRepository`, `IMemoryCache`

> Cache `require_video_verification` with a 5-minute TTL via `IMemoryCache` — same pattern as FEAT-05:
> ```csharp
> var config = await _cache.GetOrCreateAsync("cfg:require_video_verification", async entry => {
>     entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
>     return await _configRepo.GetAsync("require_video_verification", "false");
> });
> ```
> Register `IMemoryCache` in `Program.cs` via `builder.Services.AddMemoryCache()` if not already registered.

**SyncStatusAsync(Guid userId):**
1. Load `require_video_verification` from `security_config` (default `"false"`)
2. Check `_mfaChallenges.HasVerifiedChallengeAsync(userId)` → `otpVerified`
3. `videoVerified = false` (FEAT-08 will set this)
4. `requireVideo = config == "true"`
5. `fullyVerified = otpVerified && (!requireVideo || videoVerified)`
6. Load current status via `GetByUserIdAsync`
7. If `fullyVerified` just became `true` for the first time → `verifiedAt = now()`
8. `UpsertAsync(userId, otpVerified, videoVerified, fullyVerified, verifiedAt)`
9. If `fullyVerified` is newly true → log `IdentityVerificationCompleted` to `auth_audit_log`

**GetStatusAsync(Guid userId):**
1. `GetByUserIdAsync(userId)` — if null, return all-false DTO (user hasn't enrolled yet)
2. Load `require_video_verification` from `security_config` (cached, 5-min TTL)
3. **FIX — do not return the stored `fully_verified` value directly.** Recompute it using the current config:
   ```csharp
   var requireVideo  = config == "true";
   var fullyVerified = status.OtpVerified && (!requireVideo || status.VideoVerified);
   ```
   This ensures the response is always accurate even if the config flag was changed after the row was written.
4. Map to `IdentityVerificationStatusDto` using the recomputed `fullyVerified`

---

### Repository Implementation

`src/FlatPlanet.Security.Infrastructure/Repositories/IdentityVerificationRepository.cs`

```sql
-- GetByUserIdAsync
SELECT * FROM identity_verification_status WHERE user_id = @UserId

-- UpsertAsync
INSERT INTO identity_verification_status
    (user_id, otp_verified, video_verified, fully_verified, verified_at, updated_at)
VALUES
    (@UserId, @OtpVerified, @VideoVerified, @FullyVerified, @VerifiedAt, now())
ON CONFLICT (user_id) DO UPDATE SET
    otp_verified   = EXCLUDED.otp_verified,
    video_verified = EXCLUDED.video_verified,
    fully_verified = EXCLUDED.fully_verified,
    verified_at    = COALESCE(identity_verification_status.verified_at, EXCLUDED.verified_at),
    updated_at     = now()
```

> `COALESCE` on `verified_at` ensures it's only stamped once (on first full verification).

---

**Also add to `IMfaChallengeRepository`:**
```csharp
Task<bool> HasVerifiedChallengeAsync(Guid userId);
```
```sql
-- Implementation
SELECT EXISTS (
    SELECT 1 FROM mfa_challenges
    WHERE user_id = @UserId AND verified_at IS NOT NULL
)
```

---

### Controller

`src/FlatPlanet.Security.API/Controllers/IdentityVerificationController.cs`

- Route: `api/v1/identity/verification`

```
GET  /status
     [Authorize] (user JWT)
     → GetStatusAsync(GetUserId())
     → OkData(dto)

GET  /service/status/{userId}
     [Authorize(Policy = "PlatformOwner")] (ServiceToken — for HubApi)
     → GetStatusAsync(userId)
     → OkData(dto)
```

---

## Wire in Program.cs

Replace the FEAT-05 stub registration:

```csharp
// Remove stub:
// builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationServiceStub>();

// Add real implementation:
builder.Services.AddScoped<IIdentityVerificationRepository, IdentityVerificationRepository>();
builder.Services.AddScoped<IIdentityVerificationService, IdentityVerificationService>();
```

---

## Add to AuditEventType.cs

```csharp
public const string IdentityVerificationCompleted = "identity_verification_completed";
```

---

## Testing After Deploy

1. Enroll phone + verify OTP (FEAT-05 flow)
2. `GET /api/v1/identity/verification/status` → `{ otpVerified: true, fullyVerified: true }`
3. `GET /api/v1/identity/verification/service/status/{userId}` with ServiceToken → same response
4. Check `identity_verification_status` row in Supabase — `fully_verified = true`, `verified_at` set
5. Run `UPDATE security_config SET config_value = 'true' WHERE config_key = 'require_video_verification'`
   → `GET /status` → `{ otpVerified: true, videoVerified: false, fullyVerified: false }` (video now required)
   → Reset back to `'false'` after test

---

## Video Plug-in Later (FEAT-08)

When video is ready:
- Add `IIdentityVideoRepository` and check `video_verified` in `SyncStatusAsync`
- Flip `require_video_verification = true` in `security_config`
- **Zero changes to endpoints or HubApi**
