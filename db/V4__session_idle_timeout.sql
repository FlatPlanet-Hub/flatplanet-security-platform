-- Phase 8: add idle_timeout_minutes to sessions
ALTER TABLE sessions ADD COLUMN idle_timeout_minutes INTEGER NOT NULL DEFAULT 30;
