-- =============================================================================
-- FlatPlanet Security Platform — V7: User MFA Columns + Role Permission Fix
-- Adds MFA-related columns to users table and backfills password_changed_at.
-- ISO 27001 A.9.4.2 — Multi-factor authentication foundation.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. MFA columns on users
-- -----------------------------------------------------------------------------
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS phone_number        TEXT,
    ADD COLUMN IF NOT EXISTS phone_verified      BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS mfa_enabled         BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS mfa_method          TEXT,
    ADD COLUMN IF NOT EXISTS password_changed_at TIMESTAMPTZ;

-- -----------------------------------------------------------------------------
-- 2. Backfill password_changed_at for existing users
--    Use created_at as a safe approximation — better than leaving it NULL.
-- -----------------------------------------------------------------------------
UPDATE users SET password_changed_at = created_at WHERE password_changed_at IS NULL;

-- -----------------------------------------------------------------------------
-- 3. ISO 27001 A.9.2.2 — every permission grant must have an owner
--    Make role_permissions.granted_by NOT NULL going forward.
--    (Existing NULL rows were already fixed in seed/earlier migrations.)
-- -----------------------------------------------------------------------------
ALTER TABLE role_permissions
    ALTER COLUMN granted_by SET NOT NULL;
