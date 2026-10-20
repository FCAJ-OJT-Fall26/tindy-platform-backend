# STEP 01 — Finalize Profile Backend Contracts

> Thuộc `TINDY_PROFILE_BACKEND_IMPLEMENTATION_PLAN(2).md`; chỉ làm STEP 01, không làm STEP 02.  
> Source kiểm tra: `tindy-backend-platform(20261020-101054).zip`.  
> **Deliverable chính:** `docs/api/PROFILE_API_CONTRACT.md` (soạn sẵn bên cạnh tài liệu này).

## I. Kết luận nhanh sau khi review source

1. **API prefix** đang là `/api` (`MeController`, `CatalogController`, `AuthController`), không phải `/api/v1`.
2. **JWT & Authorization:** `MeController` dùng `AuthenticatedOnly` cho pre-onboarding, khác default auth policy JWT + completed onboarding tại `ServiceCollectionExtensions.cs`. Profile mới phải chọn default policy; `user_id` từ claim `sub`.
3. **Existing profile schema:** `UserProfile` đã có full name, university, preferred role, interests/goals, bio, availability, skills, social links, avatar/cover, phone, resume. `IsPublic` vẫn tồn tại; chỉ xóa ở STEP 02.
4. **Existing validation:** Onboarding Step 2: 1–30 skills; Step 3: UUID role; Step 5: 1–40; Complete: Bio <= 2000; Step 7: URLs <=1000, http/https. DB giới hạn DisplayName 100, University 255, Phone 20, Headline/Location/CurrentRole 255, URL 1000.
5. **Existing PATCH-related pattern:** `OnboardingCompleteRequest.BioWasProvided` phân biệt field thiếu với null. Có thể tái sử dụng ý tưởng nhưng cần kỹ thuật áp dụng nhiều field và HTTP tests.
6. **Success/Error:** success envelope `{success,message,data}`; model validation trả 400 `VALIDATION_FAILED` với ValidationProblemDetails; exception handler: 404 `NOT_FOUND`, 409 `CONFLICT`, 422 `BUSINESS_RULE_VIOLATION`; JWT 401 `AUTH_UNAUTHORIZED`; onboarding gate 403 `ONBOARDING_REQUIRED`.
7. **Catalog:** `SkillResponse` = (`skill_id`, `name`); `ProfessionalRoleResponse` dùng `professional_role_id`. Các ID dạng `skill_dotnet` của FE mock không hợp lệ.
8. **Experiences / Certificates:** source snapshot chưa có bảng, entities hoặc controllers mới. Không có storage/avatar upload implementation.
9. **GET /api/me** đã tồn tại cho account data: không thay thế hoặc đổi shape khi thêm `GET /api/me/profile`.
10. **Lưu ý branch:** Trong ZIP, `.git/HEAD` trỏ `main`, dù có ref `feature/profile`. Khi triển khai trên máy thật, phải kiểm tra `git branch --show-current` và `git status` trước khi tạo commit.

## II. Mục tiêu của STEP 01

- Khóa contract V1 của tất cả 13 routes Profile/Experiences/Certificates.
- Khóa tên request/response DTO và `snake_case` fields dựa theo backend.
- Khóa validation/nullable/partial update/date formats/authorization/ownership.
- Tạo một tài liệu Markdown để làm nguồn chuẩn cho STEP 02–12, không phụ thuộc frontend mock.
- Không cần chạy migration, PostgreSQL, hay thay đổi source code trong bước này.

## III. Files cần đọc (đã đối chiếu)

| File | Lý do |
|---|---|
| `src/Tindy.Application/Entities/UserProfile.cs` | Profile fields, `IsPublic`, nullable |
| `src/Tindy.Application/Entities/User.cs` | Username, primary email, status, completed |
| `src/Tindy.Application/Entities/Skill.cs` / `UserSkill.cs` / `ProfessionalRole.cs` | Catalog & relationships |
| `src/Tindy.Infrastructure/Persistence/Configurations/UserProfileConfiguration.cs` | DB constraints / max lengths |
| `src/Tindy.Application/DTOs/Onboarding/OnboardingRequests.cs` | Validation hiện tại và BioWasProvided |
| `src/Tindy.Application/Services/OnboardingService.cs` | Mapping, active user, SaveStep semantics |
| `src/Tindy.Api/Controllers/MeController.cs` | Current /me and onboarding routes; special policy |
| `src/Tindy.Api/Controllers/CatalogController.cs` | Catalog route, UUID fields |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | snake_case, validation and auth policies |
| `src/Tindy.Api/Authorization/*` | Completed onboarding and active-user gates |
| `src/Tindy.Api/ExceptionHandlers/GlobalExceptionHandler.cs` | 404/409/422/500 and code |
| `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | Current entity graph |
| `src/Tindy.Application/Services/AuthService.cs`, `ExternalAuthService.cs` | `IsPublic` registration sites (STEP 02) |
| `src/Tindy.Application/Models/Authentication/AuthenticatedExternalIdentity.cs` | Chưa có avatar URL (STEP 10) |
| `tests/Tindy.Tests/Validation/RequestValidationTests.cs` | Existing validation style, Bio presence test |

## IV. Files được phép tạo/chỉnh trong STEP 01

- **Tạo mới:** `docs/api/PROFILE_API_CONTRACT.md` (dùng file đã soạn sẵn ở output).
- **Tùy chọn:** ghi ngắn quyết định/changelog vào `docs/guide/PROFILE_STEP01_DETAILED.md` nếu nhóm muốn đưa vào repo.
- **Không sửa:** `.cs`, `.csproj`, `TindyDbContext`, migrations, Snapshot, seed, JWT/OAuth controllers, Frontend. Không chạy database drop/update.

## V. Các quyết định kỹ thuật đưa vào contract

| Nội dung | Kết quả |
|---|---|
| Profile GET | MyProfileResponse, không nhúng Experiences/Certificates; có counts |
| Member Profile GET | MemberProfileResponse đã lọc private fields, thành viên active + completed |
| Profile PATCH | JSON partial object, omit giữ nguyên, null xóa optional, unknown fields 400 |
| Profile Skills | 1–30, `skill_ids` được gửi là replace full list |
| Experience POST/PUT | `skill_ids` UUID từ Catalog, response `technologies` objects |
| Experience time | `YYYY-MM`, lưu DB ngày 1 mỗi tháng |
| Certificate time | `YYYY-MM-DD`, optional |
| Collection GET | array `[]`, chưa pagination |
| DELETE | 204 không body |
| Account fields | Không thuộc Profile PATCH hoặc Member Profile |
| `is_public` | Không có trong contract, xóa thực tế STEP 02 |
| Avatar/Cover | Read-only trong profile; OAuth avatar STEP 10, cover CSS FE |
| Upload/Verification | Ngoài MVP |
| Business errors | Reuse ProblemDetails/error codes hiện có |

Chi tiết, ví dụ JSON, field matrix, null semantics, validation, quyền truy cập nằm trong `PROFILE_API_CONTRACT.md`.

## VI. Các test cases cần soạn trước khi code (đưa cho STEP sau)

- `PATCH` omit vs null vs empty: giữ, xóa, lỗi required; whitelist; forbidden fields; empty body.
- Role/skills exist & active; inactive previously selected allowed to remain; duplicate GUID/invalid Guid.
- Experience `2025-09`, invalid month, ongoing conflicting end, end<start, `skill_ids` 0–20.
- Certificate `2026-02-30`, end<issued, URL invalid, credential link null allowed.
- JWT 401; incomplete onboarding 403; inactive caller denied; target missing/deleted/incomplete 404; ownership.
- Không expose primary email, phone, resume, OAuth, status trong Member Profile JSON.
- Không làm hỏng GET `/api/me`, Local/OAuth, Catalog, 8-step onboarding.
- Validate 204 no content và success envelope 200/201; snake_case mapping.

## VII. Checklist hoàn thành STEP 01

- [ ] Đọc `PROFILE_API_CONTRACT.md` và kiểm tra tất cả 13 routes.
- [ ] Chấp nhận/chỉnh các **[STEP01 default]** (giới hạn text, Skills/Experience, sort, empty PATCH, DELETE=204, no pagination).
- [ ] Xác minh member DTO không lộ Account data.
- [ ] Chốt cách nhận field-presence với `null` khi triển khai (không cần viết implementation ở STEP 01).
- [ ] Đảm bảo contract không đòi sửa Auth/OAuth/Onboarding ngay trong STEP 01.
- [ ] Đưa `docs/api/PROFILE_API_CONTRACT.md` vào repo đúng nhánh Profile.
- [ ] `git diff --check`; kiểm tra không commit secrets hoặc file ZIP.
- [ ] Chỉ commit tài liệu STEP 01, chưa có migrations.

## VIII. Git (PowerShell từ root `tindy-backend-platform`)

```powershell
git branch --show-current
git status

# Copy tài liệu vào docs/api/PROFILE_API_CONTRACT.md
# (có thể tạo folder docs/api nếu chưa tồn tại)

git add docs/api/PROFILE_API_CONTRACT.md
git diff --cached --check
git diff --cached --stat
git commit -m "docs(profile): finalize backend API contracts"
git status
```

**Không chạy `dotnet ef migrations add` / `database update` / `database drop` ở STEP 01.** Tài liệu-only commit không yêu cầu chạy `dotnet build/test`, nhưng nên giữ build/test cho các STEP có code và migration. Không báo build/test pass nếu chưa chạy.

## IX. Prompt cho Codex — nếu muốn Codex đưa tài liệu vào repo

```text
Bạn đang ở repo Tindy Backend .NET 10, Clean Architecture.
Chỉ làm STEP 01 — Finalize Profile Backend Contracts, KHÔNG thực hiện STEP 02.

1) Xem branch, git status và đọc source mới nhất, đặc biệt:
   UserProfile/User/Skill/ProfessionalRole Entities, EF Configurations,
   OnboardingRequests/OnboardingService, MeController, CatalogController,
   ServiceCollectionExtensions, Onboarding Authorization handlers,
   GlobalExceptionHandler, DTO conventions, tests hiện có.
2) Đối chiếu file PROFILE_API_CONTRACT.md tôi cung cấp với source hiện tại.
3) Chỉ tạo/cập nhật docs/api/PROFILE_API_CONTRACT.md:
   - 13 routes Profile, Experiences, Certificates;
   - MyProfileResponse, MemberProfileResponse, UpdateProfileRequest;
   - Experience & Certificate Create/Update/Response DTO;
   - PATCH missing vs null; whitelist fields; Skill Catalog UUID;
   - validation, status codes, snake_case, authorization, owner privacy;
   - YYYY-MM experience, YYYY-MM-DD certificate;
   - GET Profile và 2 collections tách riêng.
4) Nếu phát hiện khác biệt source vs contract, báo rõ trước khi thay đổi
   quyết định nghiệp vụ. Không tự mở rộng scope.
5) Chỉ commit docs sau khi kiểm tra git diff và không có secrets.

CẤM: sửa .cs/.csproj/migrations/snapshot/FE;
     chạy migration/database drop;
     đổi JWT/OAuth/Onboarding/Catalog behavior;
     bắt đầu STEP 02.

Kết quả: tóm tắt files thay đổi, các quyết định contract,
những điểm mở cần xác nhận, kết quả git diff, commit message:
docs(profile): finalize backend API contracts
```

## X. Đầu ra mong đợi để chuyển STEP 02

- `docs/api/PROFILE_API_CONTRACT.md` đã được review và commit.
- Mọi field/request/status/validation có ý nghĩa duy nhất; các `[STEP01 default]` đã được chấp thuận hoặc sửa lại.
- Không có schema/entity/controller mới; STEP 02 mới được phép thay đổi Database.
