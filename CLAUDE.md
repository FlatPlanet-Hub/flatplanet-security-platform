# FlatPlanet Security Platform — Claude Project Guide

> The live API token lives in `CLAUDE-local.md` (gitignored).
> Read `CLAUDE-local.md` at session start — it has the token and full project config.
> If `CLAUDE-local.md` is missing, ask the user to regenerate it from the FlatPlanet Hub.

---

## Mandatory Checks — Do These Regularly, Not Just at Session Start

### Step 0 — Pull latest
```
git pull origin main
```

### Step 1 — Read CLAUDE-local.md
Contains: live API token, full Platform API endpoints, DB query endpoints.
Version 1.7 or later required. If outdated, tell the user to regenerate.

### Step 2 — Check FlatPlanet Standards for updates
Fetch the latest STANDARDS.md regularly:
  https://raw.githubusercontent.com/FlatPlanet-Hub/FLATPLANET-STANDARDS/main/FLATPLANET-STANDARDS/STANDARDS.md
If the version has changed, read the full file before writing any code.

### Step 3 — Read the conversation log
Read `CONVERSATION-LOG.md` in the project root before doing anything else.
Append a new entry at the end of every session.

---

## Project

- **Name**: FlatPlanet Security Platform
- **Description**: Centralized IAM service for all FlatPlanet projects
- **Project ID**: 2b702cde-58a1-4f08-9db0-63bc357abfd9
- **Schema**: project_security
- **Stack**: .NET 10 / C#
- **Auth**: Disabled on this project (this IS the auth service)

## Platform API

Base URL: `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`
Token: **see CLAUDE-local.md**

All Platform API JSON responses use **camelCase** field names.
Always deserialize with `JsonNamingPolicy.CamelCase` — never `SnakeCaseLower`.

## Security Platform (this service)

Base URL: `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`
App Slug: `security-platform`
App ID: `5f5705b5-13e0-4076-9255-f6ea5b7ee9a5`
JWT Issuer: `flatplanet-security`
JWT Audience: `flatplanet-apps`

## Azure Deployment

Status: NOT PROVISIONED — no App Service yet.
To provision: tell Claude Code "provision Azure for this project".

## Team

This project uses the full agent team. See `.claude/CLAUDE.md` for the team definition.

| Agent | Role |
|---|---|
| Squall | Team Lead / Orchestrator |
| Cloud | Coder |
| Lightning | Reviewer & Planner |
| Yuffie | Integration Tester |
| Tifa | Tech Writer |

## Git Workflow

1. Feature branch: `git checkout -b feature/{name}`
2. Build before committing: `dotnet build`
3. Commit prefixes: `feat:` `fix:` `refactor:` `docs:`
4. Push to feature branch, PR to `main`

## API Reference

- Security Platform: https://github.com/FlatPlanet-Hub/flatplanet-security-platform/blob/main/docs/security-api-reference.md
- Platform API: https://github.com/FlatPlanet-Hub/platform-api/blob/main/docs/platform-api-reference.md
