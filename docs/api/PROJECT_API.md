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
  "recruitment_closing_days": 14,
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
Positions. Schema, migrations, Authentication and Profile contracts are unchanged.

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

Project-only checks can also run without PostgreSQL:

```sh
dotnet test Tindy.slnx --filter "FullyQualifiedName~Tindy.Tests.Projects"
```
