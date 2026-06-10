# V27 Companion Changes — HubApi + Hub Frontend

Reference plan for the cross-repo work that pairs with `V27__role_streamline_user.sql`.
**P1 + P5** carve-outs from Lightning's review are encoded here.

---

## HubApi (`platform-api` repo) — Cloud's PR sketch

### File 1 — `FlatPlanet.Platform.Infrastructure/ExternalServices/SecurityPlatformService.cs`

Two changes inside `ProvisionAppRolesAsync`:

```diff
-        foreach (var roleName in new[] { "owner", "developer", "viewer" })
+        foreach (var roleName in new[] { "owner", "developer", "user" })
```

```diff
         var assignments = new[]
         {
             ("owner",     new[] { "read", "write", "ddl", "manage_members", "delete_project" }),
-            ("developer", new[] { "read", "write", "ddl" }),
-            ("viewer",    new[] { "read" })
+            ("developer", new[] { "read", "write", "ddl", "manage_members" }),
+            ("user",      new[] { "read" })
         };
```

Seeds new projects with the target template.

### File 2 — `FlatPlanet.Platform.Application/Services/ProjectService.cs`

Creator grant flips from `owner` to `developer`:

```diff
-        await _securityPlatform.GrantRoleAsync(appId, userId, "owner");
+        await _securityPlatform.GrantRoleAsync(appId, userId, "developer");
```

```diff
-        return ToResponse(created, "owner");
+        return ToResponse(created, "developer");
```

### File 3 — `FlatPlanet.Platform.Tests/UnitTests/Services/ProjectServiceTests.cs`

Update the 4 `_securityPlatform.Setup/Verify` mocks from `"owner"` to `"developer"`.

### File 4 — `FlatPlanet.Platform.Application/Services/ProjectMemberService.cs`

⚠️ **P1 — DO NOT CHANGE this line** in this PR:

```cs
await _securityPlatform.GrantRoleAsync(dashboardAppId.Value, request.UserId, "viewer");
```

Reason: dashboard-hub still has `Admin`/`User`/`Viewer` after V27. The `ResolveRoleIdAsync` uses
`StringComparison.OrdinalIgnoreCase` — passing `"user"` would silently match dashboard-hub's
`User` role (which has `write_data`), silently elevating every new member from read-only to read+write.

Keep the literal as `"viewer"`. It matches `Viewer` (capital V) case-insensitively. Revisit
when dashboard-hub itself is converted.

### File 5 — `FlatPlanet.Platform.Infrastructure/Azure/ProvisionAzureService.cs`

**Decision (2026-06-10):** developers should have Azure provisioning ability, same as owners.
Destructive boundary stays at `delete_project` (owner-only).

**Discovery on closer reading:** the three guard sites check **permissions**, not role names:

```cs
projectAccess.Permissions.Any(p =>
    p.Equals("write", ...) || p.Equals("manage_members", ...) || p.Equals("owner", ...));
```

`p` iterates over `Permissions` (e.g. `read`, `write`, `ddl`). The third comparison checks for a
permission named `"owner"` — which **does not exist** in any project's permission set. It is dead
code. The effective check is `write OR manage_members`, and:

- Pre-V27: `developer` already has `write` → already passes.
- Post-V27: `developer` has `write` + `manage_members` → still passes.

The user's intent (developers can provision) is **already true in production**. No code change is
required in this PR.

**Follow-up (non-blocking, separate PR later):**
- Remove the dead `p.Equals("owner", ...)` line.
- Fix the misleading comment at line 42 (it conflates role names with permission names).

This finding does **not** gate V27 deploy.

---

## Hub Frontend (`fp-development-hub` repo) — Cloud's PR sketch

### Rule from P5

dashboard-hub stays `Admin`/`User`/`Viewer` (out of V27 scope). The hub frontend renders roles
from all apps it sees, so we must **keep `viewer` keys alongside new `user` keys** in every
color/style map. Don't replace — add.

### Files

| File | Change |
|---|---|
| `src/Pages/ProjectsPage.tsx:177-178` | Color map: keep `viewer:'default'`, add `user:'default'`. |
| `src/Pages/ProjectsPage.tsx:522` | `ROLE_OPTIONS` → `['owner', 'developer', 'user']` (the dropdown only shows when adding members to non-hub projects). |
| `src/Pages/SecurityPage.tsx:645` | `PROJECT_ROLES` → `['owner', 'developer', 'user']`. |
| `src/Pages/SecurityPage.tsx:649-650` | Add `user:'green'` next to `viewer:'green'`. |
| `src/Pages/SecurityPage.tsx:655-656` | Add `user:'read'`; update `developer:'read, write, ddl, manage_members'`. |
| `src/Pages/SecurityPage.tsx:668,707` | Default `addRole` `'viewer'` → `'user'`. |
| `src/Components/ActivityReport.tsx:46-47` | Color map + ROLE_OPTIONS — add `user` keys, **keep** `viewer` keys for dashboard-hub. |
| `src/Components/ActivityReport.tsx:1065-1067` | Same in the inner ROLE color/order block. |
| `src/Components/RepoBoard.tsx:659-660` | Add `user:'default'` next to `viewer:'default'`. |
| `src/styles/ActivityReport.scss:1019` | Add `&--user { … }` rule alongside `&--viewer`. |

---

## Execution sequence

1. **SP DB**: run V27 (snapshot is inside the script, transactional). Verification assertions
   roll back on any mismatch.
2. **HubApi PR**: merge → auto-deploy. New projects from this point seed `owner/developer/user`,
   creator becomes `developer`.
3. **Hub Frontend PR**: merge → Netlify deploy. Role labels render correctly for both renamed
   apps and dashboard-hub.
4. **Yuffie smoke test**:
   - Invite a user as `user` on rtw-list → verify role label + permissions
   - Create a brand-new test project → verify creator gets `developer`, app has `owner/developer/user`
   - Log in as a dashboard-hub `Viewer` user → still works, role label unchanged

Between steps 1 and 2: HubApi keeps working because `ResolveRoleIdAsync` is case-insensitive and
the renamed role `user` is what HubApi would look up *if* it had been changed. Until HubApi ships,
the seed template still tries `"viewer"` for new apps — which won't exist post-V27. So **no new
projects can be created via Hub between V27 commit and HubApi deploy.** Schedule the two close
together (minutes apart) to minimize the window.

---

## Rollback decision tree

| Failure point | Action |
|---|---|
| V27 assertion fails | Auto-rollback (transaction). Re-investigate. No state change. |
| V27 commits but HubApi seed fails on new projects | Revert HubApi to previous deploy, OR roll forward by fixing seed. SP state safe. |
| Frontend renders broken labels | Hotfix frontend, no DB action needed. |
| Wider issue post-deploy | Run the rollback block at the bottom of V27 (restores from `archive.roles_pre_v27`). |
