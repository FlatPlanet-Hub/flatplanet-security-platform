-- V22: Remove plaintext refresh token storage (security fix S1)
ALTER TABLE refresh_tokens DROP COLUMN IF EXISTS replaced_by_token_plain;
