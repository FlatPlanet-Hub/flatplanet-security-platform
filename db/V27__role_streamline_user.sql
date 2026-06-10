-- V27 — Role streamlining: viewer -> user, developer += manage_members
-- Target design (approved 2026-06-10):
--   owner     = read, write, ddl, manage_members, delete_project  (manual escalation only)
--   developer = read, write, ddl, manage_members                  (Hub project creators)
--   user      = read                                              (all invited members)
--
-- Scope: ALL apps EXCEPT dashboard-hub (51d3fe9a-383e-4b5f-a670-d311725c7308).
--   dashboard-hub is the hub frontend with its own Admin/User/Viewer vocabulary,
--   26 live grants, and a pending Netlify slug switch — handled in its own pass.
--
-- fp-esignature (legacy Admin/User/Viewer, created manually 2026-04-10) is
-- converted to the standard triad: Admin->owner, User->developer, Viewer->user.
--
-- All grants survive: user_app_roles links by role_id (UUID), not name.
-- Per-app role names are NOT in any JWT — no token invalidation needed.
--
-- Run against SP database directly (Supabase: aws-1-ap-southeast-1.pooler.supabase.com:5432).
-- This is SP's OWN database — NOT the Platform/HubApi shared DB.

BEGIN;

-- ── 0. Snapshot pre-state into archive schema (atomic with the migration) ───
-- If anything below fails or is rolled back, the snapshot disappears with it.
-- On COMMIT, the snapshot persists for manual rollback if needed.

CREATE SCHEMA IF NOT EXISTS archive;

DROP TABLE IF EXISTS archive.roles_pre_v27;
DROP TABLE IF EXISTS archive.role_permissions_pre_v27;

CREATE TABLE archive.roles_pre_v27 AS
  SELECT * FROM roles;

CREATE TABLE archive.role_permissions_pre_v27 AS
  SELECT * FROM role_permissions;

-- Sanity: snapshots populated
DO $$
DECLARE r_count int; rp_count int;
BEGIN
  SELECT count(*) INTO r_count  FROM archive.roles_pre_v27;
  SELECT count(*) INTO rp_count FROM archive.role_permissions_pre_v27;
  IF r_count = 0 OR rp_count = 0 THEN
    RAISE EXCEPTION 'Snapshot failed: roles=%, role_permissions=%', r_count, rp_count;
  END IF;
  RAISE NOTICE 'Snapshot OK: roles=%, role_permissions=%', r_count, rp_count;
END $$;

-- ── 1. fp-esignature legacy conversion (do BEFORE the global rename so its
--       'User' row is already renamed and cannot collide on UNIQUE(app_id,name))
-- Order is significant: Admin->owner, User->developer, Viewer->user.
-- After Admin->owner: {owner,User,Viewer}   no collision
-- After User->developer: {owner,developer,Viewer}   no collision
-- After Viewer->user: {owner,developer,user}   no collision
UPDATE roles SET name = 'owner'
WHERE app_id = '5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf' AND name = 'Admin';

UPDATE roles SET name = 'developer'
WHERE app_id = '5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf' AND name = 'User';

UPDATE roles SET name = 'user'
WHERE app_id = '5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf' AND name = 'Viewer';

-- ── 2. Global rename: viewer/Viewer -> user (all apps except dashboard-hub) ─
UPDATE roles
SET name = 'user'
WHERE lower(name) = 'viewer'
  AND is_platform_role = false
  AND app_id IS DISTINCT FROM '51d3fe9a-383e-4b5f-a670-d311725c7308';

-- ── 3. developer += manage_members (every app that defines the permission,
--       except dashboard-hub which has no manage_members permission) ─────────
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p ON p.app_id = r.app_id AND p.name = 'manage_members'
WHERE r.name = 'developer'
  AND r.is_platform_role = false
  AND NOT EXISTS (
        SELECT 1 FROM role_permissions rp
        WHERE rp.role_id = r.id AND rp.permission_id = p.id)
ON CONFLICT DO NOTHING;

-- ── 4. Description backfill (only where NULL — keeps custom descriptions) ───
UPDATE roles SET description = 'Full access'
WHERE is_platform_role = false AND name = 'owner' AND description IS NULL;

UPDATE roles SET description = 'Read, write, schema access, manage members'
WHERE is_platform_role = false AND name = 'developer' AND description IS NULL;

UPDATE roles SET description = 'Read-only'
WHERE is_platform_role = false AND name = 'user' AND description IS NULL;

-- ── 5. Verification assertions (inline, rollback on failure) ────────────────

-- a) No 'viewer' left outside dashboard-hub
DO $$
DECLARE v_left int;
BEGIN
  SELECT count(*) INTO v_left FROM roles r
  WHERE lower(r.name) = 'viewer'
    AND r.app_id IS DISTINCT FROM '51d3fe9a-383e-4b5f-a670-d311725c7308';
  IF v_left > 0 THEN
    RAISE EXCEPTION 'V27 step 2 incomplete: % viewer rows remain outside dashboard-hub', v_left;
  END IF;
END $$;

-- b) Every developer role (where manage_members perm exists) now has it
DO $$
DECLARE missing int;
BEGIN
  SELECT count(*) INTO missing
  FROM roles r
  JOIN permissions p ON p.app_id = r.app_id AND p.name = 'manage_members'
  WHERE r.name = 'developer'
    AND r.is_platform_role = false
    AND NOT EXISTS (
      SELECT 1 FROM role_permissions rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id);
  IF missing > 0 THEN
    RAISE EXCEPTION 'V27 step 3 incomplete: % developer roles missing manage_members', missing;
  END IF;
END $$;

-- c) rtw-list still has 8 active grants on its renamed role
DO $$
DECLARE rtw_grants int;
BEGIN
  SELECT count(*) INTO rtw_grants
  FROM user_app_roles uar
  JOIN roles r ON r.id = uar.role_id
  JOIN apps a  ON a.id = r.app_id
  WHERE a.slug = 'rtw-list' AND r.name = 'user' AND uar.status = 'active';
  IF rtw_grants <> 8 THEN
    RAISE EXCEPTION 'V27 grant survival failed: rtw-list expected 8 user grants, found %', rtw_grants;
  END IF;
END $$;

-- d) dashboard-hub untouched — still has exactly Admin/User/Viewer
DO $$
DECLARE dh_names text;
BEGIN
  SELECT string_agg(name, ',' ORDER BY name) INTO dh_names
  FROM roles
  WHERE app_id = '51d3fe9a-383e-4b5f-a670-d311725c7308';
  IF dh_names <> 'Admin,User,Viewer' THEN
    RAISE EXCEPTION 'dashboard-hub roles changed unexpectedly: got [%]', dh_names;
  END IF;
END $$;

-- e) fp-esignature converted cleanly — exactly owner/developer/user
DO $$
DECLARE esig_names text;
BEGIN
  SELECT string_agg(name, ',' ORDER BY name) INTO esig_names
  FROM roles
  WHERE app_id = '5feb6718-8cdc-4f6e-b0e8-fdb9e3683ccf';
  IF esig_names <> 'developer,owner,user' THEN
    RAISE EXCEPTION 'fp-esignature conversion failed: got [%]', esig_names;
  END IF;
END $$;

-- All assertions passed; commit the snapshot and the changes together.
COMMIT;

-- ── Rollback procedure (if a problem surfaces post-COMMIT) ──────────────────
--   BEGIN;
--   TRUNCATE role_permissions;
--   INSERT INTO role_permissions SELECT * FROM archive.role_permissions_pre_v27;
--   -- For roles, individual UPDATEs (cannot TRUNCATE due to FK from user_app_roles):
--   UPDATE roles r SET name = a.name, description = a.description
--   FROM archive.roles_pre_v27 a WHERE a.id = r.id;
--   COMMIT;
