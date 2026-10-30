# Tindy Discovery and Recruitment V1 — backend contract

Implemented on 2026-10-30. All paths below require the existing JWT + active
account + completed onboarding policy. Leader review requires the actual
`projects.leader_id`; an account can lead A and apply to/join B. Public general
`GET /api/projects` remains separate. All JSON uses snake_case.

## Endpoints

| Method | Path | Result / ownership |
| --- | --- | --- |
| GET | `/api/discovery/projects?limit=20&cursor=...&category=...&role_id=...&skill_id=...` | Personalized eligible cards and matching; 200. |
| PUT / DELETE | `/api/me/discovery/saved/{project_id}` | Idempotent save/unsave for caller; 200. |
| GET | `/api/me/discovery/saved?page=1&page_size=20` | Caller-only saved summaries, including closed/archived status; 200. |
| PUT / DELETE | `/api/me/discovery/skipped/{project_id}` | Idempotent skip/undo for caller; 200. |
| POST | `/api/projects/{project_id}/applications` | New pending application to one Position; 201. |
| GET | `/api/me/project-applications?page=1&page_size=20` | Caller-only history, all statuses; 200. |
| POST | `/api/me/project-applications/{application_id}/withdraw` | Pending owner only; 200. |
| GET | `/api/projects/{project_id}/applications?position_id=...&status=pending&sort_by=match&page=1&page_size=20` | Actual Leader only; 200. |
| POST | `/api/projects/{project_id}/applications/{application_id}/accept` | Actual Leader, pending only; atomic acceptance; 200. |
| POST | `/api/projects/{project_id}/applications/{application_id}/reject` | Actual Leader, pending only; 200. |

Optional standalone match endpoint is not needed: cards include the complete
breakdown, and application review includes the applied Position's breakdown.

## Discovery eligibility

Before scoring, the repository requires lifecycle `recruiting`, no archive,
current UTC time strictly before cutoff, active membership count (including
Leader) below capacity, and at least one open Position with remaining slots.
Exclude the caller's own led projects, active memberships, pending applications
and skipped projects. Rejected/withdrawn history never excludes a project.
Leading another project is allowed. There is no role/skill/score threshold.

`category` matches a catalog-independent project category string after trimming;
`role_id` and `skill_id` bind as GUIDs and match an open, available Position.
When both IDs are supplied they must match the same Position. An unknown valid
GUID produces no matches; malformed GUIDs return 400. No arbitrary ID/name
substitution is performed. Profile input always belongs to the caller.

## rule-v1 positional match

Required skills weight 40; exact preferred role ID 30; availability 20; optional
skills 10. Skill factors are distinct matching IDs divided by distinct Position
IDs in the corresponding group. Availability is `min(1, member_hours / expected_hours)`.
An empty Position skill group drops its weight; an empty member skill set scores
zero for nonempty groups. Unknown availability drops its weight and is reported
as null. Missing preferred role contributes zero and is marked incomplete.
Included weights normalize to 100; decimal calculations are rounded once, with
midpoint away from zero, to integer 0..100.

The best open available Position supplies the project score; ties use Position
ID. Experience level is displayed, and experiences/certificates have no score
bonus. Skill names are labels only. `data_incomplete` is true for missing role,
availability or member skills. Explanations use computed counts and role/hour
facts only. There are no AI, embedding, vector database or Bedrock calls, and the
score is not an acceptance probability.

Example response (illustrative IDs; evaluated on 2026-10-30 in Vietnam):

```json
{
  "success": true,
  "message": "Discovery projects retrieved successfully.",
  "data": {
    "items": [
      {
        "project_id": "11111111-1111-4111-8111-111111111111",
        "name": "Tindy REST API",
        "tagline": "Build together",
        "description": "Build a REST API for collaborative student projects.",
        "category": "Web Application",
        "difficulty_level": "intermediate",
        "expected_commitment_hours": 10,
        "duration": "3 months",
        "color_theme": "indigo",
        "current_team_size": 1,
        "max_team_capacity": 5,
        "leader": {
          "user_id": "22222222-2222-4222-8222-222222222222",
          "display_name": "Project Leader",
          "avatar_url": null,
          "initials": null
        },
        "created_at": "2026-10-30T03:00:00Z",
        "recruitment_closing_date": "2026-11-12",
        "recruitment_closes_at": "2026-11-12T17:00:00Z",
        "is_recruitment_open": true,
        "closing_days_left": 13,
        "match_score": 100,
        "algorithm_version": "rule-v1",
        "best_position": {
          "position_id": "33333333-3333-4333-8333-333333333333",
          "title": "Backend Developer",
          "role_id": "44444444-4444-4444-8444-444444444444",
          "role_name": "Backend Developer",
          "experience_level": "intermediate",
          "required_count": 2,
          "filled_count": 0,
          "remaining_slots": 2
        },
        "match_factors": [
          { "name": "required_skills", "factor": 1, "normalized_weight": 40, "contribution": 40 },
          { "name": "preferred_role", "factor": 1, "normalized_weight": 30, "contribution": 30 },
          { "name": "availability", "factor": 1, "normalized_weight": 20, "contribution": 20 },
          { "name": "optional_skills", "factor": 1, "normalized_weight": 10, "contribution": 10 }
        ],
        "matched_skills": [
          { "skill_id": "55555555-5555-4555-8555-555555555555", "name": "C#", "is_required": true },
          { "skill_id": "66666666-6666-4666-8666-666666666666", "name": "Docker", "is_required": false }
        ],
        "missing_skills": [],
        "data_incomplete": false,
        "explanation": "Required skills: 1/1; optional skills: 1/1. Preferred role matches. Availability: 10 hours/week; project expects 10.",
        "feed_bucket": "best_match",
        "is_saved": false
      }
    ],
    "next_cursor": null,
    "has_more": false,
    "feed_version": "feed-v1"
  }
}
```

`factor` is 0..1 or null, `normalized_weight` is a percentage, and `contribution`
is the weighted score contribution in percentage points. Null factors have zero
weight/contribution. These whitelist DTOs contain no private email, phone,
credentials or OAuth metadata.

## feed-v1 pagination

Rank **all** eligible candidates. Each full page of 20 takes 12 Best Match, then
5 New Projects, then 3 Explore, without duplicates. Explore deterministically
samples the lower half of remaining scores using a snapshot-specific hash.
Other limits round 60% and 25% to nearest integer (midpoint away from zero), with
the rest for Explore. Unused quota fills from remaining score-ranked projects.
Score ties use project ID; New sorts by creation time then ID.

`limit` defaults to 20 and clamps to 1..50. Reuse the returned opaque cursor with
the **same limit and filters**. Signed cursors bind to the caller and the server
snapshot. Ordering, score inputs and explanations are frozen for that snapshot;
eligibility, saved flag, cutoff, capacity and slot counts are rechecked for each
page. If the snapshot's best Position closes/fills, that card is omitted until
refresh, when another available Position may become best. Inspected offsets
advance even when a project becomes ineligible, so subsequent pages do not
repeat cards. New projects require a cursor-free refresh.

V1 uses bounded, process-local snapshots for 15 minutes: up to four per user,
128 globally and 100,000 retained cards globally. Eviction/restart requires
refresh, with 400 `DISCOVERY_CURSOR_EXPIRED` or `INVALID_DISCOVERY_CURSOR`.
Multiple application instances need session affinity for cursor reuse. This is
a documented V1 single-process snapshot strategy, not a distributed feed store.
A single snapshot over 100,000 candidates returns 503
`DISCOVERY_SNAPSHOT_TOO_LARGE`, without truncating the ranking. V1 is tested with
1,000 projects; queries use batched projections rather than per-card reads.

## Saved/skipped

Saved and skipped are independent persisted flags keyed by caller/project.
Saving never creates an application; skipping only hides regular discovery and
does not prohibit an explicit application. Saving does not undo skip. PUTs use
atomic UPSERTs updating only their own flag, including under concurrent save/skip.
DELETEs are idempotent even when the interaction/project no longer exists.
PUT for a nonexistent project returns 404 `PROJECT_NOT_FOUND`.

Saved reads remain available for closed/archived projects, exposing the current
`status`, cutoff and `is_recruitment_open: false` when expired/archived. The open
flag covers lifecycle/archive/deadline; it is not personalized slot eligibility.
Each saved summary has `project_id`, `name`, `tagline`, `category`, `status`,
`recruitment_closes_at`, `is_recruitment_open`, `is_skipped`, `updated_at`.
There is no separate Interested/Applied interaction state.

Saved lists, member application lists and Leader review use `page` (minimum 1),
`page_size` (default 20, clamp 1..50), with overflow-safe offsets and:

```json
{
  "items": [],
  "pagination": {
    "current_page": 1, "page_size": 20, "total_items": 0,
    "total_pages": 0, "has_next": false, "has_previous": false
  }
}
```

## Application lifecycle

Apply body:

```json
{
  "position_id": "33333333-3333-4333-8333-333333333333",
  "message": "I would like to contribute."
}
```

Position ID is required. Message is optional/null, trimmed, maximum 2000
characters before trimming. A successful POST commits a **new** pending row and
returns 201 using the existing success/data envelope:

```json
{
  "success": true,
  "message": "Application submitted.",
  "data": {
    "application_id": "77777777-7777-4777-8777-777777777777",
    "project_id": "11111111-1111-4111-8111-111111111111",
    "position_id": "33333333-3333-4333-8333-333333333333",
    "applicant_id": "88888888-8888-4888-8888-888888888888",
    "status": "pending",
    "message": "I would like to contribute.",
    "created_at": "2026-10-30T03:10:00Z",
    "updated_at": "2026-10-30T03:10:00Z",
    "reviewed_at": null,
    "reviewed_by": null
  }
}
```

Statuses: `pending`, `accepted`, `rejected`, `withdrawn`. Terminal statuses are
immutable through these APIs. Rejected/withdrawn applicants may immediately
POST a new row, including the same Position, without cooldown or attempt cap.
Every prior row, ID, message and timestamps remain in history. At most one
pending row exists per `(project_id, applicant_id)` across all Positions.
Duplicate submissions return 409 `APPLICATION_ALREADY_PENDING`; the database
partial unique index independently enforces this rule. Already-active members
cannot reapply in that project; membership in or leadership of another project
does not block applications here.

Apply checks project/Position eligibility again inside the write transaction,
using the current clock after acquiring the project lock. Reject and withdraw
only update a pending application; they never add membership or change slot
counts. Withdraw hides another account's application with 404. Leader decisions
set `reviewed_at`, `reviewed_by` and `updated_at`; withdraw updates `updated_at`.

Apply has an ordinary per-user fixed-window burst limit of 120 attempts/minute,
zero queue. Excess attempts return 429 `APPLICATION_RATE_LIMITED`. This limiter
does not inspect rejection/withdrawal history and imposes no re-apply delay.
JWT/account/onboarding authorization runs before quotas, and rate limiting runs
once per request.

## Leader review and atomic acceptance

Review filters: `position_id` optional, `status` default `pending` (also
`accepted`, `rejected`, `withdrawn`, `all`), `sort_by` default `match` (also
`newest`). Match sorting scores the **applied** Position using current member and
Position data, including historical applications to closed Positions. Ties use
creation time descending then application ID. Newest uses creation time and ID.
Matching is computed on demand, never persisted as all-pairs score records.

Each review item is:

```json
{
  "application": { "application_id": "77777777-7777-4777-8777-777777777777", "status": "pending" },
  "applicant": {
    "user_id": "88888888-8888-4888-8888-888888888888",
    "display_name": "Applicant",
    "preferred_role_id": "44444444-4444-4444-8444-444444444444",
    "preferred_role_name": "Backend Developer",
    "available_hours_per_week": 10,
    "skills": [],
    "experiences_count": 0,
    "certificates_count": 0,
    "profile_url": "/api/users/88888888-8888-4888-8888-888888888888/profile",
    "experiences_url": "/api/users/88888888-8888-4888-8888-888888888888/profile/experiences",
    "certificates_url": "/api/users/88888888-8888-4888-8888-888888888888/profile/certificates"
  },
  "applied_position": { "position_id": "33333333-3333-4333-8333-333333333333" },
  "matching": { "match_score": 100, "algorithm_version": "rule-v1" }
}
```

The application, Position and matching objects above are abbreviated: application
uses the complete Application DTO shown earlier; `applied_position` includes
project Position role/title/level/status/counts/skills; matching uses the complete
rule-v1 result including `best_position`, factors and explanation, where
`best_position.position_id` is always the applied Position. Applicant skills are
`{skill_id,name}`. Only safe profile summaries/counts and existing authorized
profile links are included, never private account/contact/OAuth data.

Accept begins one database transaction and locks Project, then Position, then
Application. It rechecks actual Leader, pending state, lifecycle `recruiting`,
archive, open Position slots, actual active team size, active/onboarded applicant
and absence of active membership. Existing pending applications may be accepted
**after the deadline**. It creates/reactivates the composite ProjectMember with
the applied Position and role title, non-Leader active status; increments the
slot once; marks Position `filled` when full; and accepts the application in the
same commit. `left`/`removed` memberships are reused; their prior departure is
expected to have released the slot. No new Leave/Remove workflow is introduced.

Repeat or competing terminal decisions return 409 `APPLICATION_NOT_PENDING`.
Concurrent approvals cannot exceed project/Position limits. Reject/withdraw
serialize on the same project lock, so only one competing terminal update
commits. Project capacity and Position edits/deletes acquire the same project
lock and recheck current counts. Reducing required_count below filled_count or
deleting a Position with members or **any** application history is rejected;
close it instead. Composite FKs prevent cross-project Position references.

`GET /api/me/projects` now returns real pending counts for led projects. Accepted
members appear in `joined_projects`; existing team/detail reads include them.
Leader's initial membership retains null Position ID.

## Errors and rollout

Existing ProblemDetails + `error_code` conventions apply. Main codes:

| HTTP | Code / case |
| --- | --- |
| 400 | `VALIDATION_FAILED`: malformed body/query UUID/date, message too long, invalid status/sort. |
| 400 | `INVALID_DISCOVERY_CURSOR`, `DISCOVERY_CURSOR_EXPIRED`: refresh feed. |
| 401 / 403 | Existing JWT/account/onboarding policies. |
| 403 | `PROJECT_ACCESS_DENIED`: another user attempting Leader review/decision. |
| 404 | `PROJECT_NOT_FOUND`, `APPLICATION_NOT_FOUND` (including another owner's withdraw). |
| 409 | `APPLICATION_ALREADY_PENDING`, `APPLICATION_NOT_PENDING`. |
| 409 | `PROJECT_NOT_ELIGIBLE`: new application fails common eligibility. |
| 409 | `PROJECT_NOT_RECRUITING`, `POSITION_UNAVAILABLE`, `APPLICANT_UNAVAILABLE`, `ALREADY_PROJECT_MEMBER`: failed acceptance. |
| 400 / 409 | `PROJECT_CAPACITY_EXCEEDED`: existing capacity edit returns 400; approval capacity conflict returns 409. |
| 409 | `POSITION_CAPACITY_EXCEEDED`, `POSITION_IN_USE`: unsafe Position change/delete. |
| 429 | `APPLICATION_RATE_LIMITED`: ordinary burst protection. |
| 503 | `DISCOVERY_SNAPSHOT_TOO_LARGE`: narrow filters. |

Migrations, in order:

1. `20261030031756_Project_AddRecruitmentDeadline` — UTC cutoff/backfill/index;
   see [PROJECT_API.md](PROJECT_API.md) for compatibility and guarded downgrade.
2. `20261030034108_Discovery_CreateProjectInteractions` — account/project flags
   and saved-query partial index.
3. `20261030034750_Project_CreateApplicationsAndMemberPositions` — application
   history/status check/FKs/indexes, pending-only unique index, nullable member
   Position linkage and composite project/Position integrity.

Apply migrations before deploying the backend; older writers must be stopped
for the required deadline column. Application/interaction Down migrations drop
their new tables/links, so back up feature history before any downgrade. Existing
auth/profile migrations and data are preserved. Migration verification in this
work used only disposable PostgreSQL databases; the configured project DB has
not been migrated. No frontend changes are included in this backend handoff.

See [DISCOVERY_V1_BACKEND_PROGRESS.md](DISCOVERY_V1_BACKEND_PROGRESS.md) for exact
verification commands, evidence and changed files.
