-- STEP 07 — READ-ONLY CHECKS: run only against the intended PostgreSQL database.
-- No DDL, DML, DROP, TRUNCATE or DELETE.

-- 1) EF migration history (should include six migrations, with the two new names).
SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id;

-- 2) Business tables (expected count: 14; excludes EF migration history).
SELECT COUNT(*) AS business_tables
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
  AND table_name <> '__EFMigrationsHistory';

SELECT table_name FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;

-- 3) PreferredRole FK migration: preferred_role must be absent.
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'public'
  AND ((table_name = 'user_profiles' AND column_name IN ('preferred_role', 'preferred_role_id'))
       OR (table_name = 'skills' AND column_name = 'is_active'))
ORDER BY table_name, column_name;

-- 4) Seed counts: with no pre-existing data expect 11, 7, 49, 54.
SELECT 'professional_roles' AS entity, COUNT(*) AS total FROM professional_roles
UNION ALL SELECT 'skill_categories', COUNT(*) FROM skill_categories
UNION ALL SELECT 'skills', COUNT(*) FROM skills
UNION ALL SELECT 'skill_category_skills', COUNT(*) FROM skill_category_skills;

-- 5) Unique name protection and FK constraints.
SELECT tablename, indexname, indexdef FROM pg_indexes
WHERE schemaname='public' AND tablename IN
('professional_roles','skill_categories','skills','skill_category_skills','user_profiles')
ORDER BY tablename, indexname;

SELECT tc.table_name, tc.constraint_name, tc.constraint_type
FROM information_schema.table_constraints tc
WHERE tc.table_schema='public' AND tc.table_name IN
('user_profiles','skill_categories','skill_category_skills','user_skills','user_onboarding_steps')
ORDER BY tc.table_name, tc.constraint_name;

-- 6) Onboarding: Step 8 must not exist as a marker.
SELECT step_number, COUNT(*) AS count_rows
FROM user_onboarding_steps GROUP BY step_number ORDER BY step_number;

-- 7) No orphaned user skill links.
SELECT COUNT(*) AS orphan_user_skills
FROM user_skills us
LEFT JOIN user_profiles up ON up.user_id=us.user_id
LEFT JOIN skills s ON s.skill_id=us.skill_id
WHERE up.user_id IS NULL OR s.skill_id IS NULL;
