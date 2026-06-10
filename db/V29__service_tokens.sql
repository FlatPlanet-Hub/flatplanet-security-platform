-- V29 — Per-service tokens (Phase 1)
--
-- Replaces the single shared `ServiceToken__Token` with a database-backed
-- registry of per-service tokens. Each token has its own identity, scope set,
-- and revocation status. The legacy single-token validator path remains
-- active during Phase 1 — this migration is purely additive.
--
-- Token format (plaintext returned to admin on mint, then discarded):
--   fps_<service-slug>_<43-char base64url of 32 random bytes>
-- Stored as the hex-encoded SHA-256 of the plaintext (input is full-domain
-- random → bcrypt's slowness buys nothing; SHA-256 is correct here).
--
-- Special scope `bootstrap` matches any RequireScope check — used for new
-- services during onboarding before scopes are narrowed.
--
-- Run against SP database (Supabase: aws-1-ap-southeast-1.pooler.supabase.com:5432).

BEGIN;

CREATE TABLE IF NOT EXISTS service_tokens (
  id            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  service_name  TEXT        NOT NULL UNIQUE,
  token_hash    TEXT        NOT NULL UNIQUE,
  scopes        TEXT[]      NOT NULL DEFAULT '{}',
  description   TEXT,
  status        TEXT        NOT NULL DEFAULT 'active',
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  created_by    UUID        REFERENCES users(id) ON DELETE SET NULL,
  revoked_at    TIMESTAMPTZ,
  revoked_by    UUID        REFERENCES users(id) ON DELETE SET NULL,
  last_used_at  TIMESTAMPTZ,
  CONSTRAINT service_tokens_status_check CHECK (status IN ('active', 'revoked')),
  CONSTRAINT service_tokens_service_name_format CHECK (service_name ~ '^[a-z][a-z0-9-]{1,49}$'),
  CONSTRAINT service_tokens_token_hash_format CHECK (length(token_hash) = 64),
  CONSTRAINT service_tokens_revoked_consistency CHECK (
    (status = 'revoked' AND revoked_at IS NOT NULL) OR
    (status = 'active'  AND revoked_at IS NULL)
  )
);

-- Lookup index: validator reads by token_hash on every server-to-server request.
-- Partial index on status='active' keeps it tight and skips revoked rows.
CREATE INDEX IF NOT EXISTS ix_service_tokens_token_hash_active
  ON service_tokens(token_hash)
  WHERE status = 'active';

-- Service-name index for admin listing / lookup
CREATE INDEX IF NOT EXISTS ix_service_tokens_service_name
  ON service_tokens(service_name);

COMMENT ON TABLE service_tokens IS
  'Per-service authentication tokens used by trusted backends (HubApi, etc) to call SP. '
  'Replaces the single shared ServiceToken__Token. token_hash stores the hex-encoded SHA-256 '
  'of the plaintext token, which is shown to the admin exactly once at mint time. '
  'Scope `bootstrap` matches any RequireScope check.';
COMMENT ON COLUMN service_tokens.service_name  IS 'Slug identifying the calling service (e.g. hub-api). Unique. Lowercase, alphanumeric + hyphens, 2-50 chars.';
COMMENT ON COLUMN service_tokens.token_hash    IS 'Hex-encoded SHA-256 of the plaintext token. 64 chars.';
COMMENT ON COLUMN service_tokens.scopes        IS 'Permitted scopes (e.g. users:read, apps:write). Scope `bootstrap` is a wildcard for onboarding.';
COMMENT ON COLUMN service_tokens.status        IS 'active or revoked. Validator rejects revoked tokens (subject to a 60s cache).';
COMMENT ON COLUMN service_tokens.last_used_at  IS 'Updated fire-and-forget on each successful validation. May lag by cache TTL.';

-- ── Verification assertions (rollback on mismatch) ──────────────────────────

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = current_schema() AND table_name = 'service_tokens'
  ) THEN
    RAISE EXCEPTION 'V29 failed: service_tokens table was not created';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE schemaname = current_schema()
      AND tablename = 'service_tokens'
      AND indexname = 'ix_service_tokens_token_hash_active'
  ) THEN
    RAISE EXCEPTION 'V29 failed: ix_service_tokens_token_hash_active index missing';
  END IF;
END $$;

COMMIT;

-- ── Rollback (manual, if ever needed) ──────────────────────────────────────
-- BEGIN;
-- DROP TABLE IF EXISTS service_tokens;
-- COMMIT;
