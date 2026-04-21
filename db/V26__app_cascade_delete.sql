-- =============================================================================
-- FlatPlanet Security Platform — V26: App cascade delete rules
-- Adds ON DELETE CASCADE (owned data) and ON DELETE SET NULL (audit/session refs)
-- so that hard-deleting an app via DELETE /api/v1/apps/{id} works without
-- manually cleaning up every child table first.
-- =============================================================================

-- resources: owned by app — cascade delete
ALTER TABLE resources
    DROP CONSTRAINT IF EXISTS resources_app_id_fkey,
    ADD  CONSTRAINT resources_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE CASCADE;

-- roles: app-level roles are owned by the app — cascade delete
-- (role_permissions already cascades from roles.id)
ALTER TABLE roles
    DROP CONSTRAINT IF EXISTS roles_app_id_fkey,
    ADD  CONSTRAINT roles_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE CASCADE;

-- user_app_roles: assignments are owned by the app — cascade delete
ALTER TABLE user_app_roles
    DROP CONSTRAINT IF EXISTS user_app_roles_app_id_fkey,
    ADD  CONSTRAINT user_app_roles_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE CASCADE;

-- permissions: app-level permissions are owned by the app — cascade delete
ALTER TABLE permissions
    DROP CONSTRAINT IF EXISTS permissions_app_id_fkey,
    ADD  CONSTRAINT permissions_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE CASCADE;

-- sessions: survive app deletion — nullify the reference
ALTER TABLE sessions
    DROP CONSTRAINT IF EXISTS sessions_app_id_fkey,
    ADD  CONSTRAINT sessions_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE SET NULL;

-- auth_audit_log: immutable — nullify app_id on app deletion
ALTER TABLE auth_audit_log
    DROP CONSTRAINT IF EXISTS auth_audit_log_app_id_fkey,
    ADD  CONSTRAINT auth_audit_log_app_id_fkey
        FOREIGN KEY (app_id) REFERENCES apps(id) ON DELETE SET NULL;
