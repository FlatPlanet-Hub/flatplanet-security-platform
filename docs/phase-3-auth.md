# Phase 3 — Authentication

## Goal
Full login/logout/refresh/me flow with Supabase Auth, JWT issuance, session management, and audit logging.

## Tasks

- [ ] Supabase Auth client (Infrastructure/ExternalServices/)
  - [ ] `ISupabaseAuthClient` interface
  - [ ] `SupabaseAuthClient` implementation (verify credentials via REST)
- [ ] JWT service (Application/Services/)
  - [ ] `IJwtService` interface
  - [ ] `JwtService` — issue access token, generate + hash refresh token
- [ ] Auth service (Application/Services/)
  - [ ] `IAuthService` interface
  - [ ] `AuthService` — full login flow:
    1. Rate limit check (login_attempts by IP + email)
    2. Account lockout check
    3. Supabase Auth verify
    4. User lookup + status check
    5. Company status check
    6. Session limit check
    7. Create session
    8. Issue JWT + refresh token
    9. Audit log (login_success + session_start)
    10. Update last_seen_at
  - [ ] Logout (end session, revoke refresh token, audit)
  - [ ] Refresh (validate token hash, rotate, extend session, audit)
- [ ] Repositories (Infrastructure/Repositories/)
  - [ ] `IUserRepository` + implementation
  - [ ] `ISessionRepository` + implementation
  - [ ] `IRefreshTokenRepository` + implementation
  - [ ] `ILoginAttemptRepository` + implementation
  - [ ] `IAuditLogRepository` + implementation
  - [ ] `ISecurityConfigRepository` + implementation
- [ ] DTOs
  - [ ] `LoginRequest`
  - [ ] `LoginResponse` (JWT + refresh token + user context)
  - [ ] `RefreshRequest` / `RefreshResponse`
- [ ] Controller: `AuthController`
  - [ ] `POST /api/v1/auth/login`
  - [ ] `POST /api/v1/auth/logout`
  - [ ] `POST /api/v1/auth/refresh`
  - [ ] `GET /api/v1/auth/me`

## Status: PENDING
