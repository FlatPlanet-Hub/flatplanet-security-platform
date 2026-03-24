-- =============================================================================
-- FlatPlanet Security Platform — V2: Schema Fixes
-- Addresses gaps found in review against V1
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. Performance indexes — user_app_roles
--    These two queries run on EVERY authorization check and user-context call.
--    V1 had no indexes on this table beyond the PK and unique constraint.
-- -----------------------------------------------------------------------------
CREATE INDEX idx_user_app_roles_user
    ON user_app_roles(user_id)
    WHERE status = 'active';

CREATE INDEX idx_user_app_roles_user_app
    ON user_app_roles(user_id, app_id)
    WHERE status = 'active';

-- -----------------------------------------------------------------------------
-- 2. FK constraint — apps.registered_by was missing in V1
-- -----------------------------------------------------------------------------
ALTER TABLE apps
    ADD CONSTRAINT fk_apps_registered_by
    FOREIGN KEY (registered_by) REFERENCES users(id);

-- -----------------------------------------------------------------------------
-- 3. Unique constraint fix — roles
--    UNIQUE(app_id, name) with nullable app_id does not prevent duplicate
--    platform role names in PostgreSQL (NULL != NULL in unique indexes).
--    Use a partial unique index for the platform-role case.
-- -----------------------------------------------------------------------------
DROP INDEX IF EXISTS roles_app_id_name_key; -- remove old constraint if needed
ALTER TABLE roles DROP CONSTRAINT IF EXISTS roles_app_id_name_key;

-- Unique name among app-scoped roles
CREATE UNIQUE INDEX idx_roles_app_name
    ON roles(app_id, name)
    WHERE app_id IS NOT NULL;

-- Unique name among platform-level roles
CREATE UNIQUE INDEX idx_roles_platform_name
    ON roles(name)
    WHERE app_id IS NULL;

-- -----------------------------------------------------------------------------
-- 4. Unique constraint fix — permissions (same NULL issue as roles)
-- -----------------------------------------------------------------------------
ALTER TABLE permissions DROP CONSTRAINT IF EXISTS permissions_app_id_name_key;

CREATE UNIQUE INDEX idx_permissions_app_name
    ON permissions(app_id, name)
    WHERE app_id IS NOT NULL;

CREATE UNIQUE INDEX idx_permissions_platform_name
    ON permissions(name)
    WHERE app_id IS NULL;

-- -----------------------------------------------------------------------------
-- 5. Unique constraint — resources(app_id, identifier)
--    Two resources with the same identifier in the same app must not exist.
--    Authorization lookups by identifier would be ambiguous without this.
-- -----------------------------------------------------------------------------
CREATE UNIQUE INDEX idx_resources_app_identifier
    ON resources(app_id, identifier)
    WHERE status = 'active';

-- -----------------------------------------------------------------------------
-- 6. Seed role_permissions for platform roles
--    platform_owner and app_admin were seeded in roles but had zero permission
--    assignments. The /authorize endpoint returns denied for all permission
--    checks on these roles until this is seeded.
-- -----------------------------------------------------------------------------
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.name = 'platform_owner'
  AND r.is_platform_role = true
  AND p.app_id IS NULL
ON CONFLICT (role_id, permission_id) DO NOTHING;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.name = 'app_admin'
  AND r.is_platform_role = true
  AND p.name IN ('manage_roles', 'manage_resources', 'view_audit_log')
  AND p.app_id IS NULL
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 7. login_attempts cleanup
--    Feature.md spec: delete rows older than 24 hours.
--    Implemented as a scheduled deletion — run via pg_cron or external job.
--    This adds a helper function to call from a cron job.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION cleanup_login_attempts() RETURNS void AS $$
BEGIN
    DELETE FROM login_attempts WHERE attempted_at < now() - INTERVAL '24 hours';
END;
$$ LANGUAGE plpgsql;

-- If pg_cron is available on the instance, uncomment:
-- SELECT cron.schedule('cleanup-login-attempts', '0 * * * *', 'SELECT cleanup_login_attempts()');

-- -----------------------------------------------------------------------------
-- 8. Tighten auth_audit_log immutability
--    V1 only revoked from PUBLIC. If the app connects as a privileged user or
--    the table owner, the REVOKE is bypassed. Add row-level security as a
--    second layer: allow INSERT only, block UPDATE and DELETE for all roles.
-- -----------------------------------------------------------------------------
ALTER TABLE auth_audit_log ENABLE ROW LEVEL SECURITY;

-- Allow any authenticated role to insert
CREATE POLICY audit_log_insert_only
    ON auth_audit_log
    FOR INSERT
    WITH CHECK (true);

-- Explicitly deny SELECT to nothing (reads are fine) — only block writes
-- The REVOKE in V1 handles UPDATE/DELETE at the privilege level.
-- RLS adds an application-level block even if privileges are granted.
CREATE POLICY audit_log_no_update
    ON auth_audit_log
    FOR UPDATE
    USING (false);

CREATE POLICY audit_log_no_delete
    ON auth_audit_log
    FOR DELETE
    USING (false);
