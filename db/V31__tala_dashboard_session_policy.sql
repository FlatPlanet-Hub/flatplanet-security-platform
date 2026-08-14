-- Migration V31: Long-lived session policy for the Tala V2 Dashboard only
--
-- The Tala V2 Dashboard (https://fp-tala-v2-dashboard.netlify.app/) runs unattended
-- on a wall-mounted TV. It is the only app granted a long session lifetime; every
-- other app is untouched and continues to use the platform defaults.
--
-- 525600 minutes = 365 days.
--   absolute: the screen re-authenticates once a year instead of once a workday.
--   idle:     the dashboard polls the Tala API rather than SP, so SP sees no activity
--             between token refreshes; without this the 30-minute idle timeout ends
--             the session regardless of the absolute value.
--
-- IMPORTANT: verify the slug below against the live apps table before applying.
-- Apps are registered in HubApi, not in this repo, so the value is unverified here.
-- The guard raises rather than silently updating zero rows if the slug is wrong.

DO $$
DECLARE
    v_slug TEXT := 'tala-v2-dashboard';
    v_rows INTEGER;
BEGIN
    UPDATE apps
    SET session_absolute_timeout_minutes = 525600,
        session_idle_timeout_minutes     = 525600
    WHERE slug = v_slug;

    GET DIAGNOSTICS v_rows = ROW_COUNT;

    IF v_rows = 0 THEN
        RAISE EXCEPTION
            'V31: no app found with slug ''%''. Correct the slug in this migration to match the live apps table before re-running.', v_slug;
    END IF;
END $$;
