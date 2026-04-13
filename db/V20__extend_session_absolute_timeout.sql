-- Migration V20: Extend session absolute timeout for long-lived dashboard sessions
-- Default was 480 minutes (8 hours) — increasing to 10080 minutes (7 days)
-- to match refresh token expiry. Dashboards use the heartbeat endpoint to
-- reset idle timeout; the absolute timeout is the final safety net.

UPDATE security_config
SET config_value = '10080',
    description  = 'Max session duration in minutes (7 days — matches refresh token expiry)'
WHERE config_key = 'session_absolute_timeout_minutes';
