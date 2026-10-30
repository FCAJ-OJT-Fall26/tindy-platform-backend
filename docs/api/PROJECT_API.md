# Project CRUD contract

Project routes and response envelopes remain unchanged. Successful responses use
`{ "success": true, "message": "...", "data": ... }`; errors retain the existing
ProblemDetails format and `error_code`.

## Creation

`POST /api/projects` requires a valid JWT, an active account and completed
onboarding. Example request (replace the role and skill UUIDs with active catalog
IDs):

```json
{
  "name": "Tindy REST API",
  "tagline": "Build together",
  "description": "Build a REST API for collaborative student projects.",
  "category": "Web Application",
  "difficulty_level": "intermediate",
  "expected_commitment_hours": 10,
  "duration": "3 months",
  "initial_team_size": 1,
  "max_team_capacity": 5,
  "color_theme": "indigo",
  "recruitment_closing_date": "2026-11-12",
  "goals": ["Deliver a working API"],
  "positions": [
    {
      "role_id": "33333333-3333-4333-8333-333333333333",
      "title": "Backend Developer",
      "description": "Develop REST APIs using .NET",
      "required_count": 2,
      "experience_level": "intermediate",
      "skills": [
        {
          "skill_id": "44444444-4444-4444-8444-444444444444",
          "is_required": true
        }
      ]
    }
  ]
}
```

- `positions[].description` is optional, accepts `null`, and is trimmed before
  persistence. Omitting it stores `null`; a whitespace-only value stores an empty
  string, consistently with standalone Position creation. Project detail responses
  include the saved description in `data.positions[].description`.
- `initial_team_size` must be `1`. A new Project creates exactly one active Leader
  membership. Other submitted values return HTTP 400, `VALIDATION_FAILED`, with
  detail: `Initial team size must be 1. A new project starts with only its leader.`
- `max_team_capacity` must be at least `1`. On update it cannot be smaller than
  the active member count or the stored initial team size, including historical
  Projects (`PROJECT_CAPACITY_EXCEEDED`). Historical rows are not rewritten.
- Project names have 3–150 trimmed characters; descriptions have at least 20;
  expected commitment hours are 1–40. Creation requires at least one Position.
- Position titles have 2–100 trimmed characters and `required_count >= 1`.
  Role and skill IDs must identify active catalog entries. Invalid UUIDs are
  rejected by request binding, never replaced with another catalog ID.
- Repeating a skill ID within one Position during Project creation returns HTTP
  400, `VALIDATION_FAILED`. Adding an existing skill through the standalone
  Position skill endpoint retains its existing behavior: update the existing link.

## Public responses and authorization

POST creation, GET list, GET detail and PUT update expose only these Leader fields:

```json
{
  "user_id": "11111111-1111-4111-8111-111111111111",
  "display_name": "Leader Name",
  "avatar_url": "https://example.com/avatar.png",
  "initials": "LN"
}
```

Neither `leader.email` nor `leader.primary_email` is part of the contract.
`current_team_size` counts actual active Project Members.

Anonymous GET access to `/api/projects`, `/api/projects/{project_id}` and
`/api/projects/{project_id}/positions` is preserved. All write routes retain the
existing authentication/account/onboarding policies. Project update/archive and
Position create/update/delete/skill add/remove require the actual `LeaderId`.

DELETE Project archives it, sets `archived_at`, and closes all associated
Positions. Archived Projects cannot be modified. Existing HTTP statuses remain:
201 for create/add skill, 200 for successful reads/updates/deletes, 401 for
unauthenticated writes, 403 for denied access, and 404 for unavailable Projects or
Positions. Authentication and Profile contracts are unchanged.

## Recruitment deadline (STEP 01)

| Endpoint | Deadline contract |
| --- | --- |
| `POST /api/projects` | Optional `recruitment_closing_date` (`YYYY-MM-DD`). Omitted/null defaults to today's Vietnam date + 14 calendar days. Returns deadline fields, HTTP 201. |
| `PUT /api/projects/{project_id}` | Optional `recruitment_closing_date`. Omitted/null preserves the existing instant, including legacy midday cutoffs. An explicit future date replaces the deadline; expired projects may be extended. |
| `GET /api/projects` | Each card includes the deadline fields. General browsing remains public and may include expired recruiting projects. `sort_by=deadline` uses the actual UTC cutoff. |
| `GET /api/projects/{project_id}` | Includes deadline fields. Expiry does not prevent reads or edits and does not mutate lifecycle `status`. |

All four responses add these fields inside the existing `data` envelope (inside
`data.items[]` for list):

```json
{
  "recruitment_closing_date": "2026-11-12",
  "recruitment_closes_at": "2026-11-12T17:00:00Z",
  "is_recruitment_open": true,
  "closing_days_left": 13
}
```

This example is evaluated on 2026-10-30 in Vietnam. Closing date 2026-11-12
means an **exclusive** cutoff at 2026-11-13 00:00 in `Asia/Ho_Chi_Minh`, or
2026-11-12 17:00 UTC. A request exactly at the cutoff is closed. The server uses
Vietnam's calendar date regardless of its host timezone. Windows timezone
fallback is `SE Asia Standard Time`.

Create and explicit edit require the earliest date to be **tomorrow in Vietnam**.
Client presets 7/14/30 calculate a calendar date and send the same field; arbitrary
future custom dates use the same validation. `9999-12-31` is rejected because its
following midnight cannot be represented. Date/time strings and invalid calendar
dates return HTTP 400 `VALIDATION_FAILED`, as do today/past dates. Example:

```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "Recruitment closing date must be a future date (earliest tomorrow in Asia/Ho_Chi_Minh) before 9999-12-31.",
  "instance": "/api/projects",
  "error_code": "VALIDATION_FAILED"
}
```

Binding errors retain the existing validation ProblemDetails `errors` shape.
Existing 401/403 account, onboarding and actual-project-leader checks apply to
writes. An invalid deadline does not persist a new project or partially edit one.

`closing_days_left` counts calendar days from today's Vietnam date to the
closing date, clamped at zero; it is zero on the closing day even while open.
Use `recruitment_closes_at` / `is_recruitment_open` to determine expiry.
`is_recruitment_open` means lifecycle `recruiting`, `archived_at == null`, and
current UTC time before cutoff. It does **not** establish that a particular member
is eligible or that project/position slots remain; those checks belong to the
later discovery/application steps. Lifecycle values remain `draft`, `recruiting`,
`in_progress`, `completed`, `archived`; expiry adds no stored status.

Legacy JSON field `recruitment_closing_days` is retired and ignored like other
unknown JSON fields. Existing clients that send `recruitment_closing_days: 14`
without a date still create using the default 14 calendar days. Non-default
duration callers must migrate to `recruitment_closing_date`; sending 7/30 in the
retired field does not set a deadline. No response returns a day-count duration.

### Migration and rollout

New migration: `20261030031756_Project_AddRecruitmentDeadline`.

- Adds `projects.recruitment_closes_at timestamptz`, backfills every row using
  the old effective instant `created_at + recruitment_closing_days * 24 hours`
  explicitly in UTC, then makes it NOT NULL. Adds
  `ix_projects_recruitment_closes_at` for cutoff sorting/filtering.
- Backfill preserves legacy cutoffs even at midday and under a session timezone
  with DST. A midday cutoff displays its Vietnam local date; an exact midnight
  cutoff displays the preceding day. The timestamp remains authoritative.
- The old column remains as an EF shadow property solely for migration/rollback
  during rollout. Application logic never uses it. Removing it requires a later
  migration after clients adapt. Auth/Profile migrations are unchanged.
- Migrate before deploying this backend. Older backend writers must be stopped
  during rollout: they do not populate the new required column. This is not a
  mixed-version rolling-write migration.
- Down preserves old rows and refuses a lossy rollback if any new/edited cutoff
  differs from the legacy duration. Back up and reconcile those deadlines before
  downgrading; do not silently discard or shift calendar deadlines.

Personalized Discovery, rule-v1 matching, saved/skipped persistence, Applications,
atomic Leader review and real pending counts are documented in
[DISCOVERY_API.md](DISCOVERY_API.md). They use dedicated authenticated endpoints;
general Project browsing remains public.

## Verification

`ProjectServiceTests` and `ProjectPositionServiceTests` exercise service behavior.
`ProjectHttpContractTests` exercises the real HTTP/JWT/authorization/serialization
pipeline with an in-memory repository and account-status reader; it does not prove
PostgreSQL persistence. Run all required checks with:

```sh
dotnet restore Tindy.slnx
dotnet build Tindy.slnx
dotnet test Tindy.slnx
```

Project service/HTTP checks can run without PostgreSQL:

```sh
dotnet test Tindy.slnx --filter "FullyQualifiedName~Tindy.Tests.Projects&FullyQualifiedName!~RecruitmentDeadlineMigrationTests"
```

`RecruitmentDeadlineTests` adds deterministic clock, midnight, presets, edit,
legacy-date and lifecycle checks. `ProjectHttpContractTests` checks the deadline
JSON contract and validation through the existing real JWT/HTTP pipeline.
`RecruitmentDeadlineMigrationTests` uses a real PostgreSQL DB for backfill,
up/down, rollback guard, profile-history preservation and repository cutoff sort.
It uses the existing Testcontainers helper by default, or the existing
`TINDY_TEST_POSTGRES` loopback-only test-cluster setting. Each test creates and
deletes its own random database. It does not migrate the configured application DB.
