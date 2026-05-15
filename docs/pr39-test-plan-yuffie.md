# PR #39 — Yuffie's Test Plan

> **From:** Lightning (Reviewer)
> **For:** Yuffie (Integration Tester)
> **PR:** [#39 — fix: SP cache resilience — wire decorator + stale-on-error fallback](https://github.com/FlatPlanet-Hub/platform-api/pull/39)
> **What changed:** HubApi now caches Security Platform's `GetUserAppAccessAsync` for 5 min, and serves last-known-good data for up to 2 hours if SP is unreachable. Cache invalidates immediately on role grant/change/revoke. Also adds `PublishReadyToRun` to the API csproj.

---

## Test environment

- **HubApi base:** `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`
- **SP base:** `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`
- **You'll need:**
  - A valid Security Platform JWT (login via SP `/api/v1/auth/login`)
  - A test user that has at least one project assignment (any non-admin role works)
  - Azure CLI access for downloading logs

---

## Pre-flight (wait before testing)

GitHub Actions deploy must finish first. Confirm before starting:

```bash
gh run list --limit 1
```

Expected: latest run shows `completed success` for the `fix: SP cache resilience...` commit.

Then confirm HubApi is healthy:
```bash
curl -m 10 -w "\nHTTP %{http_code} in %{time_total}s\n" https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/health
```

Expected: `HTTP 200` in under 2 seconds.

---

## Test 1 — Smoke test (basic functionality not broken)

**Goal:** Confirm the new DI wiring doesn't break basic operations.

**Steps:**
1. Log in to the hub frontend at `https://fpdevelopmenthub.netlify.app`
2. Verify you land on the project list page (no logout loop, no 401, no 500)
3. Click into any project
4. Open the member list for that project

**Expected:** All three pages load successfully. Member list shows expected users.

**Failure mode if cache decorator is broken:** App fails to start (503/504 on every request), or every authenticated request returns 500. Report immediately if you see this.

---

## Test 2 — Cache is actually active (steady-state)

**Goal:** Confirm `GetUserAppAccessAsync` is hitting the cache, not SP, on repeated calls.

**Steps:**
1. Open browser dev tools, Network tab
2. Reload the hub project list 3 times within 60 seconds, observing each request to `/api/projects`
3. Have Cloud or someone with Azure access download HubApi logs:
   ```bash
   az webapp log download --name flatplanet-api --resource-group FPPlatform --log-file pr39-test2.zip
   ```
4. Open the most recent `*_default_docker.log`
5. Search for outbound calls to `flatplanet-security-api`

**Expected:**
- First `/api/projects` call → likely 1 outbound call to SP (cache miss after deploy)
- Second and third calls within 5 minutes → **zero new SP calls** for the same user (cache hit)
- Cache key pattern in logs would be something like `sp_access_fresh_{userId}`

**Failure mode if cache isn't wired:** Every single request to `/api/projects` produces a fresh SP call. That means the decorator isn't in the DI chain. **CRITICAL — would mean PR #39 had zero effect.**

---

## Test 3 — Cache invalidation on role change

**Goal:** Confirm explicit role changes immediately invalidate the cache (no stale permissions).

**Steps:**
1. Pick a test user (User A) who currently has `developer` role on a test project
2. As User A, log into the hub and load the project — note the role shown
3. As an admin, change User A's role on that project to `viewer` via the hub member management UI (which calls `ChangeRoleAsync`)
4. **Immediately** (within 5 seconds) — as User A, reload the project page

**Expected:** User A's role now shows as `viewer`. The cache must have been invalidated on the role change — otherwise they'd still see `developer` for up to 5 more minutes.

**Failure mode if invalidation is broken:** User A still shows `developer` for several minutes after the change. Means `_cache.Remove` isn't running, or the cache key doesn't match.

**Important:** Test all three change types if you have time:
- `GrantRoleAsync` — grant a new role to a user without one
- `ChangeRoleAsync` — change existing role (above)
- `RevokeRoleAsync` — remove a role completely

---

## Test 4 — Stale-on-error during SP outage

**Goal:** Confirm hub keeps working for active users during a brief SP outage.

> ⚠️ **Coordinate with Erick before doing this** — it requires intentionally stopping SP, which affects all platform apps. Don't run this in business hours unannounced.

**Steps:**
1. As a test user, log into the hub and load the project list. Note which projects you see. This populates the cache.
2. Stop SP:
   ```bash
   az webapp stop --name flatplanet-security-api --resource-group FPPlatform
   ```
3. Wait 30 seconds. SP is now unreachable.
4. Reload the hub project list as the same user.
5. **Watch what happens.**
6. Within 5 minutes of step 1, restart SP:
   ```bash
   az webapp start --name flatplanet-security-api --resource-group FPPlatform
   ```

**Expected (the new behavior):**
- At step 4, the project list **still loads** with the same projects you saw in step 1
- HubApi logs should show a warning like:
  ```
  warn: CachedSecurityPlatformService — SP unreachable for user {GUID} — serving stale access data.
  ```
- User experience: hub works normally despite SP being down

**Expected for a NEW user who logs in during the SP outage:** Hard failure — they have no cached data yet. This is correct behavior, not a bug.

**Failure mode if stale-on-error is broken:** Step 4 returns a 502 with `Security Platform error: …`. Means the `catch` block isn't returning stale data. Compare logs against the code in `CachedSecurityPlatformService.cs` to debug.

---

## Test 5 — Startup time (R2R verification)

**Goal:** Confirm `PublishReadyToRun` is reducing cold startup time on future restarts.

**Steps:**
1. Restart HubApi explicitly:
   ```bash
   az webapp restart --name flatplanet-api --resource-group FPPlatform
   ```
2. Note the timestamp.
3. Poll `/health` every 10 seconds until it returns 200:
   ```bash
   for i in {1..30}; do
     code=$(curl -m 8 -s -o /dev/null -w "%{http_code}" https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net/health)
     echo "[$(date +%H:%M:%S)] HTTP $code"
     [[ "$code" == "200" ]] && break
     sleep 10
   done
   ```
4. After it comes back, download the docker log:
   ```bash
   az webapp log download --name flatplanet-api --resource-group FPPlatform --log-file pr39-test5.zip
   ```
5. In `*_docker.log` (not the default_docker one), find the line:
   ```
   Site startup probe succeeded after N seconds.
   ```

**Expected:** N should be **significantly less than 130 seconds** (yesterday's number on B1 without R2R). On B3 with R2R, expect something in the **30-60 second** range.

**Failure mode:** Startup takes > 100 seconds. Means R2R didn't get applied. Check the published artifact's binaries on Azure — should have `.ni.dll` files alongside the regular `.dll`s if R2R is working.

---

## Test 6 — No regression on heartbeat / refresh

**Goal:** Confirm the cache changes haven't broken the auth flow.

**Steps:**
1. Log in to the hub
2. Leave the page open for 5 minutes (heartbeat should fire periodically)
3. Click around — load projects, switch projects, view members
4. Wait until your access token gets close to expiry (4 hours), or force a refresh by clearing the access token cookie and reloading

**Expected:** Heartbeats work silently in the background, the page never spontaneously redirects to login. Refresh works seamlessly.

**Failure mode:** Random 401s, forced re-login, or session drops. Could indicate the cache is interfering with session validation somehow (but shouldn't — session validation is in SP, not in the cached methods).

---

## What to report back

For each test, write back with:
- ✅ Pass / ❌ Fail / ⚠️ Unexpected
- For passes: confirmation + any relevant timings (response times, startup duration)
- For fails: exact step that failed, what you saw vs. expected, and log snippet if available

**Priority order for failures to escalate:**
1. **Test 1 fail** (basic functionality broken) — **CRITICAL**, page Cloud immediately for rollback
2. **Test 2 fail** (cache not active) — **HIGH**, the PR had zero effect, needs investigation
3. **Test 3 fail** (invalidation broken) — **HIGH**, stale permissions risk
4. **Test 4 fail** (no stale-on-error) — **MEDIUM**, defensive feature didn't activate
5. **Test 5 fail** (R2R not working) — **MEDIUM**, future restarts still slow
6. **Test 6 fail** (auth regression) — **CRITICAL** if it logs users out, page Cloud immediately

---

## When to call it green

PR #39 passes when:
- ✅ Test 1, 2, 3, 6 all pass
- ✅ Test 5 passes OR is deferred (R2R is a "next restart" benefit, not blocking)
- ✅ Test 4 passes if you can coordinate the controlled outage; otherwise defer until natural restart event

If Tests 1-3 and 6 pass, the PR is safe to call done. Tests 4 and 5 verify defensive behavior that only matters during incidents.
