<!-- ⚠️  DO NOT COMMIT THIS FILE ⚠️  -->
<!-- This file contains a live API token scoped to your project database. -->
<!-- Add CLAUDE-local.md to your .gitignore immediately if not already done. -->
<!-- Regenerate from the FlatPlanet Hub if this token is ever exposed.      -->

<!-- CLAUDE_LOCAL_VERSION: 1.4 -->
<!-- Generated: 2026-04-15 -->

> **⚠️ LOCAL FILE — DO NOT COMMIT**
> This file is git-ignored for a reason. It contains a **live API token** tied to your project's database.
> If you accidentally commit this file, go to the FlatPlanet Hub immediately and click **Regenerate** to revoke the token.
> Add this entry to your `.gitignore`: `CLAUDE-local.md`

---

## Mandatory Checks — Do These Regularly, Not Just at Session Start

**CLAUDE-local.md version: 1.4**

These checks must run at session start AND whenever you switch tasks or resume after a break.
STANDARDS.md and CLAUDE-local.md can be updated at any time — not just between sessions.
If a version change is detected mid-session, stop and notify the user before continuing.

### Step 0 — Version check (CLAUDE-local.md)
This file is version **1.4**.
Check the `<!-- CLAUDE_LOCAL_VERSION -->` comment at the top of this file.
If it does not match the latest version in STANDARDS.md, tell the user:
  ⚠️ Your CLAUDE-local.md is outdated (you have v1.4).
  Please regenerate it from the FlatPlanet Hub to get the latest template.
  POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/claude-config/regenerate

### Step 1 — Check CLAUDE.md for updates
Check regularly whether CLAUDE.md in the repo has been updated:
  git fetch origin
  git diff HEAD origin/main -- CLAUDE.md
If there are changes, pull them before continuing any work:
  git pull origin main

### Step 2 — Check FlatPlanet Standards for updates
Fetch the latest STANDARDS.md regularly — not just at session start:
  https://raw.githubusercontent.com/FlatPlanet-Hub/FLATPLANET-STANDARDS/main/FLATPLANET-STANDARDS/STANDARDS.md
Compare the version number at the top against the version you last read.
If it has changed, tell the user immediately and read the full updated file before writing any code.

### Step 3 — Read the conversation log
Before doing anything else, read `CONVERSATION-LOG.md` in the project root.
This is Claude's memory across sessions — current state, decisions made, open issues, what to do next.
If the file does not exist yet, create it before closing the session.
At the end of every session, append a new entry to `CONVERSATION-LOG.md` before committing.

---

# Project Context

## Project
- **Name**: FlatPlanet Security Platform
- **Description**: Centralized IAM service
- **Project ID**: 2b702cde-58a1-4f08-9db0-63bc357abfd9
- **Schema**: project_security
- **Tech Stack**: .NET 10 / C#
- **Project Type**: fullstack
- **Auth Enabled**: False

## Platform API

Base URL: https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net
Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkYzg4Nzg2YS0wYjM4LTQzYmItOGRjMy03ZWMzNmYwNTBlYzkiLCJuYW1lIjoiY2hyaXMubW9yaWFydHlAZmxhdHBsYW5ldC5jb20iLCJlbWFpbCI6ImNocmlzLm1vcmlhcnR5QGZsYXRwbGFuZXQuY29tIiwiYXBwX3NsdWciOiJzZWN1cml0eS1wbGF0Zm9ybSIsInBlcm1pc3Npb25zIjoibWFuYWdlX21lbWJlcnMscmVhZCx3cml0ZSIsInRva2VuX3R5cGUiOiJhcGlfdG9rZW4iLCJqdGkiOiI1ZjExOWVjNy04NjkwLTQwNDctOGE1MS1jMTU0YmU4NDEwYTUiLCJhcHBfaWQiOiI1ZjU3MDViNS0xM2UwLTQwNzYtOTI1NS1mNmVhNWI3ZWU5YTUiLCJzY2hlbWEiOiJwcm9qZWN0X3NlY3VyaXR5IiwiZXhwIjoxNzc4ODA3MTE2LCJpc3MiOiJmbGF0cGxhbmV0LXNlY3VyaXR5IiwiYXVkIjoiZmxhdHBsYW5ldC1hcHBzIn0.v0Ag_v0TnZUuTnG6Ed8Q79KZiqqEq2478vOPXxbXR30
Token Expires: 2026-05-15

All API requests require this header:
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkYzg4Nzg2YS0wYjM4LTQzYmItOGRjMy03ZWMzNmYwNTBlYzkiLCJuYW1lIjoiY2hyaXMubW9yaWFydHlAZmxhdHBsYW5ldC5jb20iLCJlbWFpbCI6ImNocmlzLm1vcmlhcnR5QGZsYXRwbGFuZXQuY29tIiwiYXBwX3NsdWciOiJzZWN1cml0eS1wbGF0Zm9ybSIsInBlcm1pc3Npb25zIjoibWFuYWdlX21lbWJlcnMscmVhZCx3cml0ZSIsInRva2VuX3R5cGUiOiJhcGlfdG9rZW4iLCJqdGkiOiI1ZjExOWVjNy04NjkwLTQwNDctOGE1MS1jMTU0YmU4NDEwYTUiLCJhcHBfaWQiOiI1ZjU3MDViNS0xM2UwLTQwNzYtOTI1NS1mNmVhNWI3ZWU5YTUiLCJzY2hlbWEiOiJwcm9qZWN0X3NlY3VyaXR5IiwiZXhwIjoxNzc4ODA3MTE2LCJpc3MiOiJmbGF0cGxhbmV0LXNlY3VyaXR5IiwiYXVkIjoiZmxhdHBsYW5ldC1hcHBzIn0.v0Ag_v0TnZUuTnG6Ed8Q79KZiqqEq2478vOPXxbXR30

## Platform API Capabilities

The Platform API provides shared services for all FlatPlanet projects.
Use the token above as Bearer on all calls to these endpoints.

### File Storage
⚠️  DO NOT build your own file upload endpoint or connect to Azure Blob Storage directly.
All file storage is handled centrally by the Platform API — use the endpoints below.
Files are automatically scoped to YOUR app — your project can only see its own files.
Scoping is enforced by the app_id in your API token — no extra config needed.
SAS URLs are time-limited (60 min) — always fetch a fresh URL before displaying.
Never cache or hardcode blob URLs.

Upload a file:
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/v1/storage/upload
Content-Type: multipart/form-data
Fields: file (binary), businessCode (e.g. "fp"), category (e.g. "logos"), tags (comma-separated, optional)
Returns: { fileId, sasUrl, sasExpiresAt, businessCode, category, originalName, fileSizeBytes, tags }

List files:
GET https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/v1/storage/files?businessCode=fp&category=logos&tags=primary
Returns: array of file objects each with a fresh sasUrl

Get a fresh SAS URL for an existing file:
GET https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/v1/storage/files/{fileId}/url
Returns: { sasUrl, expiresAt }

Delete a file:
DELETE https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/v1/storage/files/{fileId}
Returns: 204 No Content

### Business Membership
The JWT contains a business_codes[] claim (e.g. ["fp"]) — use this to filter content per business.
Do NOT hardcode business IDs — always use the code from the JWT claim.

Read business_codes from the decoded JWT:
  const codes = jwt.business_codes; // ["fp"]
  const isFlatPlanet = codes.includes("fp");

### Full API Reference
For complete endpoint docs, request/response schemas, and error codes:
  Platform API:      https://github.com/FlatPlanet-Hub/platform-api/blob/main/docs/platform-api-reference.md
  Security Platform: https://github.com/FlatPlanet-Hub/flatplanet-security-platform/blob/main/docs/security-api-reference.md

## Working With the Database

### Step 0 — Check the naming dictionary BEFORE naming anything

> ⚠️ Do NOT read DATA_DICTIONARY.md from the local filesystem — it may be stale.
> Always query the live API below for accurate schema context.

Before creating any table, column, variable, or function, query the data dictionary
to find the approved standard name for the concept you are working with.

Search by concept:
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/query/read
```json
{
  "sql": "SELECT field_name, data_type, format, description, example, entity FROM data_dictionary WHERE field_name ILIKE @search OR description ILIKE @search OR entity ILIKE @search ORDER BY field_name LIMIT 20",
  "parameters": { "search": "%<concept>%" }
}
```

If the standard name exists — use it exactly as recorded in `field_name`.
If no matching entry exists, create one before proceeding:
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/query/write
```json
{
  "sql": "INSERT INTO data_dictionary (field_name, data_type, format, description, example, entity, category, is_required, source) VALUES (@field_name, @data_type, @format, @description, @example, @entity, @category, @is_required, 'project') ON CONFLICT DO NOTHING",
  "parameters": {
    "field_name": "snake_case_name",
    "data_type": "text|uuid|timestamptz|boolean|numeric|...",
    "format": null,
    "description": "What this field represents",
    "example": "example_value",
    "entity": "the table or entity this belongs to",
    "category": "field",
    "is_required": false
  }
}
```

### Step 1 — Read the project schema
GET https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/schema/full

Returns all tables, columns, types, and foreign key relationships in this project.

### Create a Table
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/migration/create-table
```json
{
  "tableName": "table_name",
  "columns": [
    { "name": "id", "type": "uuid", "isPrimaryKey": true, "default": "gen_random_uuid()" },
    { "name": "name", "type": "text", "nullable": false },
    { "name": "created_at", "type": "timestamptz", "default": "now()" }
  ],
  "enableRls": true
}
```

### Alter a Table
PUT https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/migration/alter-table
```json
{
  "tableName": "table_name",
  "operations": [
    { "type": "AddColumn", "columnName": "new_col", "dataType": "text" },
    { "type": "DropColumn", "columnName": "old_col" },
    { "type": "RenameColumn", "columnName": "old_name", "newColumnName": "new_name" }
  ]
}
```

### Drop a Table
DELETE https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/migration/drop-table?table={name}

### Read Query
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/query/read
```json
{
  "sql": "SELECT * FROM table_name WHERE column = @param LIMIT @limit",
  "parameters": { "param": "value", "limit": 50 }
}
```

### Write Query
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/query/write
```json
{
  "sql": "INSERT INTO table_name (col1, col2) VALUES (@val1, @val2)",
  "parameters": { "val1": "hello", "val2": "world" }
}
```

## Rules
1. ALWAYS check the data dictionary (Step 0) before naming any table, column, variable, or function
2. ALWAYS read the schema (Step 1) before writing any database-related code
3. ALWAYS use @paramName syntax in queries — NEVER concatenate values into SQL strings
4. Use migration endpoints for CREATE TABLE / ALTER TABLE / DROP TABLE — never raw DDL in query endpoints
5. All database access goes through the API — NEVER connect to the database directly
6. If an API call fails, check the "success" field and "error" message in the response
7. If the token has expired, ask the user to regenerate CLAUDE-local.md from the FlatPlanet Hub

## Git Workflow

No GitHub repo linked to this project yet.

1. Work on a feature branch: git checkout -b feature/{feature-name}
2. Build and test locally before committing
3. Commit with descriptive messages: feat:, fix:, refactor:, docs:
4. Push: git push origin feature/{feature-name}
5. For major features, create a PR to main

## Coding Standards

### Frontend Standards (React / TypeScript)
- React.js with TypeScript (latest version)
- Strict TypeScript — no `any`, explicit return types on all functions
- Component naming: PascalCase, one component per file
- Hooks: prefix with `use`, keep side effects in useEffect only
- State management: follow existing pattern in the codebase
- Folder structure: feature-based (components, hooks, services, types per feature)
- API calls: always through a service layer — never fetch directly in components
- Error boundaries: wrap major sections
- No unused imports, no console.log in production code
- Deploy target: Netlify

### Backend Standards (.NET 10 / C#)
- .NET 10 / C# — use latest language features (primary constructors, pattern matching, etc.)
- Clean Architecture: Controller → Application Service → Domain → Infrastructure
- SOLID principles enforced
- Dependency Injection for all services — never instantiate dependencies manually
- Apply design patterns where appropriate: Strategy, Chain of Responsibility, Factory, Decorator
- No EF Core — Dapper only, raw SQL via IDbConnectionFactory
- All async/await — no blocking calls (.Result, .Wait())
- GlobalExceptionMiddleware handles all errors — never swallow exceptions silently
- Always run `dotnet build` before committing
- Deploy target: Azure App Service

### Database Standards (Supabase / PostgreSQL)
- Supabase / PostgreSQL
- ALWAYS check the data dictionary before naming anything (Step 0)
- ALWAYS read the schema before writing DB code (Step 1)
- All DDL goes through migration endpoints — never raw DDL in query endpoints
- All queries use @paramName — never concatenate values into SQL
- snake_case for all table and column names
- UUID primary keys with gen_random_uuid()
- Always include created_at TIMESTAMPTZ DEFAULT now()
- Soft deletes preferred — use is_active boolean over hard deletes
- Always add indexes on foreign keys and frequently queried columns

- Clean, readable code — add comments only where logic is non-obvious
- Handle errors gracefully — never swallow exceptions silently
- Follow naming conventions of the existing codebase

## Project Management

Use these endpoints (with your SP JWT, not the API token) to manage this project:

### Enable Authentication on this project
PUT https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9
Header: Authorization: Bearer <SP JWT>
Body: { "authEnabled": true }
After enabling: regenerate this file to get the SP auth integration guide injected.

### Regenerate this workspace file
POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/claude-config/regenerate
Header: Authorization: Bearer <SP JWT>
Returns a fresh CLAUDE-local.md with a new token. Ask the user to save the new file.

## FlatPlanet Security Platform (SP)

All FlatPlanet projects use the Security Platform for authentication and authorization.
NEVER build your own auth system — always use the SP endpoints below.

SP Base URL:  https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net
App Slug:     security-platform
App ID:       5f5705b5-13e0-4076-9255-f6ea5b7ee9a5
JWT Issuer:   flatplanet-security
JWT Audience: flatplanet-apps
Auth Status:  DISABLED — enable when ready (see Project Management above)

### Login
POST https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net/api/v1/auth/login
Body: { "email": "...", "password": "...", "appSlug": "security-platform" }
Returns: { accessToken (60 min), refreshToken, expiresIn, user }

### Protect Routes
All protected routes require:
  Authorization: Bearer <accessToken>
On 401 → try refresh. If refresh fails → redirect to login.

### Check Permission
POST https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net/api/v1/authorize
Body: { "appSlug": "security-platform", "resourceIdentifier": "/your-route", "requiredPermission": "read" }
Returns: { allowed: true/false } — HTTP 200 either way. allowed: false → show no-access page.

### Refresh Token
POST https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net/api/v1/auth/refresh
Body: { "refreshToken": "..." }
Single-use — store the new token immediately. On 401 → redirect to login.

### Logout
POST https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net/api/v1/auth/logout
Revokes all refresh tokens. Clear both tokens client-side and redirect to login.

## Azure Deployment

Status: NOT PROVISIONED

This project has no Azure App Service yet.

### Deployment type guidance
All FlatPlanet backend projects deploy to **Azure App Service** — not Azure Functions.
If the current codebase is structured as Azure Functions, recommend converting it
to a standard ASP.NET Core Web API before provisioning.
Azure Functions is only appropriate for isolated event-driven/scheduled workloads,
not for full REST APIs serving the frontend.

To provision, tell Claude Code:
  "provision Azure for this project"

Claude Code will call:
  POST https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/api/projects/2b702cde-58a1-4f08-9db0-63bc357abfd9/provision-azure
And update this file automatically once complete.

