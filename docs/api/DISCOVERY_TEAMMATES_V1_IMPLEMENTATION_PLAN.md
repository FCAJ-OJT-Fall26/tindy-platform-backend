# Discovery Teammates V1 — backend implementation plan

Status: STEP 00 only, 2026-11-03; source baseline `2f1e2f5`. The companion [requirements/API design](DISCOVERY_TEAMMATES_V1.md) is normative for proposed behavior, schema, JSON, edge cases and errors. Every file addition/change below is **future work**, not work performed in this step. Stop after review of these two documents; do not automatically start STEP 01.

## 1. Architecture decisions and boundaries

The actual solution has Application, Infrastructure and API projects targeting .NET 10. Entities live in Application, with DTOs, services and repository/service interfaces; Infrastructure holds EF Core/PostgreSQL, transactions and implementations. Follow this structure; do not create a new Domain assembly to satisfy an assumed layout.

| Reuse | Add | Preserve |
| --- | --- | --- |
| `PositionMatchScorer.Score`, `DiscoveryMemberProfile`, `DiscoveryPosition`, `PositionMatch`, `MatchFactor`, safe skill/role DTOs, `PaginationMeta` | Teammate discovery projection/query/service/session store; separate interaction entity/repository/service; Invitation entity/repository/service/controllers | rule-v1 weights, missing-factor normalization, rounding and existing Project feed mix/cursors |
| `ProjectWriteLock`, Application approval transaction/reload patterns, composite ProjectMember and Position principal key | Shared pending-Invitation normalization helper used by Send **and existing Apply**, new partial index, cooldown/state logic | Application endpoints/statuses/history, rejected/withdrawn immediate reapplication, no new Application delay |
| Existing owner Profile GET/PATCH/parser/User lock, active/onboarded authorization, snake-case envelopes/ProblemDetails | Owner-only Boolean opt-in; narrow candidate DTO and proposed business codes | Authentication, Onboarding, member-visible Profile privacy and public Project browsing |
| `TestPostgres`, `TindyApiFactory`, existing clock/test data and independent request-scope race patterns | Real PostgreSQL invitation/interactions/schema/HTTP/concurrency coverage | Stable Project/Position/Application schema and membership counting semantics |

All new names listed below are proposed. Existing files are identified separately. No Postman setup, collection, OpenAPI export, frontend, notification transport, AI, new membership departure workflow or scheduled cleanup worker is required for these four backend steps.

Important discrepancies to carry forward:

- The current scorer includes missing preferred role as zero, while dropping absent availability/empty Position skill groups. Do not silently fix that asymmetry.
- There is no current Invitation entity, opt-in field, teammate interaction table or candidate endpoint. Existing Project interactions cannot supply the requested Project/Position/candidate key or exclusive action semantics.
- Existing Apply knows nothing about Invitations. A future narrow code change is mandatory for cross-table exclusion, even though its stable schema stays unchanged.
- Position deletion currently guards members and Applications only. Add history awareness before enabling Invitations, with `POSITION_IN_USE` instead of an FK-generated 500.
- No single-led-Project database/service guard was found. Do not add one here; a Leader may participate elsewhere.
- Existing Position skill insertion/removal does not always take ProjectWriteLock. Advisory matching can tolerate requirement edits and acceptance has no skill gate. Document this existing limitation; broad write-path cleanup is outside this implementation.

## 2. STEP 01 — Database Foundation and owner opt-in

Dependencies: approved STEP 00 design only. Do not expose candidate/Invitation routes yet.

### Existing files to update

| File under repository root | Planned change |
| --- | --- |
| `src/Tindy.Application/Entities/UserProfile.cs` | Add `OpenToTeamInvitations`, default false. No onboarding behavior changes. |
| `src/Tindy.Infrastructure/Persistence/Configurations/UserProfileConfiguration.cs` | Map `open_to_team_invitations`, required/default false. |
| `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | Add `ProjectInvitations` and `TeammateDiscoveryInteractions` DbSets; retain assembly configuration discovery. |
| `src/Tindy.Application/DTOs/Profile/UpdateProfileRequest.cs` | Presence-aware Boolean field and strict parser support: missing preserves, true/false accepted, null/wrong type/duplicate invalid. |
| `src/Tindy.Application/DTOs/Profile/MyProfileResponse.cs` | Add owner response Boolean, carefully update positional record call sites. Do not add to MemberProfileResponse. |
| `src/Tindy.Application/Services/ProfileService.cs` | Set supplied Boolean in existing Profile transaction, retain all onboarding invariants. |
| `src/Tindy.Infrastructure/Repositories/ProfileRepository.cs` | Project Boolean only into owner DTO; member projection unchanged. |
| `src/Tindy.Infrastructure/Persistence/Migrations/TindyDbContextModelSnapshot.cs` | EF-generated change only when future migrations are created; inspect for unrelated churn. |

`MyProfileController` and `IProfileService` signatures should need no change: they already parse/return the owner Profile contract. Locate all `new MyProfileResponse(...)` fixtures/call sites before changing the record; avoid accidental field ordering changes.

### New files/classes

- `src/Tindy.Application/Entities/ProjectInvitation.cs`: exact fields/state defaults in design Section 4.2, explicit navigations to Project, Position, recipient and sender; no need to add reverse collections to stable entities.
- `src/Tindy.Application/Entities/TeammateDiscoveryInteraction.cs`: composite-key preference fields/navigations.
- `src/Tindy.Infrastructure/Persistence/Configurations/ProjectInvitationConfiguration.cs`: columns, all Restrict FKs, existing composite Position principal key, status/actor/time checks and named indexes.
- `src/Tindy.Infrastructure/Persistence/Configurations/TeammateDiscoveryInteractionConfiguration.cs`: composite PK, action/time checks, cascade FKs and indexes.
- `tests/Tindy.Tests/Projects/TeammateDiscoverySchemaTests.cs`: metadata plus actual PostgreSQL constraints, up/down/up preservation.

No extra repository/interface/service/controller is needed solely to persist these new tables. Leave invitation lifecycle logic to STEP 04.

### Future migrations

Use existing `<Feature>_<Action><Target>` naming, without manual timestamps or step numbers:

1. `Profile_AddTeamInvitationOptIn`: nonnull Boolean default false; verify existing Profiles and old writers default to false.
2. `Discovery_CreateTeammateRecruitmentTables`: two new tables, checks, FKs, indexes from design. No redundant Position alternate key and no unrelated Project/Application changes. `expires_at` check must use 168 elapsed hours, verified under UTC and a DST session timezone.

EF should generate main migration, Designer and snapshot together. Inspect SQL/Up/Down; only apply later to an explicitly disposable development/test database. Down drops new tables/Boolean and loses feature history/consent: back up and disable feature writers before downgrade. Never edit older applied migrations. No migrations are created/applied during STEP 00.

### Tests and acceptance criteria

- Extend `tests/Tindy.Tests/Profile/ProfilePartialUpdateTests.cs`, `MyProfileReadTests.cs`, `ProfileSecurityRegressionTests.cs`, `Integration/ProfileHttpRegressionTests.cs`: Boolean false backfill/default, true/false/omission, null/string/number/duplicate rejection, unrelated fields preserved, owner-only visibility, default JWT/account/onboarding policy. Member self-view still cannot return owner-only Boolean.
- Schema tests inspect pending-only unique Invitation key across Positions; allow terminal history plus one new pending row. Direct duplicate pending insert must fail with named unique constraint.
- Test invalid status/action, self-actor constraint, timestamp/expiry/response combinations, missing principals and cross-Project Position FKs. Verify no new stable-table indexes/columns beyond Profile Boolean.
- Test Invitation history Restrict behavior and preference cascade behavior for Position/Project/User. Composite FK uses existing alternate key.
- Apply migrations on prefeature fixture, confirm Project/Application/Profile/Leader data unchanged except default false; down/up in disposable PostgreSQL and verify expected feature-data loss only.
- Acceptance: model matches proposed schema, existing suite compiles after DTO extension, consent is never inferred, and no new API route claims working Invitations.

## 3. STEP 02 — Candidate Discovery and Matching

Dependencies: STEP 01 schema/Profile opt-in. Must exclude stored effective pending Invitations even if no API can send them yet; tests can seed Invitations directly. STEP 02 reads persisted skipped interactions in anticipation of STEP 03.

### New files/classes

| Proposed path | Responsibility |
| --- | --- |
| `src/Tindy.Application/DTOs/Projects/TeammateDiscoveryDtos.cs` | Filters, safe candidate preview, scored candidate card, page with scored_at/cursor/version; reuse PositionMatch instead of duplicating scorer output. |
| `src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryRepository.cs` | Read selected context with actual ownership; batched candidate projections and current-eligibility refresh. Return data needed by service without exposing EF/DbContext. |
| `src/Tindy.Application/Services/TeammateDiscoveryService.cs` | Query validation, all-candidate scoring via Score for selected Position, stable score/UUID order, pagination/session orchestration. |
| `src/Tindy.Application/Services/TeammateDiscoveryFeedSessions.cs` | Separate bounded signed cursor store; bind caller/Project/Position/filters/limit/version, 15 minutes, eviction and advancing inspected offsets. Do not refactor existing feed store merely to share code. |
| `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryEligibility.cs` | Separate readable relational predicates for candidate account/consent/membership/pending/cooldown/skip and selected context. Effective expiry is status pending AND expires_at > now. Preserve existing DiscoveryEligibility. |
| `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryRepository.cs` | AsNoTracking whitelist projections, split/batched reads, full eligible result before scoring, no N+1. User/profile/skills/catalog/state from database. |
| `src/Tindy.Api/Controllers/ProjectCandidatesController.cs` | GET selected Project/Position candidates, default `[Authorize]`, route/query binding and success envelope. |
| `tests/Tindy.Tests/Projects/TeammateDiscoveryEligibilityTests.cs` | Predicate and boundary matrix. |
| `tests/Tindy.Tests/Projects/TeammateDiscoveryFeedTests.cs` | Selected-Position scoring, ranking/cursors/current rechecks/performance. |
| `tests/Tindy.Tests/Projects/TeammateDiscoveryHttpTests.cs` | Real routing/auth/DTO/error/privacy contract. |

Existing files to update: `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` (scoped service, singleton teammate sessions, reuse singleton TimeProvider); `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` (scoped repository). No migration. Concrete Application services are consistent with existing Discovery/Application registration; do not add empty service interfaces solely for symmetry.

### Validation and authorization

- Actual selected Project Leader, not a global role; verify Position belongs to it. Require recruiting/unarchived/deadline/Project capacity/Position capacity.
- Full candidate eligibility from design; no skill threshold; extra filters AND on catalog IDs and hours. Return safe missing-candidate behavior through writes later; never expose account-status reasons in cards.
- Cursor binds all context. Revalidate ownership and live eligibility on each page, including opt-out, pending Applications/Invitations, cooldown and skipped action. Freeze preview/score/explanation at scored_at; refresh slots/action only.
- Preserve existing DiscoveryService/Repository/Eligibility/FeedSessions/PositionMatchScorer files and Project feed output.

### Tests and acceptance criteria

- Include active/onboarded/opted-in candidate; exclude every failed account predicate, Leader/self, active membership, pending Application, live pending Invitation to any Position, cooldown and Position-specific skip. Include former membership, another Project's Leader, partial/zero required-skills match. Expired pending rows do not exclude; exact expiration/cooldown boundaries use fixed TimeProvider.
- Compare candidate result to direct existing scorer for perfect/partial/empty skills, missing role/hours, absent skill groups, exact IDs and renamed skills. Selected Position must determine score even when another Position scores higher. Preserve `best_position` schema meaning.
- Test tied scores/UUID order, 1,000+ candidates with high scorer beyond first 50 DB records, all filters, empty result, clamping, malformed query and GUID route behavior.
- Traverse one snapshot without duplicates while candidates become skipped, opted out, active Members or pending applicants/invitees. Verify stale offsets advance; newly eligible candidates require refresh if already inspected. Profile/requirement changes have frozen scores until refresh.
- Test cross-account/Project/Position cursors, changed filters/limit/sort, tampering, expiry, eviction/restart and oversized snapshot response. No unbounded per-card queries; record query count as existing DiscoveryFeedTests does.
- HTTP: anonymous/inactive/incomplete/nonleader access; cross-Project Position; correct snake_case, factors, no email/phone/credentials/private media metadata. Existing member-detail links reused.
- Acceptance: backend scores and filters every candidate, deterministic snapshot traversal, PostgreSQL eligibility and safe previews, existing Project discovery tests unchanged.

## 4. STEP 03 — Candidate Interactions

Dependencies: STEPS 01–02. No migration; table already created. Candidate feed already reads skipped state and current action.

### Files and responsibilities

New files:

- `src/Tindy.Application/DTOs/Projects/TeammateInteractionDtos.cs`: strict action request/parser, stored interaction response and paged management item with safe candidate/null and is_discoverable.
- `src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryInteractionRepository.cs`: list, atomic set and reset abstractions.
- `src/Tindy.Application/Services/TeammateDiscoveryInteractionService.cs`: action/query validation and orchestration.
- `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryInteractionRepository.cs`: ownership/context checks, Project → Position → candidate User locking for PUT, parameterized UPSERT, reset, timestamp-preserving no-op, paging/projections. Reuse teammate eligibility semantics without coupling to Project-direction interactions.
- `src/Tindy.Api/Controllers/ProjectCandidateInteractionsController.cs`: the three Project-scoped routes with mandatory position_id query; no duplicate Position route family.
- `tests/Tindy.Tests/Projects/TeammateInteractionHttpTests.cs` and `TeammateInteractionConcurrencyTests.cs`.

Update existing API/Infrastructure `Extensions/ServiceCollectionExtensions.cs` for service/repository DI. No change to `DiscoveryInteractionsController`, `DiscoveryInteractionRepository`, `DiscoveryProjectInteraction` or Project preferences.

### Rules, tests and acceptance

- Verify actual Leader on all operations; mandatory Position context, wrong/missing Position/query, invalid action, unknown/duplicate body field, forged actor/status/score rejected. PUT requires current admission/candidate eligibility, ignoring existing skip; GET/DELETE allow archived/closed context and reset unavailable candidate.
- Verify actions replace each other; same-action PUT retains created_at/updated_at, DELETE repeat succeeds, PUT after DELETE starts fresh timestamp, one tuple across concurrent PUTs. Different Positions/Projects stay isolated.
- Separate concurrent PostgreSQL request scopes test conflicting UPSERT and UPSERT/DELETE; one final committed state with no duplicates, 500s or lost partial fields.
- GET sorted updated_at DESC/candidate UUID ASC; paging/filter/empty cases; inactive/deleted/incomplete/opted-out candidate yields null preview. Preserve identifiers/preference while omitting unavailable private details.
- Skip removes only that Position's regular candidate card, undo permits it on appropriate page/refresh. Saved/shortlisted have no Invitation/membership side effect. Explicit Send in STEP 04 may still invite a skipped candidate.
- Acceptance: all preferences durable in PostgreSQL, no localStorage dependency, reload survives restart, privacy enforced, Project-direction saved/skipped remains unchanged.

## 5. STEP 04 — Invitations and Membership

Dependencies: STEPS 01–03 for full feature delivery; core Invitation lifecycle technically needs STEP 01 plus shared teammate eligibility/DTO patterns. No additional migration is expected. Do not enable Send before both cross-table-aware writers and Position history guard are deployed.

### New files/classes

| Proposed path | Responsibility |
| --- | --- |
| `src/Tindy.Application/DTOs/Projects/ProjectInvitationDtos.cs` | Create request/strict parser, full InvitationDto, list/query/page, repository outcome model for post-commit business errors. Server-only identity/state/time fields never writable. |
| `src/Tindy.Application/Interfaces/Repositories/IProjectInvitationRepository.cs` | Transactional Send/Accept/Decline/Cancel and owner/Leader list contracts, with explicit outcomes where expiration must commit before reporting conflict. |
| `src/Tindy.Application/Services/ProjectInvitationService.cs` | Input validation/message normalization, orchestration, map committed repository outcomes to existing ProjectException; no DbContext/SQL. |
| `src/Tindy.Infrastructure/Repositories/ProjectInvitationRepository.cs` | Actual ownership/recipient privacy, state/time/cooldown, effective read status, all transactions and membership/count updates. |
| `src/Tindy.Infrastructure/Repositories/ProjectInvitationPendingState.cs` | Small shared internal helper used by Invitation Send and existing Apply: under caller-owned Project transaction lock normalize elapsed pending pair; query live pending/cooldown consistently. It never commits another caller's transaction. |
| `src/Tindy.Api/Controllers/ProjectInvitationsController.cs` | Leader POST/GET/cancel, default auth, Send rate limiter. |
| `src/Tindy.Api/Controllers/MyProjectInvitationsController.cs` | Recipient GET/accept/decline, default auth, caller identity only. |
| `tests/Tindy.Tests/Projects/ProjectInvitationHttpTests.cs` | Contracts, lifecycle/time/eligibility/security/history. |
| `tests/Tindy.Tests/Projects/ProjectInvitationConcurrencyTests.cs` | Real PostgreSQL competing transactions and rollback. |

### Existing files to update

| File | Narrow planned change |
| --- | --- |
| `src/Tindy.Infrastructure/Repositories/ProjectApplicationRepository.cs` | CreateAsync: after Project lock normalize elapsed recipient Invitations, reject live pending Invitation before Application insert; preserve duplicate pending Application precedence and existing eligibility. If expiry-only changes occur before admission conflict, commit them via an outcome before reporting business error. Application approval/reject/withdraw behavior unchanged. |
| `src/Tindy.Infrastructure/Repositories/ProjectRepository.cs` | DeletePositionAsync checks any Invitation history under existing Project lock and returns POSITION_IN_USE before delete. No archive auto-cancel or general Project redesign. |
| `src/Tindy.Infrastructure/Repositories/ProjectWriteLock.cs` | Clarify invitation/Apply lock-order documentation; retain transaction-scoped parameterized Project lock implementation. |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | Scoped Invitation service; proposed project-invitations Send fixed-window policy and INVITATION_RATE_LIMITED rejection mapping without changing Application limiter. |
| `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Scoped Invitation repository; shared helper can remain internal static or instance helper as appropriate. |
| `tests/Tindy.Tests/Projects/ProjectApplicationHttpTests.cs`, `ProjectApplicationConcurrencyTests.cs` | New Invitation exclusion/normalization cases; retain immediate reapply and original status assertions. |

Existing team/detail/my-project reads should require **no** changes: they already use ProjectMembers, so accepted invitation members appear naturally. Do not change led-project pending Application counts to include Invitations. No global exception-handler redesign: use existing ProjectException and named-constraint mapping; strict parser validation uses existing ProfileValidationException pattern for field errors.

Keep `IProjectApplicationRepository.CreateAsync` and `ProjectApplicationService` signatures unchanged: the existing repository can collect an admission-error outcome, commit any expiry-only update, then throw ProjectException after commit. New Invitation operations may return their explicit outcome to the new service, where translation happens after the repository commits. The shared pending-state helper never commits independently.

### Transaction implementation checklist

1. Each mutation owns one transaction and takes ProjectWriteLock before capacity/state decisions. Send locks selected Position then pending Invitation then candidate User; accept/decline/cancel use Project → Position → Invitation → User if needed. No User → Project path.
2. Use untracked ownership/context lookup only to resolve locks; re-read under lock. Reload tracked Position/Invitation/member state as existing Application approval does. Capture UTC now after relevant waits.
3. Perform elapsed-pending normalization before pending-index insertion; SaveChanges expiry before insert to avoid EF command-order surprises. Commit expiry-only outcomes on business conflicts and translate after commit, including Apply. Exceptions must not accidentally undo a required expiration transition.
4. Acceptance checks active/onboarded/nondeleted recipient, current opt-in/Profile, actual Leader relationship, recruiting/unarchived Project, selected Position open/capacity, Project active count, no active membership and no pending Application. Exempt recruitment deadline for delivered valid Invitations. Do not call the existing admission-only DiscoveryEligibility unchanged.
5. Add/reactivate membership and increment Position in same save/commit as accepted status; RoleTitle from current Position, no Project filled-count column. A retry cannot increment twice. Send/decline/cancel never reserve/release capacity.
6. State/time/error ordering follows normative design. Decline cooldown spans Project/recipient/any Position; no Application cooldown. Terminal rows immutable, reinvite new ID. Effective read expiry/filter/order uses one request time without writing GETs.
7. Map only `ux_project_invitations_pending` uniqueness to INVITATION_ALREADY_PENDING. Unexpected FK/DB errors still roll back and use existing internal error treatment; prevent predictable Position deletion errors explicitly.

### Unit/HTTP/integration tests

- Strict request validation: required/empty UUIDs, message pretrim 2000 limit, blank/null, unsupported/duplicate properties and server fields. Default policies, actual Leader vs unrelated Leader, guessed resource IDs, recipient-only 404 masking, no private data.
- Send self/member/full/closed/deadline/archived/inactive/unonboarded/opt-out/pending Application/pending Invitation/cooldown cases, explicit skipped candidate send, another Project's Leader accepted as candidate. 201 full DTO/server timestamps/exact 168-hour expiry, 429 policy and preserved Application limiter.
- Both lists: all five effective statuses plus all, filters, tied timestamp ordering, metadata, empty pages, archived history, owner-only isolation, effective expired pending excludes pending filter without DB write.
- Expiry boundaries one tick before/at/after; lazy expired-pending row is terminal before reinvite; old row preserved, new ID created. Expiry survives 409 Accept/Decline/Cancel and failed Send/Apply admission. Force infrastructure failure to establish rollback and subsequent effective-read/retry correctness.
- Decline/cancel/accept/repeat and competing terminal transitions; no cooldown extension by repeat; cooldown exact boundary across Positions; cancelled/expired have no cooldown. Reject/withdraw Application immediate reapply unchanged, including while independent declined Invitation history exists.
- Acceptance after deadline succeeds; archive/not recruiting/closed/filled/Project-full opt-out after send fails correctly, pending remains live, and explicit Decline is still allowed. Profile skills/role/hours/Position requirements changes do not create skill gates. Current membership title/count/state comes from locked Position.
- Reactivate left/removed composite membership, no duplicate active membership, Project active count includes Leader, existing `/api/me/projects`/team reads expose member, invitation response is committed.
- PostgreSQL independent connections: Send vs Apply (same/different Position), Send vs Send, expiry normalization vs reinvite, Accept vs Accept last Position slot, last Project slot across Positions, Accept vs Application approval, Accept vs Decline/Cancel, archive/close/capacity-shrink vs Accept. Assert exactly legal winners/losers, counts/FKs/history, no double membership or 500.
- User-lock race: Profile opt-out vs Accept/Send. If opt-out commits first, admission fails; if acceptance commits first, later opt-out does not revoke membership. No deadlock from reverse lock order.
- Inject failure before acceptance SaveChanges/commit using existing PostgreSQL test trigger pattern; assert Invitation pending, membership absent/original, count/state unchanged. Retrying terminal success is conflict, never another increment.
- Position DELETE with pending/accepted/declined/cancelled/expired history returns POSITION_IN_USE; close/archive preserve history; interaction-only deletion cascades preferences.

Acceptance: all new routes satisfy normative contract, exactly one pending offer type across cooperating Apply/Send transactions, exactly one winner for last-slot contention, no partial acceptance, historical rows preserved, and existing Discovery/Profile/Project/Application tests remain green. Do not claim arbitrary SQL writers are protected by the cross-table protocol.

## 6. Verification, migration and rollout risks

| Risk | Required mitigation/evidence |
| --- | --- |
| Mixed old/new writers can create pending Application + Invitation | Drain old instances; deploy both aware writers and Position history guard together before allowing Send. Same Project row lock in every write path; future jobs/importers must follow it. No trigger/table merge in V1. |
| Expired pending index blocks next send | Shared normalization under Project lock; flush expiry before insert; committed conflict outcomes; reads use effective expiry. No reliance on a cleanup timer. |
| Seven-day date arithmetic shifts with session timezone/DST | UTC TimeProvider, 168 elapsed hours for expiry/cooldown, database CHECK boundary tests under multiple session timezones. Recruitment deadline keeps existing Vietnam calendar semantics. |
| Restrict history FKs change deletion outcomes | Guard Position DELETE explicitly; archive remains soft, future physical User/Project deletion needs history policy. |
| Profile positional record addition breaks constructors/fixtures | Find/update all owner DTO constructors and test fakes; member DTO/schema stays unchanged. Consent defaults false for existing/new Profiles. |
| Full candidate scoring/session memory | Batched whitelist projections, full-result stable ranking, bounded separate snapshots, 503 on oversize, 1,000+ candidate query-count and traversal tests. No arbitrary top-50 preselection. |
| Multi-instance cursors | Single-process V1 with affinity and explicit expiry/refresh errors. PostgreSQL preferences/Invitations durable; distributed snapshot storage later. |
| Snapshot match becomes stale | scored_at and documented frozen inputs; live eligibility checks each page; fresh feed recomputes; authoritative Send/Accept never trusts score. |
| Inconsistent existing membership/filled counts | Test reactivation assumptions and inspect dev data; do not silently backfill stable counters in migration. Existing departure workflows/data must already have released old slots. |
| Downgrade destroys consent/history | Back up, disable feature writers, review generated Down. No automated downgrade on shared application DB. |

For implementation validation, run `dotnet build Tindy.slnx` and appropriate targeted tests, then the required full `dotnet test Tindy.slnx` before release. PostgreSQL tests use existing Testcontainers PostgreSQL 16 helper or explicitly configured loopback-only `TINDY_TEST_POSTGRES`; each fixture creates its own random test database. Do not migrate or reset the configured application database to run these checks. Report actual test results and infrastructure limits; inspected test source is not a passing test run.

No build/API/database test is needed to establish runtime behavior in STEP 00 because runtime behavior is unchanged; documentation validation should parse JSON examples, check local links/source references, verify endpoint/state/schema consistency, and confirm only the two requested files changed. Runtime guarantees remain future acceptance criteria.

## 7. Recommended next action

Review the documented assumptions (exclusive interaction action, opt-in required again at Accept, cooldown-hidden candidates, unchanged Project feed, process-local snapshot limit), then explicitly start **STEP 01 — Database Foundation**. That step adds schema and owner opt-in only. STEPS 02–04 remain separate work; this STEP 00 task ends with the design deliverables.
