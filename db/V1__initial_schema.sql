-- =============================================================================
-- FlatPlanet Security Platform — Initial Schema
-- V1: Core tables (14 total)
-- All timestamps in UTC (timestamptz)
-- =============================================================================

-- -----------------------------------------------------------------------------
-- companies
-- -----------------------------------------------------------------------------
CREATE TABLE companies (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name         TEXT NOT NULL,
    country_code TEXT NOT NULL,
    status       TEXT NOT NULL DEFAULT 'active',
    created_at   TIMESTAMPTZ DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- users
-- -----------------------------------------------------------------------------
CREATE TABLE users (
    id           UUID PRIMARY KEY,                  -- matches Supabase Auth uid
    company_id   UUID NOT NULL REFERENCES companies(id),
    email        TEXT UNIQUE NOT NULL,
    full_name    TEXT NOT NULL,
    role_title   TEXT,
    status       TEXT NOT NULL DEFAULT 'active',    -- active / inactive / suspended
    created_at   TIMESTAMPTZ DEFAULT now(),
    last_seen_at TIMESTAMPTZ
);

-- -----------------------------------------------------------------------------
-- apps
-- -----------------------------------------------------------------------------
CREATE TABLE apps (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    company_id    UUID NOT NULL REFERENCES companies(id),
    name          TEXT NOT NULL,
    slug          TEXT UNIQUE NOT NULL,
    base_url      TEXT NOT NULL,
    status        TEXT NOT NULL DEFAULT 'active',
    registered_at TIMESTAMPTZ DEFAULT now(),
    registered_by UUID NOT NULL
);

-- -----------------------------------------------------------------------------
-- resource_types
-- -----------------------------------------------------------------------------
CREATE TABLE resource_types (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT UNIQUE NOT NULL,
    description TEXT
);

INSERT INTO resource_types (name, description) VALUES
    ('page',         'A full HTML page or route'),
    ('section',      'A named section within a page'),
    ('panel',        'A UI panel — may be visible to some roles and hidden from others'),
    ('api_endpoint', 'A serverless function or API route');

-- -----------------------------------------------------------------------------
-- resources
-- -----------------------------------------------------------------------------
CREATE TABLE resources (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id           UUID NOT NULL REFERENCES apps(id),
    resource_type_id UUID NOT NULL REFERENCES resource_types(id),
    name             TEXT NOT NULL,
    identifier       TEXT NOT NULL,
    status           TEXT NOT NULL DEFAULT 'active',
    created_at       TIMESTAMPTZ DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- roles
-- -----------------------------------------------------------------------------
CREATE TABLE roles (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id           UUID REFERENCES apps(id),     -- null for platform-level roles
    name             TEXT NOT NULL,
    description      TEXT,
    is_platform_role BOOLEAN DEFAULT false,
    created_at       TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app_id, name)
);

INSERT INTO roles (name, description, is_platform_role) VALUES
    ('platform_owner', 'Full platform access across all apps and companies', true),
    ('app_admin',      'Admin access within a specific app', true);

-- -----------------------------------------------------------------------------
-- user_app_roles
-- -----------------------------------------------------------------------------
CREATE TABLE user_app_roles (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app_id     UUID NOT NULL REFERENCES apps(id),
    role_id    UUID NOT NULL REFERENCES roles(id),
    granted_at TIMESTAMPTZ DEFAULT now(),
    granted_by UUID NOT NULL REFERENCES users(id),
    expires_at TIMESTAMPTZ,
    status     TEXT NOT NULL DEFAULT 'active',     -- active / suspended / expired
    UNIQUE(user_id, app_id, role_id)
);

-- -----------------------------------------------------------------------------
-- permissions
-- -----------------------------------------------------------------------------
CREATE TABLE permissions (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id      UUID REFERENCES apps(id),          -- null for platform-level permissions
    name        TEXT NOT NULL,
    description TEXT,
    category    TEXT NOT NULL,
    created_at  TIMESTAMPTZ DEFAULT now(),
    UNIQUE(app_id, name)
);

INSERT INTO permissions (name, description, category) VALUES
    ('manage_companies', 'Create and edit company records',             'admin'),
    ('manage_users',     'Create, edit, and deactivate users',          'admin'),
    ('manage_apps',      'Register and configure apps',                 'admin'),
    ('manage_roles',     'Create and edit roles and permissions',       'admin'),
    ('view_audit_log',   'View auth and access audit logs',             'admin'),
    ('manage_resources', 'Register and configure protected resources',  'admin');

-- -----------------------------------------------------------------------------
-- role_permissions
-- -----------------------------------------------------------------------------
CREATE TABLE role_permissions (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id       UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id UUID NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    granted_at    TIMESTAMPTZ DEFAULT now(),
    granted_by    UUID REFERENCES users(id),
    UNIQUE(role_id, permission_id)
);

-- -----------------------------------------------------------------------------
-- sessions
-- -----------------------------------------------------------------------------
CREATE TABLE sessions (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    app_id         UUID REFERENCES apps(id),
    ip_address     TEXT,
    user_agent     TEXT,
    started_at     TIMESTAMPTZ DEFAULT now(),
    last_active_at TIMESTAMPTZ DEFAULT now(),
    expires_at     TIMESTAMPTZ NOT NULL,
    is_active      BOOLEAN DEFAULT true,
    ended_reason   TEXT       -- logout / idle_timeout / absolute_timeout / revoked
);

CREATE INDEX idx_sessions_user   ON sessions(user_id);
CREATE INDEX idx_sessions_active ON sessions(is_active) WHERE is_active = true;

-- -----------------------------------------------------------------------------
-- refresh_tokens
-- -----------------------------------------------------------------------------
CREATE TABLE refresh_tokens (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    session_id     UUID REFERENCES sessions(id) ON DELETE CASCADE,
    token_hash     TEXT UNIQUE NOT NULL,  -- SHA256 hash, never plaintext
    expires_at     TIMESTAMPTZ NOT NULL,
    revoked        BOOLEAN DEFAULT false,
    revoked_at     TIMESTAMPTZ,
    revoked_reason TEXT,
    created_at     TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_refresh_tokens_user ON refresh_tokens(user_id);

-- -----------------------------------------------------------------------------
-- auth_audit_log  (IMMUTABLE — no UPDATE or DELETE allowed)
-- -----------------------------------------------------------------------------
CREATE TABLE auth_audit_log (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID REFERENCES users(id),
    app_id     UUID REFERENCES apps(id),
    event_type TEXT NOT NULL,
    ip_address TEXT,
    user_agent TEXT,
    details    JSONB,
    created_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_auth_audit_user    ON auth_audit_log(user_id);
CREATE INDEX idx_auth_audit_type    ON auth_audit_log(event_type);
CREATE INDEX idx_auth_audit_created ON auth_audit_log(created_at);

-- ISO 27001: Immutable audit log
REVOKE UPDATE, DELETE ON auth_audit_log FROM PUBLIC;

-- -----------------------------------------------------------------------------
-- login_attempts
-- -----------------------------------------------------------------------------
CREATE TABLE login_attempts (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email        TEXT NOT NULL,
    ip_address   TEXT,
    success      BOOLEAN NOT NULL,
    attempted_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX idx_login_attempts_email ON login_attempts(email);
CREATE INDEX idx_login_attempts_ip    ON login_attempts(ip_address);
CREATE INDEX idx_login_attempts_time  ON login_attempts(attempted_at);

-- -----------------------------------------------------------------------------
-- security_config
-- -----------------------------------------------------------------------------
CREATE TABLE security_config (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    config_key   TEXT UNIQUE NOT NULL,
    config_value TEXT NOT NULL,
    description  TEXT,
    updated_at   TIMESTAMPTZ DEFAULT now(),
    updated_by   UUID REFERENCES users(id)
);

INSERT INTO security_config (config_key, config_value, description) VALUES
    ('session_idle_timeout_minutes',          '30',  'Session idle timeout in minutes'),
    ('session_absolute_timeout_minutes',      '480', 'Max session duration (8 hours)'),
    ('max_concurrent_sessions',               '3',   'Max active sessions per user'),
    ('max_failed_login_attempts',             '5',   'Lock account after N failed logins'),
    ('lockout_duration_minutes',              '30',  'Account lockout duration'),
    ('jwt_access_expiry_minutes',             '60',  'JWT access token expiry'),
    ('jwt_refresh_expiry_days',               '7',   'Refresh token expiry'),
    ('audit_log_retention_days',              '365', 'Minimum audit log retention'),
    ('rate_limit_login_per_ip_per_minute',    '5',   'Max login attempts per IP per minute'),
    ('rate_limit_login_per_email_per_minute', '10',  'Max login attempts per email per minute');
