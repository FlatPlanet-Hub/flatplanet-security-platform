-- V16: Remove test/sample apps and all related data
-- Cleans up: testhub, githubtest, high-five, ting, sample, sample-two, sample-three, sample-four, sample-five, case-test

DO $$
DECLARE
    test_slugs TEXT[] := ARRAY['testhub','githubtest','high-five','ting','sample','sample-two','sample-three','sample-four','sample-five','case-test'];
    test_app_ids UUID[];
    test_role_ids UUID[];
BEGIN
    -- Collect app IDs
    SELECT ARRAY_AGG(id) INTO test_app_ids FROM apps WHERE slug = ANY(test_slugs);

    IF test_app_ids IS NULL OR array_length(test_app_ids, 1) = 0 THEN
        RAISE NOTICE 'No test apps found — skipping cleanup.';
        RETURN;
    END IF;

    -- Collect role IDs for those apps
    SELECT ARRAY_AGG(id) INTO test_role_ids FROM roles WHERE app_id = ANY(test_app_ids);

    -- 1. Delete user_app_roles for these apps
    DELETE FROM user_app_roles WHERE app_id = ANY(test_app_ids);

    -- 2. Delete role_permissions for roles in these apps
    IF test_role_ids IS NOT NULL AND array_length(test_role_ids, 1) > 0 THEN
        DELETE FROM role_permissions WHERE role_id = ANY(test_role_ids);
    END IF;

    -- 3. Delete permissions for these apps
    DELETE FROM permissions WHERE app_id = ANY(test_app_ids);

    -- 4. Delete roles for these apps
    DELETE FROM roles WHERE app_id = ANY(test_app_ids);

    -- 5. Nullify app_id in sessions (keep session records, just unlink)
    UPDATE sessions SET app_id = NULL WHERE app_id = ANY(test_app_ids);

    -- 6. Nullify app_id in auth_audit_log
    UPDATE auth_audit_log SET app_id = NULL WHERE app_id = ANY(test_app_ids);

    -- 7. Delete the apps themselves
    DELETE FROM apps WHERE id = ANY(test_app_ids);

    RAISE NOTICE 'Cleaned up % test apps.', array_length(test_app_ids, 1);
END;
$$;
