# Discovery Teammates V1 — requirements and proposed API contract

Status: STEP 00 design only, reviewed against backend commit `2f1e2f5` on 2026-11-03. None of the new tables, fields, classes, error codes or routes described as **proposed** below exists yet. This document and the [implementation plan](DISCOVERY_TEAMMATES_V1_IMPLEMENTATION_PLAN.md) are the only STEP 00 changes. No feature code or migrations are included.

## 1. Architecture review and source evidence

Paths in this document are relative to `tindy-backend-platform`.

| Inspected source | Actual behavior and design consequence |
| --- | --- |
| `src/Tindy.Application/Tindy.Application.csproj`, `src/Tindy.Api/Tindy.Api.csproj` | Target `net10.0`. There is no separate Domain project in the inspected source tree. Domain entities currently live in `Tindy.Application/Entities`; follow that structure rather than introducing a layer reorganization. |
| `src/Tindy.Application/Services/PositionMatchScorer.cs` | Pure static rule-v1 scoring; `Score` scores exactly one Position, while `Best` selects among available Positions. Reuse `Score`, not `Best`, for selected-Position candidate matching. |
| `src/Tindy.Application/Services/DiscoveryService.cs`, `DiscoveryFeedSessions.cs`; `src/Tindy.Infrastructure/Repositories/DiscoveryRepository.cs`, `DiscoveryEligibility.cs` | Existing Discovery searches Projects for one Member; uses batched whitelist projections, server scoring and signed process-local snapshot cursors. Eligibility includes deadline, capacity, pending Applications, active membership and leader exclusion. It has no Invitation or opt-in awareness. The 60/25/15 Project feed mix does not apply to candidates. |
| `src/Tindy.Application/Services/ProjectApplicationService.cs`; `src/Tindy.Infrastructure/Repositories/ProjectApplicationRepository.cs`, `ProjectWriteLock.cs` | Application creation locks Project before checking eligibility. Approval locks Project → Position → Application, reloads tracked rows, checks capacity and account eligibility, then creates/reactivates membership and increments Position once in one transaction. Existing pending Applications can be accepted after the deadline. Terminal decisions return 409 on repetition. |
| `src/Tindy.Application/Entities/{Project,ProjectPosition,ProjectMember,ProjectApplication,User,UserProfile,DiscoveryProjectInteraction}.cs` and corresponding Infrastructure configurations | Membership PK is `(project_id,user_id)`; Position FK is nullable and composite with Project. Team size is active membership count, including Leader; only Position has `filled_count`. Applications have history and a pending-only unique index. Existing Discovery interactions are two independent booleans keyed by Member/Project, with no Position or candidate. They cannot safely represent this feature. |
| `src/Tindy.Infrastructure/Repositories/ProjectRepository.cs`; `src/Tindy.Application/Services/{ProjectService,ProjectPositionService}.cs` | Project DELETE archives and closes Positions. Position DELETE is physical and rejects member/Application history. Capacity/Position writes generally serialize on Project. New skill insertion/removal currently uses direct SaveChanges without that lock; see compatibility notes below. |
| `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | Explicit DbSets, configurations applied from assembly. Add two DbSets and configurations; no new persistence framework. |
| `src/Tindy.Api/Controllers/{DiscoveryController,DiscoveryInteractionsController,ProjectApplicationsController,MyProjectApplicationsController,MyProfileController,MemberProfileController}.cs` | `/api`, JWT `sub`, success/data envelopes, GUID-constrained resource routes. Owner Profile uses GET/PATCH, not PUT. Existing Member Profile/Experience/Certificate reads already provide safe detail links. |
| `src/Tindy.Application/DTOs/Profile/{UpdateProfileRequest,MyProfileResponse,MemberProfileResponse}.cs`; `Services/ProfileService.cs`; `src/Tindy.Infrastructure/Repositories/ProfileRepository.cs` | Strict presence-aware `ProfilePatchParser`; owner and member DTOs differ. Profile updates lock `users` before loading profile/skills. Add opt-in only to owner read/update. Member DTO excludes private phone/resume/account email but intentionally includes `public_email`; candidate previews can be narrower. |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs`, `ExceptionHandlers/GlobalExceptionHandler.cs`, `Authorization/*`; Infrastructure DI extension | Snake-case JSON, model validation ProblemDetails, scoped services/repositories, singleton TimeProvider and feed sessions. Plain `[Authorize]` requires active, nondeleted, onboarded caller. JWT failures and onboarding have their own existing responses. Business-specific HTTP/code pairs can use `ProjectException`. |
| `docs/api/{DISCOVERY_API,PROJECT_API,PROFILE_API}.md`; `docs/guide/MIGRATION_NAMING_CONVENTION.md` | Existing contracts, Vietnam recruitment cutoff semantics, partial Profile updates, and `<Feature>_<Action><Target>` migration names. Source takes precedence over historical documentation checkpoints. |
| `tests/Tindy.Tests/Projects/{PositionMatchScorerTests,DiscoveryEligibilityTests,DiscoveryFeedTests,DiscoveryHttpTests,ProjectApplicationHttpTests,ProjectApplicationConcurrencyTests,RecruitmentSchemaTests}.cs`; Profile read/update/security tests; `Integration/{TestPostgres,TindyApiFactory}.cs` | Existing scoring, 1,000-Project snapshot, eligibility, privacy, immediate reapply, membership reactivation, PostgreSQL constraints, rollback and competing capacity tests are reusable patterns. Separate connections against real PostgreSQL establish concurrency; an in-memory provider does not. Tests were inspected, not executed during STEP 00. |

Reuse the scorer, DTO components (`DiscoveryMemberProfile`, `DiscoveryPosition`, `PositionMatch`, `MatchFactor`, `ApplicantSkill`, `PaginationMeta`), account policies, Profile PATCH, membership semantics, ProjectWriteLock and real-PostgreSQL test infrastructure. Add distinct teammate repositories/services/DTOs/controllers, invitation lifecycle, and two tables. Preserve Authentication, Onboarding, Project discovery mixing/cursors, scorer semantics, Application statuses/history/immediate reapplication, existing endpoints, and stable Project/Application schema.

### Requirement conflicts and explicit assumptions

1. The requested cross-table pending exclusion needs a future change to `ProjectApplicationRepository.CreateAsync`. A partial unique index on Invitations alone cannot enforce it. This is a narrow Step 04 extension, not an Application redesign. Keep existing Project discovery behavior unchanged: it may still display a Project with a pending Invitation; Apply then returns the new explicit conflict. Its feed does not gain an Invitation exclusion in V1.
2. No one-led-Project enforcement was found in `ProjectService.CreateProjectAsync` or Project schema. This feature neither adds a one-led-Project constraint nor prevents a Leader joining another Project. Actual Project ownership always decides permission.
3. Opt-in is checked at discovery, send and accept. Turning it off does not silently cancel delivered Invitations; Accept returns `409 CANDIDATE_UNAVAILABLE` until opt-in is restored, while Decline remains possible. This is the chosen interpretation of rechecking eligibility at acceptance.
4. Candidate actions are one mutually exclusive state, not three independent flags: saving after shortlisting replaces the action; DELETE resets it. This matches the requested single `action` column. Skipping is Position-specific and does not prohibit an explicit invitation.
5. Suppress candidates during a decline cooldown as an additional discoverability filter, so ordinary cards are sendable at read time. No skill threshold is added. A rejected/withdrawn Application itself never starts a cooldown; an independent Invitation decline cooldown still applies to another Invitation.
6. Scores are advisory. Changed skills, availability, role, Position requirements or expected hours never become acceptance thresholds. Live eligibility/capacity remain authoritative.
7. History keeps references to existing entities, not immutable copies of every old Profile/Position value. Requirement edits do not rewrite the Invitation's Project/Position/message/actors/timestamps. No email, notification transport, leadership transfer, membership departure workflow, AI or scheduled worker is added in V1.

## 2. Requirements and sources of truth

Leader candidate discovery requires a selected Project and Position, owned by the caller (`projects.leader_id`, never JWT role or `project_members.is_leader` alone). The Project must be `recruiting`, have `archived_at IS NULL`, and `recruitment_closes_at > now`. Active memberships must be below `max_team_capacity`. The selected Position must belong to that Project, have `status='open'`, and `filled_count < required_count`.

Eligible candidates have `users.status='active'`, `deleted_at IS NULL`, completed onboarding, a Profile, and `open_to_team_invitations=true`. Exclude the current Project Leader, any active membership in that Project, any pending Application there, any **effective pending** Invitation there regardless of Position, a current decline cooldown, and a `skipped` interaction for this Position. Membership/leadership elsewhere and former `left`/`removed` membership here do not exclude a candidate. No 100% required-skills or minimum-score rule exists.

| Fact | Authoritative source |
| --- | --- |
| Actor | Validated JWT `sub` plus existing account/onboarding policy |
| Ownership/lifecycle/deadline/team cap | Current Project row and server UTC clock; `RecruitmentDeadline` remains responsible for existing local-date conversion |
| Position/slots | Current ProjectPosition row and composite FK; never client counts |
| Actual team size/duplicate membership | `project_members WHERE status='active'`, including Leader; do not use `initial_team_size` as current size |
| Skills/role/hours/opt-in | Current Profile, `user_skills` and catalog IDs, not names or submitted scores |
| Pending Application | `project_applications.status='pending'` across all Positions |
| Pending Invitation/cooldown | Invitation lifecycle and server time as defined below |
| Saved/shortlisted/skipped | PostgreSQL teammate interaction row for the exact Project/Position/candidate tuple |
| Match | Backend `PositionMatchScorer.Score(member, selectedPosition, project.ExpectedCommitmentHours)` |

New invitation admission requires the same Project/Position/candidate rules, except that a skip does not prevent explicit Send. Acceptance omits the recruitment-deadline check and the candidate's own pending Invitation exclusion; it still requires all other account/opt-in/membership/pending-Application/Project/Position/capacity checks. Candidate cooldown cannot normally coexist with that pending Invitation and is enforced on new sends only.

## 3. Matching contract — unchanged rule-v1

For each candidate use distinct catalog skill IDs, current preferred role ID, available hours, and the one selected Position. The factor behavior verified in source is:

| Factor | Base weight | Value | Missing-data behavior |
| --- | --- | --- | --- |
| Required skills | 40 | matched distinct required IDs / distinct required IDs | Empty Position group excluded; empty Member skills gives 0 when group exists |
| Preferred role | 30 | 1 for exact ID equality, otherwise 0 | A missing Member role is **included as 0**, not dropped |
| Availability | 20 | `min(1, available_hours / expected_commitment_hours)` | Null/negative Member hours or nonpositive expected hours excludes factor; zero hours is included as 0 by scorer, though current Profile validation permits 1–40 |
| Optional skills | 10 | matched distinct optional IDs / distinct optional IDs | Empty Position group excluded; empty Member skills gives 0 when group exists |

For included factors, `normalized_weight = 100 * base_weight / sum(included base weights)` and `contribution = normalized_weight * factor`. Excluded factors serialize `factor:null`, weight/contribution 0. Sum decimal contributions, round once away from zero at midpoint to integer, clamp 0–100. Preferred-role inclusion means the denominator is never zero. `data_incomplete` means no preferred role, excluded availability, or empty Member skill set. Skill names, experience level, experiences/certificates, text descriptions and `minimum_experience_level` do not influence scores.

Example: two required skills with one matched, one unmatched optional skill, matching role, 5 available hours against 10 expected yields `20+30+10+0=60`. It remains eligible. If both Position skill groups are empty and availability is unknown, a matching role alone scores 100; a missing role alone scores 0. Preserve those results.

Use the complete existing `PositionMatch` JSON shape, including its legacy name `best_position`; here that property always means the explicitly selected Position. Do not add a parallel match endpoint. A possible future change to treat missing preferred role as an excluded factor must be separately approved, versioned and regression-tested; it is **not** part of this proposal or a silent rule-v1 correction.

## 4. Proposed database design

All timestamps below are PostgreSQL `timestamp with time zone` (`timestamptz`) and UTC in API output. IDs are `uuid`, generated by the application as existing entities do. Add no all-pairs score table and no changes to stable Project/Application columns or indexes.

### 4.1 Existing `user_profiles`

Add `open_to_team_invitations boolean NOT NULL DEFAULT false`, including existing Profiles. Model default is false. No onboarding completion, seeder consent inference, or automatic opt-in. GET/PATCH owner Profile expose it; member-visible Profile and account `/api/me` do not gain it. Existing Profile writes already serialize on the User row. No opt-in index initially: inspect candidate-query plans before adding a low-selectivity index.

### 4.2 New `project_invitations`

| Column | Type/null/default | Meaning |
| --- | --- | --- |
| `invitation_id` | uuid PK, not null | New ID for every send, including reinvites |
| `project_id` | uuid not null | Project |
| `position_id` | uuid not null | Specific invited Position; immutable |
| `invited_user_id` | uuid not null | Recipient; immutable |
| `inviting_leader_id` | uuid not null | Actual sender; immutable |
| `message` | varchar(2000), nullable | Optional trimmed message |
| `status` | varchar(20) not null, default `'pending'` | `pending`, `accepted`, `declined`, `cancelled`, `expired` |
| `created_at` | timestamptz not null | Creation instant |
| `updated_at` | timestamptz not null | Last lifecycle change; initially created_at |
| `expires_at` | timestamptz not null | Exactly created_at + 7 elapsed days |
| `responded_at` | timestamptz nullable | Accepted/declined instant only |

FKs: Project → `projects(project_id)`; Position → existing alternate key `project_positions(position_id,project_id)` via `(position_id,project_id)`; both actors → `users(user_id)`. All four use **Restrict/NO ACTION**, with no history cascades. The composite Position principal key already exists for membership/Applications; do not add a redundant key. Requiring sender to be actual Project Leader is a transactional business check, not a new cross-table CHECK constraint.

Constraints proposed:

- `ck_project_invitations_status`: status in the five values above.
- `ck_project_invitations_distinct_users`: invited_user_id differs from inviting_leader_id.
- `ck_project_invitations_expiry`: `expires_at = created_at + interval '168 hours'`; verify under UTC and a DST session timezone. Seven days means elapsed hours, not seven local calendar midnights.
- `ck_project_invitations_timestamps`: updated_at >= created_at; accepted/declined require nonnull responded_at, created_at <= responded_at < expires_at, and updated_at = responded_at; other statuses require null responded_at. Expired requires updated_at >= expires_at. Cancelled requires updated_at < expires_at. Pending requires updated_at = created_at.

Indexes proposed:

| Name | Definition/purpose |
| --- | --- |
| `ux_project_invitations_pending` | UNIQUE `(project_id,invited_user_id) WHERE status='pending'`; independent of Position |
| `ix_project_invitations_project_history` | `(project_id,created_at DESC,invitation_id)` for Leader history |
| `ix_project_invitations_member_history` | `(invited_user_id,created_at DESC,invitation_id)` for recipient history |
| `ix_project_invitations_declined` | `(project_id,invited_user_id,responded_at DESC) WHERE status='declined'` for latest cooldown |
| `ix_project_invitations_position` | `(position_id,project_id)` for FK/history deletion check |
| `ix_project_invitations_inviting_leader` | `(inviting_leader_id)` for FK |

No `now()` in a partial-index predicate: clock passage does not remove index entries. No reservation counter, cooldown column or separate membership row on send. The existing membership PK prevents duplicate Project membership rows.

### 4.3 New `teammate_discovery_interactions`

| Column | Type/null | Meaning |
| --- | --- | --- |
| `project_id` | uuid not null | Owner context |
| `position_id` | uuid not null | Explicit matching context |
| `candidate_user_id` | uuid not null | Candidate |
| `action` | varchar(20) not null | `saved`, `shortlisted`, `skipped` |
| `created_at` | timestamptz not null | Initial row creation |
| `updated_at` | timestamptz not null | Last actual action change |

Composite PK `(project_id,position_id,candidate_user_id)` provides the requested unique key; no surrogate ID is needed. `ck_teammate_discovery_interactions_action` restricts values; timestamp CHECK requires updated_at >= created_at. Project FK cascades, composite Position FK to `(position_id,project_id)` cascades, candidate User FK cascades. Interactions are disposable preferences, unlike Invitation history. Use existing composite principal key. These cascade relationships require PostgreSQL migration tests; do not assume another database provider has the same semantics.

Index `ix_teammate_interactions_list` on `(project_id,position_id,action,updated_at DESC,candidate_user_id)` supports list/filter. Index `ix_teammate_interactions_candidate` on `(candidate_user_id)` supports FK cleanup. No actor/leader column: state belongs to the Project and is accessible only to its current actual Leader; there is no leadership transfer workflow in scope.

UPSERT atomically creates/changes one row; same-action PUT preserves timestamps. DELETE physically resets the preference, including when the candidate is no longer discoverable. A new PUT after reset gets a new created_at. Existing `discovery_project_interactions` remains untouched because its actor, key, independent flag semantics and lifecycle differ.

### 4.4 Deletion and archive

Project DELETE continues to archive and close Positions. It does not erase or cancel Invitations; pending records remain visible until explicit cancellation/decline/expiration, and Accept fails while archived. Leader history, cancellation and interaction reset must authorize archived Projects directly, bypassing general detail methods that treat them as missing. Close a Position with Invitation history; physical Position deletion returns existing `409 POSITION_IN_USE` for **any** Invitation history, not just pending. Add this guard before deletion so the Restrict FK never becomes an unhandled 500. Interaction-only Positions may still be physically deleted, cascading preferences. There is no Position archive field today.

Hard Project/User deletion with Invitation references is restricted; existing Application history already uses this approach. Existing soft-deleted/inactive accounts retain IDs/history, with safe unavailable previews. No hard-delete/erasure redesign is included.

## 5. Invitation lifecycle, time and concurrency

### 5.1 State machine

| Stored state | Allowed transition | Actor and condition |
| --- | --- | --- |
| pending | accepted | Recipient explicitly accepts before expires_at; all authoritative acceptance checks pass |
| pending | declined | Recipient explicitly declines before expires_at; no Project/Position capacity or opt-in requirement |
| pending | cancelled | Current actual Project Leader before expires_at, including closed/archived Project |
| pending | expired | Server observes now >= expires_at |
| any terminal state | none | History immutable; reinvite creates a new row |

Send does not reserve a slot. Overlapping pending Invitations to different Members can exceed remaining slots. Decline starts a Project/Member cooldown at `responded_at + 168 hours`, across all Positions. At exactly the boundary a new Invitation is allowed. Accepted/cancelled/expired rows start no cooldown. Use the latest declined responded_at for that Project/recipient; never Application timestamps. During Invitation cooldown a Member may still **apply** if otherwise eligible and there is no pending Invitation. Rejection/withdrawal preserves immediate reapplication with no new Application delay.

### 5.2 Effective expiration and normalization

Read contract: effective status is `expired` whenever stored status is pending and expires_at <= one request-level server now, otherwise stored status. Discovery exclusion and list `status` filtering must use this same expression. GET has no write side effect; an effective expired row may still physically occupy the pending index until a write normalizes it. Its API updated_at is the effective transition instant expires_at (not the later read time), responded_at is null. Persisted normalization also sets updated_at=expires_at. No worker is necessary for correctness.

Before **Send or Apply**, within the Project lock, update elapsed pending rows for the target Project/Member to `expired`, flush that update, then check pending conflicts/cooldown and insert if valid. Both actions call one shared proposed Infrastructure helper. This frees the pending unique key before another send. No delete/reuse of the old row. Competing Send/Send and Send/Apply serialize on the Project even for different Positions.

Before Accept/Decline/Cancel, locate the authorized row, acquire locks in the order below, capture now after waiting for locks, and check stored terminal state first. If still pending but elapsed, persist expiry and **commit that transition before returning** `409 INVITATION_EXPIRED`. Do not throw inside an uncommitted scope and accidentally roll expiry back. Return a repository outcome after commit, then map it to ProjectException at the service boundary. Later requests see stored expired and receive the same error. Other acceptance business failures leave an unexpired Invitation pending and make no membership/count change.

Normalization during Send/Apply also commits if admission fails after the expiration update: retain the business-error outcome, commit expiry-only changes, then report it. The existing Apply repository can retain its current CreateAsync return type by throwing ProjectException only after this commit; no public Application DTO or repository interface change is required. Validation/authorization errors before identifying an authorized target do not mutate anything. An infrastructure failure rolls back the entire transaction, including expiry; the effective read rule remains correct and a later authorized write retries normalization. Time is evaluated at the locked validation point, not at HTTP arrival or commit time.

### 5.3 Transaction and lock order

Proposed order for any invitation lifecycle mutation: **Project → Position → Invitation → candidate User**, skipping unused rows. If locking multiple Positions/Invitations, use ascending IDs within each class. Never hold User and then acquire Project. Existing Profile writes hold User only; they do not acquire Project, so the invitation account/opt-in recheck can serialize with opt-out without a reverse lock order. Use parameterized SQL and reload tracked rows after lock acquisition as existing approval does.

Send: begin transaction; lock Project with `ProjectWriteLock`; verify actual ownership; lock selected Position; lock/normalize any pending recipient Invitation (even to another Position, without locking its Position); lock candidate User; re-read current Profile/eligibility and capacity, pending Applications and cooldown; insert; SaveChanges; commit. Expiration normalization only locks Invitation rows after the selected Position lock. Map only the named pending unique violation to `409 INVITATION_ALREADY_PENDING`; do not swallow unrelated DB failures.

Apply: retain existing Project-first transaction, normalize elapsed recipient Invitations under that lock, then reject effective pending Invitation before existing eligibility/insert. There is no need to acquire Position after locking an Invitation: Apply does not write Position capacity and currently only queries Position eligibility. Both writers must be deployed together; two separate unique indexes do not enforce cross-table exclusion for arbitrary SQL writers.

Accept transaction:

1. Resolve recipient-owned Invitation to Project/Position using an untracked lookup (not a row lock). An unknown/foreign Invitation is 404. Begin transaction and acquire Project, then selected Position, then Invitation row locks. Reload current values and verify the Invitation still belongs to recipient/Project/Position.
2. Stored expired → `INVITATION_EXPIRED`; other terminal → `INVITATION_NOT_PENDING`. For pending, lock candidate User; take server now and persist expiry-only outcome if elapsed.
3. Recheck `project.Status=='recruiting'`, no archive; current sender still equals actual Leader (`409 INVITATION_SENDER_UNAVAILABLE` otherwise), recipient differs from Leader; Position belongs to Project, is open and has slots. **Do not** call `DiscoveryEligibility.ForMember` unchanged: it rejects post-deadline acceptance and includes other admission-only checks.
4. Recheck active/nondeleted/onboarded account, existing Profile and opt-in; no pending Application; actual active team count < max capacity; no active membership for `(project_id,recipient)`. If opt-out wins the User lock first, Accept fails; if Accept commits first, a later opt-out does not remove membership.
5. Create or reuse a `left`/`removed` ProjectMember. Set PositionId, RoleTitle=current Position title, IsLeader=false, Status=active, JoinedAt=now. Never create another row for the same composite PK. Former departures are assumed to have already released their old slot, as in existing approval.
6. Increment selected Position.FilledCount once, set UpdatedAt=now, and Status=filled when count reaches RequiredCount. No stored Project filled-count exists to update.
7. Set Invitation.Status=accepted, RespondedAt=UpdatedAt=now; save all membership/Position/Invitation changes and commit together. Failure before commit rolls back all acceptance changes.

All accepts share the Project lock with Application approvals, Project archive/capacity edits and Position state/capacity edits. Two accepts for the last Project slot (even different Positions), or last Position slot, yield exactly one success. The loser receives the current `POSITION_UNAVAILABLE` or `PROJECT_CAPACITY_EXCEEDED` conflict. No extra Leader approval or application creation occurs.

## 6. Proposed API conventions

Every route below requires existing JWT/default authorization. Invalid/expired/missing token → `401 AUTH_UNAUTHORIZED`; inactive/deleted caller → `403 AUTH_FORBIDDEN`; incomplete onboarding → `403 ONBOARDING_REQUIRED`. Project Leader routes additionally compare current LeaderId with caller under authoritative reads/locks; another Leader/member gets `403 PROJECT_ACCESS_DENIED`. Unknown Project → `404 PROJECT_NOT_FOUND`. Resource IDs use existing `{name:guid}` route constraints: malformed route UUIDs normally fail routing with 404; invalid body/query UUIDs return `400 VALIDATION_FAILED`. Empty required UUIDs return 400.

JSON uses snake_case except the existing ProblemDetails extension `traceId`. Success is `{success:true,message,data}`; errors are `application/problem+json`, without a success envelope. Body DTOs accept only documented business values; never bind actor/status/timestamp/score/membership input. New mutation request parsing rejects unknown and duplicate properties with `400 VALIDATION_FAILED`, following strict Profile parsing. No body is required for transition/reset endpoints. No pagination/sorting applies to single-result mutations.

Lists other than candidate feed use existing `PaginationMeta`: page defaults to 1 and clamps to >=1; page_size defaults to 20, clamps 1–50, overflow-safe offset. Deterministic ordering within each request; history/preference page numbers are live lists, not frozen snapshots. Out-of-range page returns empty items with current metadata.

Common business error example (all strings illustrative):

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "A pending invitation already exists for this member and project.",
  "instance": "/api/projects/11111111-1111-4111-8111-111111111111/invitations",
  "error_code": "INVITATION_ALREADY_PENDING",
  "traceId": "server-generated-trace-id"
}
```

### 6.1 GET `/api/projects/{project_id}/positions/{position_id}/candidates`

Leader only. Example request (no body):

```http
GET /api/projects/11111111-1111-4111-8111-111111111111/positions/33333333-3333-4333-8333-333333333333/candidates?limit=20&sort_by=match&skill_id=55555555-5555-4555-8555-555555555555
Authorization: Bearer <access_token>
```

Query: `limit` default 20/clamp 1–50; optional opaque `cursor`; `sort_by` only `match` (default); optional `role_id` (candidate's preferred role), `skill_id` (candidate has this catalog skill), `min_available_hours_per_week` integer 1–40 (null candidate hours fail this filter). Filters are ANDed and applied on backend before scoring/ranking. Unknown well-formed catalog IDs return no matches; never substitute names. Invalid enum/range → 400. No client score or mandatory skill-match filter. A missing/cross-Project Position → `404 POSITION_NOT_FOUND`. Project state/deadline conflict → `409 PROJECT_NOT_ELIGIBLE`; team full → `409 PROJECT_CAPACITY_EXCEEDED`; closed/full Position → `409 POSITION_UNAVAILABLE`.

200 example (two required skills, one optional; only first required matched):

```json
{
  "success": true,
  "message": "Project candidates retrieved.",
  "data": {
    "project_id": "11111111-1111-4111-8111-111111111111",
    "position_id": "33333333-3333-4333-8333-333333333333",
    "scored_at": "2026-11-03T03:00:00Z",
    "items": [
      {
        "candidate": {
          "user_id": "88888888-8888-4888-8888-888888888888",
          "display_name": "Alex",
          "headline": "Backend developer",
          "avatar_url": null,
          "preferred_role_id": "44444444-4444-4444-8444-444444444444",
          "preferred_role_name": "Backend Developer",
          "available_hours_per_week": 5,
          "skills": [{"skill_id":"55555555-5555-4555-8555-555555555555","name":"C#"}],
          "experiences_count": 1,
          "certificates_count": 0,
          "profile_url": "/api/users/88888888-8888-4888-8888-888888888888/profile",
          "experiences_url": "/api/users/88888888-8888-4888-8888-888888888888/profile/experiences",
          "certificates_url": "/api/users/88888888-8888-4888-8888-888888888888/profile/certificates"
        },
        "interaction_action": null,
        "matching": {
          "match_score": 60,
          "algorithm_version": "rule-v1",
          "best_position": {
            "position_id":"33333333-3333-4333-8333-333333333333",
            "title":"Backend Developer",
            "role_id":"44444444-4444-4444-8444-444444444444",
            "role_name":"Backend Developer",
            "experience_level":"intermediate",
            "required_count":2,"filled_count":0,"remaining_slots":2
          },
          "match_factors": [
            {"name":"required_skills","factor":0.5,"normalized_weight":40,"contribution":20},
            {"name":"preferred_role","factor":1,"normalized_weight":30,"contribution":30},
            {"name":"availability","factor":0.5,"normalized_weight":20,"contribution":10},
            {"name":"optional_skills","factor":0,"normalized_weight":10,"contribution":0}
          ],
          "matched_skills": [{"skill_id":"55555555-5555-4555-8555-555555555555","name":"C#","is_required":true}],
          "missing_skills": [
            {"skill_id":"66666666-6666-4666-8666-666666666666","name":"SQL","is_required":true},
            {"skill_id":"77777777-7777-4777-8777-777777777777","name":"Docker","is_required":false}
          ],
          "data_incomplete":false,
          "explanation":"Required skills: 1/2; optional skills: 0/1. Preferred role matches. Availability: 5 hours/week; project expects 10."
        }
      }
    ],
    "next_cursor": null,
    "has_more": false,
    "feed_version": "teammates-v1"
  }
}
```

Rank **all** eligible filtered candidates by score DESC, candidate UUID ASC using one fixed .NET comparer for snapshot creation. Do not paginate unsorted database candidates before scoring. Use a separate proposed `TeammateDiscoveryFeedSessions` following the existing cursor pattern, without changing `DiscoveryFeedSessions` or calling Project `Mix`. Bind signed cursor to caller, Project, Position, normalized filters, sort, limit, version and snapshot offset. Freeze candidate previews, score inputs/order/explanations at scored_at. New candidates and Profile/requirement edits affect a cursor-free refresh. Each continuation rechecks actual Leader, Project/selected Position state/capacity and current candidate account/opt-in/membership/pending/cooldown/skip eligibility; refresh action and slot counts only. Frozen match/explanation is explicitly a snapshot, never an admission guarantee.

Advance offsets for every inspected candidate, including omitted ones; return a cursor only if eligible candidates remain. No repeats within a snapshot. Existing candidates becoming eligible again can reappear only if their offset was not yet inspected; a fresh request resets the snapshot. Candidate-only snapshot budgets: 15-minute lifetime, <=4 per caller, <=128 globally, <=100,000 retained candidate cards globally; oldest evicted first. An oversized single result returns `503 TEAMMATE_DISCOVERY_SNAPSHOT_TOO_LARGE` without truncation. Bad/tampered/cross-context cursor → `400 INVALID_TEAMMATE_DISCOVERY_CURSOR`; expired/evicted/restarted snapshot → `400 TEAMMATE_DISCOVERY_CURSOR_EXPIRED`. Single-process V1 requires session affinity for multiple API instances; distributed storage is deferred. GET is otherwise read-only; sources are Section 2 and server scorer/session state.

### 6.2 Candidate interactions

Use the suggested Project routes with a **required** `position_id` query for every operation. There is no Project-wide PUT/DELETE default and no second set of Position-nested interaction routes.

| Method and example | Request and validation | Response, errors, retry behavior |
| --- | --- | --- |
| GET `/api/projects/{project_id}/candidate-interactions?position_id={position_id}&action=saved&page=1&page_size=20` | Leader; Position belongs to Project. action optional, one of saved/shortlisted/skipped; omitted means all. Archived/closed context allowed for managing history. | 200 paged interactions, updated_at DESC then candidate_user_id ASC. 400 invalid/missing query, 403 ownership, 404 Project/Position. Read-only. |
| PUT `/api/projects/{project_id}/candidate-interactions/{candidate_user_id}?position_id={position_id}` | Leader; body `{"action":"shortlisted"}`. Require live eligible recruiting Project/Position and currently eligible candidate, ignoring its existing skipped action. | 200 row; atomic UPSERT, same action no-op, different action replaces. 400 invalid body/query; 403 ownership; 404 context or `CANDIDATE_NOT_FOUND` for inactive/deleted/incomplete/missing/opted-out candidate; 409 admission conflicts from Section 7. |
| DELETE `/api/projects/{project_id}/candidate-interactions/{candidate_user_id}?position_id={position_id}` | Leader; no body; existing Project/Position ownership still checked, including archived/closed. Candidate need not still exist/be eligible. | 200 reset ID tuple; missing row is success. 400 missing/invalid query; 403 ownership; 404 Project/Position. Serialized with PUT, last committed operation wins. |

PUT example response (same schema for saved/skipped):

```json
{
  "success": true,
  "message": "Candidate interaction updated.",
  "data": {
    "project_id":"11111111-1111-4111-8111-111111111111",
    "position_id":"33333333-3333-4333-8333-333333333333",
    "candidate_user_id":"88888888-8888-4888-8888-888888888888",
    "action":"shortlisted",
    "created_at":"2026-11-03T03:00:00Z",
    "updated_at":"2026-11-03T03:01:00Z"
  }
}
```

GET response example (a candidate who has since opted out):

```json
{
  "success":true,
  "message":"Candidate interactions retrieved.",
  "data":{
    "items":[{
      "project_id":"11111111-1111-4111-8111-111111111111",
      "position_id":"33333333-3333-4333-8333-333333333333",
      "candidate_user_id":"88888888-8888-4888-8888-888888888888",
      "action":"saved",
      "created_at":"2026-11-03T03:00:00Z",
      "updated_at":"2026-11-03T03:00:00Z",
      "candidate":null,
      "is_discoverable":false
    }],
    "pagination":{"current_page":1,"page_size":20,"total_items":1,"total_pages":1,"has_next":false,"has_previous":false}
  }
}
```

Each GET item contains the stored interaction plus `candidate` (same whitelist as candidate feed, or null) and `is_discoverable` (full current feed eligibility including skip). Pending Invitation/Application, capacity or skip can make is_discoverable false while retaining a preview; inactive/deleted/incomplete/missing/opted-out candidates have null preview. No new score computation is needed for this management list. Source is persisted preferences plus current eligibility. PUT takes Project → Position → candidate User locks and reads current state; DELETE takes Project → Position. UPSERT and DELETE serialize on Project, avoiding read/insert races. No invitation or membership side effect.

DELETE response example:

```json
{"success":true,"message":"Candidate interaction reset.","data":{"project_id":"11111111-1111-4111-8111-111111111111","position_id":"33333333-3333-4333-8333-333333333333","candidate_user_id":"88888888-8888-4888-8888-888888888888","action":null}}
```

### 6.3 Leader Send — POST `/api/projects/{project_id}/invitations`

Actual Leader only; all new-send rules in Sections 2/5. Request example:

```json
{"position_id":"33333333-3333-4333-8333-333333333333","invited_user_id":"88888888-8888-4888-8888-888888888888","message":"Would you like to join our backend team?"}
```

Both UUIDs required/nonempty. Message optional/null, <=2000 characters **before** trimming, trim and store blank as null. Created/updated/expiry/actor/status are server values. Return 201; no Location pointing to a nonexistent GET-by-ID endpoint. No idempotency key in V1: a retry after a successful send returns `409 INVITATION_ALREADY_PENDING`, and Leader can retrieve history. After terminal state a subsequent send is a genuinely new Invitation if otherwise eligible.

```json
{
  "success":true,
  "message":"Project invitation sent.",
  "data":{
    "invitation_id":"99999999-9999-4999-8999-999999999999",
    "project_id":"11111111-1111-4111-8111-111111111111",
    "position_id":"33333333-3333-4333-8333-333333333333",
    "invited_user_id":"88888888-8888-4888-8888-888888888888",
    "inviting_leader_id":"22222222-2222-4222-8222-222222222222",
    "message":"Would you like to join our backend team?",
    "status":"pending",
    "created_at":"2026-11-03T03:00:00Z",
    "updated_at":"2026-11-03T03:00:00Z",
    "expires_at":"2026-11-10T03:00:00Z",
    "responded_at":null
  }
}
```

This complete object is `InvitationDto`, reused for all lifecycle mutation responses and lists. Send errors: common auth/validation/context errors; private/unavailable account targets → `404 CANDIDATE_NOT_FOUND`; self/member, pending, cooldown, Project/Position/capacity conflicts → Section 7. Apply ordinary per-caller fixed-window burst protection following Application policy: proposed `project-invitations`, 120 Send attempts/minute, zero queue, `429 INVITATION_RATE_LIMITED`; it does not alter cooldown. Authorize before quota. Source of truth is locked rows/server time; response means persisted Invitation only, no notification delivery promise.

### 6.4 Leader list — GET `/api/projects/{project_id}/invitations`

Actual Leader only, including archived Project history. No body. Example `?status=pending&position_id={position_id}&invited_user_id={user_id}&page=1&page_size=20`. status default `all`; accepts all five statuses or `all`. Position/recipient filters optional; Position must belong to Project or 404; well-formed unknown recipient filter returns empty list. Invalid query → 400, ownership → 403, missing Project → 404. Sort created_at DESC then invitation_id ASC. Source is Invitation history with effective expiration; no client status override.

200 example:

```json
{
  "success":true,
  "message":"Project invitations retrieved.",
  "data":{
    "items":[{
      "invitation_id":"99999999-9999-4999-8999-999999999999",
      "project_id":"11111111-1111-4111-8111-111111111111",
      "position_id":"33333333-3333-4333-8333-333333333333",
      "invited_user_id":"88888888-8888-4888-8888-888888888888",
      "inviting_leader_id":"22222222-2222-4222-8222-222222222222",
      "message":null,"status":"pending",
      "created_at":"2026-11-03T03:00:00Z","updated_at":"2026-11-03T03:00:00Z",
      "expires_at":"2026-11-10T03:00:00Z","responded_at":null
    }],
    "pagination":{"current_page":1,"page_size":20,"total_items":1,"total_pages":1,"has_next":false,"has_previous":false}
  }
}
```

Keep V1 list DTO minimal; client uses existing Project/Position/member-visible Profile reads for further detail. Those reads may return 404 for archived/unavailable resources; history IDs remain. Lists neither expose private profiles nor invent a new detail API. List reads are read-only and repeatable for unchanged data/time; live history changes can shift pages.

### 6.5 Leader cancel — POST `/api/projects/{project_id}/invitations/{invitation_id}/cancel`

Actual current Project Leader, including archived Project; no body. Invitation must belong to route Project, else `404 INVITATION_NOT_FOUND`. Only effective pending may cancel. 200 `{success:true,message:"Project invitation cancelled.",data:InvitationDto}`. Example data is the Send object with `status:"cancelled"`, `updated_at:"2026-11-03T04:00:00Z"`, `responded_at:null`; all other fields unchanged. Example request:

```http
POST /api/projects/11111111-1111-4111-8111-111111111111/invitations/99999999-9999-4999-8999-999999999999/cancel
Authorization: Bearer <access_token>
```

No pagination/sort. Expired → `409 INVITATION_EXPIRED`; any other terminal/repeat → `409 INVITATION_NOT_PENDING`. Competing response/cancel has one winning terminal transition under Project → Position → Invitation locks; neither decline cooldown nor capacity changes on cancel.

200 JSON example:

```json
{
  "success":true,"message":"Project invitation cancelled.",
  "data":{
    "invitation_id":"99999999-9999-4999-8999-999999999999",
    "project_id":"11111111-1111-4111-8111-111111111111",
    "position_id":"33333333-3333-4333-8333-333333333333",
    "invited_user_id":"88888888-8888-4888-8888-888888888888",
    "inviting_leader_id":"22222222-2222-4222-8222-222222222222",
    "message":"Would you like to join our backend team?","status":"cancelled",
    "created_at":"2026-11-03T03:00:00Z","updated_at":"2026-11-03T04:00:00Z",
    "expires_at":"2026-11-10T03:00:00Z","responded_at":null
  }
}
```

### 6.6 Member list — GET `/api/me/project-invitations`

Authenticated onboarded recipient only (`invited_user_id=caller`, including a caller leading another Project). No body. Query example `?status=pending&project_id={project_id}&page=1&page_size=20`; status default `all`, same enum as Leader list; optional Project filter, unknown valid Project ID yields empty list without probing access. 200 uses the **same complete list JSON shape and InvitationDto** as Section 6.4, with message `"My project invitations retrieved."`. Sort created_at DESC then invitation_id ASC. 400 invalid query; common auth errors. No Project leadership requirement; no opt-in requirement to read old Invitations. Effective status filtering/updated_at matches Leader list. No mutation or capacity guarantee.

200 empty-list JSON example:

```json
{"success":true,"message":"My project invitations retrieved.","data":{"items":[],"pagination":{"current_page":1,"page_size":20,"total_items":0,"total_pages":0,"has_next":false,"has_previous":false}}}
```

### 6.7 Member accept — POST `/api/me/project-invitations/{invitation_id}/accept`

Recipient only; no body. Unknown or another recipient's ID → `404 INVITATION_NOT_FOUND`, including attempts by its sender. Example request:

```http
POST /api/me/project-invitations/99999999-9999-4999-8999-999999999999/accept
Authorization: Bearer <recipient_access_token>
```

200 `{success:true,message:"Project invitation accepted.",data:InvitationDto}`; example is the Send object with `status:"accepted"`, `responded_at` and `updated_at` both `"2026-11-03T04:00:00Z"`. Full atomic checks/side effects are in Section 5.3. Existing Project detail/team and `/api/me/projects` reads reflect the committed active membership; no parallel join API or second approval. No pagination/sort. Repeat accept → `409 INVITATION_NOT_PENDING`, with no second increment. Expired → `409 INVITATION_EXPIRED`; other live acceptance conflicts → Section 7. Timeout/lost-response retry may yield 409; read own invitation list to confirm the recorded outcome.

200 JSON example (independent alternative to cancel/decline examples):

```json
{
  "success":true,"message":"Project invitation accepted.",
  "data":{
    "invitation_id":"99999999-9999-4999-8999-999999999999",
    "project_id":"11111111-1111-4111-8111-111111111111",
    "position_id":"33333333-3333-4333-8333-333333333333",
    "invited_user_id":"88888888-8888-4888-8888-888888888888",
    "inviting_leader_id":"22222222-2222-4222-8222-222222222222",
    "message":"Would you like to join our backend team?","status":"accepted",
    "created_at":"2026-11-03T03:00:00Z","updated_at":"2026-11-03T04:00:00Z",
    "expires_at":"2026-11-10T03:00:00Z","responded_at":"2026-11-03T04:00:00Z"
  }
}
```

### 6.8 Member decline — POST `/api/me/project-invitations/{invitation_id}/decline`

Recipient only; no body. Example request:

```http
POST /api/me/project-invitations/99999999-9999-4999-8999-999999999999/decline
Authorization: Bearer <recipient_access_token>
```

200 `{success:true,message:"Project invitation declined.",data:InvitationDto}`; example Send object becomes `status:"declined"`, responded_at=updated_at=`"2026-11-03T04:00:00Z"`. Cooldown ends exactly `2026-11-10T04:00:00Z`. Project closure/archive, full Position and opt-out do not prevent Decline; existing caller account/onboarding policy still applies. Unknown/foreign → 404; expired → 409 INVITATION_EXPIRED; repeat/other terminal → 409 INVITATION_NOT_PENDING. Project → Position → Invitation locks; no membership/count change, and repeats never extend cooldown. No pagination/sort.

200 JSON example:

```json
{
  "success":true,"message":"Project invitation declined.",
  "data":{
    "invitation_id":"99999999-9999-4999-8999-999999999999",
    "project_id":"11111111-1111-4111-8111-111111111111",
    "position_id":"33333333-3333-4333-8333-333333333333",
    "invited_user_id":"88888888-8888-4888-8888-888888888888",
    "inviting_leader_id":"22222222-2222-4222-8222-222222222222",
    "message":"Would you like to join our backend team?","status":"declined",
    "created_at":"2026-11-03T03:00:00Z","updated_at":"2026-11-03T04:00:00Z",
    "expires_at":"2026-11-10T03:00:00Z","responded_at":"2026-11-03T04:00:00Z"
  }
}
```

### 6.9 Existing owner Profile GET/PATCH extension

Reuse `/api/me/profile`; no invitation-settings endpoint. GET has no body/query/pagination; PATCH request example:

```json
{"open_to_team_invitations":true}
```

Add a presence-aware Boolean field to the existing parser/request: omitted preserves value; true/false accepted; null, strings, numbers and duplicate property rejected with `400 VALIDATION_FAILED` and `errors.open_to_team_invitations`. Existing unknown-field/empty-object rules and all other Profile validation remain. Persist inside the existing User-row transaction. Repeating the same Boolean has the same persisted state; existing Profile updated_at behavior need not change. GET/PATCH return the complete **existing owner DTO plus the Boolean**; no other field is removed. 200 examples, abbreviated explicitly to show the extension:

```json
{"success":true,"message":"User profile retrieved successfully.","data":{"user_id":"88888888-8888-4888-8888-888888888888","open_to_team_invitations":true}}
```

```json
{"success":true,"message":"User profile updated successfully.","data":{"user_id":"88888888-8888-4888-8888-888888888888","open_to_team_invitations":true}}
```

Full owner DTO remains defined by `MyProfileResponse.cs` and [PROFILE_API.md](PROFILE_API.md). Owner-only, common auth errors, `404 NOT_FOUND` for missing/ineligible Profile, existing `422 BUSINESS_RULE_VIOLATION` for Profile invariants. No sorting. Source is stored user_profiles under existing owner transaction; opting out affects new discovery/sends and authoritative Accept, not existing membership/history.

## 7. Conflict taxonomy and edge cases

All codes in this section for Invitations/candidates are **proposed** except explicitly reused Project/Application codes. For live Send/interaction writes use validation → Project existence/ownership → Position relation → private candidate visibility → expired normalization → same-pair pending Invitation → pending Application → cooldown → self/membership → Project recruiting/deadline/team capacity → Position open/capacity. Self is visible to its owner and gets the explicit self error. Acceptance ordering follows Section 5.3: recipient/state/time checks precede eligibility and capacity; Position unavailable precedes Project-capacity conflict. Avoid returning SQL constraint text.

| Case | Exact V1 outcome |
| --- | --- |
| Invite yourself | `409 CANNOT_INVITE_SELF`; no row, count or membership change |
| Invite active Member of this Project | `409 ALREADY_PROJECT_MEMBER`; other Project membership/leadership allowed |
| Invite with no Project/Position capacity | `409 PROJECT_CAPACITY_EXCEEDED` or `POSITION_UNAVAILABLE`; no reservation |
| Invite after cutoff, at cutoff, archived, or not recruiting | `409 PROJECT_NOT_ELIGIBLE`; Project existence/ownership still checked first |
| Send to missing/inactive/deleted/incomplete/opted-out candidate | `404 CANDIDATE_NOT_FOUND`; do not disclose account status or opt-in reason |
| Pending Application exists, any Position | Send/interaction PUT: `409 APPLICATION_ALREADY_PENDING` (reused code) |
| Pending Invitation exists, any Position | Send/interaction PUT: `409 INVITATION_ALREADY_PENDING`; do not overwrite message/Position or create another row |
| Apply with effective pending Invitation | Existing Apply: new `409 INVITATION_ALREADY_PENDING`; no pending Application created. Existing pending Application duplicate precedence stays unchanged. |
| Simultaneous Invite/Apply | Project lock serializes; one may create 201, loser sees pending conflict. If other validations fail, neither need succeed. No committed state contains both pending types. |
| Two duplicate sends | One 201; other 409 INVITATION_ALREADY_PENDING, including different Positions. PostgreSQL partial index independently protects Invitation duplicates. |
| Accept concurrently for last slot | Exactly one 200; loser 409 POSITION_UNAVAILABLE or PROJECT_CAPACITY_EXCEEDED; losing unexpired Invitation remains pending |
| Accept vs Application approval on last slot | Same Project lock, same capacity result as above; no double fill |
| Accept twice | First 200; subsequent 409 INVITATION_NOT_PENDING; membership/count unchanged by repeat |
| Decline twice | First 200; subsequent 409 INVITATION_NOT_PENDING; cooldown anchored to first response |
| Cancel twice; competing Accept/Decline/Cancel | First terminal mutation wins; others 409 INVITATION_NOT_PENDING, or INVITATION_EXPIRED if expiry won |
| Accept/decline/cancel a still-pending Invitation exactly at or after expiry | `409 INVITATION_EXPIRED`, expiry transition committed; responded_at remains null. Previously accepted/declined/cancelled rows remain terminal and return INVITATION_NOT_PENDING. |
| Accept after archive or lifecycle change | `409 PROJECT_NOT_RECRUITING` (reused); keep live Invitation pending, allow Decline/Cancel |
| Accept after Position closes or fills | `409 POSITION_UNAVAILABLE` (reused); pending unchanged |
| Accept after Project fills | `409 PROJECT_CAPACITY_EXCEEDED` (reused); pending unchanged |
| Accept while caller opt-out/Profile missing | `409 CANDIDATE_UNAVAILABLE`; pending unchanged. Inactive/deleted/incomplete caller normally fails existing authorization before handler; recheck inside transaction still protects changes after middleware. |
| Accept while already active Member/self | `409 ALREADY_PROJECT_MEMBER`; pending unchanged |
| Original sender no longer matches actual Project Leader | `409 INVITATION_SENDER_UNAVAILABLE`; pending unchanged. No leadership transfer API is introduced. |
| Legacy/corrupt pending Application coexists at Accept | `409 APPLICATION_ALREADY_PENDING`; never auto-withdraw/delete history; release requires explicit existing workflow |
| Reinvite before latest declined responded_at + 168h | `409 INVITATION_COOLDOWN_ACTIVE`, even for another Position. No new Invitation. |
| Reinvite exactly at/after cooldown | 201 new row if admission otherwise passes; preserve declined row |
| Reinvite after cancelled/expired | Immediately allowed after expiration normalization if otherwise eligible; no cooldown |
| Invitation sent before Member opts out | Hidden from new discovery; lists/history retained; Accept blocked until opt-in restored; Decline 200 while still valid |
| Requirements/expected hours edited while pending | Invitation remains valid; fresh matches recompute. Accept has no skill/role/availability threshold; current title used for membership and current capacity/state enforced. |
| Member changes Profile | Fresh discovery recomputes; existing snapshots retain scored_at inputs; opt-in/account eligibility rechecked live, including Accept. Changing skills/role/hours alone does not block acceptance. |
| Accept after recruitment deadline | 200 while unexpired, Project still recruiting/unarchived and Position/Project available. Deadline is admission-only; no extension needed. |
| Position deleted with any Invitation history | Existing Position DELETE gains `409 POSITION_IN_USE`; close instead. A nonexistent/wrong Position on new request → 404 POSITION_NOT_FOUND. |
| Project archived while pending | History preserved; no implicit cancellation. Accept 409; authorized Decline/Cancel still possible before expiry. |
| Physical Project/Position/User deletion attempted outside APIs | Restrict FK protects Invitation history; not a new public delete operation. Interactions may cascade as specified. |
| Recipient owns/joins other Projects | No cross-Project quota; invitation can be accepted subject to selected Project checks |

`INVITATION_COOLDOWN_ACTIVE` detail can include the next allowed UTC timestamp as human-readable text; no new ProblemDetails extension is required. Keep API consumers keyed to error_code, not message parsing. Existing auth/validation/rate-limit responses continue to use their own middleware conventions.

## 8. Security, compatibility and operational limits

Use explicit safe projections, not serialized User/UserProfile entities or owner DTO reuse. Candidate previews contain only the Section 6.1 whitelist; no primary/auth email, phone, resume, credentials, OAuth metadata, storage keys, private certificate files or owner media metadata. Opt-in itself is owner-only; discovery membership implies consent without returning the setting. Existing member-visible Profile may intentionally expose `public_email`; that authorized contract is unchanged. Existing media/attachment visibility checks remain mandatory when following detail links.

All candidate resources require actual ownership, even if a user guesses valid IDs or supplies a foreign Position. All recipient mutations mask foreign Invitations as 404. Send accepts no leader ID; member responses accept no recipient/status/counts/score. Return server values after commit, avoid caching private responses publicly, and use parameterized EF/SQL writes.

Minimum future integration changes are Apply's Invitation exclusion/normalization and Position deletion's Invitation-history guard. No stable Project/Application schema redesign, new Application cooldown, scorer correction, global role restriction or Project feed behavior change is proposed. Requirement edits may race scoring reads just as current Discovery projections do; fresh feed is an advisory snapshot, and acceptance does not enforce skill thresholds. Existing direct Position skill insert/delete methods lack Project locks: do not claim they already serialize with approval. Hardening them is a separately identified follow-up, unnecessary to the capacity invariant here; Position state/count edits already use the Project lock.

Deploy the two cross-table-aware writers together and drain older API writers before enabling Invitations. Cross-table pending exclusion is guaranteed for cooperating repository transactions, not arbitrary SQL or old binaries. A DB-wide trigger or unified recruitment table would require a separate design and is deliberately avoided. Initial false opt-in means no discovery candidates until Members consent. Process-local cursor state limits multi-instance routing; PostgreSQL preferences/history remain durable through API restarts. History retention and foreign-key restrictions can affect future physical account deletion. These are implementation risks, not verified production guarantees from this documentation review.
