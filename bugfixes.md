# Security Platform — Bug Fix Backlog

---

## BUG-01 — `POST /api/v1/apps/{appId}/users` hits unique constraint on re-grant

**Severity:** P1
**Status:** Open
**File:** `src/FlatPlanet.Security.Infrastructure/Repositories/UserAppRoleRepository.cs`

**Problem:**
`RevokeAccessAsync` soft-deletes by setting `status = 'revoked'` — the row stays in `user_app_roles`.
`CreateAsync` always does a plain `INSERT`. If a revoked row exists for the same `(app_id, user_id)`,
the INSERT hits the unique constraint and returns `409`.
`GET /apps/{appId}/users` filters to `status = 'active'` only — so the state looks clean but the
stale row is still there, silently blocking any future grant.

**Fix:**
In `UserAppRoleRepository.CreateAsync`, replace the plain INSERT with an upsert:

```sql
INSERT INTO user_app_roles (user_id, app_id, role_id, granted_by, expires_at, status)
VALUES (@UserId, @AppId, @RoleId, @GrantedBy, @ExpiresAt, 'active')
ON CONFLICT (app_id, user_id)
DO UPDATE SET
    role_id    = EXCLUDED.role_id,
    granted_by = EXCLUDED.granted_by,
    expires_at = EXCLUDED.expires_at,
    status     = 'active'
RETURNING id
```

This reactivates an existing revoked row instead of inserting a duplicate.

**Branch:** `fix/user-app-role-upsert` (branch from `main`)
**Target:** `main` — hotfix, sync back to `develop` after merge

---
