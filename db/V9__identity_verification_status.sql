-- =============================================================================
-- FlatPlanet Security Platform — V9: Identity Verification Status
-- Tracks OTP and (future) video verification per user.
-- fully_verified is a plain BOOLEAN computed by the service layer using the
-- require_video_verification config flag — NOT a generated column, because
-- generated columns cannot read from security_config.
-- ISO 27001 A.9.4.2.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. Identity verification status table
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS identity_verification_status (
    id             UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID        NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    otp_verified   BOOLEAN     NOT NULL DEFAULT false,
    video_verified BOOLEAN     NOT NULL DEFAULT false,
    fully_verified BOOLEAN     NOT NULL DEFAULT false,
    verified_at    TIMESTAMPTZ,
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 2. Index
-- -----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_ivs_user ON identity_verification_status(user_id);

-- -----------------------------------------------------------------------------
-- 3. Config flag — flip to true when video verification (FEAT-08) is live
-- -----------------------------------------------------------------------------
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('require_video_verification', 'false', 'When true, both OTP and video must pass for full verification')
ON CONFLICT (config_key) DO NOTHING;
