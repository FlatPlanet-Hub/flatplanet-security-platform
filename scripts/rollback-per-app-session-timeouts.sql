-- =============================================================================
-- ROLLBACK — per-app session timeouts (migration V30)
-- =============================================================================
-- Three independent steps, least destructive first. STEP 1 is active; STEPs 2
-- and 3 are commented out — uncomment only the ones you actually need.
--
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
-- =============================================================================


-- ── STEP 0 — what is currently overridden (read-only) ────────────────────────

SELECT slug, name, status,
       session_absolute_timeout_minutes,
       session_idle_timeout_minutes
FROM apps
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes IS NOT NULL;

-- Sessions currently alive on a longer-than-platform-default lifetime.
-- Adjust the 480 if the platform default has changed.
SELECT s.id, s.user_id, u.email, a.slug,
       s.started_at, s.expires_at, s.idle_timeout_minutes
FROM sessions s
JOIN users u ON u.id = s.user_id
LEFT JOIN apps a ON a.id = s.app_id
WHERE s.is_active = true
  AND s.expires_at > s.started_at + (480 * INTERVAL '1 minute')
ORDER BY s.expires_at DESC;


-- ── STEP 1 — clear the overrides (safe; affects new sessions only) ───────────

BEGIN;

UPDATE apps
SET session_absolute_timeout_minutes = NULL,
    session_idle_timeout_minutes     = NULL
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes IS NOT NULL;

-- Expect zero rows.
SELECT COUNT(*) AS remaining_overrides
FROM apps
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes IS NOT NULL;

COMMIT;


-- ── STEP 2 — end sessions already issued a long lifetime ─────────────────────
-- UNCOMMENT ONLY IF NEEDED. This logs those users out on their next request and
-- will black out the dashboard until someone signs it in again. Refresh tokens
-- are revoked too, otherwise a refresh would resurrect the session.
--
-- BEGIN;
--
-- UPDATE refresh_tokens
-- SET revoked = true, revoked_at = now(), revoked_reason = 'session_policy_rollback'
-- WHERE revoked = false
--   AND session_id IN (
--       SELECT id FROM sessions
--       WHERE is_active = true
--         AND expires_at > started_at + (480 * INTERVAL '1 minute')
--   );
--
-- UPDATE sessions
-- SET is_active = false, ended_reason = 'session_policy_rollback'
-- WHERE is_active = true
--   AND expires_at > started_at + (480 * INTERVAL '1 minute');
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
-- and the values are accurate history. To blank it anyway:
--   UPDATE sessions SET app_id = NULL WHERE app_id IS NOT NULL;
