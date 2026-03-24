# Phase 4 — Authorization

## Goal
Core authorization check and user-context endpoints consumed by all connected apps.

## Tasks

- [ ] Authorization service (Application/Services/)
  - [ ] `IAuthorizationService` interface
  - [ ] `AuthorizationService`:
    1. Lookup user_app_roles for user + app
    2. Check role status active + not expired
    3. Lookup role_permissions for user's roles
    4. Check required permission exists
    5. Audit log the check
    6. Return allowed + roles + permissions
- [ ] User context service (Application/Services/)
  - [ ] `IUserContextService` interface
  - [ ] `UserContextService`:
    1. Get user from JWT sub
    2. Query all active user_app_roles
    3. Resolve role_permissions for requested app
    4. Build allowedApps list
- [ ] Repositories
  - [ ] `IUserAppRoleRepository` + implementation
  - [ ] `IRolePermissionRepository` + implementation
  - [ ] `IAppRepository` + implementation
- [ ] DTOs
  - [ ] `AuthorizeRequest`
  - [ ] `AuthorizeResponse` (allowed, roles[], permissions[])
  - [ ] `UserContextResponse`
- [ ] Controllers
  - [ ] `AuthorizeController` — `POST /api/v1/authorize`
  - [ ] `UserContextController` — `GET /api/v1/apps/{appSlug}/user-context`

## Status: PENDING
