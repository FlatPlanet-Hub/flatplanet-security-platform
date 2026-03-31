-- =============================================================================
-- FlatPlanet Security Platform — V13: Drop role_permissions granted_by FK/NOT NULL
-- Mirrors V11 (user_app_roles). Then re-runs the V12 role_permissions grants
-- which failed due to the NOT NULL constraint.
-- =============================================================================

-- 1. Drop FK and NOT NULL (same pattern as V11)
ALTER TABLE role_permissions DROP CONSTRAINT role_permissions_granted_by_fkey;
ALTER TABLE role_permissions ALTER COLUMN granted_by DROP NOT NULL;

-- 2. Re-run the role_permissions grants from V12 (idempotent — ON CONFLICT DO NOTHING)
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN apps a ON r.app_id = a.id
JOIN permissions p ON p.app_id = a.id
WHERE a.slug = 'dashboard-hub'
  AND p.name = 'view_projects'
ON CONFLICT (role_id, permission_id) DO NOTHING;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN apps a ON a.slug = 'dashboard-hub'
JOIN permissions p ON p.app_id = a.id
WHERE r.name = 'platform_owner'
  AND r.is_platform_role = true
  AND p.name = 'view_projects'
ON CONFLICT (role_id, permission_id) DO NOTHING;
