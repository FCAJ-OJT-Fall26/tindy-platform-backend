# Development demo seed — Code First

The opt-in dataset extends the existing `Persistence/Seeding` pipeline. It inserts actual PostgreSQL rows consumed by the normal APIs; it is not a frontend mock. No model changes or new EF migrations are required.

## Dataset

- Catalog: 35 additional skills (84 total), three additional categories (10 total), 102 category/skill links. Existing catalog IDs and names are preserved; existing normalized skill names are reused.
- Users: `demo01` through `demo20`, email `demo01@tindy.test` through `demo20@tindy.test`. These are synthetic profiles with explicit Demo labels, varied preferred roles, 5–6 skills, 5–25 hours/week, universities, cities and learning goals.
- Every user has local BCrypt credentials using the existing password hasher, the Member RBAC role, a completed onboarding flag and all seven onboarding records. They are not granted admin permissions. Email ownership is not marked verified.
- Each user has two Experiences (one historical, one ongoing) and three technologies per Experience: 40 Experiences and 120 experience/skill links total.
- Users alternate between one and two Certificates: 30 metadata-only demo certificates. Issuer is the fictional Tindy Demo Academy; no actual certification, media file or upload/scanner status is fabricated.
- Projects: 50 distinct Projects across six UI categories and ten subject tracks. `demo01`–`demo06` are Leaders; they retain the same Member RBAC role and may apply to other Leaders' Projects.
- All 50 start recruiting, with one active Leader, capacity seven, three Positions with two slots each, and required/optional skills: 150 Positions and 600 Position-skill links. Recruitment deadlines are future Vietnam dates of +7/+14/+30/+45 days from the first run. Commitment, difficulty and duration vary.
- No Applications, saved or skipped flags are pre-created, leaving the feed available for manual testing. Existing users, Projects and interactions are never reset.

| Login | Preferred role | Leads seeded Projects |
| --- | --- | --- |
| demo01 | Backend Developer (.NET) | Yes |
| demo02 | Frontend Developer (React) | Yes |
| demo03 | Cloud Engineer | Yes |
| demo04 | Mobile Developer (Flutter) | Yes |
| demo05 | AI Engineer | Yes |
| demo06 | UI/UX Designer | Yes |
| demo07 | Backend Developer (Java) | No |
| demo08 | Frontend Developer (Vue) | No |
| demo09 | Full-stack Developer (Node) | No |
| demo10 | QA Engineer | No |
| demo11 | DevOps Engineer | No |
| demo12 | Data Engineer | No |
| demo13 | Mobile Developer (React Native) | No |
| demo14 | Business Analyst | No |
| demo15 | Backend Developer (Python) | No |
| demo16 | Frontend Developer (Angular) | No |
| demo17 | Full-stack Developer (Next.js) | No |
| demo18 | AI Engineer | No |
| demo19 | QA Engineer | No |
| demo20 | UI/UX Designer | No |

## Run with the existing EF seeding mechanism

Configure the normal Development connection using User Secrets as before. `DevelopmentSeed` defaults to disabled. Enabling it outside Development fails immediately. A separate development-only password is required (12 characters minimum, 72 UTF-8 bytes maximum, uppercase/lowercase/digit/symbol); no default password is committed.

PowerShell 7, repository root:

```powershell
dotnet build
$env:DevelopmentSeed__Enabled = 'true'
$env:DevelopmentSeed__Password = Read-Host 'Password for local demo accounts' -MaskInput
try {
    dotnet ef database update `
        --project ./src/Tindy.Infrastructure `
        --startup-project ./src/Tindy.Api `
        --no-build `
        -- --environment Development
} finally {
    Remove-Item Env:DevelopmentSeed__Enabled -ErrorAction SilentlyContinue
    Remove-Item Env:DevelopmentSeed__Password -ErrorAction SilentlyContinue
}
```

Both EF `UseSeeding` and `UseAsyncSeeding` invoke the same optional dataset after the existing Catalog seeders. EF database update invokes seeding even when there are no pending migrations. Keep the explicit flags limited to the seed command rather than leaving them enabled for ordinary application startup.

The dataset uses stable dedicated IDs. Re-running inserts missing root aggregates only. It does not restore deleted child rows, reset edited profiles/passwords, reopen archived Projects, extend old deadlines, or erase test Applications. Reserved demo username/email collisions with unrelated accounts fail rather than adopting those accounts. New development aggregates are saved in a single transaction; a failed graph insert rolls back all new demo users and Projects.

No database drop/reset is needed. Catalog additions are normal shared seed data; user and Project demo data requires opt-in. Existing default seed callers still seed only the Catalog.

## Verify / use

Login with a username or the corresponding `@tindy.test` email and the password supplied when the accounts were first inserted. Use a non-Leader such as `demo20` to see all 50 seeded Projects; unrelated existing eligible Projects can also appear. Seed Leaders do not see their own led Projects in Discovery. Pending applications, active membership and Skip continue to exclude cards through the normal Backend rules.

```sql
SELECT count(*) FROM users WHERE user_id::text LIKE 'd3e00000-0001-%';       -- 20
SELECT count(*) FROM projects WHERE project_id::text LIKE 'd3e00000-0002-%'; -- 50
SELECT count(*) FROM profile_experiences WHERE user_id::text LIKE 'd3e00000-0001-%'; -- 40
SELECT count(*) FROM user_certificates WHERE user_id::text LIKE 'd3e00000-0001-%';    -- 30
```

These are deterministic initial counts. Later manual test actions may legitimately change child counts or eligibility. Changes to credentials supplied on a rerun do not reset existing users' passwords.

## Verification scope

`DevelopmentSeedingTests` exercises a real isolated PostgreSQL database and ASP.NET HTTP endpoints: deliberate insert failure/rollback, graph integrity, actual password login and profile counts, all 50 cards through 20/20/10 cursor pages with varying Rule-Based scores, and both sync/async reruns preserving edited credentials/profile, archived/expired Project and unrelated rows. Options tests check default-off, environment and password guards. The catalog integrity tests continue to exercise idempotency.

The seed does not call AI, send email, create OAuth identities or manufacture certificate attachments. Experiences/certificates remain metadata and do not grant match-score bonuses.

## Execution result — 2026-10-30

- Ran EF `database update` with the Development opt-in against the inspected local database `localhost:5432/tindy`, twice. All existing migrations were already applied; there were no schema changes.
- Counts after the seed: **21 total users (20 new), 51 total Projects (50 new), 84 skills, 10 categories, 102 category/skill links, 20 new profiles, 40 Experiences, 30 Certificates, 150 new Positions**.
- Fingerprints of the original users, profiles, credentials, user skills, Projects, Positions, Position skills and memberships matched before/after. The second seed run produced the same counts and fingerprints.
- A real local API host connected to this database successfully logged in `demo20@tindy.test`, returned two Experiences/two Certificates, and served all **50 seeded Projects plus one existing eligible Project** through cursor pages **20/20/11** with four distinct Rule-Based scores. No Apply, Save or Skip writes were made by this verification.
- `dotnet build`: passed, zero warnings/errors. `dotnet test`: **912 passed, 2 existing opt-in live ClamAV tests skipped, 0 failed** (914 total). EF `migrations has-pending-model-changes`: no changes. `git diff --check`: passed.
- Actual demo credentials are provided separately to the operator and stored only in the Git-ignored `.tmp-development-seed/accounts.json`; there is no committed default password. Snapshot/HTTP verification artifacts are in that same ignored directory. No frontend source changed for this task.
