-- =============================================================================
-- FlatPlanet Security Platform — V12: Add view_projects permission
-- Adds the view_projects permission to dashboard-hub and grants it to all
-- dashboard-hub roles and to the platform_owner role.
-- =============================================================================

-- Add view_projects as a dashboard-hub app-scoped permission
INSERT INTO permissions (app_id, name, description, category)
SELECT a.id, 'view_projects', 'View projects in the dashboard', 'data'
FROM apps a
WHERE a.slug = 'dashboard-hub'
ON CONFLICT DO NOTHING;

-- Grant to all app-scoped roles within dashboard-hub
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN apps a ON r.app_id = a.id
JOIN permissions p ON p.app_id = a.id
WHERE a.slug = 'dashboard-hub'
  AND p.name = 'view_projects'
ON CONFLICT (role_id, permission_id) DO NOTHING;

-- Grant to platform_owner role so it applies when platform_owner is assigned to dashboard-hub
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN apps a ON a.slug = 'dashboard-hub'
JOIN permissions p ON p.app_id = a.id
WHERE r.name = 'platform_owner'
  AND r.is_platform_role = true
  AND p.name = 'view_projects'
ON CONFLICT (role_id, permission_id) DO NOTHING;
