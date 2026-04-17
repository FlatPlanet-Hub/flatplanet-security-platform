# Core Build Analysis — FlatPlanet Security Platform

**Compared against:** Schema v0.1 (docx) + Feature.md + actual codebase
**Date: 2026-03-25**
**Vision:** Microsoft Entra equivalent for all FlatPlanet apps

---

## What the Schema Doc v0.1 Actually Said

The schema doc was written March 14, 2026 and is explicitly **"Work in Progress"**. It defines two layers:

**Agreed (Session 2) — 7 tables:**
1. companies
2. users
3. apps
4. resource_types
5. resources
6. roles
7. user_app_roles

**Explicitly deferred to "next session" — never agreed:**
| Table Group | Purpose |
|------------|---------|
| Policy layer | Resource-level session config — idle timeout, absolute max, 2FA requirements, allowed hours. Per-resource, key-value design |
| Verification events | Identity verification records — who verified, who was verified, method, recording, outcome |
| Auth audit log | Login, logout, failure, 2FA, session expiry, password reset events — immutable |
| Attendance events | Every staff login for future payroll — staff_id, timestamp, Sydney timezone |
| Platform config | Global platform settings, key-value, extensible |

---

## What Feature.md Did

Feature.md correctly recognized that the 7 agreed tables are not enough to run a secure auth service. It added 7 more tables beyond the schema doc, pulling forward some deferred items:

**Added (not in schema v0.1):**
8. permissions — granular RBAC beyond roles
9. role_permissions — role → permission mapping
10. sessions — login tracking, concurrent session limits
11. refresh_tokens — JWT rotation, stored as SHA256 hash
12. auth_audit_log — pulled forward from "pending" (correct decision)
13. login_attempts — account lockout + IP rate limiting
14. security_config — configurable parameters (subset of deferred "Platform config")

**This was the right call.** You can't run authentication without sessions, refresh tokens, and an audit log. Feature.md correctly escalated these from "next session" to "required for core."

---

## What Was Actually Built vs Feature.md

The codebase implements everything Feature.md specified. All 14 tables have entities, repositories, services, and controllers. The full list of bugs and gaps is in `review.md` and `missing-specs.md`. Summary:

| Feature.md Item | Status |
|----------------|--------|
| Login via Supabase Auth | Built — with bugs (no company check, no per-email rate limit) |
| JWT + refresh token | Built — broken (session_id missing from JWT) |
| Session management | Built — idle timeout not enforced |
| Authorization check | Built — IDOR vulnerability |
| User context endpoint | Built — correct |
| Admin CRUD (all entities) | Built — no RBAC protecting the endpoints |
| Audit log | Built — raw string literals, JSON injection risk |
| GDPR export/anonymize | Built — export has no self-check |
| Offboarding | Built — correct |
| Security config | Built — correct |

The code matches the spec structurally. The issues are in correctness and security, not missing features from Feature.md.

---

## What's Missing vs the "Microsoft Entra" Vision

This is the real gap analysis. Entra is a complete identity platform. Here is what the core does and does not have relative to that scope.

### What the core DOES cover (built correctly)

- **Authentication** — password-based login via Supabase Auth, JWT issuance
- **Session management** — concurrent limits, token rotation, logout
- **RBAC** — per-app roles, granular permissions, role-permission assignments
- **Resource protection** — registering protected resources with type hierarchy
- **Multi-tenancy** — companies own apps, users belong to companies
- **Audit trail** — append-only log of auth events
- **Account security** — lockout, rate limiting, configurable security parameters
- **Compliance basics** — GDPR export, anonymization, user offboarding
- **Security headers** — HSTS, CSP, X-Frame-Options
- **Health endpoint** — for infrastructure monitoring

---

### What's MISSING vs Microsoft Entra (not in this build — future scope)

**1. MFA / 2FA — not built**
Schema doc deferred it. Feature.md lists `user_mfa` as "tables not in this build." No second factor of any kind exists. For a platform connecting to payroll, attendance, and access control systems, this is a significant gap. Any compromised password = full access.

**2. OAuth 2.0 / SSO — not built**
No social login, no Google/Microsoft SSO, no OAuth authorization code flow. Feature.md lists `oauth_providers + user_oauth_links` as future. Users can only log in with email + password. FlatPlanet staff who use Google Workspace daily will have a separate credential for this platform.

**3. Machine-to-machine tokens — not built**
No API tokens for service accounts. Feature.md lists `api_tokens` as future. When Tala or Dashboard Hub needs to call the Security API server-side (not on behalf of a user), there's no mechanism for it. They'd have to use a real user account.

**4. Conditional access / policy engine — not built**
Schema doc deferred this as "Policy layer — next session." No concept of:
- "This resource requires MFA regardless of role"
- "Access only during business hours (Sydney time)"
- "This role can only access from these IP ranges"
- "Temporary elevated access with approval"

This is core to Entra's value. Without it, you have RBAC but no situational control.

**5. App suspension cascade — missing**
When a company is suspended, the spec says users should be suspended too (partially tracked in missing-specs.md). But there's also no cascade when an **app** is suspended — existing user sessions for that app remain valid. A suspended app's users can still get `user-context` and pass authorization checks.

**6. User self-registration — not built**
There is no `POST /api/v1/users` endpoint. Users can only exist if a Supabase Auth account is created separately and then linked. No invitation flow, no self-sign-up, no email verification webhook. This is fine for an internal platform where HR creates accounts — but it's a manual process.

**7. Password reset / email verification hooks — not built**
Supabase handles these at the Auth layer. But the Security Platform has no webhooks or callbacks from Supabase when a password changes. If a user resets their password, their existing sessions and refresh tokens are not revoked by this platform. A compromised account that gets a password reset still has active tokens until they expire naturally.

**8. Cross-company access — not modeled**
The schema ties users to one company. There's no way to give an external auditor or contractor from Company B access to Company A's apps. Entra's B2B guest access has no equivalent here. For FlatPlanet's current structure this is likely fine — but worth knowing the constraint exists.

**9. Verification events — not built**
Schema doc deferred this. No identity verification records — no log of who verified a user's identity, by what method, with what outcome. The schema doc mentioned this in the context of recording verification calls.

**10. Attendance events — not built**
Schema doc deferred this. Every staff login was supposed to generate an attendance event for payroll integration. This is explicitly future scope but it means the payroll integration can't use this platform yet.

---

## Honest Assessment: Did We Build the Core?

**Yes — for an authentication and authorization core. No — for a full identity platform.**

Here is the breakdown:

| Layer | Status |
|-------|--------|
| Registry (companies, users, apps, resources, roles) | Complete |
| Identity (login, sessions, tokens, audit) | Built — several bugs to fix |
| Authorization (RBAC, permissions, access checks) | Built — IDOR to fix, RBAC on own endpoints missing |
| Compliance (export, anonymize, offboard, audit retention) | Built — minor gaps |
| Policy (conditional access, 2FA requirements, time-based) | Not started — was deferred in schema doc |
| Verification | Not started — was deferred in schema doc |
| SSO / OAuth | Not started — marked out of scope in Feature.md |
| Machine auth (API tokens) | Not started — marked out of scope in Feature.md |
| Payroll integration (attendance events) | Not started — was deferred in schema doc |

The core is a working RBAC identity service. It is not yet the full Entra equivalent. The schema doc itself acknowledged the deferred layers — they were never in scope for this build. What was in scope is done, with bugs that need fixing before it can be used in production.

---

## Recommended Next Steps (After Bug Fixes)

Fix the issues in `review.md` first. Then the logical next phases are:

1. **RBAC on the platform's own endpoints** — define `platform_owner` and `app_admin` platform roles, enforce them on admin endpoints. This is the most urgent missing security control.
2. **MFA** — add `user_mfa` table, TOTP enrolment endpoint, verification on login. Required before connecting payroll or attendance systems.
3. **Session idle timeout enforcement** — middleware that checks `last_active_at` on every request and kills expired sessions. Currently the config exists but nothing enforces it.
4. **Conditional access / policy layer** — agree the `resource_policies` table design and implement it. This is what elevates the platform from "RBAC" to "Entra-like."
5. **App suspension cascade** — when an app is deactivated, invalidate all sessions for that app.
6. **Password reset webhook from Supabase** — revoke sessions on password change.
