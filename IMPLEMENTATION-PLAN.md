# FlatPlanet Security Platform — Implementation Plan
**Branch:** `feature/feat-mfa-changes-clean`  
**Authors:** Lightning (plan), Cloud (execution), reviewed by user before any commit  
**Last updated:** 2026-04-17

---

## Review Gate — Three-Tier (applies at every gate marker)

```
⛔ STOP — No commit until all three tiers pass.

TIER 1 — Cloud self-review (before pinging Lightning):
  Cloud reads every file he changed, line by line.
  Checklist:
  ✓ dotnet build is green
  ✓ Changes match the plan exactly — no scope creep, no shortcuts
  ✓ All null paths handled
  ✓ All new repository methods exist on the interface AND the implementation
  ✓ No constructor signature broken elsewhere in the codebase
  ✓ Fire-and-forget blocks are wrapped in try/catch with LogError
  ✓ Transactions roll back on exception (catch → rollback → rethrow)
  ✓ No hardcoded strings where a config value should be used
  ✓ No plaintext secrets or tokens in logs or DB
  ✓ No phone_number / PhoneNumber references left after Phase 2
  Cloud writes a short self-review summary: what changed, what was checked,
  any concerns or deviations from the plan.

TIER 2 — Lightning peer review (after receiving Cloud's summary):
  Lightning reads the diff and Cloud's self-review summary.
  Lightning focuses on: blast radius, transaction correctness,
  cache invalidation correctness, ISO 27001 compliance, race conditions,
  anything Cloud flagged as a concern.
  Lightning produces a verdict: Approved / Needs Changes (with specifics).

TIER 3 — User sign-off:
  User reviews Lightning's verdict and the diff summary.
  User gives explicit go/no-go before Cloud commits or pushes.
  No merge to develop or main without user sign-off — ever.
```

Cloud also performs a quick **Tier 1 self-check after each individual step** within a phase.
Catch problems early rather than compounding them across 10 steps.

---

## Build Checkpoints

| After | Required |
|---|---|
| Each individual step (1.x or 2.x) | `dotnet build` — green before moving on |
| End of Phase 1 | `dotnet build` + `dotnet test` — both green |
| End of Phase 2 | `dotnet build` + `dotnet test` — both green |

---

## Migration Numbering

Highest existing migration: **V21**  
Next available: **V22**, then **V23**.  
Never reuse a number. Cloud checks the `db/` folder before creating any migration.

---

---

# PHASE 1 — Platform Refactoring (All Audit Issues)

Fix all 25 audit findings before adding any new feature complexity.  
Ordered by dependency: infrastructure first, services second, API layer last.

---

## Step 1.1 — Security config caching in `AuthService`

**Covers:** P2 (no cache on LoadConfigAsync), P5 (17 DB calls per login)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/AuthService.cs`
- `tests/FlatPlanet.Security.Tests/AuthServiceTests.cs`

**What to do:**
- Inject `IMemoryCache` into `AuthService` constructor
- Change `LoadConfigAsync()` to check `IMemoryCache` first using key `"fp:sec:cfg:all"` (5-minute TTL via `AbsoluteExpirationRelativeToNow`)
- On cache miss: call `_securityConfig.GetAllAsync()`, populate cache, return result
- `AuthServiceTests.cs`: add `Mock<IMemoryCache>` field, pass `_cache.Object` as new constructor arg in `CreateService()`

**Notes:**
- `IMemoryCache` is already registered in Program.cs — no DI change needed
- Both `LoginAsync` and `RefreshAsync` call `LoadConfigAsync()` — both benefit automatically
- Cache key naming convention: `"fp:sec:{domain}:{key}"` — used consistently throughout Phase 1 for future Redis migration

---

## Step 1.2 — Post-login side effects → fire-and-forget

**Covers:** P6 (response blocked after session committed), SC6 (scalability)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/AuthService.cs`

**What to do:**
- Lines 201–225: the `await Task.WhenAll(auditLog×2, UpdateLastSeen, RecordAttempt)` block currently blocks the response after the session transaction is committed
- Replace with fire-and-forget:
  ```csharp
  _ = Task.Run(async () =>
  {
      try
      {
          await Task.WhenAll(
              _auditLog.LogAsync(...),
              _auditLog.LogAsync(...),
              _users.UpdateLastSeenAtAsync(user.Id, now),
              _loginAttempts.RecordAsync(...)
          );
      }
      catch (Exception ex)
      {
          _logger.LogError(ex, "Post-login side effects failed for user {UserId}", user.Id);
      }
  });
  return new LoginResponse { ... };
  ```
- The `return` moves up immediately after the fire-and-forget launch

**Notes:**
- Session and refresh token are already committed before this block — safe to fire-and-forget
- This pattern is already established in the codebase (ChangePasswordAsync)

---

## Step 1.3 — Session limit race condition → atomic SQL

**Covers:** SC4 (race condition: 3 sequential calls outside transaction)

**Files to change:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/ISessionRepository.cs`
- `src/FlatPlanet.Security.Infrastructure/Repositories/SessionRepository.cs`
- `src/FlatPlanet.Security.Application/Services/AuthService.cs`

**What to do:**
- Add to `ISessionRepository`:
  ```csharp
  Task EvictOldestIfOverLimitAsync(Guid userId, int maxSessions, IDbConnection conn, IDbTransaction tx);
  ```
- Implement in `SessionRepository` using single atomic SQL:
  ```sql
  WITH to_evict AS (
      SELECT id FROM sessions
      WHERE user_id = @UserId AND is_active = true
      ORDER BY last_active_at ASC
      LIMIT GREATEST(0,
          (SELECT COUNT(*) FROM sessions WHERE user_id = @UserId AND is_active = true)
          - @MaxSessions + 1
      )
  )
  UPDATE sessions
  SET is_active = false, ended_at = now(), end_reason = 'replaced'
  WHERE id IN (SELECT id FROM to_evict)
  ```
- In `AuthService.LoginAsync`: remove lines 145–151 (CountActive / GetOldest / EndSession). Add `await _sessions.EvictOldestIfOverLimitAsync(user.Id, maxSessions, conn, tx)` inside the existing session/token transaction (the `using (var conn...) using (var tx...)` block), before `CreateAsync(session)`

**Notes:**
- The SQL is a no-op if the user is under the session limit — safe to always run
- Moving inside the existing transaction makes the eviction + new session creation atomic

---

## Step 1.4 — `login_attempts` table cleanup

**Covers:** R4 (AuditLogCleanupService doesn't clean login_attempts — table grows unbounded)

**Files to change:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/ILoginAttemptRepository.cs`
- `src/FlatPlanet.Security.Infrastructure/Repositories/LoginAttemptRepository.cs`
- `src/FlatPlanet.Security.Infrastructure/BackgroundServices/AuditLogCleanupService.cs`

**What to do:**
- Add to `ILoginAttemptRepository`: `Task DeleteOlderThanAsync(int days)`
- Implement in `LoginAttemptRepository`:
  ```sql
  DELETE FROM login_attempts
  WHERE attempted_at < now() - (@Days || ' days')::interval
  ```
- In `AuditLogCleanupService.ExecuteAsync`: inject `ILoginAttemptRepository`, call `await loginAttempts.DeleteOlderThanAsync(retentionDays)` alongside the existing audit and MFA cleanup calls

---

## Step 1.5 — Remove plaintext refresh token from DB

**Covers:** S1 (live refresh tokens stored in plaintext — exposed if DB is compromised)

**Files to change:**
- `db/V22__remove_token_plain.sql` *(new migration)*
- `src/FlatPlanet.Security.Domain/Entities/RefreshToken.cs`
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IRefreshTokenRepository.cs`
- `src/FlatPlanet.Security.Infrastructure/Repositories/RefreshTokenRepository.cs`
- `src/FlatPlanet.Security.Application/Services/AuthService.cs`

**What to do:**
- Migration V22: `ALTER TABLE refresh_tokens DROP COLUMN IF EXISTS replaced_by_token_plain`
- `RefreshToken.cs`: remove `public string? ReplacedByTokenPlain { get; set; }`
- `IRefreshTokenRepository`: change `RotateAsync(Guid id, string newTokenHash, string newTokenPlain)` → `RotateAsync(Guid id, string newTokenHash)` (remove plain param)
- `RefreshTokenRepository.RotateAsync`: remove `replaced_by_token_plain = @NewTokenPlain` from UPDATE SQL, remove the parameter
- `AuthService.RefreshAsync` grace period branch (currently returns `stored.ReplacedByTokenPlain`):
  - In the grace period, we can no longer re-return the original new token since we don't store it
  - Instead: generate a brand new token pair (plain3, hash3), revoke the existing new token (found by `replaced_by_token_hash` on the stored row), create a fresh RefreshToken row, return plain3
  - This eliminates the need to ever store a plaintext token in the DB
- `AuthService.RefreshAsync` normal rotation: update `RotateAsync` call to remove the `newTokenPlain` argument

**Notes:**
- The grace period re-issue approach is slightly more expensive (extra DB write) but only happens in actual race conditions — extremely rare in practice
- Cloud must trace ALL callers of `RotateAsync` and update them

---

## Step 1.6 — `OffboardingService` → single DB transaction

**Covers:** S2 (security), R1 (reliability) — partial failure leaves user deactivated but tokens live

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/OffboardingService.cs`
- Possibly: `IUserRepository`, `UserRepository`, `ISessionRepository`, `SessionRepository`, `IRefreshTokenRepository`, `RefreshTokenRepository`, `IUserAppRoleRepository`, `UserAppRoleRepository`, `IAuditLogRepository`, `AuditLogRepository`

**What to do:**
- Inject `IDbConnectionFactory` into `OffboardingService`
- Wrap all 5 operations inside a single `conn + tx`:
  ```csharp
  using var conn = await _db.CreateConnectionAsync();
  using var tx = conn.BeginTransaction();
  try
  {
      await _users.UpdateStatusAsync(userId, "inactive", conn, tx);
      await _sessions.EndAllActiveSessionsByUserAsync(userId, "offboarded", conn, tx);
      await _refreshTokens.RevokeAllByUserAsync(userId, "offboarded", conn, tx);
      await _userAppRoles.SuspendAllByUserAsync(userId, conn, tx);
      await _auditLog.LogAsync(new AuthAuditLog { ... }, conn, tx);
      tx.Commit();
  }
  catch
  {
      tx.Rollback();
      throw;
  }
  ```
- **Before adding conn/tx overloads**: Cloud checks which of these methods already have a `(IDbConnection, IDbTransaction)` overload. Add only what is missing. Never duplicate existing signatures.

---

## Step 1.7 — `CompanyService` → transactions for suspension and deactivation

**Covers:** S3 (security), R2 (reliability), S5 (no atomic cascade on deactivation)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/CompanyService.cs`
- Possibly: `ICompanyRepository`, `CompanyRepository`, `IUserRepository`, `UserRepository`, `IRefreshTokenRepository`, `RefreshTokenRepository`, `IUserAppRoleRepository`, `UserAppRoleRepository`, `IAuditLogRepository`, `AuditLogRepository`

**What to do:**
- Inject `IDbConnectionFactory` into `CompanyService`
- **Suspended path** (lines 76–87): wrap `UpdateStatusAsync(company)` + `SuspendByCompanyIdAsync` + `RevokeAllByCompanyIdAsync` + `LogAsync` in a single transaction
- **Inactive path** (lines 89–102): replace the `Task.WhenAll(users.Select(...))` per-user fan-out with bulk SQL inside a transaction:
  - `UPDATE users SET status = 'inactive' WHERE company_id = @CompanyId`
  - `UPDATE user_app_roles SET status = 'suspended' WHERE user_id IN (SELECT id FROM users WHERE company_id = @CompanyId)`
  - Add bulk repository methods if they don't exist: `IUserRepository.DeactivateAllByCompanyIdAsync`, `IUserAppRoleRepository.SuspendAllByCompanyIdAsync`
  - `LogAsync` inside the transaction too
- **Before adding overloads**: Cloud checks existing signatures first

---

## Step 1.8 — Rate limiting on additional endpoints

**Covers:** S4 (missing rate limits on forgot-password, change-password, authorize)

**Files to change:**
- `src/FlatPlanet.Security.API/Program.cs`
- `src/FlatPlanet.Security.API/Controllers/AuthController.cs` (or wherever these actions live)

**What to do:**
- Cloud first checks what rate limiting policies already exist in `Program.cs` and which controllers already have `[EnableRateLimiting]`
- Add new policies (or extend existing ones):
  - `POST /api/auth/forgot-password` → per-IP: 3 requests per 15 minutes
  - `POST /api/auth/change-password` → per-user (JWT sub claim as key): 5 requests per 15 minutes
  - `POST /api/auth/authorize` → per-user: 60 requests per minute (API-to-API call, don't strangle it)
- Apply via `[EnableRateLimiting("policy-name")]` on the relevant controller actions

---

## Step 1.9 — `UserContextService` → access check before returning data

**Covers:** S6 (any authenticated user can query context for any app slug)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/UserContextService.cs`

**What to do:**
- After loading `appRoleIds` (the roles the user has in the requested app), check:
  ```csharp
  if (!appRoleIds.Any())
      throw new ForbiddenException("User does not have access to this application.");
  ```
- This uses already-loaded data — zero extra DB calls
- Currently the service returns an empty roles/permissions response instead of 403 — this is wrong

---

## Step 1.10 — `AuthorizationService` → stop flooding the audit table

**Covers:** S7 (every authorize check logged including allowed), SC5 (verbose logging)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/AuthorizationService.cs`

**What to do:**
- Option A (simpler): only call `LogAuthCheckAsync` when `allowed == false`
- Option B (configurable): inject `IMemoryCache` (already being added), read `"audit_log_authorize_allowed"` config key (default `"false"`), only log allowed checks when the flag is `"true"`
- **Use Option B** — keeps the behaviour configurable for admins without code changes
- The cached config (from Step 1.1's pattern) can be reused here — inject `ISecurityConfigRepository` + `IMemoryCache`, use the same `"fp:sec:cfg:all"` key pattern

---

## Step 1.11 — Service token: include `X-Service-Name` in audit details

**Covers:** S8 (service-to-service calls are unidentified in the audit trail)

**Files to change:**
- Cloud must first locate where service tokens are validated (likely `SessionValidationMiddleware.cs` or a separate middleware/attribute). Read those files before touching anything.
- Wherever service tokens produce an audit log entry: add `service_name` to the `Details` JSON, sourced from either a `service_name` JWT claim or the `X-Service-Name` request header

---

## Step 1.12 — `SessionValidationMiddleware` → cache session + throttle `last_active_at` writes

**Covers:** P1 (2 DB calls per authenticated request), SC1 (scalability), R3 (timeout audit not fire-and-forget)

**Files to change:**
- `src/FlatPlanet.Security.API/Middleware/SessionValidationMiddleware.cs`

**What to do:**
- Add `IMemoryCache` as a constructor parameter (middleware registered as singleton — IMemoryCache is singleton — OK)
- Cache key: `"fp:sec:session:{sessionId}"`, TTL: 30 seconds
- On each request:
  1. Try get from cache — if hit and valid, skip `GetByIdAsync` DB call
  2. If cache miss: call `GetByIdAsync`, store result in cache
  3. Only call `UpdateLastActiveAtAsync` when `now - session.LastActiveAt > 30 seconds` — throttle DB writes
  4. Update the cached session's `LastActiveAt` in-memory so the idle timeout check stays accurate
- On absolute or idle timeout:
  - Call `EndSessionAsync` directly (state-changing, must not be deferred)
  - Remove from cache: `_cache.Remove("fp:sec:session:{sessionId}")`
  - Audit log write: **fire-and-forget** with LogError (covers R3)

---

## Step 1.13 — `UserContextService` → cache results

**Covers:** P3 (7 DB calls per call, no caching)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/UserContextService.cs`

**What to do:**
- Inject `IMemoryCache`
- Cache key: `"fp:sec:ctx:{userId}:{appSlug}"`, TTL: 2 minutes
- On cache hit: return cached `UserContextResponse` immediately (0 DB calls)
- On cache miss: run the existing 7 DB calls, cache the result, return it
- **No explicit cache invalidation for now** — 2-minute TTL is acceptable. When Redis is introduced, we add invalidation on role changes. Document this as a known limitation.

---

## Step 1.14 — `AuthorizationService` → cache authorization results

**Covers:** P4 (4 DB calls + 1 audit write per authorize check)

**Files to change:**
- `src/FlatPlanet.Security.Application/Services/AuthorizationService.cs`

**What to do:**
- Inject `IMemoryCache`
- Cache key: `"fp:sec:authz:{userId}:{appSlug}:{permission}"`, TTL: 2 minutes
- Only cache `allowed == true` results — denied results must always re-evaluate (user's permissions may have been updated)
- On cache hit: return the cached `AuthorizeResponse` immediately
- On cache miss: run existing DB calls, apply caching, return

---

## Step 1.15 — `SmtpEmailService` → Polly retry

**Covers:** R5 (no retry on transient SMTP failures, 10s timeout too tight for Office 365)

**Files to change:**
- `src/FlatPlanet.Security.Infrastructure/FlatPlanet.Security.Infrastructure.csproj`
- `src/FlatPlanet.Security.Infrastructure/Email/SmtpEmailService.cs`

**What to do:**
- Check if `Polly` is already referenced in the `.csproj`. If not, add: `<PackageReference Include="Polly" Version="8.*" />`
- Increase `SmtpClient.Timeout` from 10,000 ms to 30,000 ms
- Extract a private `SendWithRetryAsync(MimeMessage message)` method:
  - Polly `AsyncRetryPolicy`: 3 attempts, exponential backoff (2s, 4s), catch `SmtpCommandException` and `SocketException`
  - All SMTP logic (connect, authenticate, send, disconnect) goes inside this method
- Both `SendPasswordResetEmailAsync` and the upcoming `SendMfaOtpEmailAsync` (Phase 2) call `SendWithRetryAsync`

---

## ⛔ PHASE 1 REVIEW GATE

```
Cloud: complete Tier 1 self-review. Write summary. Push branch. Ping Lightning.
Lightning: Tier 2 peer review of full diff.
User: Tier 3 sign-off before any commit is made.
```

---

---

# PHASE 2 — MFA Overhaul (TOTP + Email OTP)

Only begins after Phase 1 is committed and approved.

> **Gap analysis reviewed by Lightning — eight passes (2026-04-17). 38 gaps corrected.**
> G1: Insecure OTP generation. G2: Identity verification sync. G3: InvalidateActive type-scoped.
> G4: MFA routes /v1. G5: Rate limit on TOTP verify. G6: V23 resets SMS users first.
> G7: DTO cleanup. G8: UpdatePhoneNumberAsync named. NEW1: IIdentityVerificationService in constructor.
> NEW2: UserId in TOTP/email OTP challenge responses. NEW3: Build batch note (2.2–2.12). NEW4: Admin routes /v1.
> T1: Build note contradiction removed. T2: ResetMfaColumnsAsync atomic method. T3: Email send awaited (not fire-and-forget).
> T4: Session cap inside transaction for all three MFA login methods. T5: BeginTotpEnrolmentAsync returns DTO. T6: Test cases added.
> U1: MfaEnrolmentPending gate scoped to MfaMethod=="totp" only — email_otp users were permanently blocked.
> U2: UserMfaStatusResponse DTO defined for AdminMfaController GET /status.
> U3: Test case added — VerifyTotpEnrolmentAsync calls SyncStatusAsync after valid code.
> U4: OTP hash method unified to _jwt.HashToken() for both store and verify — raw SHA-256 removed.
> U5: VerifyLoginTotpAsync explicit null guards for user and MfaTotpSecret.
> U6: Config key rename annotated — old mfa_otp_max_attempts vs new mfa_max_otp_attempts.
> Dead method: EnableMfaAsync removed from IMfaService (no endpoint, no caller).
> V1: GetActiveByUserIdAsync removed from IMfaChallengeRepository (dead after Phase 2).
> V2: Base32Encoding.ToString(secret) — NOT Base32.Encode() — OtpNet compile error fix.
> V3: SendEmailOtpAsync explicit null guard for user not found.
> V4: AdminMfaController POST endpoints return 204 No Content.
> V5: AuthServiceTests MfaEnrolmentPending test requires MfaMethod="totp"; added null-method edge case.
> V6: DisableMfaAsync and ResetMfaAsync test cases added.
> W1: InvalidateActiveAsync removed from IMfaChallengeRepository (dead code after Phase 2).
> W2: DisableMfaAsync/ResetMfaAsync step bodies now reference ResetMfaColumnsAsync explicitly.
> W3: VerifyLoginEmailOtpAsync step 2 guard failures have explicit exception types.
> W4: VerifyLoginTotpAsync success path and invalid code tests added.
> X1: V23 reset condition broadened to WHERE mfa_enabled=true — original phone_verified/mfa_method='sms' condition matched zero real Phase 1 users.
> X2: Test added for VerifyLoginEmailOtpAsync wrong challenge_type guard (W3 verification).
> Y1: All three session-creating methods now explicitly call GetPlatformRoleNamesForUserAsync + IssueAccessTokenAsync before returning LoginResponse.

---

## Step 2.1 — DB Migration V23

**Note:** V22 (`remove_token_plain`) is done. Phase 2 migration is V23.

**File:** `db/V23__mfa_totp_and_challenge_type.sql`

```sql
-- GAP G6 FIX (X1 refined): Reset ALL mfa_enabled users before dropping phone columns.
-- Phase 1 VerifyOtpAsync sets mfa_enabled=true but never sets phone_verified=true or mfa_method='sms'.
-- Real Phase 1 SMS users have: mfa_enabled=true, phone_verified=false, mfa_method=null.
-- The original condition (phone_verified=true OR mfa_method='sms') resets zero real users.
-- Safe to reset ALL mfa_enabled users here: at V23 deploy time, no user has TOTP (feature didn't exist).
UPDATE users
SET mfa_enabled = false, mfa_method = null
WHERE mfa_enabled = true;

-- Users: add TOTP columns, drop SMS columns
ALTER TABLE users
  ADD COLUMN IF NOT EXISTS mfa_totp_secret   TEXT,
  ADD COLUMN IF NOT EXISTS mfa_totp_enrolled BOOLEAN NOT NULL DEFAULT false,
  DROP COLUMN IF EXISTS phone_number,
  DROP COLUMN IF EXISTS phone_verified;

-- mfa_challenges: add challenge_type, drop phone_number
-- phone_number was NOT NULL in V8 — the ADD must come first so existing rows get a default
ALTER TABLE mfa_challenges
  ADD COLUMN IF NOT EXISTS challenge_type TEXT NOT NULL DEFAULT 'email_otp';

-- Backfill any existing rows that might have null (defensive)
UPDATE mfa_challenges SET challenge_type = 'email_otp' WHERE challenge_type IS NULL;

ALTER TABLE mfa_challenges
  DROP COLUMN IF EXISTS phone_number;

-- Add index for type-scoped queries
CREATE INDEX IF NOT EXISTS idx_mfa_challenges_type
  ON mfa_challenges(user_id, challenge_type, verified_at)
  WHERE verified_at IS NULL;

-- Seed new config keys
INSERT INTO security_config (config_key, config_value, description) VALUES
  ('mfa_email_otp_expiry_minutes', '10',         'Email OTP validity window in minutes'),
  ('mfa_totp_issuer',              'FlatPlanet',  'Issuer name shown in authenticator apps'),
  ('mfa_max_otp_attempts',         '5',           'Max wrong OTP attempts before challenge expires')
ON CONFLICT (config_key) DO NOTHING;

-- NOTE (U6): V8 seeded 'mfa_otp_max_attempts' (old key from Phase 1 SMS code).
-- Phase 2 uses 'mfa_max_otp_attempts' (new unified key). Both rows may exist in DB.
-- The Phase 2 MfaService reads ONLY 'mfa_max_otp_attempts'. The old key is orphaned.
```

---

## ⚠️ Build Note — Steps 2.2 through 2.12 (NEW3)

Steps 2.2–2.12 form a single non-building batch. Deleting SMS files (2.2), dropping phone entity fields (2.3), and rewriting MfaService (2.11) are interdependent — the build will be broken until Step 2.12 is complete.

**Do NOT run `dotnet build` between Steps 2.2 and 2.12.** Only build after Step 2.12.

---

## Step 2.2 — Remove SMS infrastructure

**Files to DELETE:**
- `src/FlatPlanet.Security.Application/Interfaces/Services/ISmsSender.cs`
- `src/FlatPlanet.Security.Application/Common/Options/SmsOptions.cs`
- `src/FlatPlanet.Security.Infrastructure/ExternalServices/ConsoleSmsSender.cs`
- `src/FlatPlanet.Security.Infrastructure/ExternalServices/TwilioSmsSender.cs`

**Files to EDIT:**
- `src/FlatPlanet.Security.API/Program.cs` — remove `SmsOptions` config binding, `ISmsSender` DI registration, Twilio HttpClient registration, and any related `using` statements

**Cloud checklist before deleting:**
- Search all `.cs` files for `ISmsSender`, `SmsOptions`, `TwilioSmsSender`, `ConsoleSmsSender`
- Confirm zero remaining usages after removal
- Do NOT build here — see batch build note above (T1 fix)

---

## Step 2.3 — Purge phone fields + SMS DTOs (GAP G7 + G8 fixed)

**Files to change — Entity:**
- `src/FlatPlanet.Security.Domain/Entities/User.cs` — remove `PhoneNumber`, `PhoneVerified`; add `MfaTotpSecret`, `MfaTotpEnrolled`

**Files to change — Repository:**
- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IUserRepository.cs`:
  - **Remove** `UpdatePhoneNumberAsync(Guid userId, string phoneNumber)` *(GAP G8 — explicitly named)*
  - **Add** `UpdateMfaTotpSecretAsync(Guid userId, string? encryptedSecret)`
  - **Add** `SetMfaTotpEnrolledAsync(Guid userId, bool enrolled)` — sets `mfa_totp_enrolled`, `mfa_enabled = true`, `mfa_method = 'totp'` when `enrolled = true`
  - **Add** `ResetMfaColumnsAsync(Guid userId)` *(T2 fix)* — single UPDATE: `mfa_totp_secret = null, mfa_totp_enrolled = false, mfa_enabled = false, mfa_method = null`
- `src/FlatPlanet.Security.Infrastructure/Repositories/UserRepository.cs` — implement new methods, remove phone columns from all SELECT/INSERT/UPDATE SQL

**Files to DELETE — SMS-only DTOs (GAP G7):**
- `src/FlatPlanet.Security.Application/DTOs/Mfa/EnrollPhoneRequest.cs`
- `src/FlatPlanet.Security.Application/DTOs/Mfa/EnrollPhoneResponse.cs`
- `src/FlatPlanet.Security.Application/DTOs/Mfa/VerifyOtpRequest.cs`

**Files to CREATE — New DTOs (GAP G7):**
- `src/FlatPlanet.Security.Application/DTOs/Mfa/BeginTotpEnrolmentResponse.cs`:
  ```csharp
  public class BeginTotpEnrolmentResponse
  {
      public string QrCodeUri { get; set; } = string.Empty;
  }
  ```
- `src/FlatPlanet.Security.Application/DTOs/Mfa/VerifyTotpEnrolmentRequest.cs`:
  ```csharp
  public class VerifyTotpEnrolmentRequest
  {
      [Required] public string TotpCode { get; set; } = string.Empty;
  }
  ```
- `src/FlatPlanet.Security.Application/DTOs/Mfa/VerifyLoginTotpRequest.cs`:
  ```csharp
  public class VerifyLoginTotpRequest
  {
      [Required] public Guid UserId { get; set; }
      [Required] [StringLength(8, MinimumLength = 6)] public string TotpCode { get; set; } = string.Empty;
  }
  ```

**Files to CREATE — Admin response DTO (U2 fix):**
- `src/FlatPlanet.Security.Application/DTOs/Mfa/UserMfaStatusResponse.cs`:
  ```csharp
  public class UserMfaStatusResponse
  {
      public bool MfaEnabled { get; set; }
      public string? MfaMethod { get; set; }        // "totp" | "email_otp" | null
      public bool MfaTotpEnrolled { get; set; }
  }
  ```

**Files to UPDATE — Existing DTOs:**
- `src/FlatPlanet.Security.Application/DTOs/Mfa/MfaLoginVerifyRequest.cs` — rename `Code` → `OtpCode`; keep `ChallengeId` as `Guid`

**Cloud checklist:**
- Search ALL `.cs` files for `PhoneNumber`, `phone_number`, `PhoneVerified`, `phone_verified` in user context
- Confirm zero remaining references after Step 2.3 completes
- Do NOT build here — see batch build note above (T1 fix)

---

## Step 2.4 — Add `OtpNet` NuGet package

**File:** `src/FlatPlanet.Security.Infrastructure/FlatPlanet.Security.Infrastructure.csproj`
```xml
<PackageReference Include="OtpNet" Version="1.4.0" />
```
Check NuGet for the current latest stable version before adding.

---

## Step 2.5 — TOTP secret encryption: `ITotpSecretEncryptor` + `TotpSecretEncryptor`

**Files to create:**
- `src/FlatPlanet.Security.Application/Common/Options/MfaOptions.cs`:
  ```csharp
  public class MfaOptions
  {
      public string TotpEncryptionKey { get; set; } = string.Empty; // 32-byte base64
  }
  ```
- `src/FlatPlanet.Security.Application/Interfaces/Services/ITotpSecretEncryptor.cs`:
  ```csharp
  public interface ITotpSecretEncryptor
  {
      string Encrypt(byte[] secret);    // returns Base64(nonce + ciphertext + GCM tag)
      byte[] Decrypt(string encrypted); // returns raw TOTP secret bytes
  }
  ```
- `src/FlatPlanet.Security.Infrastructure/Security/TotpSecretEncryptor.cs`:
  - AES-256-GCM via `System.Security.Cryptography.AesGcm`
  - Key: 32 bytes decoded from `MfaOptions.TotpEncryptionKey` (base64)
  - Encryption: generate 12-byte random nonce, encrypt, output `Base64(nonce[12] + ciphertext + tag[16])`
  - Decryption: parse nonce + ciphertext + tag from base64, decrypt

**Files to edit:**
- `appsettings.json` — add `"Mfa": { "TotpEncryptionKey": "" }` (empty — real key in environment/user secrets)
- `src/FlatPlanet.Security.API/Program.cs` — `builder.Services.Configure<MfaOptions>(builder.Configuration.GetSection("Mfa"))`, register `ITotpSecretEncryptor` → `TotpSecretEncryptor` as **singleton**

---

## Step 2.6 — `IEmailService` + `SmtpEmailService`: add MFA OTP method

**Files to change:**
- `src/FlatPlanet.Security.Application/Interfaces/Services/IEmailService.cs` — add:
  ```csharp
  Task SendMfaOtpEmailAsync(string toEmail, string otp, DateTime expiresAt);
  ```
- `src/FlatPlanet.Security.Infrastructure/Email/SmtpEmailService.cs` — implement using `SendWithRetryAsync` (from Step 1.15):
  - Subject: `"Your FlatPlanet login code"`
  - Body: OTP code, expiry time, "Do not share this code" warning, "If you did not request this, contact your administrator" line

---

## Step 2.7 — `MfaChallenge` entity + repository updates (GAP G2 + G3 fixed)

**Files to change:**
- `src/FlatPlanet.Security.Domain/Entities/MfaChallenge.cs`:
  - Remove `PhoneNumber`
  - Add `public string ChallengeType { get; set; } = "email_otp"`

- `src/FlatPlanet.Security.Application/Interfaces/Repositories/IMfaChallengeRepository.cs`:
  - Remove any phone-related signatures
  - **Remove** `HasVerifiedChallengeAsync` *(GAP G2 fix — no longer used; SyncStatusAsync now reads user.MfaTotpEnrolled directly)*
  - **Remove** `GetActiveByUserIdAsync(Guid userId)` *(V1 fix — old untyped version, no callers after Phase 2; replaced by GetActiveByUserIdAndTypeAsync)*
  - **Remove** `InvalidateActiveAsync(Guid userId)` *(W1 fix — global invalidation, no callers after Phase 2; replaced by InvalidateActiveByTypeAsync)*
  - **Add** `Task<MfaChallenge?> GetActiveByUserIdAndTypeAsync(Guid userId, string challengeType)`
  - **Add** `Task InvalidateActiveByTypeAsync(Guid userId, string challengeType)` *(GAP G3 fix)*

- `src/FlatPlanet.Security.Infrastructure/Repositories/MfaChallengeRepository.cs`:
  - `CreateAsync` INSERT: remove `phone_number` column, add `challenge_type` column
  - **Remove** `HasVerifiedChallengeAsync` method
  - **Remove** `GetActiveByUserIdAsync` method *(V1 fix — dead code after Phase 2)*
  - **Remove** `InvalidateActiveAsync` method *(W1 fix — dead code after Phase 2)*
  - **Add** `GetActiveByUserIdAndTypeAsync`:
    ```sql
    SELECT * FROM mfa_challenges
    WHERE user_id = @UserId AND challenge_type = @ChallengeType
      AND verified_at IS NULL AND expires_at > now()
    ORDER BY created_at DESC LIMIT 1
    ```
  - **Add** `InvalidateActiveByTypeAsync` *(GAP G3 fix — only invalidates challenges of the specified type)*:
    ```sql
    UPDATE mfa_challenges SET expires_at = now()
    WHERE user_id = @UserId AND challenge_type = @ChallengeType
      AND verified_at IS NULL AND expires_at > now()
    ```

---

## Step 2.8 — Rename `OtpVerified` → `MfaVerified` + fix SyncStatusAsync (GAP G2 fixed)

**Files to search and update:**
- `IdentityVerificationStatusDto.cs` — rename `OtpVerified` → `MfaVerified`
- `IdentityVerificationStatus.cs` (entity) — rename `OtpVerified` → `MfaVerified`
- `IdentityVerificationRepository.cs` + SQL queries — rename `otp_verified` → `mfa_verified`

**`IdentityVerificationService.cs` — logic change required (GAP G2):**

Current `SyncStatusAsync` calls `_mfaChallenges.HasVerifiedChallengeAsync(userId)` to determine if the user has verified OTP. After Phase 2, TOTP enrollment doesn't create a challenge row — it sets `user.MfaTotpEnrolled = true`. So `HasVerifiedChallengeAsync` would always return `false`.

**Fix:** Remove `IMfaChallengeRepository` dependency from `IdentityVerificationService`. Inject `IUserRepository`. Rewrite the first lines of `SyncStatusAsync`:

```csharp
// BEFORE (Phase 1)
var otpVerified = await _mfaChallenges.HasVerifiedChallengeAsync(userId);

// AFTER (Phase 2)
var user = await _users.GetByIdAsync(userId);
var mfaVerified = user?.MfaTotpEnrolled ?? false;
```

Update `IdentityVerificationService` constructor: remove `IMfaChallengeRepository _mfaChallenges`, add `IUserRepository _users`.

Also add V23 DB migration entry for the column rename in `identity_verification_status`:
```sql
ALTER TABLE identity_verification_status
  RENAME COLUMN otp_verified TO mfa_verified;
```
Add this to `db/V23__mfa_totp_and_challenge_type.sql` at the end (Cloud: add this section to the migration file from Step 2.1).

Cloud searches `otp_verified`, `OtpVerified` across the entire solution before making changes.

---

## Step 2.9 — Update `LoginResponse` DTO

**File:** `src/FlatPlanet.Security.Application/DTOs/Auth/LoginResponse.cs`

Final shape:
```csharp
public class LoginResponse
{
    public bool RequiresMfa { get; set; }
    public bool MfaEnrolmentPending { get; set; }   // true = MFA on, TOTP not yet enrolled
    public string? MfaMethod { get; set; }            // "totp" | "email_otp" | null
    public Guid? ChallengeId { get; set; }            // only set for email_otp path
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public int IdleTimeoutMinutes { get; set; }
    public UserProfileDto User { get; set; } = new();
}
```

Cloud searches for all usages of `LoginResponse` and `ChallengeId` after changing the type from `string?` to `Guid?` — update all callers.

---

## Step 2.10 — New `IMfaService` contract

**File:** `src/FlatPlanet.Security.Application/Interfaces/Services/IMfaService.cs` — full replacement:

```csharp
public interface IMfaService
{
    // TOTP enrolment
    Task<BeginTotpEnrolmentResponse> BeginTotpEnrolmentAsync(Guid userId); // T5 fix — returns DTO, not tuple; secret already stored in DB
    Task<LoginResponse> VerifyTotpEnrolmentAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent);

    // Login
    Task<LoginResponse> VerifyLoginTotpAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent);
    Task<MfaChallenge> SendEmailOtpAsync(Guid userId, string? ipAddress);
    Task<LoginResponse> VerifyLoginEmailOtpAsync(Guid challengeId, string otpCode, string? ipAddress, string? userAgent);

    // Management
    // NOTE: EnableMfaAsync removed — no endpoint calls it. SetMfaTotpEnrolledAsync handles enabling.
    Task DisableMfaAsync(Guid userId, Guid requestedBy);
    Task ResetMfaAsync(Guid userId, Guid requestedBy);     // admin force-reset
}
```

---

## Step 2.11 — Full `MfaService` rewrite

**File:** `src/FlatPlanet.Security.Application/Services/MfaService.cs` — complete rewrite.

**Constructor dependencies:**
- `IMfaChallengeRepository`
- `IUserRepository`
- `IIdentityVerificationService` *(NEW1 fix — required for SyncStatusAsync after TOTP enrolment)*
- `ISecurityConfigRepository`
- `IJwtService`
- `IAuditLogRepository`
- `ISessionRepository`
- `IRefreshTokenRepository`
- `IRoleRepository`
- `IDbConnectionFactory`
- `IEmailService`
- `ITotpSecretEncryptor`
- `IMemoryCache`
- `ILogger<MfaService>`

**Method logic:**

### `BeginTotpEnrolmentAsync(userId)` (GAP G7 guard added)
1. Get user — throw `KeyNotFoundException` if not found
2. **Guard (GAP G7):** `if (user.MfaTotpEnrolled) throw new InvalidOperationException("TOTP already enrolled. Contact admin to reset.")`
3. Generate 20-byte random secret: `KeyGeneration.GenerateRandomKey(20)` (OtpNet)
4. Encrypt: `_encryptor.Encrypt(secret)` → `encryptedSecret`
5. Store: `await _users.UpdateMfaTotpSecretAsync(userId, encryptedSecret)` — does NOT set `mfa_totp_enrolled = true` yet (only verification does that)
6. Read issuer from config: `mfa_totp_issuer` (default `"FlatPlanet"`)
7. Build QR URI: `otpauth://totp/{issuer}:{email}?secret={Base32Encoding.ToString(secret)}&issuer={issuer}&algorithm=SHA1&digits=6&period=30`
   *(V2 fix — OtpNet API is `Base32Encoding.ToString(byte[])`, NOT `Base32.Encode()` — wrong class name causes compile error)*
8. Return `new BeginTotpEnrolmentResponse { QrCodeUri = qrUri }` *(T5 fix — EncryptedSecret is never returned to caller)*

### `VerifyTotpEnrolmentAsync(userId, totpCode)` (GAP G2 identity sync added)
1. Get user, get `MfaTotpSecret` — throw `InvalidOperationException` if null (enrolment not started)
2. Decrypt secret: `_encryptor.Decrypt(user.MfaTotpSecret)`
3. Verify: `new Totp(secret).VerifyTotp(totpCode, out _, VerificationWindow.RfcSpecifiedNetworkDelay)` (OtpNet, allows ±1 step)
4. If invalid: audit `MfaFailure`, throw `UnauthorizedAccessException("Invalid TOTP code.")`
5. If valid:
   - `await _users.SetMfaTotpEnrolledAsync(userId, true)` — sets `mfa_totp_enrolled = true`, `mfa_enabled = true`, `mfa_method = "totp"`
   - `await _identityVerification.SyncStatusAsync(userId)` *(GAP G2 fix — triggers mfa_verified = true)*
6. Load config: `maxSessions`, `absoluteTimeout`, `idleTimeoutMinutes`, `refreshExpiryDays`, `accessExpiryMinutes`
7. Open transaction. Inside transaction *(T4 fix — enforce session cap)*:
   - `await _sessions.EvictOldestIfOverLimitAsync(userId, maxSessions, conn, tx)`
   - `await _sessions.CreateAsync(session, conn, tx)`
   - `await _refreshTokens.CreateAsync(refreshToken, conn, tx)`
   - Commit
8. Fire-and-forget: audit `MfaEnrolmentComplete`
9. Build access token *(Y1 fix — must not omit this)*:
   ```csharp
   var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(userId);
   var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);
   ```
10. Return full `LoginResponse` — AccessToken, RefreshToken, ExpiresIn, IdleTimeoutMinutes, User (all fields populated)

### `VerifyLoginTotpAsync(userId, totpCode)` (T4 fix)
1. Get user — throw `KeyNotFoundException("User not found.")` if null *(U5 fix)*
   Get `user.MfaTotpSecret` — throw `InvalidOperationException("TOTP not enrolled.")` if null *(U5 fix — prevents NullReferenceException on decrypt)*
   Decrypt: `_encryptor.Decrypt(user.MfaTotpSecret)`
2. Verify code with OtpNet (`VerificationWindow.RfcSpecifiedNetworkDelay`)
3. If invalid: audit `MfaFailure`, throw `UnauthorizedAccessException`
4. If valid: load config (maxSessions, absoluteTimeout, idleTimeoutMinutes, refreshExpiryDays, accessExpiryMinutes)
5. Open transaction. Inside transaction *(T4 fix — enforce session cap before session creation)*:
   - `await _sessions.EvictOldestIfOverLimitAsync(userId, maxSessions, conn, tx)`
   - `await _sessions.CreateAsync(session, conn, tx)`
   - `await _refreshTokens.CreateAsync(refreshToken, conn, tx)`
   - Commit
6. Build access token *(Y1 fix)*:
   ```csharp
   var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(userId);
   var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);
   ```
7. Fire-and-forget: audit `MfaSuccess`
8. Return full `LoginResponse` — AccessToken, RefreshToken, ExpiresIn, IdleTimeoutMinutes, User (all fields populated)

### `SendEmailOtpAsync(userId, ipAddress)` (GAP G1 + G3 fixed)
1. Get user — throw `KeyNotFoundException("User not found.")` if null *(V3 fix)*
2. Invalidate any active `email_otp` challenge only *(GAP G3 fix)*: `await _mfaChallenges.InvalidateActiveByTypeAsync(userId, "email_otp")`
3. Generate 6-digit OTP using `RandomNumberGenerator` *(GAP G1 fix — NOT Random.Shared)*:
   ```csharp
   private static string GenerateOtp(int length) =>
       string.Join("", RandomNumberGenerator.GetBytes(length).Select(b => (b % 10).ToString()));
   var otp = GenerateOtp(6);
   ```
4. Hash: `_jwt.HashToken(otp)` → `otp_hash` *(U4 fix — use existing IJwtService helper, same as Phase 1; DO NOT use raw SHA-256 here; VerifyLoginEmailOtpAsync also uses _jwt.HashToken() for comparison — they must be identical)*
5. Read expiry from config: `mfa_email_otp_expiry_minutes` (default 10)
6. Create challenge: `challenge_type = "email_otp"`, `expires_at = now + expiry`
7. Await email send *(T3 fix — NOT fire-and-forget; silent SMTP failure means user never gets the code)*:
   ```csharp
   try
   {
       await _emailService.SendMfaOtpEmailAsync(user.Email, otp, challenge.ExpiresAt);
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, "Email OTP send failed for user {UserId}", userId);
       throw new ServiceUnavailableException("Email service is temporarily unavailable. Please try again.");
   }
   ```
8. Return the `MfaChallenge` entity

### `VerifyLoginEmailOtpAsync(challengeId, otpCode)` (T4 fix)
1. Load challenge by ID — throw `KeyNotFoundException` if not found
2. Verify guards *(W3 fix — explicit exceptions)*:
   - `challenge_type != "email_otp"` → throw `ArgumentException("Challenge is not an email OTP challenge.")`
   - `verified_at != null` → throw `InvalidOperationException("Challenge has already been used.")`
   - `expires_at <= now` → throw `ArgumentException("OTP has expired.")`
3. Load max attempts: `await _securityConfig.GetValueAsync("mfa_max_otp_attempts")` (default 5)
   *(U6 fix — the old Phase 1 `GetMaxAttemptsAsync()` helper reads `"mfa_otp_max_attempts"` — that is the WRONG key in Phase 2. Do NOT reuse it. Read `"mfa_max_otp_attempts"` directly.)*
   Check `challenge.Attempts >= maxAttempts` — throw `TooManyRequestsException` if at limit
4. Hash presented code: `_jwt.HashToken(otpCode)` — compare to `challenge.OtpHash` *(U4 fix — must use same method as SendEmailOtpAsync)*
5. If mismatch: `IncrementAttemptsAsync`, audit `MfaFailure`, throw `UnauthorizedAccessException`
6. If valid: `MarkVerifiedAsync(challengeId)`
7. Load user, load config (maxSessions, absoluteTimeout, idleTimeoutMinutes, refreshExpiryDays, accessExpiryMinutes)
8. Open transaction. Inside transaction *(T4 fix — enforce session cap before session creation)*:
   - `await _sessions.EvictOldestIfOverLimitAsync(userId, maxSessions, conn, tx)`
   - `await _sessions.CreateAsync(session, conn, tx)`
   - `await _refreshTokens.CreateAsync(refreshToken, conn, tx)`
   - Commit
9. Build access token *(Y1 fix)*:
   ```csharp
   var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
   var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);
   ```
10. Fire-and-forget: audit `MfaSuccess`
11. Return full `LoginResponse` — AccessToken, RefreshToken, ExpiresIn, IdleTimeoutMinutes, User (all fields populated)
12. **ISO 27001 compliance:** email OTP login does NOT set `mfa_verified = true` — that is only set by TOTP enrolment (`VerifyTotpEnrolmentAsync`)

### `DisableMfaAsync(userId, requestedBy)`
1. `await _users.ResetMfaColumnsAsync(userId)` *(W2 fix — use T2 atomic method, do NOT write individual column updates)*
2. Audit `MfaDisabled` with `requested_by`

### `ResetMfaAsync(userId, requestedBy)`
1. `await _users.ResetMfaColumnsAsync(userId)` *(W2 fix — same atomic method as DisableMfaAsync)*
2. Audit `MfaReset` with `requested_by`

---

## Step 2.12 — Update `AuthService.LoginAsync` MFA gate

**File:** `src/FlatPlanet.Security.Application/Services/AuthService.cs`

Replace lines 138–141:
```csharp
// OLD
if (user.MfaEnabled && !string.IsNullOrEmpty(user.PhoneNumber))
{
    var challenge = await _mfa.SendLoginOtpAsync(user.Id, user.PhoneNumber);
    return new LoginResponse { RequiresMfa = true, ChallengeId = challenge.Id.ToString() };
}
```

With:
```csharp
// NEW
if (user.MfaEnabled)
{
    // U1 fix: only gate on MfaTotpEnrolled for TOTP users.
    // Email OTP users always have mfa_totp_enrolled = false — checking it globally
    // would trap all email_otp users in the enrolment-pending branch forever.
    if (user.MfaMethod == "totp" && !user.MfaTotpEnrolled)
    {
        // MFA is enabled but TOTP hasn't been enrolled yet
        // Return a flag so the client can start the enrolment flow
        return new LoginResponse
        {
            RequiresMfa = true,
            MfaEnrolmentPending = true,
            MfaMethod = "totp"
        };
    }

    if (user.MfaMethod == "totp")
    {
        // Client presents TOTP code directly — no server challenge needed
        // NEW2 fix: include UserId so client can call /mfa/totp/verify
        return new LoginResponse
        {
            RequiresMfa = true,
            MfaMethod = "totp",
            User = new UserProfileDto { UserId = user.Id }
        };
    }

    // Email OTP path — send challenge, return challengeId
    var challenge = await _mfa.SendEmailOtpAsync(user.Id, ipAddress);
    return new LoginResponse
    {
        RequiresMfa = true,
        MfaMethod = "email_otp",
        ChallengeId = challenge.Id,
        User = new UserProfileDto { UserId = user.Id }
    };
}
```

---

## Step 2.13 — Update `MfaController` endpoints (GAP G4 + G5 fixed)

**File:** `src/FlatPlanet.Security.API/Controllers/MfaController.cs`

Keep `[Route("api/v1/mfa")]` on the class — all routes stay under `/api/v1/mfa/...` *(GAP G4 fix)*.

| Method | Route | Auth | Body | Return | Rate limit |
|---|---|---|---|---|---|
| `POST` | `/api/v1/mfa/totp/begin-enrolment` | JWT | — | `BeginTotpEnrolmentResponse` | — |
| `POST` | `/api/v1/mfa/totp/verify-enrolment` | JWT | `VerifyTotpEnrolmentRequest` | `LoginResponse` | — |
| `POST` | `/api/v1/mfa/totp/verify` | None | `VerifyLoginTotpRequest` | `LoginResponse` | `mfa-verify` |
| `POST` | `/api/v1/mfa/email-otp/verify` | None | `MfaLoginVerifyRequest` | `LoginResponse` | `mfa-verify` |
| `POST` | `/api/v1/mfa/disable` | JWT | — | `204 No Content` | — |

The `mfa-verify` rate limit policy is defined in Step 2.15 *(GAP G5 fix)*.

**Remove** all three existing SMS-based actions: `Enroll`, `VerifyOtp`, `LoginVerify`.

---

## Step 2.14 — New `AdminMfaController`

**File:** `src/FlatPlanet.Security.API/Controllers/Admin/AdminMfaController.cs` *(new)*

Use `[Route("api/v1/admin")]` to match `AdminAuditLogController` convention *(NEW4 fix — `/v1` required)*.

| Method | Route | Return | Description |
|---|---|---|---|
| `GET` | `/api/v1/admin/users/{userId}/mfa/status` | `200 UserMfaStatusResponse` | MFA enabled, method, enrolled state |
| `POST` | `/api/v1/admin/users/{userId}/mfa/disable` | `204 No Content` | Disable MFA — calls `DisableMfaAsync(userId, adminId)` |
| `POST` | `/api/v1/admin/users/{userId}/mfa/reset` | `204 No Content` | Force-reset MFA — calls `ResetMfaAsync(userId, adminId)` *(V4 fix — return types added)* |

All actions require `PlatformOwner` policy (match `AdminAuditLogController` pattern).

---

## Step 2.15 — `Program.cs` final updates (GAP G5 fixed)

**File:** `src/FlatPlanet.Security.API/Program.cs`

- Remove: `SmsOptions` binding, `ISmsSender` DI, Twilio client (done in Step 2.2 — verify it's gone)
- Add: `builder.Services.Configure<MfaOptions>(builder.Configuration.GetSection("Mfa"))`
- Add: `builder.Services.AddSingleton<ITotpSecretEncryptor, TotpSecretEncryptor>()`
- Verify: `IMfaService` → `MfaService` is registered (add if missing)
- Verify: `IEmailService` → `SmtpEmailService` is still registered (it should be from password reset)
- **Add `mfa-verify` rate limit policy (GAP G5 fix)** — 5 requests per minute per IP, applied to anonymous TOTP and email OTP verify endpoints:
  ```csharp
  // mfa-verify: 5 per min per IP — anonymous endpoints that accept userId + code
  options.AddPolicy("mfa-verify", context =>
      RateLimitPartition.GetFixedWindowLimiter(
          partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
          factory: _ => new FixedWindowRateLimiterOptions
          {
              PermitLimit = 5,
              Window = TimeSpan.FromMinutes(1)
          }));
  ```

---

## Step 2.16 — Update tests

**Files to change:**
- `tests/FlatPlanet.Security.Tests/AuthServiceTests.cs`:
  - `CreateService()` already has `Mock<IMfaService>` and `Mock<IMemoryCache>` (from Step 1.1) — verify
  - Add/update MFA gate tests:
    - `MfaEnabled=true + MfaMethod="totp" + MfaTotpEnrolled=false` → `MfaEnrolmentPending=true` *(V5 fix — MfaMethod="totp" required; without it the U1 gate doesn't fire)*
    - `MfaEnabled=true + MfaMethod="totp" + MfaTotpEnrolled=true` → `RequiresMfa=true, MfaMethod="totp"`
    - `MfaEnabled=true + MfaMethod="email_otp"` → `RequiresMfa=true, MfaMethod="email_otp", ChallengeId != null`
    - `MfaEnabled=true + MfaMethod=null + MfaTotpEnrolled=false` → falls through to email OTP path (NOT enrolment pending) *(V5 fix — additional test verifying U1 gate doesn't over-fire)*
    - `MfaEnabled=false` → full `LoginResponse` with tokens (existing happy path)

- `tests/FlatPlanet.Security.Tests/MfaServiceTests.cs` *(new)*:
  - `SendEmailOtpAsync`: challenge created, `SendMfaOtpEmailAsync` called, previous challenge invalidated by type only (not global)
  - `SendEmailOtpAsync`: SMTP failure → `ServiceUnavailableException` thrown (T3 — email send is awaited, not fire-and-forget)
  - `VerifyLoginEmailOtpAsync`: success path, wrong code, expired challenge, max attempts exceeded
  - `VerifyLoginEmailOtpAsync`: challenge_type != "email_otp" → `ArgumentException` *(X2 fix — W3 guard test)*
  - `VerifyLoginEmailOtpAsync`: session cap enforced — `EvictOldestIfOverLimitAsync` called inside transaction *(T4)*
  - `BeginTotpEnrolmentAsync`: encrypted secret stored on user, QR URI returned
  - `BeginTotpEnrolmentAsync`: re-enrollment guard — throws `InvalidOperationException` when `MfaTotpEnrolled = true` *(T6)*
  - `VerifyTotpEnrolmentAsync`: valid code → tokens issued; invalid code → `UnauthorizedAccessException`
  - `VerifyTotpEnrolmentAsync`: valid code → `SyncStatusAsync` called — verifies `mfa_verified` is set in identity_verification_status *(U3 / G2 fix verification)*
  - `VerifyTotpEnrolmentAsync`: session cap enforced — `EvictOldestIfOverLimitAsync` called inside transaction *(T4)*
  - `VerifyLoginTotpAsync`: valid code → full `LoginResponse` with tokens *(W4 fix — success path)*
  - `VerifyLoginTotpAsync`: invalid code → `UnauthorizedAccessException` *(W4 fix)*
  - `VerifyLoginTotpAsync`: session cap enforced — `EvictOldestIfOverLimitAsync` called inside transaction *(T4)*
  - `DisableMfaAsync`: `ResetMfaColumnsAsync` called, audit `MfaDisabled` logged *(V6 fix)*
  - `ResetMfaAsync`: `ResetMfaColumnsAsync` called, audit `MfaReset` logged *(V6 fix)*

---

## ⛔ PHASE 2 REVIEW GATE

```
Cloud: complete Tier 1 self-review. Write summary. Push branch. Ping Lightning.
Lightning: Tier 2 full diff review checklist:
  ✓ No phone_number / PhoneNumber references remaining anywhere
  ✓ OTP generation uses RandomNumberGenerator (NOT Random.Shared)
  ✓ VerifyTotpEnrolmentAsync calls SyncStatusAsync after enrollment
  ✓ SyncStatusAsync uses user.MfaTotpEnrolled — NOT HasVerifiedChallengeAsync (which is gone)
  ✓ SendEmailOtpAsync calls InvalidateActiveByTypeAsync("email_otp") — not InvalidateActiveAsync
  ✓ All MFA controller routes are /api/v1/mfa/...
  ✓ All Admin MFA routes are /api/v1/admin/...
  ✓ mfa-verify rate limit applied to totp/verify and email-otp/verify actions
  ✓ V23 resets SMS users before dropping phone columns
  ✓ BeginTotpEnrolmentAsync guards against re-enrollment when already enrolled
  ✓ No IMfaService SMS methods remain (EnrollAndSendOtpAsync, SendLoginOtpAsync, VerifyLoginOtpAsync)
  ✓ MfaService constructor includes IIdentityVerificationService
  ✓ LoginResponse for TOTP path includes User.UserId
  ✓ LoginResponse for email OTP path includes User.UserId
  ✓ SendEmailOtpAsync email send is awaited — not fire-and-forget — throws ServiceUnavailableException on SMTP failure
  ✓ VerifyLoginTotpAsync: EvictOldestIfOverLimitAsync called inside session transaction
  ✓ VerifyLoginEmailOtpAsync: EvictOldestIfOverLimitAsync called inside session transaction
  ✓ MfaEnrolmentPending gate is scoped to MfaMethod=="totp" — email_otp users go straight to SendEmailOtpAsync
  ✓ OTP hash uses _jwt.HashToken() in BOTH SendEmailOtpAsync (store) and VerifyLoginEmailOtpAsync (compare)
  ✓ VerifyLoginEmailOtpAsync reads config key "mfa_max_otp_attempts" — NOT the old "mfa_otp_max_attempts"
  ✓ QR URI uses Base32Encoding.ToString(secret) — NOT Base32.Encode()
  ✓ All three session-creating methods call GetPlatformRoleNamesForUserAsync + IssueAccessTokenAsync before returning LoginResponse
  ✓ V23 reset condition is WHERE mfa_enabled=true (not the narrow phone_verified/mfa_method='sms' condition)
  ✓ dotnet build green after Step 2.12
User: Tier 3 sign-off before commit and PR to develop.
```

---

---

## Appendix — Cache Key Reference

| Key | TTL | Invalidated by |
|---|---|---|
| `fp:sec:cfg:all` | 5 min | Never (TTL only) |
| `fp:sec:session:{sessionId}` | 30 sec | `EndSessionAsync`, timeout paths |
| `fp:sec:ctx:{userId}:{appSlug}` | 2 min | TTL only (Redis invalidation in future) |
| `fp:sec:authz:{userId}:{appSlug}:{permission}` | 2 min | TTL only, only cached when `allowed=true` |

All keys use `fp:sec:` prefix for future Redis namespace isolation.

---

## Appendix — Audit Events Added / Changed

| Event | Trigger | ISO 27001 note |
|---|---|---|
| `mfa_enrolment_complete` | TOTP code verified during enrolment | Sets `mfa_verified = true` |
| `mfa_success` | Any MFA factor verified at login | Does NOT set `mfa_verified` for email OTP |
| `mfa_failure` | Invalid TOTP or OTP code presented | |
| `mfa_disabled` | User or admin disables MFA | |
| `mfa_reset` | Admin force-resets MFA | |
| `authorize_denied` | Permission check fails | Logged always |
| `authorize_allowed` | Permission check passes | Only logged if `audit_log_authorize_allowed = "true"` in config |

---

## Appendix — Files to DELETE in Phase 2

| File | Reason |
|---|---|
| `src/FlatPlanet.Security.Application/Interfaces/Services/ISmsSender.cs` | SMS removed |
| `src/FlatPlanet.Security.Application/Common/Options/SmsOptions.cs` | SMS removed |
| `src/FlatPlanet.Security.Infrastructure/ExternalServices/ConsoleSmsSender.cs` | SMS removed |
| `src/FlatPlanet.Security.Infrastructure/ExternalServices/TwilioSmsSender.cs` | SMS removed |
