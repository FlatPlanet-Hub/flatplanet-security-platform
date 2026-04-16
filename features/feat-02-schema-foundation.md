# FEAT-02 — Schema Foundation

**Repo:** flatplanet-security-platform
**Branch:** `feature/feat-02-schema-foundation`
**Depends on:** nothing — do this first
**Coder:** SP coder

---

## Goal

Run all DB migrations needed for FEAT-03, FEAT-05, FEAT-06 to build on.
Zero logic changes. Migrations only.

---

## Tasks

### V7 — User MFA Columns + Role Permission Fix

File: `db/V7__user_mfa_columns.sql`

```sql
-- MFA columns on users
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS phone_number        TEXT,           -- PII: visible in DB, encrypt at app layer in future
    ADD COLUMN IF NOT EXISTS phone_verified      BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS mfa_enabled         BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS mfa_method          TEXT,
    ADD COLUMN IF NOT EXISTS password_changed_at TIMESTAMPTZ;

-- Backfill password_changed_at for existing users
UPDATE users SET password_changed_at = created_at WHERE password_changed_at IS NULL;

-- ISO 27001 A.9.2.2 — every permission grant must have an owner
-- FIX: backfill nulls FIRST or the ALTER will fail on existing seed data
-- Safety: only update if a platform_owner exists — if not, the DELETE removes orphaned rows
UPDATE role_permissions
SET granted_by = (
    SELECT u.id FROM users u
    JOIN user_app_roles uar ON uar.user_id = u.id
    JOIN roles r ON r.id = uar.role_id
    WHERE r.name = 'platform_owner' AND u.status = 'active'
    LIMIT 1
)
WHERE granted_by IS NULL
  AND EXISTS (
    SELECT 1 FROM users u
    JOIN user_app_roles uar ON uar.user_id = u.id
    JOIN roles r ON r.id = uar.role_id
    WHERE r.name = 'platform_owner' AND u.status = 'active'
  );

-- If still null after backfill (no platform_owner found), delete the orphaned rows
DELETE FROM role_permissions WHERE granted_by IS NULL;

ALTER TABLE role_permissions
    ALTER COLUMN granted_by SET NOT NULL;
```

---

### V8 — MFA Challenges Table

File: `db/V8__mfa_challenges.sql`

```sql
CREATE TABLE IF NOT EXISTS mfa_challenges (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    phone_number TEXT        NOT NULL,
    otp_hash     TEXT        NOT NULL,
    expires_at   TIMESTAMPTZ NOT NULL,
    verified_at  TIMESTAMPTZ,
    attempts     INT         NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_mfa_challenges_user   ON mfa_challenges(user_id);
CREATE INDEX IF NOT EXISTS idx_mfa_challenges_active ON mfa_challenges(user_id, verified_at)
    WHERE verified_at IS NULL;

-- Seed config entries
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('mfa_otp_expiry_minutes', '10', 'OTP validity window in minutes'),
    ('mfa_otp_max_attempts',   '3',  'Max wrong OTP attempts before challenge is invalidated'),
    ('mfa_otp_length',         '6',  'OTP digit count')
ON CONFLICT (config_key) DO NOTHING;
```

---

### V9 — Identity Verification Status Table

File: `db/V9__identity_verification_status.sql`

```sql
CREATE TABLE IF NOT EXISTS identity_verification_status (
    id             UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        UUID        NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    otp_verified   BOOLEAN     NOT NULL DEFAULT false,
    video_verified BOOLEAN     NOT NULL DEFAULT false,
    fully_verified BOOLEAN     NOT NULL DEFAULT false,  -- FIX: plain boolean, NOT generated
                                                        -- computed by IdentityVerificationService
                                                        -- using require_video_verification config
    verified_at    TIMESTAMPTZ,
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_ivs_user ON identity_verification_status(user_id);

-- Config flag: flip to true when video verification is ready to go live
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('require_video_verification', 'false', 'When true, both OTP and video must pass for full verification')
ON CONFLICT (config_key) DO NOTHING;
```

> **Note:** `video_verified` column exists now but stays false until FEAT-08 (video) is built.
> `require_video_verification = false` means `fully_verified = otp_verified` effectively.

---

### V10 — Admin Audit Log Table

File: `db/V10__admin_audit_log.sql`

```sql
CREATE TABLE IF NOT EXISTS admin_audit_log (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_id     UUID        NOT NULL REFERENCES users(id),
    actor_email  TEXT        NOT NULL,
    action       TEXT        NOT NULL,   -- e.g. 'user.create', 'role.grant', 'app.deactivate'
    target_type  TEXT        NOT NULL,   -- e.g. 'user', 'role', 'app'
    target_id    UUID,
    before_state JSONB,
    after_state  JSONB,
    ip_address   TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_admin_audit_actor  ON admin_audit_log(actor_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_target ON admin_audit_log(target_type, target_id);
CREATE INDEX IF NOT EXISTS idx_admin_audit_time   ON admin_audit_log(created_at DESC);

-- Immutable — no one can modify or delete audit records
REVOKE UPDATE, DELETE ON admin_audit_log FROM PUBLIC;

-- Retention config — ISO 27001 requires a defined retention period
INSERT INTO security_config (config_key, config_value, description) VALUES
    ('audit_log_retention_days', '1095', 'Days to retain admin_audit_log rows (default 3 years)')
ON CONFLICT (config_key) DO NOTHING;
```

---

## Verification

After running all 4 migrations, confirm:

```sql
-- Check columns exist
SELECT column_name FROM information_schema.columns
WHERE table_name = 'users' AND column_name IN ('phone_number','mfa_enabled','password_changed_at');

-- Check tables exist
SELECT table_name FROM information_schema.tables
WHERE table_name IN ('mfa_challenges','identity_verification_status','admin_audit_log');

-- Check config entries
SELECT config_key, config_value FROM security_config
WHERE config_key IN ('mfa_otp_expiry_minutes','require_video_verification','audit_log_retention_days');

-- Confirm granted_by is NOT NULL
SELECT COUNT(*) FROM role_permissions WHERE granted_by IS NULL; -- must be 0
```

---

## Notes

- Run migrations in order: V7 → V8 → V9 → V10
- Do NOT add any C# code in this feature branch
- Merge to `develop` before FEAT-03, FEAT-05, FEAT-06 branches are started
