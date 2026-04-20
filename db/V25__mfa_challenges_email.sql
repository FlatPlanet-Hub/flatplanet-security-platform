-- =============================================================================
-- FlatPlanet Security Platform — V25: Add email column to mfa_challenges
-- The email column was planned in V23 (Phase 2 MFA overhaul) but was omitted.
-- Required by SendEmailOtpAsync and ResendEmailOtpAsync to record which address
-- the OTP was sent to, and to send to the correct address on resend.
-- =============================================================================

ALTER TABLE mfa_challenges
    ADD COLUMN IF NOT EXISTS email TEXT;
