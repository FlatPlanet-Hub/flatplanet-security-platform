-- Phase 2: TOTP + Email OTP MFA overhaul
-- GAP G6 FIX (X1 refined): Reset ALL mfa_enabled users before dropping phone columns.
-- Phase 1 never set phone_verified=true or mfa_method='sms', so broadening to all mfa_enabled=true.
UPDATE users SET mfa_enabled = false, mfa_method = null WHERE mfa_enabled = true;

ALTER TABLE users
  ADD COLUMN IF NOT EXISTS mfa_totp_secret TEXT,
  ADD COLUMN IF NOT EXISTS mfa_totp_enrolled BOOLEAN NOT NULL DEFAULT false,
  DROP COLUMN IF EXISTS phone_number,
  DROP COLUMN IF EXISTS phone_verified;

ALTER TABLE mfa_challenges
  ADD COLUMN IF NOT EXISTS challenge_type TEXT NOT NULL DEFAULT 'email_otp';

UPDATE mfa_challenges SET challenge_type = 'email_otp' WHERE challenge_type IS NULL;

ALTER TABLE mfa_challenges DROP COLUMN IF EXISTS phone_number;

CREATE INDEX IF NOT EXISTS idx_mfa_challenges_type
  ON mfa_challenges(user_id, challenge_type, verified_at) WHERE verified_at IS NULL;

INSERT INTO security_config (config_key, config_value, description) VALUES
  ('mfa_email_otp_expiry_minutes', '10', 'Email OTP validity window in minutes'),
  ('mfa_totp_issuer', 'FlatPlanet', 'Issuer name shown in authenticator apps'),
  ('mfa_max_otp_attempts', '5', 'Max wrong OTP attempts before challenge expires')
ON CONFLICT (config_key) DO NOTHING;

-- Step 2.8: rename otp_verified -> mfa_verified in identity_verification_status
ALTER TABLE identity_verification_status
  RENAME COLUMN otp_verified TO mfa_verified;
