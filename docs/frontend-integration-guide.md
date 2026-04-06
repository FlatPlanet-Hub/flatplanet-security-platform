# FlatPlanet — Frontend Integration Guide

**Audience:** Frontend developers
**Last updated:** 2026-04-06
**Verified against:** Security Platform v1.2.2 · Platform API (HubApi) v1.0.0
**Tested by:** Integration tester (Claude Code)

---

## What's New in This Version

> These are verified changes from integration testing — update your frontend accordingly.

### Platform API v1.0.0 (2026-04-06)

| # | Change | Impact |
|---|---|---|
| 1 | All project endpoints now include `projectType` and `authEnabled` fields | Required on `POST /api/projects` — determines enforced tech stack and SP auth integration |
| 2 | `GET /api/projects/{id}/claude-config/workspace` — new workspace endpoint | Returns `CLAUDE-local.md` content + scoped API token. Frontend downloads and saves locally — **never committed to repo** |
| 3 | Smart token management on workspace — existing token revoked before issuing new | No token accumulation; safe to call on every workspace refresh |
| 4 | `CLAUDE.md` is no longer auto-committed to the GitHub repo | The Claude brief is now `CLAUDE-local.md`, generated locally via the workspace endpoint |

### Platform API v0.9.0 (2026-04-01)

| # | Change | Impact |
|---|---|---|
| 1 | `GET /api/projects` and `GET /api/projects/{id}` now include a `github` object | Projects with a linked repo return `repoName`, `repoFullName`, `branch`, `repoLink` |
| 2 | `POST /api/projects` supports GitHub repo creation and linking | Pass a `github` object to create a new repo or link an existing one |
| 3 | `POST /api/projects/{id}/members` accepts `githubUsername` | Optionally adds the user as a GitHub collaborator at invite time |
| 4 | Valid project roles are `owner`, `developer`, `viewer` — **`admin` is not valid** | Do not use `admin` in role dropdowns — it will return `409` |
| 5 | `GET /api/projects/{id}` `ownerId`, `appSlug`, `schemaName` now return real values | These were previously silently null due to a DB mapping bug (now fixed) |
| 6 | Adding a member to a project auto-grants them `viewer` on `dashboard-hub` | New members can immediately access the dashboard without a separate grant |
| 7 | `POST /api/v1/authorize` with `view_projects` now returns `allowed: true` for `platform_owner` | The permission was previously missing from the DB |

---

## Overview

There are two backend services the frontend talks to:

| Service | What it does | Base URL |
|---|---|---|
| **Security Platform** | Login, logout, token refresh, user identity, authorization checks | `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net` |
| **Platform API (HubApi)** | Projects, members, API tokens, DB proxy | `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net` |

The frontend only logs in through the Security Platform. The JWT it issues is used directly as the bearer token for HubApi too. There is no separate HubApi login.

---

## Auth Flow (unchanged)

```
1.  POST  /api/v1/auth/login                              → get accessToken + refreshToken
2.  GET   /api/v1/auth/me                                 → get user profile + roles
3.  GET   /api/projects                                   → list user's projects           (HubApi)
4.  GET   /api/projects/{id}                              → get single project             (HubApi)
5.  GET   /api/projects/{id}/claude-config/workspace      → download CLAUDE-local.md       (HubApi)
6.  POST  /api/v1/auth/refresh                            → rotate tokens before expiry
7.  POST  /api/v1/auth/logout                             → end session, clear tokens
```

Attach to every request:
```
Authorization: Bearer <accessToken>
Content-Type: application/json
```

---

## Step 1 — Login

**Endpoint:** `POST /api/v1/auth/login` on the Security Platform

**Request:**
```json
{
  "email": "chris.moriarty@flatplanet.com",
  "password": "••••••••",
  "appSlug": "dashboard-hub"
}
```

`appSlug` is optional. Pass `"dashboard-hub"` to get app-scoped permissions included in `/auth/me`.

**Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci...",
    "refreshToken": "TUmzgLj...",
    "expiresIn": 3600,
    "user": {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "email": "chris.moriarty@flatplanet.com",
      "fullName": "Chris Moriarty",
      "companyId": "a5af2cfc-2887-4e60-942d-8c29ccf012cf"
    }
  }
}
```

**Error cases:**

| HTTP | Meaning |
|---|---|
| `400` | Missing email or password |
| `401` | Wrong credentials |
| `403` | Account or company is suspended |
| `423` | Account temporarily locked |
| `429` | Rate limited |

---

## Step 2 — Check Authorization (Dashboard Gate)

Before rendering the dashboard, check if the user has access:

**Endpoint:** `POST /api/v1/authorize` on the Security Platform

**Request:**
```json
{
  "appSlug": "dashboard-hub",
  "resourceIdentifier": "/dashboard",
  "requiredPermission": "view_projects"
}
```

**Response — allowed:**
```json
{
  "success": true,
  "data": {
    "allowed": true,
    "roles": ["platform_owner"],
    "permissions": ["manage_apps", "manage_users", "view_audit_log", "view_projects", "manage_resources", "manage_roles", "manage_companies"]
  }
}
```

**Response — denied:**
```json
{
  "success": true,
  "data": {
    "allowed": false,
    "roles": [],
    "permissions": []
  }
}
```

> `allowed: false` with HTTP `200` is normal — it means the user doesn't have that permission, not an error. Redirect to a no-access page.

**Who can access the dashboard:**

| Role | `allowed` |
|---|---|
| `platform_owner` | ✅ Yes — has `view_projects` |
| Regular user (Viewer on dashboard-hub) | ✅ Yes — has `view_projects` |
| User with no dashboard-hub role | ❌ No |

---

## Step 3 — Projects

### List projects

`GET /api/projects` on HubApi

**Response:**
```json
{
  "success": true,
  "data": [
    {
      "id": "f56c5da7-760b-4273-a766-6bf90089d268",
      "name": "FlatPlanet Development Hub (Frontend)",
      "description": "Hub frontend",
      "schemaName": "project_hub_fe",
      "ownerId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "appSlug": "fp-development-hub",
      "roleName": "owner",
      "techStack": "Next.js",
      "projectType": "frontend",
      "authEnabled": false,
      "isActive": true,
      "createdAt": "2026-03-27T00:55:39Z",
      "github": {
        "repoName": "fp-development-hub",
        "repoFullName": "FlatPlanet-Hub/fp-development-hub",
        "branch": "main",
        "repoLink": "https://github.com/FlatPlanet-Hub/fp-development-hub"
      },
      "members": null
    }
  ]
}
```

> `github` is `null` when no repo is linked. `members` is always `null` on list — use `GET /api/projects/{id}/members` to fetch members.

**Who sees what:**
- Regular users see only their own projects
- Users with `view_all_projects` permission on `dashboard-hub` see all projects (their `roleName` shows as `"admin"` for projects they're not explicitly a member of)

---

### Create project

`POST /api/projects` on HubApi

**Request — no GitHub:**
```json
{
  "name": "My New Project",
  "description": "Optional description",
  "techStack": "React",
  "projectType": "frontend",
  "authEnabled": false
}
```

**Request — create new GitHub repo:**
```json
{
  "name": "My New Project",
  "techStack": "React",
  "projectType": "fullstack",
  "authEnabled": true,
  "github": {
    "createRepo": true,
    "repoName": "my-new-project"
  }
}
```

**Request — link existing GitHub repo:**
```json
{
  "name": "My New Project",
  "techStack": "React",
  "projectType": "backend",
  "authEnabled": false,
  "github": {
    "createRepo": false,
    "existingRepoUrl": "https://github.com/FlatPlanet-Hub/my-new-project"
  }
}
```

**`projectType` values:**

| Value | Stack enforced in CLAUDE-local.md |
|---|---|
| `frontend` | React.js + TypeScript → Netlify |
| `backend` | .NET 10 / C# → Azure App Service |
| `database` | Supabase / PostgreSQL |
| `fullstack` | Frontend + Backend + Database |

**`authEnabled`:** When `true`, the generated `CLAUDE-local.md` includes the FlatPlanet Security Platform auth integration guide. Set to `false` until the project is ready to integrate auth.

**Response `201`:**
```json
{
  "success": true,
  "data": {
    "id": "20721609-cb5e-41e2-8d16-395d49d4cfdd",
    "name": "My New Project",
    "schemaName": "project_68996f32",
    "ownerId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
    "appSlug": "my-new-project",
    "roleName": "owner",
    "techStack": "React",
    "projectType": "frontend",
    "authEnabled": false,
    "isActive": true,
    "createdAt": "2026-04-01T01:59:27Z",
    "github": {
      "repoName": "my-new-project",
      "repoFullName": "FlatPlanet-Hub/my-new-project",
      "branch": "main",
      "repoLink": "https://github.com/FlatPlanet-Hub/my-new-project"
    }
  }
}
```

`github` is `null` in the response when no GitHub configuration was provided.

**Notes:**
- Creation takes 2–4 seconds — ~19 Security Platform calls happen in sequence
- `CLAUDE-local.md` is **not** committed to the repo — the developer generates it locally after project creation via the workspace endpoint (see Step 5 below)
- On `409` the SP error message is included in the response body — surface it to the user

**Error cases:**

| HTTP | Meaning |
|---|---|
| `400` | `name` is missing or `company_id` claim absent |
| `401` | Missing or invalid JWT |
| `409` | Slug already taken or SP error (message in body) |

---

### Get single project

`GET /api/projects/{id}` on HubApi

**Response `200`:** Same shape as list item above (including `github` object).

| HTTP | Meaning |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User doesn't have access to this project |
| `404` | Project not found |

---

## Step 4 — Project Members

### List members

`GET /api/projects/{id}/members` on HubApi

**Response `200`:**
```json
{
  "success": true,
  "data": [
    {
      "userId": "dc88786a-0b38-43bb-8dc3-7ec36f050ec9",
      "fullName": "Chris Moriarty",
      "email": "chris.moriarty@flatplanet.com",
      "githubUsername": null,
      "roleName": "owner",
      "permissions": ["read", "write", "ddl", "manage_members", "delete_project"],
      "grantedAt": "2026-03-27T00:55:39Z"
    }
  ]
}
```

---

### Add member

`POST /api/projects/{id}/members` on HubApi

**Request:**
```json
{
  "userId": "f21b0f18-deb1-4d1f-a0c3-f193c3d049b2",
  "role": "developer",
  "githubUsername": "john-dev"
}
```

> `githubUsername` is optional. If provided, the user is invited as a GitHub repo collaborator (`owner` → admin, `developer` → push, `viewer` → pull). This is the **only time** GitHub access is granted — there is no endpoint to add GitHub access later without removing and re-adding the member.

**Valid roles:** `owner` | `developer` | `viewer` — do not use `admin`

**Response `200`:**
```json
{
  "success": true
}
```

**Key behaviour:**
- After granting the project role, HubApi automatically grants the user `viewer` on `dashboard-hub` if they don't already have a role there. This means new members can access the dashboard immediately.
- The user must already exist in the Security Platform. This endpoint does not create users.
- Re-inviting a previously removed user works (upsert — no `409`).

| HTTP | Meaning |
|---|---|
| `403` | Caller lacks `manage_members` on this project |
| `409` | Project not registered with Security Platform (missing `appSlug`) |

**Who can add members:**

| Role | Can add? |
|---|---|
| `platform_owner` | ✅ Yes |
| Project `owner` | ✅ Yes |
| Project `developer` / `viewer` | ❌ No — `403` |
| User with no project role | ❌ No — `403` |

---

### Update member role

`PUT /api/projects/{id}/members/{userId}/role` on HubApi

**Request:**
```json
{
  "role": "viewer"
}
```

**Response `200`:** `{ "success": true }`

> GitHub repo permissions are **not updated** on role change. Only Security Platform role changes.

---

### Remove member

`DELETE /api/projects/{id}/members/{userId}` on HubApi

**Response `200`:** `{ "success": true }`

> API tokens held by the removed user for this project are immediately revoked. GitHub collaborator access is not removed.

---

## Step 5 — Workspace File (CLAUDE-local.md)

After a project is created (or whenever the developer needs to refresh their local Claude setup), the frontend downloads the `CLAUDE-local.md` workspace file and saves it to the developer's local project folder.

**Endpoint:** `GET /api/projects/{id}/claude-config/workspace` on HubApi

**Auth:** Security Platform JWT (same bearer token used everywhere)

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "filename": "CLAUDE-local.md",
    "gitignoreEntry": "CLAUDE-local.md",
    "content": "# My New Project — Claude Code Workspace\n\n...(full file content)...",
    "tokenId": "a3f1b2c4-...",
    "expiresAt": "2026-05-06T00:00:00Z"
  }
}
```

**What `content` contains (auto-generated by HubApi):**
- Project name, type, and auth status
- Live scoped API token (valid 30 days) + expiry date
- HubApi base URL and DB proxy endpoint reference
- Enforced tech stack standards for the project's `projectType`
- Security Platform auth integration guide (only when `authEnabled = true`)
- FlatPlanet Standards URL

**Frontend implementation:**

1. Call the workspace endpoint after project creation (or on demand from a "Download Workspace File" button)
2. Save the returned `content` to the developer's local machine as `CLAUDE-local.md` (see saving options below)
3. **Never upload or store this file server-side** — it contains a live API token
4. Show the `gitignoreEntry` value and remind the developer to add `CLAUDE-local.md` to `.gitignore`
5. Show `expiresAt` so the developer knows when to regenerate

**Smart token behaviour:**
- If the project already has an active token, it is **revoked** before a new one is issued
- Safe to call on every workspace refresh — no token accumulation
- To force-regenerate: `POST /api/projects/{id}/claude-config/regenerate`

---

### How to save CLAUDE-local.md to the developer's machine

Browsers cannot write to arbitrary folders without user interaction. Use **Option B** as primary with **Option A** as fallback.

#### Option B — File System Access API (recommended, Chrome/Edge only)

Opens the native OS save dialog so the developer can navigate directly to their project folder and save the file there.

```js
async function saveWorkspaceFile(content) {
  try {
    const fileHandle = await window.showSaveFilePicker({
      suggestedName: 'CLAUDE-local.md',
      types: [{ description: 'Markdown', accept: { 'text/markdown': ['.md'] } }],
    });
    const writable = await fileHandle.createWritable();
    await writable.write(content);
    await writable.close();
  } catch (err) {
    if (err.name !== 'AbortError') throw err; // user cancelled — do nothing
  }
}
```

> Requires HTTPS (Netlify ✅). Not supported in Firefox or Safari — fall back to Option A for those.

#### Option A — Browser download (fallback, all browsers)

Triggers a standard browser download. File lands in the user's **Downloads folder** — they must move it to their project root manually.

```js
function downloadWorkspaceFile(content) {
  const blob = new Blob([content], { type: 'text/markdown' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'CLAUDE-local.md';
  a.click();
  URL.revokeObjectURL(url);
}
```

#### Combined implementation (B with A as fallback)

```js
async function handleDownloadWorkspace(projectId) {
  const res = await fetch(
    `${HUBAPI_BASE_URL}/api/projects/${projectId}/claude-config/workspace`,
    { headers: { Authorization: `Bearer ${accessToken}` } }
  );
  const { data } = await res.json();

  if ('showSaveFilePicker' in window) {
    await saveWorkspaceFile(data.content);   // Option B — native save dialog
  } else {
    downloadWorkspaceFile(data.content);     // Option A — Downloads folder fallback
  }
}
```

**Recommended UI:**
- Button label: **"Download Workspace File"**
- After save: show a reminder banner:
  > `CLAUDE-local.md` saved. Make sure `CLAUDE-local.md` is in your project's `.gitignore`. Token expires **{expiresAt}**.

---

| HTTP | Meaning |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | User doesn't have access to this project |
| `404` | Project not found |

---

## Step 6 — Token Refresh

**Endpoint:** `POST /api/v1/auth/refresh` on the Security Platform

**Request:**
```json
{
  "refreshToken": "TUmzgLj..."
}
```

**Response `200`:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci... (new)",
    "refreshToken": "9QzCQ... (new)",
    "expiresIn": 3600
  }
}
```

- Refresh token is **single-use** — store the new one immediately
- On `401` from refresh → redirect to login page

**401 handling pattern:**
```
401 received
  ├─ Try POST /api/v1/auth/refresh
  │     ├─ 200 → store new tokens, retry original request
  │     └─ 401 → redirect to login page
  └─ If no refresh token stored → redirect to login page
```

---

## Step 7 — Logout

`POST /api/v1/auth/logout` on the Security Platform

Revokes all refresh tokens and ends the session. Clear both tokens and redirect to login.

**Response `200`:** `{ "success": true, "message": "Logged out successfully." }`

---

## Session Timeouts

| Timeout | Default | Triggered by |
|---|---|---|
| **Idle** | 30 minutes | No requests for 30 min → `401` |
| **Absolute** | 8 hours | Session older than 8 hours → `401` |

---

## Standard Response Envelope

**Success:**
```json
{ "success": true, "data": { ... } }
```

**Success (no data):**
```json
{ "success": true, "message": "Logged out successfully." }
```

**Error:**
```json
{ "success": false, "message": "Invalid email or password." }
```

**Validation error (400):**
```json
{
  "success": false,
  "message": "Validation failed.",
  "errors": {
    "Password": ["The Password field is required."]
  }
}
```

---

## Known Limitations

| # | Limitation | Workaround |
|---|---|---|
| 1 | `GET /api/v1/users` and `GET /api/v1/apps` require `platform_owner` or `app_admin` platform role | Project owners cannot browse the user list for invite — platform owner must do it |
| 2 | GitHub repo collaborator is not removed when a member is removed | Must be done manually on GitHub |
| 3 | GitHub repo permissions are not updated when a member's role changes | Re-add the member with the correct role if GitHub access must match |
| 4 | Legacy projects (created before GitHub integration) have empty `repoName` and `repoLink` in the `github` object even if `repoFullName` is populated | Use `repoFullName` to construct the link: `https://github.com/{repoFullName}` |

---

## Base URLs

```
Security Platform:  https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net
Platform API:       https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net
```

## Token Types

| Token | Issued by | Used for | Lifetime |
|---|---|---|---|
| **Security Platform JWT** | SP `/auth/login` | Frontend → Security Platform, Frontend → HubApi | 60 min |
| **HubApi API Token** | HubApi `/api/auth/api-tokens` | Claude Code → DB Proxy only | 30 days |

The frontend only ever uses the **Security Platform JWT**. Never use an HubApi API Token for frontend requests.
