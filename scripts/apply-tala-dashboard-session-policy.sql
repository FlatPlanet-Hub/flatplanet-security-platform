-- =============================================================================
-- Apply the long-session policy to the Tala V2 Dashboard
-- =============================================================================
-- Run ONCE against the environment that hosts the dashboard, AFTER migration
-- V30 has been applied. Requires V30 (adds the two columns).
--
-- This is deliberately NOT a Flyway migration. It edits one row for one app that
-- exists in exactly one environment; as a migration it would abort the whole
-- chain on any database where that app is not registered (fresh dev DB, CI,
-- restored test instance) and block every migration after it.
--
-- 525600 minutes = 365 days.
--   absolute: the wall-mounted screen re-authenticates once a year, not once a workday.
--   idle:     the dashboard polls the Tala API rather than SP, so SP sees no activity
--             between token refreshes. Without this the 30-minute idle timeout ends the
--             session regardless of the absolute value.
--
-- Rollback: scripts/rollback-per-app-session-timeouts.sql
--
-- BEFORE RUNNING — verify the slug. Apps are registered in HubApi, not this repo:
--   SELECT id, slug, name, status FROM apps WHERE slug ILIKE '%tala%';
--
-- NOTE: this affects sessions created AFTER it runs. The dashboard must be logged
-- in again once for the new lifetime to take effect. Existing sessions keep the
-- timeouts they were created with.
-- =============================================================================

BEGIN;

DO $$
DECLARE
    v_slug     TEXT := 'tala-v2-dashboard';   -- <<< verify against the live apps table
    v_absolute INT  := 525600;
    v_idle     INT  := 525600;
    v_rows     INT;
BEGIN
    UPDATE apps
    SET session_absolute_timeout_minutes = v_absolute,
        session_idle_timeout_minutes     = v_idle
    WHERE slug = v_slug;

    GET DIAGNOSTICS v_rows = ROW_COUNT;

    IF v_rows = 0 THEN
        RAISE EXCEPTION
            'No app found with slug ''%''. Correct the slug and re-run — nothing has been changed.', v_slug;
    END IF;

    RAISE NOTICE 'Session policy applied to ''%'': absolute=% min, idle=% min.', v_slug, v_absolute, v_idle;
END $$;

-- Verify before committing.
SELECT slug, name, status,
       session_absolute_timeout_minutes,
       session_idle_timeout_minutes
FROM apps
WHERE session_absolute_timeout_minutes IS NOT NULL
   OR session_idle_timeout_minutes IS NOT NULL;

COMMIT;

-- Reminder: the user account the dashboard signs in with must hold an ACTIVE role
-- grant to this app, otherwise SP refuses the override and falls back to the
-- platform defaults. Check with:
--   SELECT u.email, a.slug, uar.status
--   FROM user_app_roles uar
--   JOIN users u ON u.id = uar.user_id
--   JOIN apps  a ON a.id = uar.app_id
--   WHERE a.slug = 'tala-v2-dashboard';
