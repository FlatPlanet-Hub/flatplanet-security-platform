-- V28 — Retroactive owner-grant downgrade
--
-- Per request (2026-06-10): existing per-app `owner` grants should be
-- downgraded to `developer`. The `owner` role still exists as a role
-- definition; it just stops being held by past project creators.
--
-- Going forward, `owner` is a manual escalation tier — granted only when
-- someone explicitly needs `delete_project`. This completes the policy
-- change started in V27 (which only made FUTURE creators land as developer).
--
-- Scope: all user_app_roles rows pointing to a role named `owner` where
--   - is_platform_role = false (per-app owner, not platform_owner)
--   - app slug != 'dashboard-hub' (carve-out preserved from V27; dashboard-hub
--     has no `owner` role anyway, but the exclusion is explicit for clarity)
--
-- Affected: 57 grants (54 active + 3 inactive) at time of writing.
--
-- Platform owners (Erick, JL, Chris) keep all destructive powers via the
-- platform_owner role bypass — this migration touches only per-app role grants.
--
-- Constraint analysis (UNIQUE (user_id, app_id, role_id)):
-- No (user, app) pair has both `owner` AND `developer` grants today, so the
-- UPDATE produces no constraint violations. Verified pre-flight.

BEGIN;

-- ── 0. Snapshot pre-state ────────────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS archive;
DROP TABLE IF EXISTS archive.user_app_roles_pre_v28;
CREATE TABLE archive.user_app_roles_pre_v28 AS SELECT * FROM user_app_roles;

DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM archive.user_app_roles_pre_v28;
  IF n = 0 THEN RAISE EXCEPTION 'Snapshot failed: zero rows'; END IF;
  RAISE NOTICE 'Snapshot OK: user_app_roles=%', n;
END $$;

-- ── 1. The downgrade ─────────────────────────────────────────────────────
WITH downgrades AS (
  SELECT uar.id AS uar_id, dev.id AS developer_role_id
  FROM user_app_roles uar
  JOIN roles owner_role ON owner_role.id = uar.role_id
  JOIN apps a ON a.id = owner_role.app_id
  JOIN roles dev ON dev.app_id = owner_role.app_id AND dev.name = 'developer'
  WHERE owner_role.name = 'owner'
    AND owner_role.is_platform_role = false
    AND a.slug != 'dashboard-hub'
)
UPDATE user_app_roles uar
SET role_id = d.developer_role_id
FROM downgrades d
WHERE uar.id = d.uar_id;

-- ── 2. Assertions ────────────────────────────────────────────────────────

-- a) No user_app_roles row still points to a per-app `owner` role outside dashboard-hub
DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n
  FROM user_app_roles uar
  JOIN roles r ON r.id = uar.role_id
  JOIN apps a ON a.id = r.app_id
  WHERE r.name = 'owner' AND r.is_platform_role = false
    AND a.slug != 'dashboard-hub';
  IF n > 0 THEN
    RAISE EXCEPTION 'V28 incomplete: % owner grants remain outside dashboard-hub', n;
  END IF;
END $$;

-- b) Total grant count is preserved (UPDATE shouldn't have created or lost rows)
DO $$
DECLARE before_n int; after_n int;
BEGIN
  SELECT count(*) INTO before_n FROM archive.user_app_roles_pre_v28;
  SELECT count(*) INTO after_n  FROM user_app_roles;
  IF before_n <> after_n THEN
    RAISE EXCEPTION 'V28 row-count drift: before=%, after=%', before_n, after_n;
  END IF;
END $$;

-- c) dashboard-hub grants untouched
DO $$
DECLARE before_n int; after_n int;
BEGIN
  SELECT count(*) INTO before_n FROM archive.user_app_roles_pre_v28
    WHERE app_id = '51d3fe9a-383e-4b5f-a670-d311725c7308';
  SELECT count(*) INTO after_n FROM user_app_roles
    WHERE app_id = '51d3fe9a-383e-4b5f-a670-d311725c7308';
  IF before_n <> after_n THEN
    RAISE EXCEPTION 'dashboard-hub grant count changed: before=%, after=%', before_n, after_n;
  END IF;
END $$;

-- d) Owner role definitions still exist (we only changed grants, not roles)
DO $$
DECLARE n int;
BEGIN
  SELECT count(*) INTO n FROM roles WHERE name = 'owner' AND is_platform_role = false;
  IF n = 0 THEN
    RAISE EXCEPTION 'owner role definitions vanished — V28 should NOT delete role rows';
  END IF;
END $$;

COMMIT;

-- ── Rollback procedure ──────────────────────────────────────────────────
-- BEGIN;
-- UPDATE user_app_roles uar
-- SET role_id = a.role_id
-- FROM archive.user_app_roles_pre_v28 a
-- WHERE a.id = uar.id;
-- COMMIT;
