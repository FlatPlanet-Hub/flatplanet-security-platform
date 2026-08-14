-- =============================================================================
-- ROLLBACK — per-app session timeouts (migration V30)
-- =============================================================================
-- Four steps, least destructive first. STEPs 0 and 1 are active; STEPs 2 and 3
-- are commented out — uncomment only the ones you actually need.
--
--   STEP 0  Inspect. Read-only.
--   STEP 1  Clear the per-app overrides.        Safe. No user is logged out.
--   STEP 2  End sessions already issued long.   LOGS THOSE USERS OUT.
--   STEP 3  Drop the columns entirely.          Full schema revert of V30.
--
-- ── Read this before choosing ────────────────────────────────────────────────
-- The timeouts are stamped onto the sessions row at login, not read from the app
-- at request time. So STEP 1 alone changes NOTHING for sessions that already
-- exist — a session already carrying a 365-day expires_at keeps it until that
-- date, even after the override is cleared, the code is reverted, or the columns
-- are dropped. If your reason for rolling back is that long-lived sessions were
-- issued that should not have been, you MUST run STEP 2. Reverting the deploy is
-- not sufficient.
--
-- Reverting the application code alone is safe and needs none of this: with the
-- columns present but the code gone, nothing reads them.
--
-- ── Why the predicate reads security_config ──────────────────────────────────
-- "A session that was issued a longer-than-normal lifetime" is defined against
-- the CURRENT platform default, read live below. Do not substitute a literal:
-- the platform default has changed before (V20 proposed 480 -> 10080), and a
-- stale literal that is lower than the live value makes these predicates match
-- EVERY ACTIVE SESSION ON THE PLATFORM. STEP 2 would then log out every user of
-- every app. The count gate in STEP 2 exists to catch exactly that mistake.
-- =============================================================================


-- ── STEP 0 — inspect (read-only) ─────────────────────────────────────────────

-- 0a. Which apps currently carry an override.
SELECT slug, name, status,
       session_absolute_timeout_minutes,
       session_idle_timeout_minutes
FROM apps
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes IS NOT NULL;

-- 0b. The live platform default these predicates are measured against.
SELECT config_key, config_value
FROM security_config
WHERE config_key IN ('session_absolute_timeout_minutes', 'session_idle_timeout_minutes');

-- 0c. Active sessions stamped with a longer-than-platform-default lifetime.
--     Sanity-check this list before running STEP 2 — it is the same predicate.
SELECT s.id, s.user_id, u.email, a.slug AS app_slug,
       s.started_at, s.expires_at, s.idle_timeout_minutes,
       EXTRACT(EPOCH FROM (s.expires_at - s.started_at)) / 60 AS stamped_minutes
FROM sessions s
JOIN users u ON u.id = s.user_id
LEFT JOIN apps a ON a.id = s.app_id
WHERE s.is_active = true
  AND s.expires_at > s.started_at + (
        (SELECT config_value::int FROM security_config
         WHERE config_key = 'session_absolute_timeout_minutes') * INTERVAL '1 minute')
ORDER BY s.expires_at DESC;


-- ── STEP 1 — clear the overrides (safe; affects new sessions only) ───────────

BEGIN;

UPDATE apps
SET session_absolute_timeout_minutes = NULL,
    session_idle_timeout_minutes     = NULL
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes     IS NOT NULL;

-- Expect zero rows.
SELECT COUNT(*) AS remaining_overrides
FROM apps
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes     IS NOT NULL;

COMMIT;


-- ── STEP 2 — end sessions already issued a long lifetime ─────────────────────
-- UNCOMMENT ONLY IF NEEDED. This logs those users out on their next request and
-- will black out the dashboard until someone signs it in again.
--
-- Run the count gate FIRST and read the number. It should be roughly the number
-- of long-lived clients you have (single digits for one dashboard). If it is in
-- the hundreds or thousands, STOP — the predicate is matching ordinary sessions
-- and you are about to log out the platform.
--
-- BEGIN;
--
-- SELECT COUNT(*) AS sessions_to_be_ended
-- FROM sessions
-- WHERE is_active = true
--   AND expires_at > started_at + (
--         (SELECT config_value::int FROM security_config
--          WHERE config_key = 'session_absolute_timeout_minutes') * INTERVAL '1 minute');
--
-- -- Revoke tokens for ALL long-lifetime sessions, not just active ones. A session
-- -- evicted by max_concurrent_sessions is is_active = false but keeps a live refresh
-- -- token, and RefreshAsync reactivates it — so an inactive-but-unrevoked session
-- -- would come straight back on the next refresh, carrying its original expires_at.
-- UPDATE refresh_tokens
-- SET revoked = true, revoked_at = now(), revoked_reason = 'session_policy_rollback'
-- WHERE revoked = false
--   AND session_id IN (
--       SELECT id FROM sessions
--       WHERE expires_at > started_at + (
--             (SELECT config_value::int FROM security_config
--              WHERE config_key = 'session_absolute_timeout_minutes') * INTERVAL '1 minute')
--   );
--
-- UPDATE sessions
-- SET is_active = false, ended_reason = 'session_policy_rollback'
-- WHERE is_active = true
--   AND expires_at > started_at + (
--         (SELECT config_value::int FROM security_config
--          WHERE config_key = 'session_absolute_timeout_minutes') * INTERVAL '1 minute');
--
-- COMMIT;


-- ── STEP 3 — drop the columns (full V30 revert) ──────────────────────────────
-- UNCOMMENT ONLY IF NEEDED. Do this only after the application code that reads
-- these columns has been rolled back — the running code selects them via
-- SELECT *, which tolerates their absence, but leaving them is harmless.
-- Dropping discards every override permanently.
--
-- BEGIN;
--
-- ALTER TABLE apps
--     DROP CONSTRAINT IF EXISTS apps_session_absolute_timeout_range,
--     DROP CONSTRAINT IF EXISTS apps_session_idle_timeout_range;
--
-- ALTER TABLE apps
--     DROP COLUMN IF EXISTS session_absolute_timeout_minutes,
--     DROP COLUMN IF EXISTS session_idle_timeout_minutes;
--
-- COMMIT;
--
-- If Flyway has already recorded V30, also remove its history row so the chain
-- can be re-run cleanly:
--   DELETE FROM flyway_schema_history WHERE version = '30';


-- =============================================================================
-- NOT rolled back by this script
-- =============================================================================
-- sessions.app_id is now populated where it used to be NULL. It is left alone
-- deliberately: the column predates this change (V1), nothing in SP reads it,
-- and a value is written only where the user's access to that app was verified
-- (refused claims leave it NULL). To blank it anyway:
--   UPDATE sessions SET app_id = NULL WHERE app_id IS NOT NULL;
--
-- auth_audit_log rows of type 'session_policy_denied' are immutable by design
-- (V1 revokes UPDATE and DELETE) and are not touched.
