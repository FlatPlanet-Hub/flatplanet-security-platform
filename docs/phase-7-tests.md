# Phase 7 — Tests

## Goal
Unit tests for all core services using xUnit + Moq, Arrange-Act-Assert pattern.

## Tasks

- [ ] `AuthServiceTests`
  - [ ] `Login_ShouldReturnTokens_WhenCredentialsValid`
  - [ ] `Login_ShouldReturn401_WhenSupabaseAuthFails`
  - [ ] `Login_ShouldReturn423_WhenAccountLocked`
  - [ ] `Login_ShouldReturn429_WhenRateLimitExceeded`
  - [ ] `Login_ShouldReturn403_WhenUserInactive`
  - [ ] `Login_ShouldReturn403_WhenCompanyInactive`
  - [ ] `Logout_ShouldRevokeSessionAndToken`
  - [ ] `Refresh_ShouldRotateToken_WhenValid`
  - [ ] `Refresh_ShouldFail_WhenTokenRevoked`

- [ ] `AuthorizationServiceTests`
  - [ ] `Authorize_ShouldReturnAllowed_WhenPermissionExists`
  - [ ] `Authorize_ShouldReturnDenied_WhenNoRoleAssigned`
  - [ ] `Authorize_ShouldReturnDenied_WhenRoleExpired`
  - [ ] `Authorize_ShouldReturnDenied_WhenRoleSuspended`

- [ ] `UserContextServiceTests`
  - [ ] `GetUserContext_ShouldReturnRolesAndPermissions_WhenUserHasAccess`
  - [ ] `GetUserContext_ShouldReturnAllowedApps`

- [ ] `JwtServiceTests`
  - [ ] `IssueToken_ShouldContainCorrectClaims`
  - [ ] `GenerateRefreshToken_ShouldReturnHashedToken`

- [ ] `OffboardingServiceTests`
  - [ ] `Offboard_ShouldRevokeAllSessionsAndRoles`

## Naming Convention
`<MethodName>_Should<Result>_When<Condition>`

## Status: COMPLETE
