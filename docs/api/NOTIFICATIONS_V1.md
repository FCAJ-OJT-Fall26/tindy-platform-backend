# Notifications V1 and active member positions

## Integration contract

All four routes use the existing JWT default policy: an active account and
completed onboarding are required. Send `Authorization: Bearer <access_token>`.
The recipient always comes from the authenticated `sub` claim. Do not send a
recipient ID, a body, or extra query parameters to select another user's inbox.
Successful responses use the existing `success`, `message`, `data` envelope.
Errors use the existing ProblemDetails contract with `error_code`.

### List

```http
GET /api/me/notifications?filter=unread&page=1&page_size=20
Authorization: Bearer <access_token>
```

Defaults: `filter=all`, `page=1`, `page_size=20`. V1 accepts only `all` or `unread`,
a positive page, and exactly 20 items per page. Invalid, empty, repeated, or
unsupported query parameters return HTTP 400 / `VALIDATION_FAILED`.
Ordering is `created_at DESC, notification_id DESC`; pagination is the existing
offset-based convention. An out-of-range page has an empty `items` array.

```json
{
  "success": true,
  "message": "Notifications retrieved.",
  "data": {
    "items": [
      {
        "notification_id": "70000000-0000-0000-0000-000000000001",
        "type": "invitation_received",
        "actor_user_id": "10000000-0000-0000-0000-000000000001",
        "invitation_id": "60000000-0000-0000-0000-000000000001",
        "payload": {
          "project_name": "Tindy REST API",
          "position_title": "Backend Developer",
          "actor_display_name": "Project Leader"
        },
        "is_read": false,
        "read_at": null,
        "created_at": "2026-11-05T03:00:00Z"
      }
    ],
    "pagination": {
      "current_page": 1,
      "page_size": 20,
      "total_items": 1,
      "total_pages": 1,
      "has_next": false,
      "has_previous": false
    }
  }
}
```

### Unread count

```http
GET /api/me/notifications/unread-count
Authorization: Bearer <access_token>
```

```json
{
  "success": true,
  "message": "Unread notification count retrieved.",
  "data": { "unread_count": 1 }
}
```

This uses a database COUNT scoped to the recipient and `read_at IS NULL`.

### Read one

```http
PATCH /api/me/notifications/70000000-0000-0000-0000-000000000001/read
Authorization: Bearer <access_token>
```

HTTP 200 returns the same notification fields as a list item, with `is_read=true`
and `read_at` set, inside the standard envelope:

```json
{
  "success": true,
  "message": "Notification marked as read.",
  "data": {
    "notification_id": "70000000-0000-0000-0000-000000000001",
    "type": "invitation_received",
    "actor_user_id": "10000000-0000-0000-0000-000000000001",
    "invitation_id": "60000000-0000-0000-0000-000000000001",
    "payload": {
      "project_name": "Tindy REST API",
      "position_title": "Backend Developer",
      "actor_display_name": "Project Leader"
    },
    "is_read": true,
    "read_at": "2026-11-05T03:05:00Z",
    "created_at": "2026-11-05T03:00:00Z"
  }
}
```

The first successful update determines `read_at`. Repeated/concurrent requests
preserve it. Unknown IDs, the empty UUID, and another recipient's IDs return
HTTP 404 / `NOTIFICATION_NOT_FOUND`, without revealing ownership.

### Read all

```http
POST /api/me/notifications/mark-all-read
Authorization: Bearer <access_token>
```

```json
{
  "success": true,
  "message": "Notifications marked as read.",
  "data": { "updated_count": 1 }
}
```

The service captures one UTC cutoff when the operation starts. A single UPDATE
only affects this recipient's unread rows with `created_at <= cutoff`; it never
overwrites a previous `read_at`. PostgreSQL's UPDATE statement snapshot also
excludes rows committed after that statement begins, including backdated rows
committed while the UPDATE waits for a lock. Notifications arriving later remain
unread. A repeated request returns `updated_count=0` unless new eligible rows exist.
All notifications remain stored after reading.

## Invitation events and consistency

| Successful transition | Type | Recipient | Actor |
| --- | --- | --- | --- |
| Send | `invitation_received` | Invited member | Sending leader |
| Accept | `invitation_accepted` | Current project leader | Member |
| Decline | `invitation_declined` | Current project leader | Member |
| Cancel | `invitation_cancelled` | Invited member | Cancelling leader |

The existing project lock protects the current `Project.LeaderId` used for
response recipients. Decline after leadership changes targets the new leader.
The existing accept guard still rejects an invitation from a former leader with
`INVITATION_SENDER_UNAVAILABLE`; no accepted notification is written. Historical
notifications already delivered to a user remain in that user's inbox.

Writes use the invitation repository's existing transaction and DbContext.
Notification insertion failure rolls back invitation changes, membership writes
and position counters. Existing validation, cooldown, expiry, capacity, lock
ordering and transition error codes remain in effect. Failed, expired and
repeated terminal transitions create no event. Each new invitation after an
allowed resend gets a new invitation ID and its own event history.

Payloads capture only `project_name`, `position_title`, `actor_display_name`.
They do not include contact details, invitation messages, or credentials.
Names are display snapshots and do not follow later entity renames.
`actor_user_id` is nullable; clients should render from payload snapshots.
Opening invitation detail remains subject to that API's own authorization.

## Active member position

Existing Project Detail responses now add `position_id` to every item in
`data.team_members`, sourced directly from `ProjectMember.PositionId`:

```json
[
  {
    "user_id": "10000000-0000-0000-0000-000000000001",
    "display_name": "Project Leader",
    "role_title": "Project Lead",
    "avatar_url": null,
    "initials": "PL",
    "is_leader": true,
    "joined_at": "2026-11-01T03:00:00Z",
    "position_id": null
  },
  {
    "user_id": "20000000-0000-0000-0000-000000000001",
    "display_name": "Team Member",
    "role_title": "Backend Developer",
    "avatar_url": null,
    "initials": "TM",
    "is_leader": false,
    "joined_at": "2026-11-05T03:00:00Z",
    "position_id": "50000000-0000-0000-0000-000000000001"
  }
]
```

Existing member fields remain unchanged. The DTO constructor appends an optional
nullable argument for source compatibility. No membership schema change is needed.

## Migration and rollout

New migration: `20261105022808_Notification_CreateNotifications`.

- Creates only `notifications`: UUID PK, required recipient/invitation FKs,
  nullable actor FK, varchar(60) type, JSONB payload, nullable read timestamp and
  required creation timestamp.
- `ck_notifications_type` permits exactly the four V1 types.
- `ux_notifications_invitation_event_recipient` prevents duplicate
  `(invitation_id, type, recipient_user_id)` events.
- `ix_notifications_recipient_history` supports newest-first recipient queries;
  `ix_notifications_recipient_unread` is partial on `read_at IS NULL`.
- Recipient and invitation deletes are restricted; actor deletion uses SetNull.
- No backfill, retention job, preferences table, email, realtime delivery or
  candidate archive feature is introduced.
- Apply this migration in the intended environment before enabling the new
  backend. No application/production database was migrated during implementation.
  Downgrading drops notifications, while preserving invitation/member history.

## Files in this task

Paths below are relative to `tindy-backend-platform`; pre-existing working-tree
changes outside this list were not part of this task.

Created:

- `src/Tindy.Application/Entities/Notification.cs`
- `src/Tindy.Application/DTOs/Notifications/NotificationDtos.cs`
- `src/Tindy.Application/Interfaces/Repositories/INotificationRepository.cs`
- `src/Tindy.Application/Interfaces/Services/INotificationService.cs`
- `src/Tindy.Application/Services/NotificationService.cs`
- `src/Tindy.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs`
- `src/Tindy.Infrastructure/Repositories/NotificationRepository.cs`
- `src/Tindy.Api/Controllers/MyNotificationsController.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261105022808_Notification_CreateNotifications.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/20261105022808_Notification_CreateNotifications.Designer.cs`
- `tests/Tindy.Tests/Projects/NotificationHttpTests.cs`
- `tests/Tindy.Tests/Projects/InvitationNotificationTests.cs`
- `tests/Tindy.Tests/Projects/NotificationSchemaTests.cs`
- `docs/api/NOTIFICATIONS_V1.md`

Modified:

- `src/Tindy.Application/DTOs/Projects/ProjectDtos.cs`
- `src/Tindy.Application/Services/ProjectService.cs`
- `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs`
- `src/Tindy.Infrastructure/Persistence/Migrations/TindyDbContextModelSnapshot.cs`
- `src/Tindy.Infrastructure/Repositories/ProjectInvitationRepository.cs`
- `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs`
- `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs`
- `tests/Tindy.Tests/Projects/ProjectInvitationConcurrencyTests.cs`
- `tests/Tindy.Tests/Integration/CatalogDatabaseIntegrityTests.cs`
- `docs/api/PROJECT_API.md`

## Verification

The automated checks use the existing real ASP.NET Core HTTP/JWT test server
and disposable PostgreSQL databases. Docker access was unavailable, so the
existing loopback-only `TINDY_TEST_POSTGRES` fallback was used with a new temporary
PostgreSQL 17 cluster. It never points at the application's configured database.

Coverage includes the real migration Up/Down/Up, no backfill, model consistency,
database type/unique/FK/delete constraints, all invitation events, failed and
expired transitions, retries and concurrency, notification insertion rollback,
cooldown resend identity, former-leader protection, safe payload snapshots,
recipient isolation, validation, pagination, unread count, read idempotency,
mark-all cutoff and a late backdated commit during an UPDATE lock wait.
Project Detail is verified with both an assigned member and a nullable leader.

Final validation on 2026-11-05:

| Check | Result |
| --- | --- |
| `dotnet restore` | Passed; dependencies up to date. |
| `dotnet build --no-restore` | Passed; 0 warnings, 0 errors. |
| New notification tests | 32 passed, 0 failed, 0 skipped. |
| Projects group in final full run, including Invitation/Application/Discovery | 246 passed, 0 failed, 0 skipped. |
| Final `dotnet test Tindy.slnx --no-restore` | 1006 passed, 1 failed, 2 skipped; 1009 total, exit code 1. |
| EF `migrations has-pending-model-changes` | No pending model changes. |
| EF idempotent migration script generation and review | Passed; only creates notifications and its constraints/indexes. |
| Real PostgreSQL migration Up/Down/Up and schema checks | Passed in the full run. |
| `git diff --check` | Passed. |

The one unrelated failure is
`OAuthHttpContractTests.CorsPreflight_OnlyAllowsConfiguredOrigins_WithAuthorizationHeader`
for `http://localhost:8443`: line 111 expects no
`Access-Control-Allow-Credentials` header, while the existing API CORS setup
calls `AllowCredentials()`. This CORS configuration also exists in repository
HEAD and was not changed by this task. The OAuth test and CORS policy were left
unchanged; this failure keeps the full suite from being green.
The two skips are existing opt-in `ClamAvLiveIntegrationTests`.

The exact-table-set regression test was deliberately extended to include the
new `notifications` table; its strict equality assertion remains intact. Existing
Invitation concurrency tests were strengthened to assert that only winning
events persist and invitation write failures leave only the original received
notification.

Local verification artifacts (ignored build/test outputs):

- `tests/Tindy.Tests/TestResults/notifications.trx`: targeted notification run.
- `tests/Tindy.Tests/TestResults/notifications-full-final.trx`: final full run.
- `TestResults/notifications-migration.sql`: reviewed idempotent rollout SQL.

The installed Postman CLI v1.73.0 does not expose the skill's `postman status`
command. Verification above uses the repository's .NET integration tests and EF
tools; no Postman collection run or external workspace publication is claimed.

## Scope and integration status

The backend contract is ready for Frontend integration once the new migration is
applied to the target environment. Poll the list and unread-count endpoints as
needed; no push transport is provided in V1. Offset pagination is deterministic
for a fixed dataset but concurrent new notifications can shift page boundaries.
This task does not implement or validate a Frontend UI or deployment.
