# Frontend Handoff — Role Streamline (V27)

**Audience:** `fp-development-hub` frontend dev
**Date prepared:** 2026-06-10
**Status:** SP migration approved, NOT yet deployed. Code freeze your changes until you receive a "V27 is live" signal.

---

## What's changing on the backend

The Security Platform (SP) is renaming the per-app role `viewer` → `user` across **48 apps**, and
giving the `developer` role `manage_members` permission. New projects created in the Hub will
have their creator assigned as `developer` (not `owner`).

**dashboard-hub stays unchanged** — it keeps its `Admin` / `User` / `Viewer` role triad. The hub
frontend renders roles for both flavors, so your code must handle both.

### Target template after V27 (all standard project apps)

| Role | Permissions |
|---|---|
| `owner` | read, write, ddl, manage_members, delete_project — **manual escalation only** |
| `developer` | read, write, ddl, manage_members — Hub project creators land here |
| `user` | read — every invited member |

`owner` exists as a role but is no longer auto-assigned. It becomes a deliberate "I need to delete
this project" escalation tier.

### dashboard-hub (unchanged)

| Role | Permissions |
|---|---|
| `Admin` | manage_settings, view_all_projects, view_projects, read_data, write_data |
| `User` | view_projects, read_data, write_data |
| `Viewer` | view_projects, read_data |

---

## What you need to change

### Rule: ADD `user` keys — do NOT remove `viewer` keys

dashboard-hub still emits `Viewer` in role responses. If you remove the `viewer` key from your
color/style maps, dashboard-hub badges lose their styling. **Keep both.**

### File-by-file changes

#### `src/Pages/ProjectsPage.tsx`

- **Line 177-178** — color map: add `user: 'default'` next to the existing `viewer: 'default'`.
- **Line 522** — `ROLE_OPTIONS` array: change from `['owner', 'developer', 'viewer']` to
  `['owner', 'developer', 'user']`. The role-picker on this page is for non-hub projects only, so
  it's safe to use the new triad here.

#### `src/Pages/SecurityPage.tsx`

- **Line 645** — `PROJECT_ROLES`: `['owner', 'developer', 'viewer']` → `['owner', 'developer', 'user']`.
- **Line 649-650** — color map: add `user: 'green'` (or whatever color you used for viewer).
- **Line 655-656** — permission display map:
  - Update `developer: 'read, write, ddl'` → `'read, write, ddl, manage_members'`.
  - Add `user: 'read'`.
- **Line 668, 707** — change the default `addRole` state from `'viewer'` to `'user'`.

#### `src/Components/ActivityReport.tsx`

- **Line 46-47** — color map and `ROLE_OPTIONS`: add `user` entries alongside existing `viewer`.
- **Line 1065-1067** — the inner ROLE color/order block, same treatment.
- **Line 1311** — `initialValue="developer"` on the invite form is already the right default.

#### `src/Components/RepoBoard.tsx`

- **Line 659-660** — color map: add `user: 'default'` next to `viewer: 'default'`.

#### `src/styles/ActivityReport.scss`

- **Line 1019** — add a `&--user { … }` rule alongside `&--viewer`. Same background/color
  (light gray, muted text) is fine.

---

## Behaviour you should expect after V27 + HubApi deploy

1. **Existing rtw-list members (8 of them)** — currently rendered as `viewer`. After V27 they
   render as `user`. **No permissions change** — they still have read-only.
2. **Existing project owners (Erick, JL, etc. across many apps)** — still `owner`. Unchanged.
3. **Brand new project created via Hub** — creator lands as `developer`. They can invite members,
   they can run DDL via Claude, they cannot delete the project.
4. **Inviting a new member** — UI default is now `user` (read-only). Owner/developer/user dropdown
   is your call to expose.
5. **dashboard-hub members** — render as `Admin` / `User` / `Viewer`. **Don't try to normalize**
   these to lowercase — the SP returns them exactly as stored.

---

## Edge cases to handle defensively

| Case | Backend returns | Your render |
|---|---|---|
| dashboard-hub viewer | `roleName: "Viewer"` | "Viewer" badge, viewer styling |
| rtw-list user | `roleName: "user"` | "User" badge, user styling |
| Older audit log entry referencing `viewer` | string `"viewer"` in `details.role_name` | Display as-is (history) |
| User has no role on an app | `roleName: ""` or missing | Render "No access" |

### Case-insensitive role checks

If any code does `role === 'owner'` or similar, make it **case-insensitive**:

```ts
const isOwner = (role ?? '').toLowerCase() === 'owner';
const isUser  = (role ?? '').toLowerCase() === 'user';
```

The hub mixes lowercase (standard apps) and capitalized (dashboard-hub) role names. Always
lowercase before comparing.

---

## What NOT to do

- ❌ Don't remove `viewer` keys from color/style maps — dashboard-hub still uses them.
- ❌ Don't auto-grant `owner` anywhere from the frontend. Creator assignment is HubApi's job and
  HubApi now assigns `developer`.
- ❌ Don't hide the `owner` option in role pickers for project owners. Owners can still grant
  `owner` to another user if they choose to.
- ❌ Don't expect a JWT change. Per-app role names are not in the access token. Only platform
  roles (`platform_owner`, `app_admin`) are claimed. Refresh the token if you need updated app
  role labels — they come from `/api/v1/users/{id}` or `/api/v1/apps/{id}/users`.

---

## Sanity checklist before you ship

- [ ] Open a non-hub project in the projects view. Its members render with the new `user` label
      and the styling matches your existing viewer styling.
- [ ] Open dashboard-hub in the security view. Members render as `Admin`/`User`/`Viewer` and the
      styling still applies — no missing color.
- [ ] Invite a new member to a non-hub project. Default role in the dropdown is `user`. Submit
      with `user` selected. The new member appears in the list correctly.
- [ ] Invite a new member to dashboard-hub from the security view (if your UI exposes this).
      Default and options still work — `User` / `Viewer` are still selectable.
- [ ] Create a new project. After creation, your role on it is `developer`, not `owner`.
- [ ] An existing rtw-list viewer logs in. Their role label in any list now shows as `user`.
      They can still read; they cannot write.

---

## Rollout signal

You will get a Teams message: **"V27 is live, HubApi deployed, your turn."**

Until then, you can develop and PR locally but do not merge to `main` / deploy to Netlify. The
window between V27 commit and HubApi deploy is intentionally minutes-long — frontend deploy
happens after both.

---

## Questions / edge cases

Ping Squall (Erick) on Teams. If you hit something surprising about a role label, send:
- App slug
- User ID
- Raw `roleName` returned by SP `/api/v1/apps/{id}/users`
- What you expected vs. what you saw

That triplet is enough to triage in seconds.
