# Runbook — Per-Service Tokens

**Audience:** platform owners (Erick, JL, Chris)
**Last updated:** 2026-06-11

This is the operational guide for managing per-service tokens — the tokens that trusted backends (HubApi today, more later) use to authenticate to SP.

---

## Token format

```
fps_<service-name>_<43-char base64url>
```

- `fps` = FlatPlanet Service prefix (greppable, scannable)
- `<service-name>` = the calling service slug, e.g. `hub-api`
- 32 random bytes encoded as URL-safe base64 (no padding)

**The plaintext is shown exactly once at mint time and never again.** Store it immediately in the consuming service's secret store (Azure App Service env var, Key Vault, etc.).

The database stores only the SHA-256 hash. Losing the plaintext = mint a new one + revoke the old.

---

## Scopes

| Scope | Grants |
|---|---|
| `bootstrap` | Wildcard — matches any `RequireScope` check. Also receives the legacy `platform_owner` + `app_admin` roles so existing admin endpoints work. Use for new services during onboarding; narrow later. |
| `users:read` | Read user records |
| `users:write` | Create / update users |
| `apps:read` / `apps:write` / `apps:admin` | Read / mutate / delete apps |
| `roles:read` / `roles:write` | Read / mutate roles + role-permission assignments |
| `permissions:write` | Create / update permissions |
| `grants:read` / `grants:write` | Read / grant / revoke per-app role grants |
| `companies:read` / `companies:admin` | Read / mutate companies |
| `authorize` | Call `/api/v1/authorize` |
| `audit:read` | Read audit log |

Scopes are case-insensitive in checks. Always store lowercase.

---

## Common operations

All commands assume you have a platform-owner JWT in `$JWT`:

```bash
JWT=$(curl -s -X POST "$SP/api/v1/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"you@flatplanet.com","password":"...","appSlug":"security-platform"}' \
  | jq -r '.data.accessToken')
SP=https://flatplanet-security-api-d5cgdyhmgxcebyak.southeastasia-01.azurewebsites.net
```

### Mint a new token

```bash
curl -X POST "$SP/api/v1/admin/service-tokens" \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d '{
    "serviceName": "hub-api",
    "scopes": ["bootstrap"],
    "description": "HubApi server-to-server auth"
  }'
```

Response includes a `token` field containing the **plaintext** — save it now. The same response also returns the token's `id` for later management.

### List all tokens

```bash
curl "$SP/api/v1/admin/service-tokens" -H "Authorization: Bearer $JWT"
```

Returns each token's metadata (id, service name, scopes, status, created/used dates). No plaintext.

### Narrow scopes

```bash
curl -X PUT "$SP/api/v1/admin/service-tokens/<id>/scopes" \
  -H "Authorization: Bearer $JWT" -H "Content-Type: application/json" \
  -d '{"scopes": ["users:read", "apps:write", "grants:write"]}'
```

Changes take effect immediately — the validator cache for this token is dropped automatically.

### Revoke

```bash
curl -X DELETE "$SP/api/v1/admin/service-tokens/<id>" \
  -H "Authorization: Bearer $JWT"
```

Subsequent calls with that token return `401` after the 60-second validator cache expires.

### Urgent revocation (cache flush)

If the 60-second cache TTL is too slow (e.g. token leaked to a public channel):

```bash
curl -X DELETE "$SP/api/v1/admin/service-tokens/<id>" -H "Authorization: Bearer $JWT"
curl -X POST   "$SP/api/v1/admin/service-tokens/<id>/flush-cache" -H "Authorization: Bearer $JWT"
```

Cache is dropped immediately.

---

## Rotation procedure (zero-downtime via dual-token window)

Single-token services (today's pattern) can't rotate without a 60-90 second outage window. **Per-service tokens have no such limit** — both old and new can be active concurrently.

To rotate HubApi's token (example):

1. **Mint a new token** for the same service. SP allows only one *active* token per service-name, so first:
   - Revoke the existing token (HubApi keeps working — the old token is in HubApi's env var and the legacy single-token fallback still validates it until Phase 4 completes; OR if we're past Phase 4, you need the dual-active extension)

2. **Or:** add support for short-lived dual-active tokens (future enhancement — not in Phase 1).

For now, rotation = revoke + mint + update consumer env var. Schedule during a low-traffic window.

---

## Audit log

Service-token events live in `auth_audit_log` (same table as user events). Event types:

| Type | Trigger |
|---|---|
| `service_token_minted` | Admin minted a new token |
| `service_token_revoked` | Admin revoked a token |
| `service_token_scopes_changed` | Admin updated a token's scopes |
| `service_token_used` | First use per token per UTC day (sampled — not every request) |
| `service_token_scope_denied` | A request was rejected for missing scope |
| `service_token_cache_flushed` | Admin force-flushed the validator cache |

Query:

```bash
curl "$SP/api/v1/admin/audit-log?eventType=service_token_minted" -H "Authorization: Bearer $JWT"
```

---

## Forensics checklist — "did this token get used today?"

```bash
# 1. Find the token id by service name
curl "$SP/api/v1/admin/service-tokens" -H "Authorization: Bearer $JWT" \
  | jq '.data[] | select(.serviceName=="hub-api") | {id,lastUsedAt,status}'

# 2. Check audit log entries for that token
curl "$SP/api/v1/admin/audit-log?eventType=service_token_used&from=2026-06-11" -H "Authorization: Bearer $JWT"

# 3. Any scope-denied events (potential attack signal)?
curl "$SP/api/v1/admin/audit-log?eventType=service_token_scope_denied" -H "Authorization: Bearer $JWT"
```

---

## When to mint a new token

| Trigger | Action |
|---|---|
| **Onboarding a new backend** (e.g. ApprovalFlow needs to call SP) | Mint with `bootstrap` scope; narrow after the integration is verified |
| **Rotation (routine)** | Once per 90 days |
| **Rotation (suspected leak)** | Immediately. Revoke + flush cache + mint new |
| **Rotation (after a team member leaves)** | Immediately for any token they could have accessed |

---

## When NOT to use service tokens

- **End-user authentication** — use the SP login flow (`POST /api/v1/auth/login`) and JWT bearer.
- **Per-project Claude credentials** — use the per-project Platform API token in `CLAUDE-local.md`. Service tokens are for backends, not for Claude or Hub.
- **CLI scripts run locally by admins** — use your own user JWT, not a service token.

Service tokens are for **machine-to-machine, no human in the loop**.
