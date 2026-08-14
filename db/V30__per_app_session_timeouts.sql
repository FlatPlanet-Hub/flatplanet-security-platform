-- Migration V30: Per-app session timeout overrides
--
-- Adds optional per-app overrides for the two session timeouts. NULL means
-- "use the platform default from security_config" — so every existing app keeps
-- its current behaviour until an override is set explicitly.
--
-- Motivation: always-on kiosk/dashboard clients (wall-mounted TVs) cannot survive
-- the platform-wide 8-hour absolute timeout, but raising that value globally would
-- weaken the control for every normal user session. Both timeouts are stamped onto
-- the session row at creation (sessions.expires_at, sessions.idle_timeout_minutes),
-- so an override changes only sessions created after it is set.

ALTER TABLE apps
    ADD COLUMN session_absolute_timeout_minutes INTEGER,
    ADD COLUMN session_idle_timeout_minutes     INTEGER;

-- Lower bound: a zero or negative timeout would expire every session for the app
-- immediately. Upper bound: 525600 minutes (365 days) is the deliberate ceiling for how
-- long any session may live — the absolute timeout is the only hard cap in the system,
-- so raising it past a year should be a conscious schema change, not a typo.
ALTER TABLE apps
    ADD CONSTRAINT apps_session_absolute_timeout_range
        CHECK (session_absolute_timeout_minutes IS NULL
               OR session_absolute_timeout_minutes BETWEEN 1 AND 525600),
    ADD CONSTRAINT apps_session_idle_timeout_range
        CHECK (session_idle_timeout_minutes IS NULL
               OR session_idle_timeout_minutes BETWEEN 1 AND 525600);

COMMENT ON COLUMN apps.session_absolute_timeout_minutes IS
    'Overrides security_config.session_absolute_timeout_minutes for sessions created against this app. NULL = platform default.';
COMMENT ON COLUMN apps.session_idle_timeout_minutes IS
    'Overrides security_config.session_idle_timeout_minutes for sessions created against this app. NULL = platform default.';
