-- =============================================================================
-- FlatPlanet Security Platform — V3: RLS Fixes on auth_audit_log
-- Fixes two bugs introduced by the V2 RLS block.
-- =============================================================================

-- Fix missing FORCE RLS — without this, table owner bypasses all policies.
-- Supabase apps connect as `postgres` which is the table owner, so the V2
-- UPDATE/DELETE block policies were silently bypassed.
ALTER TABLE auth_audit_log FORCE ROW LEVEL SECURITY;

-- Fix missing SELECT policy — when RLS is enabled, non-owner roles see zero
-- rows unless a permissive SELECT policy exists. V2 had no SELECT policy,
-- making GET /api/v1/audit return empty results for the application user.
CREATE POLICY audit_log_select
    ON auth_audit_log
    FOR SELECT
    USING (true);
