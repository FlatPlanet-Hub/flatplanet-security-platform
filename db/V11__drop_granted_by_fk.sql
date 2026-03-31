ALTER TABLE user_app_roles DROP CONSTRAINT user_app_roles_granted_by_fkey;
ALTER TABLE user_app_roles ALTER COLUMN granted_by DROP NOT NULL;
