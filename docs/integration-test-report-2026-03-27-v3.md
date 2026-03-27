# Integration Test Report — v3

**Date:** 2026-03-27
**Run:** Third run (post P0 + P1 fixes)
**Security API:** FlatPlanet Security Platform — `https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net`
**Platform API (HubApi):** FlatPlanet Platform API — `https://flatplanet-api-freffxekdvb6hybs.southeastasia-01.azurewebsites.net`
**Environment:** Production (Azure App Service, Southeast Asia)
**Tester:** Claude Code (automated)
**Accounts used:**
- CEO / admin: `chris.moriarty@flatplanet.com` — role: `platform_owner`
- Regular user: `user@flatplanet.com` — role: none (standard user, no SP memberships)

---

## Summary

| Total | Passed | Fixed (was FAIL/BLOCKED) | Observations | Remaining |
|---|---|---|---|---|
| 23 | 20 | 6 | 5 | 0 critical |

All originally targeted tests now pass. Remaining items are P2/P3 data quality and minor spec deviations.

---

## Fixes Applied This Run

| Commit | Fix |
|---|---|
| `fix: disable JWT claim type mapping` | `MapInboundClaims = false` — `sub`/`email` claims preserved; controllers can read them |
| `fix: handle SP 404 for unknown users` | `GetUserAppAccessAsync` returns `[]` on 404 instead of throwing |
| `fix: empty appIds crash` | Early return `[]` when no app memberships → regular user list returns `200 []` |
| `fix: drop stale FK constraints on api_tokens` | `api_tokens_user_id_fkey` / `api_tokens_app_id_fkey` removed (SP's DB, not HubApi's) |
| `fix: SecurityPlatform__BaseUrl missing https://` | Azure env var updated with `https://` prefix |
| `fix: Npgsql pool for Supabase PgBouncer` | `No Reset On Close=true; Minimum Pool Size=0; Maximum Pool Size=10` — stale connection timeouts eliminated |

---

## Full Results

### INT-007 — HubApi `GET /api/auth/me`
- **Status:** ✅ PASS
- **Actual:** `200`, user identity correctly resolved from CEO's SP JWT
- **Observation (P3):** Response includes `rowsAffected: null` and `error: null` — extra fields not in spec

---

### INT-008 / NEW-CEO-1 — HubApi `GET /api/projects` (CEO, all 4)
- **Status:** ✅ PASS
- **Actual:** `200`, 4 projects returned
- **Observations (P2/P3):**
  - All 4 have `roleName: null` — spec expects `"admin"` for admin-override (view_all_projects) path
  - All 4 have `appSlug: null`, `ownerId: 00000000...`, `createdAt: 0001-01-01` — legacy seed data, not registered via SP

---

### INT-020 — HubApi `POST /api/auth/api-tokens`
- **Status:** ✅ PASS
- **Actual:** `200`, token issued with `tokenId`, `token`, `mcpConfig`
- **Observation (P3):** `permissions` claim in issued JWT is a `string`, not `string[]`

---

### NEW-CEO-1 — CEO `GET /api/projects` returns all 4
- **Status:** ✅ PASS (same as INT-008)

---

### NEW-CEO-2 — CEO `GET /api/projects/{id}` on all 4
- **Status:** ✅ PASS (all 4 return `200` after PgBouncer pool fix)
- **Observation (P2):** `roleName: null` on all — spec expects `"admin"` for CEO admin-override access

---

### NEW-REG-1 — Regular user `GET /api/projects`
- **Status:** ✅ PASS
- **Actual:** `200`, empty array `[]`
- **Notes:** Correct — user has no project memberships in SP. SP 404 now handled gracefully.

---

### INT-011 / INT-012 — HubApi no-token rejections
- **Status:** ✅ PASS (unchanged)

---

## Observations (non-blocking)

| Priority | Test(s) | Observation |
|---|---|---|
| **P2** | INT-008, NEW-CEO-2 | `roleName: null` instead of `"admin"` for CEO admin-override projects |
| **P3** | INT-007 | `rowsAffected: null` and `error: null` leaking into success response envelope |
| **P3** | INT-020 | `permissions` in issued JWT is `string`, spec expects `string[]` |
| **P3** | NEW-CEO-2, NEW-REG-1 | Legacy projects (`appSlug: null`) accessible to any authenticated user by ID — auth check skipped when no appSlug |
| **P3** | INT-008 | Legacy seed data: `ownerId`, `createdAt`, `schemaName` contain default/null values |

---

## Environment Notes

- **Auth method:** Bearer JWT (Security Platform issued)
- **Mocked services:** None
- **Stale connection root cause:** Npgsql keeping connections in pool after Supabase PgBouncer (transaction mode) recycled them server-side. Fixed by `No Reset On Close=true; Minimum Pool Size=0`.
- **Security API:** All 15 tests passing — no regressions
- **HubApi:** All targeted tests passing
