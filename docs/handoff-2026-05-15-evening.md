# Tonight's Handoff — 2026-05-15

> Self-contained document. If you're reading this on a fresh machine or in a fresh Claude session, this is everything you need to pick up where we left off. No prior context required.

---

## TL;DR — What to do tonight

1. Wait for a low-traffic window (late evening PH time)
2. Merge **HubApi PR #39** → https://github.com/FlatPlanet-Hub/platform-api/pull/39
3. Watch logs for clean startup (~3 min outage during deploy)
4. Verify cache decorator is wired (see [verification](#post-deploy-verification))
5. Optionally start the next PR: per-project rate limiting on `/query/*`

That's it. Everything else in this document is context if you need it.

---

## What's deployed right now

### Security Platform (SP) — `flatplanet-security-api`
- Branch: `main`
- Live deployment: `a44fe17b` (deployed 2026-05-15 00:18 UTC)
- Contains:
  - `PublishReadyToRun=true` — prevents JIT crash loop on cold start
  - JWT access token expiry: 4 hours (was 60 min) — users stay logged in through SP outages
  - `AuditLogCleanupService` 5-min startup delay (PgBouncer pressure mitigation)
- Connection string: `Minimum Pool Size=0;Maximum Pool Size=20;Timeout=30;` (intentional — do not change)
- Known characteristic: cold-start warmup takes ~130s on Azure B1 even with R2R. This is acceptable, doesn't crash loop.

### HubApi — `flatplanet-api`
- Branch: `main`
- Live deployment: pre-R2R, pre-cache. **Vulnerable to JIT crash loop if Azure moves it to a new VM.**
- Today at 04:50 UTC Azure did exactly that — moved both SP and HubApi to host `lw1sdlwk000ACL`. HubApi warmup took 136s. Survived but barely.
- Next move could go either way. PR #39 fixes this.

---

## PR #39 — What it actually does

Branch: `feature/cache-sp-user-access` on `platform-api` repo.

### Three changes:

**1. `PublishReadyToRun=true` in HubApi API csproj**
Pre-compiles .NET IL to native at publish time. Without it, JIT on Azure B1 (single vCPU) can take 2-3 minutes for cold startup. Same fix that already saved SP earlier today.

**2. DI wiring fix — `InfrastructureExtensions.cs`**

Before:
```csharp
services.AddScoped<ISecurityPlatformService, SecurityPlatformService>();
```

After:
```csharp
services.AddScoped<SecurityPlatformService>();
services.AddScoped<ISecurityPlatformService, CachedSecurityPlatformService>();
```

**Why this matters:** the previous PR (`CachedSecurityPlatformService.cs`) existed as an untracked file but was **never registered**. The cache had zero effect in production. This wires it up.

**3. Two-tier cache with stale-on-error in `CachedSecurityPlatformService.cs`**

```csharp
private static readonly TimeSpan FreshTtl = TimeSpan.FromMinutes(5);
private static readonly TimeSpan StaleTtl = TimeSpan.FromHours(2);
```

- Normal request: serve from fresh cache (5 min), avoid SP entirely
- SP returns successfully: refresh both fresh and stale entries
- SP fails / unreachable AND fresh expired: serve stale (last known good)
- Explicit role grant/change/revoke: clear both keys immediately

Result: brief SP outages (Azure restarts, deploys) become invisible to active users.

### What PR #39 does NOT fix

- **Today's actual pain (Finvoice agent pool saturation)** — that's a HubApi-side concurrency problem, not an SP problem. PR #39 is defensive infrastructure for **future** Azure restarts and SP outages, not a fix for today's specific incident.
- `AuthorizeAsync`, `GetAppMembersAsync`, `GetUserAsync`, `GetAppIdBySlugAsync` still hit SP live. These are P2, separate PR.

---

## Deploy procedure

### Before merging
- [ ] Confirm low traffic (check hub usage, ask the team in chat)
- [ ] Confirm no active migrations or deploys in flight
- [ ] You don't need to do anything to SP — PR #39 is HubApi only

### Merging
1. Go to https://github.com/FlatPlanet-Hub/platform-api/pull/39
2. Squash-and-merge into `main`
3. GitHub Actions will:
   - Run `dotnet restore / build / test`
   - Run `dotnet publish` with `--runtime linux-x64` (R2R kicks in here)
   - Deploy to `flatplanet-api` App Service

### What you'll see
- Azure stops the current container
- New container pulls image, mounts volumes
- New container starts — **this deploy itself doesn't benefit from R2R yet**, so warmup will be ~130s
- After warmup probe succeeds, Azure routes traffic to new container
- Total user-facing outage: ~3-4 minutes
- **Future** Azure restarts of HubApi will be ~30s instead of 130s (R2R is baked into the next image)

---

## Post-deploy verification

### 1. Clean startup (HubApi logs)

Pull logs:
```
az webapp log download --name flatplanet-api --resource-group FPPlatform --log-file hubapi-postdeploy.zip
```

Look for in the docker log:
```
Site startup probe succeeded after [N] seconds.
Site started.
```

### 2. Cache decorator is wired

After deploy, hit the hub or any endpoint that calls `ISecurityPlatformService`. In HubApi logs, you should NOT see SP being called for the same user twice within 5 minutes. The second call should hit the cache.

If logging is too sparse to confirm, you can verify by code path: any HTTP call to `flatplanet-security-api/api/v1/users/{userId}` should appear at most once per user per 5 minutes.

### 3. Stale-on-error works (this won't fire unless SP goes down)

If SP restarts later, the hub should keep working for active users. You'll see this log in HubApi:
```
warn: CachedSecurityPlatformService — SP unreachable for user [GUID] — serving stale access data.
```

---

## Today's incident — context if you need it

Hub felt slow on and off. Three diagnoses before finding it:

1. **Azure restart at 04:50 UTC** — SP + HubApi both moved to new VM. 4-5 min outage. Real but one-off.
2. **Cold DB pool theory** — investigated SP's `Minimum Pool Size=0` and PgBouncer cold start. Considered building a `BackgroundService` to warm 2 connections after `app.Run()` (different from the old removed pre-warm — this would run AFTER the warmup probe, so it doesn't block Azure). Shelved — not what caused today.
3. **Real cause: Finvoice agent DOSing HubApi.** Project `934e65c0-e369-4d45-a9a5-0b3cb64f2b1d` was running a Claude agent in a tight retry loop with broken SQL. 29 failed requests in 2 seconds at one point. Every retry held a DB connection while the exception unwound through middleware. The 20-slot pool saturated. Other users' requests queued, hit the 30s `Timeout`, frontend gave up at 25s and showed "server warming up". Stopping the agent fixed it.

### The exact SQL errors from Finvoice

- `column "satisfaction_score" is of type numeric but expression is of type text` — sending strings to a numeric column
- `column "is_new" does not exist` — invented a column name
- `duplicate key value violates unique constraint "tickets_pkey"` — generating duplicate primary keys

The agent skipped the schema read step (Step 1 in CLAUDE-local.md). The user has been told to fix on their side. If the agent restarts and the underlying query bug isn't fixed, the same pattern will recur.

### ApprovalFlow's separate (harmless) issue

Project `aa09bfd5-9e16-4597-a3cf-e4a9ce13f046` was firing requests then cancelling them mid-body. Kestrel `BadHttpRequestException: Unexpected end of request content`. Loud in logs but never reaches the DB, so no pool impact. Their frontend is probably firing requests in a `useEffect` without an AbortController cleanup.

---

## Open follow-ups after PR #39

| # | Item | Priority | Notes |
|---|---|---|---|
| 1 | Per-project rate limiting on `/query/read` and `/query/write` | **HIGH** | The actual fix for the Finvoice pattern. Add a rate limiter partitioned by `app_id` claim, e.g. 30 req/min. Prevents any single Claude session from DOSing the pool. |
| 2 | Catch `BadHttpRequestException` in `GlobalExceptionMiddleware` | LOW | Log at Debug, not Error. ApprovalFlow's cancellations shouldn't surface as unhandled exceptions. |
| 3 | Cache `GetAppIdBySlugAsync`, `GetAppMembersAsync`, `GetUserAsync` | MEDIUM | These still hit SP live. `AuthorizeAsync` is trickier — needs interface signature change to include `userId`. |
| 4 | Background DB pool warm-up (SP) | LOW | Only build this if post-restart slowness persists after PR #39. Currently shelved. |
| 5 | Fix `RegisterAppAsync` `base_url` bug | MEDIUM | Stores HubApi URL not project frontend URL. Blocks any automated CORS origin derivation. |
| 6 | FEAT-04 audit log for project events (HubApi) | MEDIUM | Pre-existing item |
| 7 | Fix `fp-development-hub` `github_branch` in DB | LOW | Currently `'master'`, should be `'main'` |
| 8 | Share frontend SP resilience guide with frontend team | LOW | At `docs/frontend-sp-resilience-guide.md` in HubApi repo. Confirm 502 body string first. |

---

## Quick reference

### Repos and paths
- SP repo: `C:\Users\Erick\source\ClaudeCode\flatplanet-security-platform` — https://github.com/FlatPlanet-Hub/flatplanet-security-platform
- HubApi repo: `C:\Users\Erick\source\ClaudeCode\FlatPlanetHubApi` — https://github.com/FlatPlanet-Hub/platform-api

### Azure
- Resource group: `FPPlatform`
- SP: `flatplanet-security-api` (`flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`)
- HubApi: `flatplanet-api` (`flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`)
- Tier: B1 (single vCPU, AlwaysOn enabled — no idle shutdown)
- Region: southeastasia

### Log commands
```bash
# SP
az webapp log download --name flatplanet-security-api --resource-group FPPlatform --log-file sp.zip

# HubApi
az webapp log download --name flatplanet-api --resource-group FPPlatform --log-file hub.zip

# Extract
unzip -o sp.zip -d sp-logs

# Find recent docker logs
find sp-logs/LogFiles -name "*docker*.log" -printf "%T@ %p\n" | sort -rn | head -5
```

### Key files in PR #39
- `FlatPlanet.Platform.API/FlatPlanet.Platform.API.csproj` — R2R
- `FlatPlanet.Platform.Infrastructure/ExternalServices/CachedSecurityPlatformService.cs` — cache
- `FlatPlanet.Platform.Infrastructure/Extensions/InfrastructureExtensions.cs` — DI wiring

---

## If something goes wrong during deploy

### New container fails to start (logs show crash loop)
1. Check the docker log: `find hubapi-logs/LogFiles -name "*docker*" | sort -rn | head -3`
2. If warmup probe keeps failing past 230s → roll back via Azure Portal → Deployment Center → previous deployment → Redeploy
3. The PR is the suspect: cache decorator constructor injection might be misconfigured. Read `CachedSecurityPlatformService.cs` constructor and verify `SecurityPlatformService inner` resolves.

### Container starts but requests 500
1. Check `default_docker.log` for unhandled exceptions
2. If you see `Unable to resolve service for type 'FlatPlanet...SecurityPlatformService'` → the DI wiring change is broken, roll back
3. Most likely fix: ensure `services.AddScoped<SecurityPlatformService>();` is **before** the `ISecurityPlatformService` registration in `InfrastructureExtensions.cs`

### SP suddenly slow after HubApi deploy
This shouldn't happen — HubApi deploying shouldn't affect SP. But if it does:
1. Check if SP's connection string changed (it shouldn't have — different repo)
2. Check if Azure moved SP to a new host concurrently (timestamps in SP docker log)
3. Most likely just coincidence — wait 5 min for SP's pool to warm

### Rollback procedure
1. Azure Portal → `flatplanet-api` → Deployment Center → Logs
2. Find previous successful deployment
3. Click the three-dot menu → Redeploy
4. Confirms — Azure swaps containers within ~3-4 min

---

*Last updated: 2026-05-15 PH evening | Session: Erick + Claude (Sonnet 4.6) — full diagnosis is in `CONVERSATION-LOG.md`*
