# STEP 07 — Validate Catalog Migrations & Integration Tests

## Phạm vi

Chốt 14 bảng: 11 bảng ban đầu + `professional_roles`, `skill_categories`, `skill_category_skills` (không có `professional_role_skills`/`user_custom_skills`). `skills` có `is_active` và `user_profiles` chỉ có `preferred_role_id` (không còn cột `preferred_role` string).

**Trạng thái nguồn:** Bản source tham chiếu để viết STEP 07 chưa chứa hai migration EF Core do bạn tạo trên máy local. Test `CatalogDatabaseIntegrityTests` yêu cầu bạn đã tạo/commit cả hai migration vào repo. Không giả định test đã pass nếu chưa chạy trên máy có .NET SDK, Docker Engine và PostgreSQL Testcontainers.

## 1. Trước khi chạy

- Đảm bảo đã commit STEP 01–06 và working tree sạch; lưu thay đổi local trước khi apply patch.
- Đảm bảo có 6 migrations: 4 migrations ban đầu + `Catalog_CreateProfessionalRolesAndSkillCategories` + `Profile_ConvertPreferredRoleToReference`.
- Review `Up()/Down()` của Step 05. Nếu bỏ backfill vì DB local hiện không có User, **không áp dụng migration xóa cột cũ vào DB khác đang có User** nếu chưa chuyển dữ liệu trước.
- Docker Desktop/Engine phải chạy cho Integration Tests (`TindyApiFactory` tự tạo PostgreSQL **riêng**, không lấy DB Development).
- Chưa cần thao tác `database drop` hay reset DB dev.

## 2. Lệnh kiểm tra (PowerShell 7)

Chạy ở root chứa `Tindy.slnx`:

```powershell
git status
dotnet restore .\Tindy.slnx
dotnet build .\Tindy.slnx

dotnet ef migrations list `
  --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
  --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
  --context TindyDbContext `
  -- --environment Development
```

**Không chạy** `dotnet ef migrations add` lại để làm test; migration chỉ nên được tạo một lần và commit từ nhánh chịu trách nhiệm schema.

## 3. Integration Tests — PostgreSQL Testcontainers

Chạy Docker trước. Mỗi fixture sử dụng database container riêng:

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
  --filter "FullyQualifiedName~CatalogReadApiTests"

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
  --filter "FullyQualifiedName~CatalogDatabaseIntegrityTests"

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
  --filter "FullyQualifiedName~OnboardingSkillCatalogTests|FullyQualifiedName~OnboardingPreferredRoleCatalogTests|FullyQualifiedName~OnboardingCompletionRulesTests|FullyQualifiedName~AuthenticationOnboardingTests"

dotnet test .\Tindy.slnx
```

Test kiểm tra Catalog GET chỉ trả active, filter Category, tìm kiếm không phân biệt chữ hoa, không lặp Skill, JWT chưa Onboarding vẫn truy cập được, không JWT trả 401. Test khác kiểm tra Skill ID UUID, Role ID UUID, gate 403→200, completion 7 markers + boolean, Bio null và retry Complete.

**Nếu test schema báo thiếu migration:** kiểm tra migration mới đã được commit cùng các file `.Designer.cs` và `TindyDbContextModelSnapshot.cs`. Không sửa test để bỏ qua migration chưa có.

## 4. Kiểm tra Database Development (chỉ đọc)

Xác nhận đúng connection string trước khi chạy SQL. Có thể dùng [verify-catalog-schema.sql](../../database/scripts/verify-catalog-schema.sql) trong `psql` hoặc pgAdmin. Script chỉ SELECT, không xóa/sửa dữ liệu.

Khi cần apply migration trên **DB Development đã xác nhận đúng**:

```powershell
dotnet ef database update `
  --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
  --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
  --context TindyDbContext `
  -- --environment Development
```

Chạy lại lần 2 và so sánh row counts. Seeder idempotent không tự sinh Users, User Profiles hay tạo Skills từ yêu cầu Onboarding.

## 5. Hai kịch bản migration

**A. Database trống (bắt buộc):** `TindyApiFactory` đã chạy migration từ đầu trên PostgreSQL Testcontainers trong các Integration Tests. Kiểm tra 14 bảng, các cột mới, 2 migration mới trong `__EFMigrationsHistory`, dữ liệu catalog và run seed hai lần. Không reset DB thật để giả lập database trống.

**B. Database cũ có User (kiểm tra khi cần deploy nâng cấp):** Dùng **bản sao backup cô lập**, tạo trạng thái migration trước bước chuyển đổi rồi thử migrate, đối chiếu các bản ghi trước/sau. So sánh `users`, `user_profiles`, `user_skills`, `skill_id`, `preferred_role` → `preferred_role_id`. Kiểm thử rollback khi Role legacy không mapping được. **Chưa được coi là đã kiểm thử** nếu chỉ chạy kịch bản A, nhất là trong trường hợp STEP 05 không có Backfill SQL.

## 6. Kết quả cần đạt

- [ ] Build và toàn bộ tests pass trên máy chạy được .NET 10 và Docker.
- [ ] `CatalogDatabaseIntegrityTests`: có 14 bảng và đủ 6 migration, không có cột legacy `preferred_role`.
- [ ] Seed lặp lại 2 lần, counts & `skill_id` không đổi.
- [ ] GET Catalog đúng JSON `snake_case`, filter/search/inactive hoạt động.
- [ ] Onboarding Step 2 không tạo Skill mới, Step 3 chỉ nhận UUID Role active.
- [ ] Step 8: 7 marker và `is_onboarding_completed=true`.
- [ ] Test Register/Login/JWT/Authorization Gate vẫn pass.
- [ ] API Docs và Database Guides đúng contract mới.
- [ ] `git diff --check` không phát sinh whitespace errors.

## 7. Commit

```powershell
git status
git add tests/Tindy.Tests/Integration/AuthenticationOnboardingTests.cs
git add tests/Tindy.Tests/Integration/CatalogReadApiTests.cs
git add tests/Tindy.Tests/Integration/CatalogDatabaseIntegrityTests.cs
git add docs/guide/ONBOARDING_API_IMPLEMENTATION.md
git add docs/guide/DATABASE_SETUP.md docs/guide/DATABASE_SEED.md docs/guide/DATABASE_RESET.md
git add docs/guide/CATALOG_AND_ONBOARDING_VERIFICATION.md
git add database/scripts/verify-catalog-schema.sql
git diff --cached --check
git diff --cached --stat
git commit -m "test(onboarding): verify catalog migration and onboarding flow"
```

Mọi migration mới từ STEP 01 hoặc STEP 05 phải được commit trong STEP tương ứng. Nếu có sai khác so với source thực tế, review patch trước khi apply thay vì ghi đè code đã được đồng đội sửa.
