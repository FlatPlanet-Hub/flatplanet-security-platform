-- Phase 10: standalone auth — own the full authentication stack
-- Give users a generated UUID by default (was relying on Supabase to supply the id)
ALTER TABLE users ALTER COLUMN id SET DEFAULT gen_random_uuid();

-- Add password_hash — bcrypt hash stored here, never plaintext
ALTER TABLE users ADD COLUMN password_hash TEXT NOT NULL DEFAULT '';

-- Remove the placeholder default after column is added
ALTER TABLE users ALTER COLUMN password_hash DROP DEFAULT;
