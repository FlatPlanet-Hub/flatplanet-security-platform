-- FIX 11-2: registered_by is an audit field that stores either a user UUID or the
-- service-token sentinel (00000000-0000-0000-0000-000000000001).
-- The FK added in V2 conflicts with service-token app registration — the sentinel
-- Guid has no matching row in users, causing FK violation on every server-to-server
-- POST /api/v1/apps call.
ALTER TABLE apps DROP CONSTRAINT IF EXISTS fk_apps_registered_by;
