# Tindy Discovery V1 backend — STEP 01–07 delivery

Date: 2026-10-30 (Asia/Ho_Chi_Minh). Baseline commit:
`6d10825095767361233cc0362faa7416a7458169`.

## Progress and scope

| Step | Delivered |
| --- | --- |
| 01 — Deadline | Date-only create/edit, injected clock/Vietnam timezone, exclusive UTC cutoff, exact legacy backfill, safe rollback guard. |
| 02 — Eligibility | Focused batched read repository; own/joined/pending/expired/full/no-slot/skipped exclusion; GUID filters; caller-scoped profile. General public browsing is preserved. |
| 03 — Scorer | Pure deterministic rule-v1, required/role/hours/optional factors, missing-data normalization, facts-only explanation, stable Position tie-break. |
| 04 — Feed | Authorized endpoint, rank all candidates, feed-v1 60/25/15 mix, signed per-account snapshot cursors, limit clamp, stable unique pagination and refresh. |
| 05 — Saved/skipped | Persisted account/project flags, race-safe independent UPSERTs, idempotent endpoints, paginated saved/closed summaries and account isolation. |
| 06 — Applications | Immutable terminal history, immediate same-Position re-apply, pending-only DB uniqueness, actual Leader review, applied-Position matching, transactional approvals/membership/slots, real pending/joined counts. |
| 07 — Verification/docs | Real HTTP/PostgreSQL flow, schema, 1000-project pagination/query counts, concurrent approvals/withdraw/reject/capacity edits, rollback injection and contract documentation. Final full-suite result is recorded below. |

The original checkpoint stopped after STEP 01. This continuation implements
STEP 02–07; the old three baseline failures were subsequently resolved with the
small corrections described below. No Discovery/Matching/Explain Match code
uses AI, HTTP providers, embeddings, vector DBs or Bedrock. Existing AI profile
modules remain in place. No candidate search/invites, chat, notifications,
automatic approvals, Redis or queues were added. Frontend is outside this
backend delivery and was not changed.

## API contract

The complete endpoint table, request/response JSON, errors, enums, cursor
semantics and rollout are in [DISCOVERY_API.md](DISCOVERY_API.md). Deadline
details and compatibility remain in [PROJECT_API.md](PROJECT_API.md).

- New API groups: `/api/discovery/projects`, `/api/me/discovery/saved`,
  `/api/me/discovery/skipped`, `/api/projects/{project_id}/applications`,
  `/api/me/project-applications`.
- All use existing JWT/active-account/completed-onboarding authorization and
  snake_case success/ProblemDetails envelopes. Leader access uses actual
  project ownership. Member/card/review DTOs whitelist public fields; privacy
  canaries, private email and phone fields are checked in HTTP tests.
- Saved never means applied. Application rows are the only source of Applied
  state. Rejected/withdrawn rows remain unchanged when a new application is
  submitted immediately. At most one pending row exists per project/applicant,
  regardless of Position. There is no cooldown or historical attempt limit.
- Accept locks Project then Position then Application in one transaction;
  slots, membership and status commit together. Reject/withdraw use the same
  serialization and never touch membership/slots. Duplicate terminal decisions
  return 409. Existing pending applications can be accepted after deadline.
- Capacity and Position edits/deletes use the same Project lock and fresh DB
  counts. Position history/member links cannot be destroyed. Reactivation
  reuses the composite membership row. Leader membership keeps Position null.

## Migrations

| New migration | Purpose |
| --- | --- |
| `20261030031756_Project_AddRecruitmentDeadline` | Required timestamptz cutoff, exact UTC legacy backfill/index and guarded downgrade. |
| `20261030034108_Discovery_CreateProjectInteractions` | Saved/skipped flags keyed by caller/project, FKs and saved partial index. |
| `20261030034750_Project_CreateApplicationsAndMemberPositions` | Application history, four-status check, FKs/indexes, `ux_project_applications_pending` filtered to pending only; nullable member Position and composite project/Position FKs. |

Existing committed migrations were not edited/deleted. Snapshot/Designers match
the current model. Up/Down/Up checks preserve pre-existing Project/Leader data;
deadline backfill additionally preserves profile/experience/certificate data
and exact midday timestamps under a DST-observing PostgreSQL session.

Only disposable test databases were migrated. Apply the three migrations to
the intended application DB before deploying; older writers must be stopped for
the required deadline column. The retired day-count column remains as a legacy
shadow property until clients adapt. Retiring it needs a later migration.
Application/interaction Down drops new feature data; back it up before downgrade.
Deadline Down refuses cutoffs that cannot be represented by the old duration.

Generated rollout SQL: `TestResults/discovery-v1-migrations.sql` (ignored local
verification artifact). No configured application database was modified.

## Verification

Docker daemon was unavailable, so verification used the temporary PostgreSQL 17
cluster bound to 127.0.0.1:55439. Existing TestPostgres creates/drops random
databases through its loopback-only test-admin guard. Process-scoped setting:

```powershell
$env:TINDY_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=tindy_test_admin'
dotnet build Tindy.slnx
dotnet test Tindy.slnx --no-build --logger 'trx;LogFileName=discovery-all.trx'
dotnet test Tindy.slnx --filter FullyQualifiedName~Tindy.Tests.Projects
```

| Check | Result |
| --- | --- |
| `dotnet build Tindy.slnx` | PASS, 0 warnings/errors. |
| Project suite | **157 passed**, 0 failed/skipped. |
| Final full suite | **904 passed, 0 failed, 2 skipped**, total 906; exit code 0. Two skips are the existing real-ClamAV opt-in tests. |
| EF `migrations has-pending-model-changes` | No changes since last migration. |
| EF `migrations list --no-connect` | All three new IDs discovered in order; intentionally does not query application-DB applied status. |
| EF `migrations script` from previous Project migration to latest | Generated and reviewed successfully. |
| Real PostgreSQL migration tests | Fresh migrations, Up/Down/Up, pending migrations empty, snapshot equal, pending partial uniqueness, composite FKs, status check and preserved baseline data. |
| Static feature-code scan | No AI/provider/HTTP/DateTime.Now calls in new Discovery/Application/scorer paths. |

Real PostgreSQL/HTTP evidence includes:

- 1000 eligible projects; oldest highest match ranked beyond a first-50 window;
  full-page bucket counts 12/5/3; all pages deduplicated; cursor retry stable;
  new projects appear on refresh; limit clamps 1..50; skip and closed best
  Position rechecked during an existing session.
- Per-page query counts bounded independently of card count: initial page at
  most six reader commands, subsequent pages at most four in the 1000-project
  test. These are bounded-query checks, not a production throughput benchmark.
- Six immediate reject/withdraw/re-apply cycles to the same Position, preserving
  seven distinct application rows and exactly one pending. Database concurrent
  inserts independently prove the pending-only partial unique constraint.
- Other-project Leader applying/joining; late approval; real pending/joined
  counts; applied-Position score differs from best-project score; null profile
  data handled; authorized existing profile/experience/certificate URLs work.
- Concurrent same-Position and cross-Position approvals cannot exceed Position
  or Project capacity; eight simultaneous approvals fill exactly two slots;
  retries cannot increment again.
- Withdraw-vs-accept, withdraw-vs-reject, duplicate Apply and capacity/Position
  shrink-vs-approval races use separate request scopes/DB connections.
- A database trigger deliberately failing approval proves rollback leaves the
  application pending, no membership and no slot increment.
- Per-account flags, concurrent save/skip, privacy fields, default authorization,
  explicit 429 per-user burst limit, inactive applicant/closed/full acceptance
  failure and `left`/`removed` composite membership reactivation.

### Baseline failures resolved

The previous run reproduced three failures on isolated tracked HEAD. Continuing
verification found that `WebApplicationExtensions` invoked `UseRateLimiter()`
twice. Removing the duplicate invocation before authorization keeps one limiter
after authorization, fixes unauthorized extraction returning 429, and allows
two intended concurrent uploads rather than spending both leases on one request.
The catalog schema test's expected table set was updated from its obsolete 21
tables to include the existing four Project tables and two new feature tables.
The three formerly failing tests and related auth/rate-limit checks now pass.
No Auth/Profile service modules were rewritten.

## Operational limits / checks not run

- Feed snapshots are bounded process-local state, 15 minutes; restart/eviction
  requires refresh. Multiple instances require session affinity. This is the
  documented feed-v1 implementation, without distributed infrastructure.
- No new Leave/Remove endpoint was requested. A future departure workflow must
  release its Position slot before later reactivation; existing left/removed
  rows are reused without duplicating active membership.
- Real ClamAV tests retain their existing opt-in skip unless its live container
  is provided. No real AWS/provider requests were made.
- No frontend edits, npm/browser runs, deployed environment checks or real
  application-DB rollout were performed. The backend contract is ready for the
  separate frontend handoff. No dedicated optional match endpoint is needed.

Local ignored evidence: `tests/Tindy.Tests/TestResults/discovery-projects.trx`,
`tests/Tindy.Tests/TestResults/discovery-all.trx`, migration SQL above, and the
earlier baseline reproduction under `TestResults/step01-baseline`.

## Changed files

The exhaustive list below includes the earlier STEP 01 and this continuation.
The pre-existing untracked `CODEX_BACKEND_PROMPT.md` and
`TINDY_V1_SCOPE_REQUIREMENTS.md` were not edited.

- `docs/api/DISCOVERY_API.md`
- `docs/api/DISCOVERY_V1_BACKEND_PROGRESS.md`
- `docs/api/PROJECT_API.md`
- `src/Tindy.Api/Controllers/DiscoveryController.cs`
- `src/Tindy.Api/Controllers/DiscoveryInteractionsController.cs`
- `src/Tindy.Api/Controllers/MyProjectApplicationsController.cs`
- `src/Tindy.Api/Controllers/ProjectApplicationsController.cs`
- `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs`
- `src/Tindy.Api/Extensions/WebApplicationExtensions.cs`
- `src/Tindy.Application/DTOs/Projects/DiscoveryDtos.cs`
- `src/Tindy.Application/DTOs/Projects/ProjectApplicationDtos.cs`
- `src/Tindy.Application/DTOs/Projects/ProjectDtos.cs`
- `src/Tindy.Application/Entities/DiscoveryProjectInteraction.cs`
- `src/Tindy.Application/Entities/Project.cs`
- `src/Tindy.Application/Entities/ProjectApplication.cs`
- `src/Tindy.Application/Entities/ProjectMember.cs`
- `src/Tindy.Application/Interfaces/Repositories/IDiscoveryInteractionRepository.cs`
- `src/Tindy.Application/Interfaces/Repositories/IDiscoveryRepository.cs`
- `src/Tindy.Application/Interfaces/Repositories/IProjectApplicationRepository.cs`
- `src/Tindy.Application/Services/DiscoveryFeedSessions.cs`
- `src/Tindy.Application/Services/DiscoveryService.cs`
- `src/Tindy.Application/Services/PositionMatchScorer.cs`
- `src/Tindy.Application/Services/ProjectApplicationService.cs`
- `src/Tindy.Application/Services/ProjectPositionService.cs`
- `src/Tindy.Application/Services/ProjectService.cs`
- `src/Tindy.Application/Services/RecruitmentDeadline.cs`
- `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- `src/Tindy.Infrastructure/Persistence/Configurations/DiscoveryProjectInteractionConfiguration.cs`
- `src/Tindy.Infrastructure/Persistence/Configurations/ProjectApplicationConfiguration.cs`
- `src/Tindy.Infrastructure/Persistence/Configurations/ProjectConfiguration.cs`
- `src/Tindy.Infrastructure/Persistence/Configurations/ProjectMemberConfiguration.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030031756_Project_AddRecruitmentDeadline.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030031756_Project_AddRecruitmentDeadline.Designer.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030034108_Discovery_CreateProjectInteractions.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030034108_Discovery_CreateProjectInteractions.Designer.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030034750_Project_CreateApplicationsAndMemberPositions.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261030034750_Project_CreateApplicationsAndMemberPositions.Designer.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/TindyDbContextModelSnapshot.cs`
- `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs`
- `src/Tindy.Infrastructure/Repositories/DiscoveryEligibility.cs`
- `src/Tindy.Infrastructure/Repositories/DiscoveryInteractionRepository.cs`
- `src/Tindy.Infrastructure/Repositories/DiscoveryRepository.cs`
- `src/Tindy.Infrastructure/Repositories/ProjectApplicationRepository.cs`
- `src/Tindy.Infrastructure/Repositories/ProjectRepository.cs`
- `src/Tindy.Infrastructure/Repositories/ProjectWriteLock.cs`
- `tests/Tindy.Tests/Integration/CatalogDatabaseIntegrityTests.cs`
- `tests/Tindy.Tests/Projects/DiscoveryEligibilityTests.cs`
- `tests/Tindy.Tests/Projects/DiscoveryFeedTests.cs`
- `tests/Tindy.Tests/Projects/DiscoveryHttpTests.cs`
- `tests/Tindy.Tests/Projects/PositionMatchScorerTests.cs`
- `tests/Tindy.Tests/Projects/ProjectApplicationConcurrencyTests.cs`
- `tests/Tindy.Tests/Projects/ProjectApplicationHttpTests.cs`
- `tests/Tindy.Tests/Projects/ProjectHttpContractTests.cs`
- `tests/Tindy.Tests/Projects/ProjectServiceTests.cs`
- `tests/Tindy.Tests/Projects/RecruitmentDeadlineMigrationTests.cs`
- `tests/Tindy.Tests/Projects/RecruitmentDeadlineTests.cs`
- `tests/Tindy.Tests/Projects/RecruitmentSchemaTests.cs`
