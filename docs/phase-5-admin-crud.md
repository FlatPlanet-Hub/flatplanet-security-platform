# Phase 5 — Admin CRUD

## Goal
Full management endpoints for companies, apps, resources, roles, permissions, and user access.

## Tasks

- [ ] Companies (platform_owner only)
  - [ ] `ICompanyRepository` + implementation
  - [ ] `ICompanyService` + `CompanyService`
  - [ ] `CompanyController`
    - [ ] `POST /api/v1/companies`
    - [ ] `GET /api/v1/companies`
    - [ ] `GET /api/v1/companies/{id}`
    - [ ] `PUT /api/v1/companies/{id}`
    - [ ] `PUT /api/v1/companies/{id}/status` (cascade suspend to users)

- [ ] Apps (platform_owner / app_admin)
  - [ ] `IAppService` + `AppService`
  - [ ] `AppController`
    - [ ] `POST /api/v1/apps`
    - [ ] `GET /api/v1/apps`
    - [ ] `GET /api/v1/apps/{id}`
    - [ ] `PUT /api/v1/apps/{id}`

- [ ] Resource Types
  - [ ] `IResourceTypeRepository` + implementation
  - [ ] `IResourceTypeService` + `ResourceTypeService`
  - [ ] `ResourceTypeController`
    - [ ] `GET /api/v1/resource-types`
    - [ ] `POST /api/v1/resource-types` (platform_owner only)

- [ ] Resources (app_admin)
  - [ ] `IResourceRepository` + implementation
  - [ ] `IResourceService` + `ResourceService`
  - [ ] `ResourceController`
    - [ ] `POST /api/v1/apps/{appId}/resources`
    - [ ] `GET /api/v1/apps/{appId}/resources`
    - [ ] `PUT /api/v1/apps/{appId}/resources/{id}`

- [ ] Roles (app_admin / platform_owner)
  - [ ] `IRoleRepository` + implementation
  - [ ] `IRoleService` + `RoleService`
  - [ ] `RoleController`
    - [ ] `POST /api/v1/apps/{appId}/roles`
    - [ ] `GET /api/v1/apps/{appId}/roles`
    - [ ] `PUT /api/v1/apps/{appId}/roles/{id}`
    - [ ] `DELETE /api/v1/apps/{appId}/roles/{id}` (only if no users assigned)

- [ ] Permissions (app_admin)
  - [ ] `IPermissionRepository` + implementation
  - [ ] `IPermissionService` + `PermissionService`
  - [ ] `PermissionController`
    - [ ] `POST /api/v1/apps/{appId}/permissions`
    - [ ] `GET /api/v1/apps/{appId}/permissions`
    - [ ] `PUT /api/v1/apps/{appId}/permissions/{id}`
    - [ ] `POST /api/v1/apps/{appId}/roles/{roleId}/permissions`
    - [ ] `DELETE /api/v1/apps/{appId}/roles/{roleId}/permissions/{permId}`

- [ ] User Access Management (app_admin / manage_users)
  - [ ] `IUserAccessService` + `UserAccessService`
  - [ ] `UserAccessController`
    - [ ] `POST /api/v1/apps/{appId}/users`
    - [ ] `GET /api/v1/apps/{appId}/users`
    - [ ] `PUT /api/v1/apps/{appId}/users/{userId}/role`
    - [ ] `DELETE /api/v1/apps/{appId}/users/{userId}`

- [ ] User Management (platform_owner / manage_users)
  - [ ] `IUserService` + `UserService`
  - [ ] `UserController`
    - [ ] `GET /api/v1/users`
    - [ ] `GET /api/v1/users/{id}`
    - [ ] `PUT /api/v1/users/{id}`
    - [ ] `PUT /api/v1/users/{id}/status`

## Status: COMPLETE
