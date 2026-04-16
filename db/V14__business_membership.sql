-- =============================================================================
-- FlatPlanet Security Platform — V14: Business Membership
-- Adds short code to companies and multi-business membership table
-- =============================================================================

-- Add short code to companies table
ALTER TABLE companies ADD COLUMN IF NOT EXISTS code TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS uq_companies_code ON companies(code) WHERE code IS NOT NULL;

-- Multi-business membership table
CREATE TABLE IF NOT EXISTS user_business_memberships (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    company_id  UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    role        TEXT NOT NULL DEFAULT 'member',
    status      TEXT NOT NULL DEFAULT 'active',
    invited_by  UUID REFERENCES users(id),
    joined_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at  TIMESTAMPTZ,
    UNIQUE(user_id, company_id)
);

CREATE INDEX IF NOT EXISTS idx_ubm_user_id    ON user_business_memberships(user_id);
CREATE INDEX IF NOT EXISTS idx_ubm_company_id ON user_business_memberships(company_id);
