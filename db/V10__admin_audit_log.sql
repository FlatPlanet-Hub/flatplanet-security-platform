-- =============================================================================
-- FlatPlanet Security Platform — V10: Admin Audit Log
-- Records every admin write operation (create/update/suspend user,
-- grant/revoke role, register/update/deactivate app) with before/after state.
-- ISO 27001 A.12.4.1 / A.12.4.3 — Logging and monitoring.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. Admin audit log table
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS admin_audit_log (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_id     UUID        NOT NULL REFERENCES users(id),
    actor_email  TEXT        NOT NULL,
    action       TEXT        NOT NULL,
    target_type  TEXT        NOT NULL,
    target_id    UUID,
    before_state JSONB,
    after_state  JSONB,
    ip_address   TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 2. Indexes
-- -----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_admin_audit_actor  ON admin_audit_log(actor_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_target ON admin_audit_log(target_type, target_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_time   ON admin_audit_log(created_at DESC);

-- -----------------------------------------------------------------------------
-- 3. Immutable — auditors must never be able to modify or delete records
-- -----------------------------------------------------------------------------
REVOKE UPDATE, DELETE ON admin_audit_log FROM PUBLIC;

-- -----------------------------------------------------------------------------
-- 4. Retention config (3 years = ISO 27001 best practice)
-- -----------------------------------------------------------------------------
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('audit_log_retention_days', '1095', 'Admin audit log retention in days (default 3 years)')
ON CONFLICT (config_key) DO NOTHING;
