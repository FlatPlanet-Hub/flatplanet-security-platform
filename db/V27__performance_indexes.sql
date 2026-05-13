-- flyway:disableTransactionManagement: true
-- This migration creates indexes CONCURRENTLY and must run outside a transaction.

-- =============================================================================
-- FlatPlanet Security Platform — V27: Performance indexes
-- Adds indexes on hot query paths to eliminate full table scans.
-- =============================================================================

-- Login path: full table scan on every login without this
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_users_email_lower ON users (LOWER(email));

-- Called on every authenticated request via UserContextService
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_apps_slug ON apps (slug);

-- User access queries filter on both fields
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_user_app_roles_user_app ON user_app_roles (user_id, app_id);

-- Session queries filter on user + active status
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_sessions_user_active ON sessions (user_id, is_active);

-- Login attempts range queries
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_login_attempts_email_time ON login_attempts (email, attempted_at);
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_login_attempts_ip_time ON login_attempts (ip_address, attempted_at);
