# DATABASE_SEED — Tindy Backend

> Quy ước **khởi tạo và đồng bộ dữ liệu mặc định** bằng EF Core 10 cho các thành viên team Tindy.
>
> **Stack:** .NET 10 · EF Core 10 · PostgreSQL · PowerShell 7  
> **Đối tượng:** Thành viên phát triển backend Tindy  
> **Tài liệu liên quan:** [DATABASE_SETUP.md](./DATABASE_SETUP.md) · [DATABASE_RESET.md](./DATABASE_RESET.md)

## 1. Database Seeding là gì?

**Database Seeding** là quá trình đưa dữ liệu khởi tạo vào các bảng **sau khi bảng đã tồn tại**.

| Thành phần | Vai trò | Ví dụ |
|---|---|---|
| **EF Core Migration** | Thay đổi schema | Tạo `roles`, thêm cột cho `users` |
| **Database Seeder** | Thêm/cập nhật dữ liệu khởi tạo | Role `Admin`, `User`; Permission; Skill |
| **User/API** | Sinh dữ liệu nghiệp vụ khi dùng app | User đăng ký, cập nhật profile, nhắn tin |

**Quan trọng:** Code First + Migration **không mặc định tự sinh Roles, Permissions, Skills** chỉ vì đã có Entity. Dữ liệu chỉ được thêm khi **team thực sự triển khai** logic seed và nó được thực thi.

---

## 2. Tindy nên seed những dữ liệu nào?

### A. Default / Reference Data

Ví dụ dữ liệu có thể cần trên mọi môi trường (phụ thuộc schema thực tế):

| Bảng | Ví dụ seed | Ghi chú |
|---|---|---|
| `roles` | `Admin`, `User` | Role mặc định cho Authentication/Authorization |
| `permissions` | `user.read`, `user.update` | Theo danh sách quyền thực sự được chốt |
| `skills` | `C#`, `Java`, `React`, `AWS` | Danh mục kỹ năng dùng chung |
| Quan hệ role–permission | Permission được gán cho role | Chỉ seed sau khi role/permission tồn tại |

Tên bảng, cột và giá trị trong bảng **chỉ là ví dụ**; phải đối chiếu với Entity/Configurations hiện hành trước khi viết Seeder. Không tự tạo quyền nghiệp vụ nếu team chưa thiết kế.

### B. Development / Demo Data

Ví dụ: User test, profile mẫu, project mẫu, dữ liệu UI demo.

- **Tách rõ** khỏi Default Data.
- Chỉ bật trong môi trường `Development` khi team thống nhất.
- Không chứa user thật, mật khẩu thật, OAuth tokens, JWT signing keys hoặc dữ liệu nhạy cảm.
- Nếu cần tài khoản test đăng nhập, mật khẩu phải được hash qua cơ chế password hasher chính thức; không commit mật khẩu thật hoặc hash không được quản trị.

### C. Không seed mặc định

Không mặc định seed `users`, `user_profiles`, `user_credentials`, `external_auth_accounts`, các phiên chat, dữ liệu giao dịch hoặc dữ liệu người dùng thật.

---

## 3. Cơ chế được đề xuất cho Tindy

**EF Core 10: `UseSeeding()` + `UseAsyncSeeding()`**.

- `dotnet ef database update` gọi **`UseSeeding()` (đồng bộ)**.
- `MigrateAsync()` gọi **`UseAsyncSeeding()` (bất đồng bộ)**.
- Các seeding delegate có thể chạy **ngay cả khi không có Migration mới**.
- Seeding sử dụng cơ chế migration locking của EF Core, tuy vậy vẫn phải kiểm tra tồn tại và đặt unique constraint ở database để tránh trùng dữ liệu.
- Nếu project chỉ cài `UseAsyncSeeding()` mà không cài `UseSeeding()`, không nên kỳ vọng EF CLI sẽ seed.

> **Trạng thái triển khai:** Tài liệu này mô tả **kiến trúc và quy trình được đề xuất**. Chỉ sau khi team đã thêm các Seeder và **đăng ký `UseSeeding`/`UseAsyncSeeding` đúng DbContext EF CLI đang khởi tạo**, lệnh `database update` mới tự động seed. **Không giả định repo hiện đã có các Seeder này.**

### Cấu trúc thư mục gợi ý

```text
src/
└── Tindy.Infrastructure/
    └── Persistence/
        ├── TindyDbContext.cs
        ├── Configurations/
        ├── Migrations/
        └── Seeding/
            ├── TindySeedRunner.cs
            ├── RoleSeeder.cs
            ├── PermissionSeeder.cs
            └── SkillSeeder.cs
```

Đường dẫn trên là **gợi ý**, không phải khẳng định source hiện tại có sẵn các file này.

### Cách đăng ký (mã minh họa, phải triển khai thêm)

Trong nơi Infrastructure đăng ký `TindyDbContext` (thường là `AddDbContext` / `AddInfrastructure`), ví dụ:

```csharp
services.AddDbContext<TindyDbContext>(options =>
{
    options.UseNpgsql(connectionString);

    options.UseSeeding((context, _) =>
    {
        // Phải tạo TindySeedRunner.Seed(DbContext) trong Infrastructure.
        TindySeedRunner.Seed(context);
    });

    options.UseAsyncSeeding(async (context, _, cancellationToken) =>
    {
        // Phải tạo TindySeedRunner.SeedAsync(DbContext, CancellationToken).
        await TindySeedRunner.SeedAsync(context, cancellationToken);
    });
});
```

**Đây không phải đoạn code copy-paste chạy ngay**: `TindySeedRunner` cần được team triển khai, `connectionString` lấy từ cấu hình dự án, và namespace/pattern đăng ký phải khớp source thực tế. Nếu project có `IDesignTimeDbContextFactory<TindyDbContext>`, bảo đảm options ở design time cũng có `UseSeeding()`; nếu không, `dotnet ef database update` có thể **không gọi Seeder**.

### Logic Seeder: idempotent

Ví dụ pseudocode cho Role:

```text
RoleSeeder:
  nếu role có mã "ADMIN" chưa tồn tại:
      insert role "ADMIN"
  nếu role có mã "USER" chưa tồn tại:
      insert role "USER"
  save changes
```

Nên dùng **business key ổn định** như `Code`, `NormalizedName` hoặc mã permission đã quy ước, thay vì so sánh mỗi lần bằng UUID ngẫu nhiên. Đặt unique index/constraint tương ứng nếu schema cho phép.

**Seeder chạy lại không được:**

- Thêm trùng Role/Permission/Skill.
- Xóa hoặc ghi đè dữ liệu người dùng thật.
- Reset toàn bộ database.
- Tự động đổi quyền của người dùng khi chưa có yêu cầu nghiệp vụ được review.

Nếu cần **sửa dữ liệu mặc định đã có**, phải viết logic update có chủ đích, có điều kiện và test tác động; thuật toán “nếu chưa có thì thêm” **không tự cập nhật các bản ghi đã tồn tại**.

---

## 4. Flow dành cho thành viên mới clone repository

Điều kiện: Đã setup PostgreSQL local và User Secrets theo [DATABASE_SETUP.md](./DATABASE_SETUP.md).

### STEP 1 — Clone/Pull và restore

```powershell
git clone https://github.com/FCAJ-OJT-Fall26/tindy-fcaj-platform.git
cd tindy-fcaj-platform

dotnet restore
dotnet build
```

Nếu đã clone, cập nhật branch theo Git workflow của team, không clone lại.

### STEP 2 — Apply migrations + seed (nếu đã cấu hình)

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Kết quả mong đợi:

1. Các Migration còn thiếu được áp dụng vào PostgreSQL local.
2. `UseSeeding()` được gọi **nếu đã đăng ký**.
3. Dữ liệu mặc định còn thiếu được thêm vào database.
4. Dữ liệu đã tồn tại được giữ nguyên, miễn là Seeder/Migration không có logic phá hủy hay ghi đè.

> Đây **không phải** lệnh sao chép dữ liệu từ máy của leader. Chỉ những record được định nghĩa trong seed/migration hoặc nhập từ nguồn riêng mới có trong database local.

---

## 5. Flow khi cần thêm một Skill/Role/Permission mới

Ví dụ muốn seed thêm Skill `Docker`:

1. Người phụ trách kiểm tra bảng `skills` đã tồn tại và có khóa nhận diện duy nhất.
2. Thêm `Docker` vào danh sách trong `SkillSeeder`.
3. Bổ sung logic kiểm tra tồn tại trước khi insert.
4. Test trên database đã có dữ liệu, sau đó chạy **lặp lại lần 2** để xác nhận không sinh bản ghi trùng.
5. Commit code Seeder → Push feature branch → Pull Request → leader review/merge.
6. Thành viên khác pull code mới và chạy `database update`.

**Không cần tạo Migration mới** nếu chỉ thay đổi dữ liệu trong `UseSeeding()` mà không đổi schema. Tuy nhiên, nếu thêm/sửa cột, constraint, table hoặc quan hệ thì **phải tạo Migration cho thay đổi schema**.

### Trường hợp vừa thêm Column vừa seed dữ liệu

Ví dụ thêm `last_login_at` vào `users` và thêm Skill `Docker`:

**Người sửa schema:**

```powershell
# 1. Sửa Entity/Configuration, rồi tạo Migration
dotnet ef migrations add AddUserLastLoginAt `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development

# 2. Update Seeder để thêm Docker, sau đó apply
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Sau khi được merge, **thành viên khác chỉ cần pull và chạy `database update`**, không tạo lại Migration tương tự.

---

## 6. Seed lại mà KHÔNG Drop Database

Nếu muốn đồng bộ lại **dữ liệu mặc định** và giữ nguyên User/Profile/Project hiện có:

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Với `UseSeeding()` đã đăng ký, lệnh này chạy Seeder **kể cả khi không có Migration mới**.

Ví dụ:

```text
Trước:
roles: Admin, User
skills: C#, Java
users:  5 tài khoản test

Thay đổi SkillSeeder: thêm Docker

Sau database update:
roles: Admin, User
skills: C#, Java, Docker
users:  5 tài khoản test (giữ nguyên nếu Seeder không tác động vào users)
```

**Lưu ý:** Chạy seed lại khác với **xóa hết row rồi nạp lại từ đầu**. Seeder dạng idempotent chỉ bổ sung/update theo logic đã viết; nó **không xóa các bản ghi cũ** nếu bạn không yêu cầu rõ. Muốn reset cả database thì xem [DATABASE_RESET.md](./DATABASE_RESET.md), chấp nhận mất toàn bộ dữ liệu cũ.

---

## 7. Kiểm tra dữ liệu sau khi Seed

Sử dụng pgAdmin hoặc SQL. Các tên dưới đây chỉ minh họa, sửa lại theo cấu hình entity/table thực tế:

```sql
-- Ví dụ: kiểm tra record được seed
SELECT * FROM roles;
SELECT * FROM permissions;
SELECT * FROM skills;

-- Ví dụ: kiểm tra trùng tên role (nếu column thực tế tên 'name')
SELECT name, COUNT(*)
FROM roles
GROUP BY name
HAVING COUNT(*) > 1;
```

Chạy `database update` **hai lần liên tiếp** để test tính idempotent. Số lượng dữ liệu mặc định **không được tăng bất thường** ở lần thứ hai.

### Checklist test Seeder

- [ ] Database trống: seeding tạo đủ dữ liệu mặc định.
- [ ] Database đã có data: seeding không tạo duplicate.
- [ ] Chạy `database update` hai lần: kết quả ổn định.
- [ ] Có User/Profile từ trước: seeding không xóa hoặc thay đổi ngoài ý muốn.
- [ ] Các bảng quan hệ được seed theo đúng thứ tự phụ thuộc khóa ngoại.
- [ ] Seed Development không chạy trên Production.
- [ ] Seeder sync/async có hành vi tương đương (nếu cả hai được sử dụng).

---

## 8. Quy định team Tindy

1. **Default Data** và **Development Data** phải tách logic rõ ràng; chỉ seed dữ liệu test khi environment cho phép.
2. Seeder phải **idempotent**, dùng business key xác định tính duy nhất và có unique constraint khi phù hợp.
3. Cần chốt quyền nghiệp vụ trước khi seed Permission hoặc role–permission; không tự cấp quyền cao cho user.
4. Không commit credential thật, OAuth secret, token, email cá nhân hoặc dữ liệu người dùng thật vào seed.
5. Chỉ thêm Migration nếu **schema** thay đổi. Thay đổi logic `UseSeeding()` thường **không cần Migration mới**.
6. Seeder là source code, phải được commit và review như các thay đổi backend khác.
7. Không tự động drop database hoặc truncate các bảng mỗi lần seed.
8. Với Production, cần review dữ liệu seed và phương thức deploy; lệnh CLI local không phải quy trình triển khai production.
9. Nếu seeding phụ thuộc schema đã bị rollback (ví dụ `database update 0`), cần xử lý tương thích; không dùng rollback về 0 để mô phỏng seeding thông thường.
10. Không dùng `Database.EnsureCreated()`/`EnsureCreatedAsync()` thay cho Migrations khi project đã quản lý schema bằng EF Core Migrations.

---

## 9. FAQ

### `database update` chạy xong nhưng không có dữ liệu seed?

Kiểm tra: đã code Seeder chưa, đã đăng ký `UseSeeding()` chưa, cấu hình EF CLI có dùng đúng `TindyDbContext` chưa, có `IDesignTimeDbContextFactory` riêng không, Seeder có điều kiện bỏ qua hay lỗi hay không.

### Nếu không có Migration mới thì Seeder có chạy không?

**Có**, nếu EF CLI sử dụng `UseSeeding()` và bạn gọi `dotnet ef database update`. Hành vi này được EF Core hỗ trợ kể cả khi không có migration nào cần apply.

### Có cần chạy thêm `dotnet run` sau khi `database update` để seed không?

**Không cần** đối với Seeder tích hợp `UseSeeding()` khi đã cấu hình đúng. Không nên nhúng seed vào mỗi HTTP request.

### Nếu sửa Seeder để thêm dữ liệu có cần tạo Migration?

**Không**, nếu chỉ thêm/sửa logic seed và schema không thay đổi. Cần review nếu seed thay đổi dữ liệu hiện có.

### Drop Database xong seed có khôi phục User cũ không?

**Không.** Seeder chỉ phục hồi dữ liệu *được định nghĩa trong seed*. User/Profile/Chat cũ cần backup để khôi phục.

### Có thể dùng `HasData()` không?

Có, nhưng `HasData()` là **model-managed data** với nhiều giới hạn và gắn thay đổi dữ liệu với Migrations. Đối với dữ liệu khởi tạo/logic phụ thuộc trạng thái database của Tindy, ưu tiên `UseSeeding()` và `UseAsyncSeeding()`.

---

## 10. Quick Reference — PowerShell 7

```powershell
# Cập nhật schema và chạy UseSeeding (nếu đã cấu hình)
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

**Kết luận:** Dùng một lệnh `database update` để đồng bộ Migration + Default Seed **sau khi Seeder được triển khai**. Muốn giữ dữ liệu cũ thì **không Drop**; muốn làm lại toàn bộ local DB thì xem `DATABASE_RESET.md`.

**Tham khảo chính thức:** [EF Core: Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding) · [EF Core: Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) · [EF Core CLI](https://learn.microsoft.com/en-us/ef/core/cli/dotnet).
