-- GAP-G4: One-time-use TOTP backup codes for account recovery when authenticator app is unavailable.
CREATE TABLE IF NOT EXISTS mfa_backup_codes (
    id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash  TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    used_at    TIMESTAMPTZ
);

-- Partial index — only index unused codes; used ones are dead weight for lookups.
CREATE INDEX IF NOT EXISTS idx_mfa_backup_codes_user_unused
    ON mfa_backup_codes(user_id) WHERE used_at IS NULL;
