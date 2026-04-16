-- =============================================================================
-- FlatPlanet Security Platform — V15: Add view_all_projects permission
-- Adds the view_all_projects permission to dashboard-hub and grants it only to
-- the platform_owner role, enabling super-admin project visibility in HubApi.
-- =============================================================================

-- Add view_all_projects as a dashboard-hub app-scoped permission
INSERT INTO permissions (app_id, name, description, category)
SELECT a.id, 'view_all_projects', 'View all projects in the dashboard regardless of membership', 'data'
FROM apps a
WHERE a.slug = 'dashboard-hub'
ON CONFLICT DO NOTHING;

-- Grant view_all_projects only to platform_owner role on dashboard-hub
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN apps a ON a.slug = 'dashboard-hub'
JOIN permissions p ON p.app_id = a.id
WHERE r.name = 'platform_owner'
  AND r.is_platform_role = true
  AND p.name = 'view_all_projects'
ON CONFLICT (role_id, permission_id) DO NOTHING;
