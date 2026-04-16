-- =============================================================================
-- FlatPlanet Security Platform — V8: MFA Challenges Table
-- Stores OTP challenges for enrollment and login verification.
-- OTP is stored as SHA-256 hash — never plaintext.
-- ISO 27001 A.9.4.2 — Multi-factor authentication.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. MFA challenges table
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS mfa_challenges (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    phone_number TEXT        NOT NULL,
    otp_hash     TEXT        NOT NULL,
    expires_at   TIMESTAMPTZ NOT NULL,
    verified_at  TIMESTAMPTZ,
    attempts     INT         NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 2. Indexes
-- -----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_mfa_challenges_user   ON mfa_challenges(user_id);
CREATE INDEX IF NOT EXISTS idx_mfa_challenges_active ON mfa_challenges(user_id, verified_at)
    WHERE verified_at IS NULL;

-- -----------------------------------------------------------------------------
-- 3. Seed MFA config entries
-- -----------------------------------------------------------------------------
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('mfa_otp_expiry_minutes', '10', 'OTP validity window in minutes'),
    ('mfa_otp_max_attempts',   '3',  'Max wrong OTP attempts before challenge is invalidated'),
    ('mfa_otp_length',         '6',  'OTP digit count')
ON CONFLICT (config_key) DO NOTHING;
