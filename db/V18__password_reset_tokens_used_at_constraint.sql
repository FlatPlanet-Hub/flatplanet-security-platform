ALTER TABLE password_reset_tokens
    ADD CONSTRAINT chk_used_at CHECK (used = false OR used_at IS NOT NULL);
