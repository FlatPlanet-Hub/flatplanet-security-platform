-- Migration V19: Add grace period columns to refresh_tokens for reuse detection

ALTER TABLE refresh_tokens
    ADD COLUMN replaced_by_token_hash  TEXT,
    ADD COLUMN replaced_by_token_plain TEXT,
    ADD COLUMN rotated_at              TIMESTAMPTZ;

CREATE INDEX idx_refresh_tokens_replaced_by
    ON refresh_tokens(replaced_by_token_hash)
    WHERE revoked = true AND revoked_reason = 'rotated';

INSERT INTO security_config (config_key, config_value, description)
VALUES (
    'refresh_token_grace_period_seconds',
    '30',
    'Window (seconds) in which a recently-rotated refresh token is accepted and returns its replacement instead of rejecting'
);
