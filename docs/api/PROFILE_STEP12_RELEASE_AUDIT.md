# STEP 12 — Release Audit & Handoff Checklist (Backend V1)

> **Nội dung này là audit dựa trên ZIP**, không phải lệnh triển khai hay xác nhận môi trường của người dùng. 
> Ngày đối chiếu: 20/10/2026; source ZIP `tindy-backend-platform(20261020-131618).zip`.

## 1. Kết luận thực tế

- **13/13 HTTP routes** được tìm thấy trong Controller source: MyProfileController, MemberProfileController, MyExperiencesController, MemberExperiencesController, MyCertificatesController, MemberCertificatesController.
- Có đủ Entity/EF Config/Repository/Service tương ứng; `PROFILE_API.md` lấy request fields từ parser/code hiện hữu.
- **Chưa thể đóng dấu Backend V1 tested end-to-end**: chưa có kết quả Unit/HTTP Regression trên HEAD STEP 11 mới nhất được chứng minh trong ZIP; không có bằng chứng migrations đã được apply trên PostgreSQL target.
- Những test chạy thất bại với DockerUnavailableException trong TRX cũ là giới hạn môi trường, **không** có nghĩa mọi endpoint đang lỗi; cũng **không** có nghĩa E2E đã PASS.

## 2. Git metadata từ source ZIP

- Branch trong `.git/HEAD`: `feature/profile`.
- HEAD: `eaac2ba` — `test(profile): cover API security and regression cases`.
- Lịch sử 12 commits gần nhất:

```text
eaac2ba 2026-10-20 test(profile): cover API security and regression cases
27ec716 2026-10-20 feat(auth): map oauth avatar to user profile
a2ebb38 2026-10-20 feat(profile): implement certificate CRUD endpoints
8ec291f 2026-10-20 feat(profile): expose experience CRUD endpoints
c31dc23 2026-10-20 feat(profile): implement experience persistence and services
53b1f9d 2026-10-20 feat(profile): expose member profile read API
0968a3c 2026-10-20 feat(profile): support partial profile updates
e89e449 2026-10-20 feat(profile): add my profile read endpoint
331d1db 2026-10-20 fix(onboarding): prevent edits after completion
9dcf657 2026-10-20 fix(deps): update vulnerable cryptography packages
d2e78e4 2026-10-20 docs(profile): finalize backend API contracts
f7e8da5 2026-10-16 Merge pull request #8 from FCAJ-OJT-Fall26/feature/authentication-Oauth
```

**Git status caution:** Khi unzip `.git` trên Linux, `git status --porcelain` hiển thị **237 path modified** dù `git diff --ignore-space-at-eol` không có nội dung thực chất; khả năng do khác biệt CRLF/LF trong ZIP. **Không coi đây là bằng chứng local working tree trên Windows đang dirty**. Chạy `git status`, `git diff --check`, `git diff --cached --check`, `git log -1 --oneline` trực tiếp trên repo của bạn để xác nhận. STEP 12 không nên commit `.patch`, ZIP hoặc `.env` vào repo.

## 3. EF Core migrations hiện có trong ZIP

```text
20261009071303_InitialAuthenticationSchema.cs
20261012095207_UpdateUserRegistrationSchema.cs
20261012120127_AddOnboardingStateAndAvailability.cs
20261013053123_Profile_AddOnboardingDataAndSkills.cs
20261015010030_Catalog_CreateProfessionalRolesAndSkillCategories.cs
20261015014841_Profile_ConvertPreferredRoleToReference.cs
20261015140901_Auth_AddPrimaryEmailMetadata.cs
20261016022859_Auth_CreateOAuthLoginTickets.cs
20261016030956_Auth_CreateOAuthLinkTransactions.cs
20261020110755_Profile_AddExperiencesAndCertificatesAndRemoveVisibility.cs
20261020130314_Auth_AddOAuthLinkAvatarMetadata.cs
```

**Hai migration quan trọng**:

- `20261020110755_Profile_AddExperiencesAndCertificatesAndRemoveVisibility`: thêm `profile_experiences`, `profile_experience_skills`, `user_certificates`, xóa cột `user_profiles.is_public`; có FK + index + check constraints.
- `20261020130314_Auth_AddOAuthLinkAvatarMetadata`: thêm cột nullable `avatar_url` trong `oauth_link_transactions`.

Đã kiểm tra file `Up()/Down()` từ source; **chưa có bằng chứng chúng đang được apply vào PostgreSQL**. Không chạy `dotnet ef database drop` chỉ để xác nhận. Trước `database update` nên backup dữ liệu quan trọng và review SQL/Down trên DB dev riêng.

## 4. Test evidence có sẵn trong ZIP

| TRX | Total | Pass | Fail | Giải thích |
|---|---:|---:|---:|---|
| `step07-08-docker-free.trx` | 100 | 100 | 0 | Docker-free filtered test set (ở giai đoạn STEP 07–08) |
| `step07-08-full.trx` | 150 | 100 | 50 | Full suite trước STEP 09–11; nhiều lỗi DockerUnavailableException |


Kết quả không bao phủ các test mới ở STEP 09–11. Đây là **lịch sử test**, không phải test vừa chạy trong STEP 12.

## 5. Kiểm tra trên Windows — không Docker trước

```powershell
# Root của tindy-backend-platform
git branch --show-current
git status
git log -1 --oneline

dotnet restore .\Tindy.slnx
dotnet build .\Tindy.slnx

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName!~Tindy.Tests.Integration" `
    --logger "trx;LogFileName=profile-step12-unit.trx"

dotnet list .\Tindy.slnx package --vulnerable --include-transitive
```

Chỉ khi Unit Tests PASS và audit package không còn lỗ hổng cần xử lý, mới review commit docs. Không nên báo rằng Backend đã được HTTP E2E verify.

## 6. Database / HTTP / OAuth checks — phải có môi trường phù hợp

**Local PostgreSQL (không cần Docker nếu có PostgreSQL cài trực tiếp):**

```powershell
dotnet ef migrations list `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

- Kiểm tra migration 20261020110755 và 20261020130314 đã apply. Test trên DB dev an toàn; không đụng production hoặc drop database.
- Sau đó chạy app với User Secrets hợp lệ và thử Swagger UI (`/swagger`), OpenAPI (`/openapi/tindy.json`) trong Development.
- Postman smoke tests: JWT owner 200 GET/PATCH; experience/cert create 201, PUT 200, DELETE 204; member GET 200; anonymous 401; caller incomplete 403; target inactive/missing 404; cross-user write 404; invalid fields 400; wrong Catalog 422; Onboarding SaveStep sau complete 409.
- Test OAuth Google/GitHub thực qua provider và ticket exchange/linking nếu cấu hình credentials; test không được làm lộ secrets. Chỉ claim PASS khi có bằng chứng.
- Docker/Testcontainers integration (nếu sau này sử dụng) chạy riêng trên test DB; không đặt tên `tindy` production, không thay đổi connect string sang DB đang sử dụng.

## 7. Các file FE cần bàn giao

- `docs/api/PROFILE_API.md` — contract nội dung; cập nhật contract STEP 01 cũ theo source thực tế.
- `docs/api/PROFILE_OPENAPI.yaml` — đặc tả OpenAPI *curated reference*, không thay thế OpenAPI runtime.
- `docs/guide/PROFILE_FE_HANDOFF.md` — màn hình FE cần sửa, mappings, acceptance.
- `handoff/CODEX_FE_PROFILE_PROMPT.md` — prompt để Codex tích hợp trên FE branch mới nhất.

## 8. An toàn, bảo mật và cấu hình

- Không xuất JWT/token/Provider Secret/DB Connection string thật vào docs hoặc prompt.
- Không expose phone/resume/login email, password hash, OAuth links trong MemberProfile DTO.
- OAuth Avatar có thể null; không có endpoint upload file, certificate verify; cover CSS default.
- Không thay Account GET `/api/me`, Onboarding 8-step policy hoặc JWT flow trong handoff.
- Migration cần khớp Model Snapshot, hạn chế drift. Unit tests EF Metadata không thay thế integration tests.
- Environment Dev cần `ConnectionStrings:DefaultConnection`, JWT options, CORS origins, OAuth secrets nếu OAuth enabled; kiểm tra file `appsettings*.json`/User Secrets, không copy secrets vào artifacts.

## 9. Definition of Done cho STEP 12

- [x] Đối chiếu controllers/DTO/validation theo ZIP HEAD và ghi đủ 13 endpoints.
- [x] Tạo `PROFILE_API.md`, OpenAPI tham chiếu, FE handoff, Codex FE prompt.
- [x] Trích xuất Git HEAD / migrations / historical TRX từ ZIP.
- [ ] Trên Windows: build/tests/audit package mới nhất PASS (chưa nhận output).
- [ ] PostgreSQL: migrations applied & HTTP/authorization smoke tests có kết quả (chưa có evidence).
- [ ] Chạy OAuth Provider flow/test theo cấu hình thực (chưa có evidence).
- [ ] Xác nhận `git status` sạch sau commit docs (chưa có evidence).

**Commit chỉ tài liệu đề xuất:** `docs(profile): document APIs and frontend integration`.
