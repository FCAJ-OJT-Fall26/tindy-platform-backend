# DATABASE_SEED — Tindy Backend (Catalog + Development Demo Data)

> **Cập nhật 2026-10-30:** Catalog đã được mở rộng lên **84 skills / 10 categories / 102 liên kết** (11 professional roles). Có thêm bộ seed Development opt-in **20 users / 50 Projects**, chi tiết và lệnh chạy trong [DEVELOPMENT_DEMO_SEED.md](./DEVELOPMENT_DEMO_SEED.md). Mặc định vẫn chỉ seed Catalog; không tự tạo tài khoản demo trên Production.

> **Phiên bản cập nhật:** Đối chiếu `tindy-backend-platform(20261015-022026).zip`, .NET 10, EF Core 10, PostgreSQL, PowerShell 7.  
> **Mục tiêu:** Mỗi thành viên team có cùng **System Catalog** sau khi chạy migration + seeding; không tạo kỹ năng tự do từ Onboarding.

## 1. Phân biệt Migration, Seeder và dữ liệu người dùng

| Công việc | Quản lý | Ví dụ |
|---|---|---|
| **Migration** | EF Core Code First | Tạo `professional_roles`, `skill_categories`; thêm `skills.is_active` |
| **System Seed Data** | Backend Infrastructure | Member role, danh mục nghề nghiệp, danh mục nhóm kỹ năng, kỹ năng và các quan hệ |
| **User Data** | API nghiệp vụ | User, Profile, `user_skills`, tiến độ Onboarding |

**Chỉ tạo bảng không có nghĩa dữ liệu seed sẽ tự xuất hiện.** Trong source hiện tại, việc seed đã được triển khai bằng `TindySeedRunner`, `UseSeeding()` và `UseAsyncSeeding()`.

## 2. Dữ liệu được Seed hiện tại

| Bảng | Seed hiện có | Số lượng mặc định |
|---|---|---:|
| `roles` | `Member` cho RBAC | 1 role mặc định (có thể có thêm role khác ngoài Seeder) |
| `professional_roles` | Backend Developer, Frontend Developer, AI Engineer, ... | 11 |
| `skill_categories` | Các nhóm hiện có + Software Testing, Product & Business Analysis, Data Engineering | 10 |
| `skills` | Các skills hiện có + SQL, EF Core, test tools, data pipelines, messaging, observability, ... | 84 |
| `skill_category_skills` | Quan hệ Category ↔ Skill (N:N) | 102 |

Đây là **số lượng trong bộ seed**; tổng bản ghi thực tế có thể lớn hơn khi database đã chứa dữ liệu từ trước. `permissions` và `role_permissions` **chưa có Seeder trong bản source được kiểm tra**. Không mặc định seed User data hoặc OAuth identities. User/Profile/Experience/Certificate/Project demo chỉ được thêm khi bật `DevelopmentSeed:Enabled` với mật khẩu riêng trong môi trường Development.

## 3. Vị trí source và thứ tự chạy

```text
src/Tindy.Infrastructure/
├── Extensions/
│   └── ServiceCollectionExtensions.cs   # đăng ký UseSeeding / UseAsyncSeeding
└── Persistence/
    ├── TindyDbContext.cs
    ├── Migrations/
    └── Seeding/
        ├── TindySeedRunner.cs
        ├── CatalogSeedData.cs
        ├── RoleSeeder.cs
        ├── ProfessionalRoleSeeder.cs
        ├── SkillCategorySeeder.cs
        ├── SkillSeeder.cs
        └── SkillCategorySkillSeeder.cs
```

`TindySeedRunner` thực hiện:

1. `RoleSeeder`
2. `ProfessionalRoleSeeder`
3. `SkillCategorySeeder`
4. `SkillSeeder`
5. `SkillCategorySkillSeeder`

**Vì sao cần thứ tự?** Bảng trung gian `skill_category_skills` phải tham chiếu đến Categories và Skills đã tồn tại.

### Dữ liệu chuẩn nằm ở đâu?

`CatalogSeedData.cs` chứa:

- `ProfessionalRoles`: UUID cố định, tên, mô tả và `display_order`.
- `SkillCategories`: UUID cố định, tên, mô tả và `display_order`.
- `Skills`: UUID cố định và tên.
- `CategorySkills`: ánh xạ từ Category name sang danh sách Skill names.
- Hàm chuẩn hóa tên `Normalize()` để so khớp `name_normalized`.

**Quy tắc:** Không thay đổi UUID đã phát hành để tránh đứt các FK. Không tạo hai bản ghi cùng `name_normalized`. Một Skill có thể thuộc nhiều Category nhưng chỉ có một `skill_id`.

## 4. Cách chạy Seed Data trên PostgreSQL local

**Điều kiện:** PostgreSQL local hoạt động, đã cấu hình `ConnectionStrings:DefaultConnection` cho môi trường `Development`; repository có đầy đủ migrations STEP 01 và STEP 05.

Chạy tại root nơi có `Tindy.slnx`:

```powershell
dotnet build .\Tindy.slnx

dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Trong repo hiện tại:

- EF CLI `dotnet ef database update` gọi `UseSeeding()` (đồng bộ).
- `Database.MigrateAsync()` với DbContext dùng đúng production options gọi `UseAsyncSeeding()` (bất đồng bộ).
- `dotnet ef database update` có thể chạy Seeder **ngay cả khi không có migration mới**.
- Integration Test Factory **ghi đè cấu hình DbContext**, nên sau `MigrateAsync()` nó tự gọi `TindySeedRunner.SeedAsync(db)` riêng.

Không cần Docker Desktop hoặc Dockerfile để chạy database update lên **PostgreSQL local**. Docker chỉ cần cho các Integration Tests dùng Testcontainers hoặc khi bạn muốn Dockerize ứng dụng.

## 5. Kiểm tra kết quả bằng SQL

Dùng pgAdmin hoặc `psql` kết nối đúng database local:

```sql
SELECT 'professional_roles' AS table_name, COUNT(*) AS total
FROM professional_roles
UNION ALL SELECT 'skill_categories', COUNT(*) FROM skill_categories
UNION ALL SELECT 'skills', COUNT(*) FROM skills
UNION ALL SELECT 'skill_category_skills', COUNT(*) FROM skill_category_skills;
```

Với database chưa có Catalog trước đó, kết quả mặc định dự kiến là **11 / 10 / 84 / 102**. Kiểm tra danh sách:

```sql
SELECT professional_role_id, name, is_active
FROM professional_roles
ORDER BY display_order, name;

SELECT category_id, name, is_active
FROM skill_categories
ORDER BY display_order, name;

SELECT sc.name AS category, s.name AS skill
FROM skill_category_skills scs
JOIN skill_categories sc ON sc.category_id = scs.category_id
JOIN skills s ON s.skill_id = scs.skill_id
ORDER BY sc.display_order, s.name;
```

Kiểm tra migration:

```sql
SELECT "MigrationId"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";
```

## 6. Seeder chạy lặp lại có tạo trùng không?

**Mục tiêu thiết kế là idempotent:** Seeder tìm theo `name_normalized`, chỉ INSERT bản ghi chưa tồn tại. Bảng trung gian dùng cặp `(category_id, skill_id)` để tránh trùng.

Cách kiểm thử:

1. Chạy `dotnet ef database update` lần thứ nhất.
2. Ghi số lượng của 4 bảng và các `skill_id` của Skill đang được User sử dụng.
3. Chạy lại **cùng một lệnh**.
4. Truy vấn lại: không có bản ghi trùng, liên kết `user_skills` vẫn đúng.

**Lưu ý:** Seeder hiện tại **chỉ thêm dữ liệu thiếu**, không tự cập nhật `name`, `description`, `display_order` hoặc `is_active` của bản ghi đã tồn tại. Nếu đổi nhãn/mô tả hay cần disable Skill cũ, phải có kế hoạch cập nhật dữ liệu riêng (migration dữ liệu, admin job, script được review). Không sửa trực tiếp ID đã dùng ở Production.

### Khi database đã có Skills cũ

`SkillSeeder` đối chiếu `name_normalized`: nếu PostgreSQL đã có một Skill tên tương ứng, Seeder **tái sử dụng `skill_id` đó**. `SkillCategorySkillSeeder` tra ID hiện có để tạo liên kết. Không xóa và tạo lại Skill cũ, nên liên kết trong `user_skills` không bị mất chỉ vì chạy lại Seeder.

## 7. Thêm một Skill hoặc Category mới

Ví dụ cần thêm `RabbitMQ` vào Backend Development và Cloud & DevOps:

1. Kiểm tra Skill đã tồn tại chưa trong `CatalogSeedData.Skills` và PostgreSQL.
2. Nếu là Skill mới: thêm **một** `NamedItem` có UUID mới **cố định** vào `CatalogSeedData.Skills`.
3. Thêm tên Skill vào danh sách của các Categories cần thiết trong `CatalogSeedData.CategorySkills`.
4. Tạo PR và review cùng team, đảm bảo normalization không trùng.
5. Apply bằng `dotnet ef database update`, kiểm tra SQL.
6. Commit cùng thay đổi Catalog và cập nhật tài liệu khi cần.

Nếu chỉ thêm dữ liệu bằng Seeder đang có và không sửa schema thì **không cần migration mới**. Ngược lại, thay đổi Entity/cột/FK phải tạo migration.

### Không nên làm

- Không cho người dùng Onboarding nhập Skill tự do rồi INSERT vào `skills`.
- Không reset database hoặc `database drop` chỉ để cập nhật bộ Skills.
- Không dùng `Guid.NewGuid()` mỗi lần seed để tạo ID cho cùng một danh mục chuẩn.
- Không đổi tên thành Skill khác bằng cách tạo hàng mới mà không đánh giá dữ liệu tham chiếu.
- Không seed User thật, mật khẩu, JWT signing key hoặc OAuth token vào Git.

## 8. Nếu muốn xóa rồi tạo lại database Development

Đây **không phải workflow bình thường**. `dotnet ef database drop` xóa cả schema và toàn bộ dữ liệu User trong database mục tiêu, còn file Migration trong repository vẫn giữ nguyên.

Chỉ thực hiện sau khi chắc chắn đó là database Development riêng và đã backup dữ liệu quan trọng. Xem [DATABASE_RESET.md](./DATABASE_RESET.md). Để thêm/sửa catalog thông thường, **chỉ cần chạy `database update`**.

## 9. Kiểm thử STEP 07 không cần Docker ngay

```powershell
# Build
dotnet build .\Tindy.slnx

# Các bài kiểm thử không thuộc namespace Integration
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName!~Integration"
```

Sau đó dùng PostgreSQL local và Postman/Swagger để thử Catalog API, Onboarding Step 2 và Step 3. Các test trong `tests/Tindy.Tests/Integration/` dùng PostgreSQL Testcontainers và cần Docker Engine khi chạy tự động; chúng có thể được hoãn, nhưng **không ghi nhận là đã pass**.

## 10. Tài liệu liên quan

- [DATABASE_SETUP.md](./DATABASE_SETUP.md): Tạo và cập nhật PostgreSQL local.
- [DATABASE_RESET.md](./DATABASE_RESET.md): Reset database Development khi thực sự cần.
- [ONBOARDING_API_IMPLEMENTATION.md](./ONBOARDING_API_IMPLEMENTATION.md): Contract chọn Skills/Preferred Roles bằng UUID.
- `database/scripts/verify-catalog-schema.sql`: Script truy vấn chỉ đọc để kiểm tra STEP 07 (được thêm bằng patch STEP 07).

> Tài liệu phản ánh code trong ZIP ngày 15/10/2026; kết quả `dotnet build`, SQL và tests vẫn cần xác minh trên môi trường của người phát triển.
