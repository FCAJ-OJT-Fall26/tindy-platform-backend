# Discovery Teammates V1 — Báo cáo triển khai Backend

Ngày: 03/11/2026. Baseline Backend HEAD `2f1e2f5`. Báo cáo này ghi nhận implementation thực tế, bổ sung cho [requirements/API contract](DISCOVERY_TEAMMATES_V1.md) và [kế hoạch STEP 01–04](DISCOVERY_TEAMMATES_V1_IMPLEMENTATION_PLAN.md). Hai tài liệu STEP 00 được giữ nguyên như nguồn thiết kế đã duyệt, bao gồm nhãn lịch sử “proposed”; trạng thái triển khai và bằng chứng kiểm thử mới nằm ở đây.

## 1. Trạng thái các bước

| Bước | Implementation | Kiểm thử theo bước đã chạy |
| --- | --- | --- |
| STEP 01 — Schema và owner Profile opt-in | Đã thêm field, entities, configurations, DbSets và đúng hai EF migrations | 75 passed, 0 failed, 0 skipped trong bộ Profile/schema ban đầu |
| STEP 02 — Candidate discovery và matching | Đã thêm feed/query, SQL eligibility, safe preview, server Score, signed snapshot/cursor riêng | 45 passed, 0 failed, 0 skipped, gồm Project Discovery/scorer regression |
| STEP 03 — Candidate interactions | Đã thêm GET/PUT/DELETE, exclusive action, atomic UPSERT/reset, privacy và serialization | 18 passed, 0 failed, 0 skipped trong bộ interaction/Discovery HTTP sau sửa fixture pagination |
| STEP 04 — Invitations và membership | Đã thêm Send/list/cancel/accept/decline, expiry/cooldown, atomic membership, Apply exclusion và Position-history guard | 34 passed trong bộ Invitation HTTP/Application HTTP; 23 passed trong bộ Invitation/Application concurrency |

Bộ targeted tổng hợp: **82 passed, 0 failed, 0 skipped**, bao gồm feature và Applications regression, DST constraint, authorization, hơn 1.000 candidates, pagination, lifecycle và races. Sau đó thêm unit test global snapshot budgets; full suite cuối **974 passed, 1 failed, 2 skipped / 977 tests**. Failure duy nhất là CORS baseline, không phải Teammates; chi tiết mục 6.

Không sửa Frontend, Authentication/OAuth/Onboarding, PositionMatchScorer, DiscoveryService/DiscoveryEligibility/DiscoveryFeedSessions hoặc Project feed mixing. Không commit, push, deploy hoặc migrate database ứng dụng.

## 2. Files source mới và sửa

Tất cả paths tương đối từ repository root. `new` là file mới; `modified` là file tracked đã sửa. Hai docs thiết kế đã untracked từ trước task này và không bị chỉnh nội dung.

### API

| File | Trạng thái |
| --- | --- |
| `src/Tindy.Api/Controllers/ProjectCandidatesController.cs` | new |
| `src/Tindy.Api/Controllers/ProjectCandidateInteractionsController.cs` | new |
| `src/Tindy.Api/Controllers/ProjectInvitationsController.cs` | new |
| `src/Tindy.Api/Controllers/MyProjectInvitationsController.cs` | new |
| `src/Tindy.Api/Controllers/RecruitmentQuery.cs` | new |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | modified — DI và Send rate limit |

### Application

| File | Trạng thái |
| --- | --- |
| `src/Tindy.Application/Entities/UserProfile.cs` | modified |
| `src/Tindy.Application/Entities/ProjectInvitation.cs` | new |
| `src/Tindy.Application/Entities/TeammateDiscoveryInteraction.cs` | new |
| `src/Tindy.Application/DTOs/Profile/MyProfileResponse.cs` | modified |
| `src/Tindy.Application/DTOs/Profile/UpdateProfileRequest.cs` | modified |
| `src/Tindy.Application/DTOs/Projects/TeammateDiscoveryDtos.cs` | new |
| `src/Tindy.Application/DTOs/Projects/TeammateInteractionDtos.cs` | new |
| `src/Tindy.Application/DTOs/Projects/ProjectInvitationDtos.cs` | new |
| `src/Tindy.Application/DTOs/Projects/RecruitmentRequestParser.cs` | new |
| `src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryRepository.cs` | new |
| `src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryInteractionRepository.cs` | new |
| `src/Tindy.Application/Interfaces/Repositories/IProjectInvitationRepository.cs` | new |
| `src/Tindy.Application/Services/ProfileService.cs` | modified |
| `src/Tindy.Application/Services/TeammateDiscoveryService.cs` | new |
| `src/Tindy.Application/Services/TeammateDiscoveryFeedSessions.cs` | new |
| `src/Tindy.Application/Services/TeammateDiscoveryInteractionService.cs` | new |
| `src/Tindy.Application/Services/ProjectInvitationService.cs` | new |

### Infrastructure và migrations

| File | Trạng thái |
| --- | --- |
| `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | modified |
| `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | modified |
| `src/Tindy.Infrastructure/Persistence/Configurations/UserProfileConfiguration.cs` | modified |
| `src/Tindy.Infrastructure/Persistence/Configurations/ProjectInvitationConfiguration.cs` | new |
| `src/Tindy.Infrastructure/Persistence/Configurations/TeammateDiscoveryInteractionConfiguration.cs` | new |
| `src/Tindy.Infrastructure/Persistence/Migrations/20261103042450_Profile_AddTeamInvitationOptIn.cs` | new, EF generated |
| `src/Tindy.Infrastructure/Persistence/Migrations/20261103042450_Profile_AddTeamInvitationOptIn.Designer.cs` | new, EF generated |
| `src/Tindy.Infrastructure/Persistence/Migrations/20261103042556_Discovery_CreateTeammateRecruitmentTables.cs` | new, EF generated |
| `src/Tindy.Infrastructure/Persistence/Migrations/20261103042556_Discovery_CreateTeammateRecruitmentTables.Designer.cs` | new, EF generated |
| `src/Tindy.Infrastructure/Persistence/Migrations/TindyDbContextModelSnapshot.cs` | modified, EF generated |
| `src/Tindy.Infrastructure/Repositories/ProfileRepository.cs` | modified — owner projection only |
| `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryEligibility.cs` | new |
| `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryRepository.cs` | new |
| `src/Tindy.Infrastructure/Repositories/TeammateDiscoveryInteractionRepository.cs` | new |
| `src/Tindy.Infrastructure/Repositories/ProjectInvitationRepository.cs` | new |
| `src/Tindy.Infrastructure/Repositories/ProjectInvitationPendingState.cs` | new |
| `src/Tindy.Infrastructure/Repositories/ProjectApplicationRepository.cs` | modified — CreateAsync only |
| `src/Tindy.Infrastructure/Repositories/ProjectRepository.cs` | modified — Position deletion guard only |
| `src/Tindy.Infrastructure/Repositories/ProjectWriteLock.cs` | modified — protocol comment only |

### Tests

| File | Trạng thái |
| --- | --- |
| `tests/Tindy.Tests/Profile/ProfilePartialUpdateTests.cs` | modified |
| `tests/Tindy.Tests/Profile/MyProfileReadTests.cs` | modified |
| `tests/Tindy.Tests/Profile/ProfileSecurityRegressionTests.cs` | modified |
| `tests/Tindy.Tests/Integration/ProfileHttpRegressionTests.cs` | modified |
| `tests/Tindy.Tests/Integration/CatalogDatabaseIntegrityTests.cs` | modified — exact schema set và cột opt-in |
| `tests/Tindy.Tests/Projects/TeammateTestData.cs` | new |
| `tests/Tindy.Tests/Projects/TeammateDiscoverySchemaTests.cs` | new |
| `tests/Tindy.Tests/Projects/TeammateDiscoveryHttpTests.cs` | new |
| `tests/Tindy.Tests/Projects/TeammateDiscoveryFeedTests.cs` | new |
| `tests/Tindy.Tests/Projects/TeammateInteractionHttpTests.cs` | new |
| `tests/Tindy.Tests/Projects/TeammateInteractionConcurrencyTests.cs` | new |
| `tests/Tindy.Tests/Projects/ProjectInvitationHttpTests.cs` | new |
| `tests/Tindy.Tests/Projects/ProjectInvitationConcurrencyTests.cs` | new |

Tổng source/tests/migration artifacts trong task: 55 files (18 modified, 37 new), ngoài báo cáo này và test-result artifacts. Không thay package/project references và không thêm dependency.

## 3. Database thực tế

EF CLI đã sinh đúng hai feature migrations theo thứ tự:

1. `20261103042450_Profile_AddTeamInvitationOptIn`: Boolean `user_profiles.open_to_team_invitations NOT NULL DEFAULT false`; dữ liệu cũ giữ opt-out.
2. `20261103042556_Discovery_CreateTeammateRecruitmentTables`: thêm `project_invitations`, `teammate_discovery_interactions`; không thêm cột vào Project/Position/Application.

Invitation dùng UUID PK, composite Position FK `(position_id,project_id)`, Project/recipient/sender FKs Restrict. Có bốn named CHECK constraints từ design, gồm expiry `created_at + interval '168 hours'`, trạng thái, actor khác nhau và tổ hợp response/timestamps hợp lệ. Có đủ named pending/history/cooldown/Position/sender indexes; pending unique `(project_id,invited_user_id) WHERE status='pending'` không phụ thuộc Position, không dùng current time trong predicate.

Interaction dùng composite PK `(project_id,position_id,candidate_user_id)`, CHECK action/timestamps, cascade FKs và list/candidate indexes theo design. EF còn tự tạo composite FK index `(position_id,project_id)` để hỗ trợ FK; không tạo thêm principal key vì key đã có. Table Project-direction interactions giữ nguyên.

Đã kiểm tra schema trên PostgreSQL 16 Testcontainers, including Up/Down/Up từ migration Chat cuối cùng, false backfill, model/snapshot consistency, unique/FK/check enforcement, deletion restriction và preference cascade. DST test đặt session `America/New_York`: 168 elapsed hours hợp lệ, 7 calendar days qua DST bị CHECK từ chối.

Không chạy `dotnet ef database update` cho configured application database. Việc apply migrations khi deploy cần thực hiện riêng trên database được phép. Downgrade mất consent/Invitation/interaction history; cần backup trước.

## 4. APIs đã implement

Tất cả routes yêu cầu existing default JWT/account/onboarding policy; Leader routes kiểm tra actual LeaderId, recipient actions mask foreign IDs bằng 404. JSON snake_case, success envelope và ProblemDetails/error_code như contract. Unsupported/empty/repeated query parameters bị từ chối trên routes mới; body Send/interaction strict, không nhận actor/status/score/timestamps.

`P`=Project UUID, `S`=Position UUID, `U`=candidate UUID, `I`=Invitation UUID.

| Method/path | Request chính | Success data |
| --- | --- | --- |
| GET `/api/projects/{P}/positions/{S}/candidates` | limit/cursor/sort_by=match/role_id/skill_id/min_available_hours_per_week | 200 TeammateDiscoveryPage, complete safe preview + PositionMatch |
| GET `/api/projects/{P}/candidate-interactions?position_id={S}` | action/page/page_size optional | 200 items + PaginationMeta |
| PUT `/api/projects/{P}/candidate-interactions/{U}?position_id={S}` | `{"action":"shortlisted"}` | 200 tuple/action/created_at/updated_at |
| DELETE cùng interaction path/query | không body | 200 tuple/action:null, repeat idempotent |
| POST `/api/projects/{P}/invitations` | Position/recipient/message như ví dụ dưới | 201 complete InvitationDto |
| GET `/api/projects/{P}/invitations` | status default all; position_id/invited_user_id/page/page_size | 200 InvitationDto items + pagination |
| POST `/api/projects/{P}/invitations/{I}/cancel` | không body | 200 InvitationDto status=cancelled |
| GET `/api/me/project-invitations` | status default all; project_id/page/page_size | 200 owner-only history/pending |
| POST `/api/me/project-invitations/{I}/accept` | không body | 200 InvitationDto status=accepted, membership committed |
| POST `/api/me/project-invitations/{I}/decline` | không body | 200 InvitationDto status=declined |
| GET/PATCH `/api/me/profile` hiện có | PATCH `{"open_to_team_invitations":true}` | 200 complete owner DTO thêm Boolean; member DTO không thêm field |

Candidate response đầy đủ, các transition response và owner Profile extension có examples trong [contract mục 6](DISCOVERY_TEAMMATES_V1.md). Không thêm GET-by-ID Invitation, match-by-ID, notification delivery hoặc membership endpoint.

Ví dụ Send request:

```json
{
  "position_id":"33333333-3333-4333-8333-333333333333",
  "invited_user_id":"88888888-8888-4888-8888-888888888888",
  "message":"Mời bạn tham gia phần Backend."
}
```

Ví dụ 201 response:

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
    "message":"Mời bạn tham gia phần Backend.","status":"pending",
    "created_at":"2026-11-03T03:00:00Z","updated_at":"2026-11-03T03:00:00Z",
    "expires_at":"2026-11-10T03:00:00Z","responded_at":null
  }
}
```

Ví dụ interaction reset response:

```json
{"success":true,"message":"Candidate interaction reset.","data":{"project_id":"11111111-1111-4111-8111-111111111111","position_id":"33333333-3333-4333-8333-333333333333","candidate_user_id":"88888888-8888-4888-8888-888888888888","action":null}}
```

Ví dụ conflict sau expiration:

```json
{"title":"Conflict","status":409,"detail":"Invitation has expired.","instance":"/api/me/project-invitations/99999999-9999-4999-8999-999999999999/accept","error_code":"INVITATION_EXPIRED","traceId":"server-generated"}
```

## 5. Business logic và matching

- **Scoring:** gọi nguyên `PositionMatchScorer.Score` cho selected Position. Không gọi Best để chọn Position khác; không chỉnh required/preferred-role/availability/optional weights, normalization hay rounding; không AI và không persist all-pairs scores.
- **Eligibility:** SQL lọc active/nondeleted/onboarded/Profile/opt-in, self Leader, active membership, pending Application, effective pending Invitation across Positions, decline cooldown và Position-specific skip. Không có skill threshold hoặc cross-Project membership/leadership quota.
- **Feed:** score tất cả eligible candidates trước pagination; score DESC/UUID ASC. Signed opaque cursor riêng, 15 phút, <=4/caller, <=128 global, <=100.000 retained cards. Score/preview frozen, action/capacity/current eligibility được refresh; stale offsets tiến qua candidate bị ẩn. Oversized snapshot trả 503, không cắt top N.
- **Interaction:** một action loại trừ nhau; UPSERT không đổi timestamps khi cùng action; DELETE repeated thành công. Candidate unavailable trả null preview trong list; GET/reset dùng được cho archived context. Không side effect Invitation/membership.
- **Invitation:** pending→accepted/declined/cancelled/expired; terminal immutable, reinvite new row. TTL/cooldown 168 elapsed hours. Send không reserve slots. Decline mới có cooldown; cancelled/expired không có. GET effective expiration không ghi DB.
- **Normalization:** Send/Apply cùng helper dưới Project lock; update elapsed pending rows trước insert, commit expiry-only ngay cả khi admission conflict. Lifecycle quan sát expired cũng commit expiry trước khi service ném INVITATION_EXPIRED.
- **Accept:** account/Profile/opt-in, current Leader relationship, Project/Position state, capacities, membership và pending Application được recheck. Deadline tuyển dụng không chặn lời mời còn valid. Create/reactivate membership, Position filled_count/state và accepted/responded_at cùng commit; không Application hay approval thứ hai.
- **Compatibility:** chỉ CreateAsync của Application thêm Invitation exclusion/normalization. Withdraw/Reject/approval và immediate reapply giữ nguyên. Position DELETE nhận cả Invitation history và trả POSITION_IN_USE; Project archive không xóa/cancel history.

## 6. Kiểm thử và bằng chứng

Build toàn solution đã chạy: `dotnet build Tindy.slnx` — **succeeded, 0 warnings, 0 errors**. Sandbox ban đầu chặn NuGet network/signature access; sau khi chạy qua escalation, restore và build thành công. Không tắt signature verification hoặc thay package versions để né lỗi.

Targeted cuối:

```text
dotnet test tests/Tindy.Tests/Tindy.Tests.csproj --no-restore
  --filter FullyQualifiedName~Teammate|FullyQualifiedName~ProjectInvitation|FullyQualifiedName~ProjectApplication
  --logger console;verbosity=minimal
  --logger trx;LogFileName=teammates-targeted.trx
  --results-directory .tmp-teammates-results
Result: Passed 82, Failed 0, Skipped 0.
```

Các dòng command ở trên dùng mô tả tham số; khi copy PowerShell cần quote filter/logger có ký tự `|`/`;` như trong actual tool invocation. TRX: `.tmp-teammates-results/teammates-targeted.trx`.

Full command thực tế:

```powershell
dotnet test Tindy.slnx --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=teammates-full-final.trx' --results-directory .tmp-teammates-results
```

Full suite lần đầu: **972 passed, 2 failed, 2 skipped / 976 tests**. Hai failures:

1. OAuth CORS preflight mong không có Access-Control-Allow-Credentials, trong khi baseline có AllowCredentials(). Đã chạy lại trên clean HEAD archive và tái hiện đúng failure: 1 passed, 1 failed trong hai CORS cases. Giữ nguyên Auth/OAuth và assertion, không chỉnh để làm xanh suite.
2. CatalogDatabaseIntegrityTests còn expected 27 bảng, thiếu 3 bảng Chat có sẵn và 2 bảng Teammates mới. Đã tái hiện baseline thiếu 3 bảng Chat trên clean HEAD. Cập nhật expected set chính xác thành 32 bảng, giữ SetEquals và thêm assertion cột opt-in; CatalogDatabaseIntegrityTests sau sửa **4/4 passed**.

Đã bổ sung unit test global snapshot budgets. **Full suite cuối: 974 passed, 1 failed, 2 skipped / 977 tests, khoảng 7 phút**, TRX `.tmp-teammates-results/teammates-full-final.trx`. Failure duy nhất còn lại là `OAuthHttpContractTests.CorsPreflight_OnlyAllowsConfiguredOrigins_WithAuthorizationHeader(origin: "http://localhost:8443", allowed: true)`, tại assertion không có Access-Control-Allow-Credentials. Hai tests skip thuộc ClamAvLive opt-in; không được tính như đã kiểm thử scanner thật. Không tuyên bố full suite green.

Baseline verification đã chạy trên bản sao clean `git archive HEAD`, không có code Teammates:

- CORS filter: 1 passed, 1 failed, cùng failure ở assertion như working tree.
- Schema integrity filter: 1 failed, expected 27 nhưng thực tế baseline có 30 bảng (thiếu 3 tên Chat trong test). Sau feature schema là 32 bảng; test được cập nhật và đã pass, không nới SetEquals.

Build source hiện tại thành công; `.tmp-teammates-results` giữ cả targeted TRX, first-full TRX và final-full TRX. Thay đổi schema test và global budget test đã được compile/chạy trong full suite cuối.

Các integration tests dùng PostgreSQL 16 thật, `TindyApiFactory` và TestPostgres, mỗi fixture/database test dùng môi trường disposable. Không dùng in-memory provider làm bằng chứng concurrency. Test sources/fixtures đã mở rộng có assertions cho constraints, pagination, privacy, ownership, expiry/cooldown boundaries, post-deadline acceptance, reactivation, error outcomes và rollback. Một test fixture ban đầu tìm candidate chỉ ở page đầu đã được sửa thành duyệt đầy đủ cursor; không bỏ assertion candidate phải xuất hiện.

## 7. Concurrency đã kiểm tra

ProjectWriteLock trước mọi mutation cùng Project. Capacity/lifecycle/interaction order: Project → Position → Invitation nếu cần → User. Apply chỉ cần Project → pending Invitation; không lấy Position lock sau Invitation. User dùng `FOR NO KEY UPDATE`: serialize với Profile/account write lock, đồng thời tương thích implicit FK KEY SHARE khi hai Leader mời qua lại, tránh cycle không cần thiết ở User FKs. Position/Invitation acceptance được reload sau lock.

Đã chạy các races sau bằng HTTP request scopes/connections độc lập:

- Send vs Apply khác Positions: đúng một pending loại được commit.
- Send vs Send khác Positions, cùng lúc normalize expired row: một 201, một 409, history cũ expired vẫn còn.
- Hai Accept tranh slot Position cuối: một 200, một 409; membership/count chỉ tăng một; Position chuyển filled.
- Hai Accept ở hai Positions tranh Project slot cuối: một 200, một 409; active count gồm Leader không vượt cap.
- Invitation Accept vs Application approval: chung Project lock, chỉ một membership mới khi slot cuối.
- Accept vs Decline/Cancel: một terminal transition thắng, không increment nếu action khác thắng.
- Archive/Position close/capacity shrink vs Accept: state/count nhất quán; archive/close sau một Accept đã commit có thể hợp lệ cùng thành công, không giả định chỉ một HTTP 200 cho trường hợp đó.
- Profile opt-out vs Send/Accept: opt-out thắng trước chặn admission; Accept commit trước vẫn giữ membership sau opt-out.
- Reciprocal Leaders Send: cả hai có thể mời nhau ở hai Projects, không bị FK deadlock.
- Inject PostgreSQL trigger failure khi update Invitation: accepted/membership/Position changes rollback toàn bộ.
- Concurrent interaction PUT/DELETE: không duplicate tuple hoặc unexpected 500; trạng thái cuối là kết quả commit thực.

## 8. Giới hạn và assumptions còn lại

Không có blocker feature đã biết: toàn bộ tests Teammates/Application và schema integrity pass trong full suite cuối. Full suite còn CORS failure baseline đã tái hiện độc lập; giữ nguyên vì Auth/OAuth phải được bảo toàn và không được nới assertion để làm xanh tests. Cần review CORS policy/test contract riêng trước khi coi toàn solution đạt release gate. Các test ClamAvLive có opt-in riêng, không được coi là đã chạy khi framework báo skip. Không deploy hoặc xác nhận production database đã sẵn sàng.

Cross-table pending exclusion áp dụng cho cooperating writers cùng protocol, không cho arbitrary SQL/old binary. Khi deploy cần drain writers cũ và phát hành Apply-aware writer cùng Send; apply schema trước khi bật feature. FK/partial unique indexes bảo vệ từng bảng nhưng không thay cross-table lock protocol.

Process-local snapshots cần affinity khi nhiều API instances; restart/eviction trả cursor-expired và yêu cầu refresh. Không thêm distributed cache/worker. Existing direct Position skill insert/delete chưa đồng bộ toàn bộ bằng Project lock; requirement edits chỉ ảnh hưởng advisory score, không là skill gate của Accept. Không đổi các path đó trong task.

Thông tin university/full profile, per-card can_invite/pending status, shortlist match-by-ID và membership PositionId không được tự thêm ngoài contract. Source/DTO hiện có vẫn giữ các giới hạn này. Không có notification delivery promise hoặc idempotency-key; retry Send sau success trả pending conflict, client cần đọc history để reconcile ambiguous response.

## 9. Frontend integration readiness

Các API trong mục 4 đã có implementation, DI, auth/validation và targeted HTTP/PostgreSQL evidence; Frontend có thể tích hợp sau khi migrations/backend version tương ứng được deploy vào môi trường được phép. Existing Project/Profile/Catalog/member-detail consumers giữ contract, owner Profile thêm Boolean.

FE cần chọn Project/Position UUID explicit, dùng backend matching nguyên vẹn, required position_id cho interactions, confirmed mutation feedback và list reconciliation khi outcome chưa rõ. Pending invitation/cooldown theo Project/recipient across Positions; không reserve capacity hoặc tạo membership local. Accept sau deadline vẫn có thể hợp lệ; không tái dùng new-Application deadline blocker.

## 10. Git status và bàn giao

Không commit/stage/push/deploy. Inventory mục 2 liệt kê toàn bộ source files new/modified. Hai docs STEP 00 tồn tại untracked từ trước và được giữ nguyên. Báo cáo này là doc mới; TRX là artifact kiểm thử local. Bản sao clean HEAD dùng xác minh baseline nằm ngoài repository Backend, trong workspace `.tmp-teammates-baseline-150b233af9b54e97af1722ffee698b5a`; test factories override DB/auth và dùng PostgreSQL disposable, không migrate database ứng dụng.

Git status (working tree hiện tại, giữ nguyên uncommitted changes):

```text
 M src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs
 M src/Tindy.Application/DTOs/Profile/MyProfileResponse.cs
 M src/Tindy.Application/DTOs/Profile/UpdateProfileRequest.cs
 M src/Tindy.Application/Entities/UserProfile.cs
 M src/Tindy.Application/Services/ProfileService.cs
 M src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs
 M src/Tindy.Infrastructure/Persistence/Configurations/UserProfileConfiguration.cs
 M src/Tindy.Infrastructure/Persistence/Migrations/TindyDbContextModelSnapshot.cs
 M src/Tindy.Infrastructure/Persistence/TindyDbContext.cs
 M src/Tindy.Infrastructure/Repositories/ProfileRepository.cs
 M src/Tindy.Infrastructure/Repositories/ProjectApplicationRepository.cs
 M src/Tindy.Infrastructure/Repositories/ProjectRepository.cs
 M src/Tindy.Infrastructure/Repositories/ProjectWriteLock.cs
 M tests/Tindy.Tests/Integration/CatalogDatabaseIntegrityTests.cs
 M tests/Tindy.Tests/Integration/ProfileHttpRegressionTests.cs
 M tests/Tindy.Tests/Profile/MyProfileReadTests.cs
 M tests/Tindy.Tests/Profile/ProfilePartialUpdateTests.cs
 M tests/Tindy.Tests/Profile/ProfileSecurityRegressionTests.cs
?? .tmp-teammates-results/
?? docs/api/DISCOVERY_TEAMMATES_V1.md
?? docs/api/DISCOVERY_TEAMMATES_V1_BACKEND_PROGRESS.md
?? docs/api/DISCOVERY_TEAMMATES_V1_IMPLEMENTATION_PLAN.md
?? src/Tindy.Api/Controllers/MyProjectInvitationsController.cs
?? src/Tindy.Api/Controllers/ProjectCandidateInteractionsController.cs
?? src/Tindy.Api/Controllers/ProjectCandidatesController.cs
?? src/Tindy.Api/Controllers/ProjectInvitationsController.cs
?? src/Tindy.Api/Controllers/RecruitmentQuery.cs
?? src/Tindy.Application/DTOs/Projects/ProjectInvitationDtos.cs
?? src/Tindy.Application/DTOs/Projects/RecruitmentRequestParser.cs
?? src/Tindy.Application/DTOs/Projects/TeammateDiscoveryDtos.cs
?? src/Tindy.Application/DTOs/Projects/TeammateInteractionDtos.cs
?? src/Tindy.Application/Entities/ProjectInvitation.cs
?? src/Tindy.Application/Entities/TeammateDiscoveryInteraction.cs
?? src/Tindy.Application/Interfaces/Repositories/IProjectInvitationRepository.cs
?? src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryInteractionRepository.cs
?? src/Tindy.Application/Interfaces/Repositories/ITeammateDiscoveryRepository.cs
?? src/Tindy.Application/Services/ProjectInvitationService.cs
?? src/Tindy.Application/Services/TeammateDiscoveryFeedSessions.cs
?? src/Tindy.Application/Services/TeammateDiscoveryInteractionService.cs
?? src/Tindy.Application/Services/TeammateDiscoveryService.cs
?? src/Tindy.Infrastructure/Persistence/Configurations/ProjectInvitationConfiguration.cs
?? src/Tindy.Infrastructure/Persistence/Configurations/TeammateDiscoveryInteractionConfiguration.cs
?? src/Tindy.Infrastructure/Persistence/Migrations/20261103042450_Profile_AddTeamInvitationOptIn.Designer.cs
?? src/Tindy.Infrastructure/Persistence/Migrations/20261103042450_Profile_AddTeamInvitationOptIn.cs
?? src/Tindy.Infrastructure/Persistence/Migrations/20261103042556_Discovery_CreateTeammateRecruitmentTables.Designer.cs
?? src/Tindy.Infrastructure/Persistence/Migrations/20261103042556_Discovery_CreateTeammateRecruitmentTables.cs
?? src/Tindy.Infrastructure/Repositories/ProjectInvitationPendingState.cs
?? src/Tindy.Infrastructure/Repositories/ProjectInvitationRepository.cs
?? src/Tindy.Infrastructure/Repositories/TeammateDiscoveryEligibility.cs
?? src/Tindy.Infrastructure/Repositories/TeammateDiscoveryInteractionRepository.cs
?? src/Tindy.Infrastructure/Repositories/TeammateDiscoveryRepository.cs
?? tests/Tindy.Tests/Projects/ProjectInvitationConcurrencyTests.cs
?? tests/Tindy.Tests/Projects/ProjectInvitationHttpTests.cs
?? tests/Tindy.Tests/Projects/TeammateDiscoveryFeedTests.cs
?? tests/Tindy.Tests/Projects/TeammateDiscoveryHttpTests.cs
?? tests/Tindy.Tests/Projects/TeammateDiscoverySchemaTests.cs
?? tests/Tindy.Tests/Projects/TeammateInteractionConcurrencyTests.cs
?? tests/Tindy.Tests/Projects/TeammateInteractionHttpTests.cs
?? tests/Tindy.Tests/Projects/TeammateTestData.cs
```
