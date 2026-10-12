# DATABASE_RESET — Tindy Backend

> Hướng dẫn **reset toàn bộ database local** và tạo lại schema bằng EF Core Migrations.
>
> **Stack:** .NET 10 · EF Core 10 · PostgreSQL · PowerShell 7  
> **Đối tượng:** Thành viên phát triển backend Tindy  
> **Tài liệu liên quan:** [DATABASE_SETUP.md](./DATABASE_SETUP.md) · [DATABASE_SEED.md](./DATABASE_SEED.md)

## 1. Mục đích và phạm vi

**Reset database** nghĩa là **xóa hẳn database hiện tại**, sau đó dùng các migration đã có trong source code để tạo lại database và bảng từ đầu.

Áp dụng khi:

- Muốn đưa **PostgreSQL local** về trạng thái sạch để test lại đăng ký/đăng nhập, API hoặc migration.
- Dữ liệu **development** không còn cần giữ, hoặc đã có bản backup.
- Cần tạo lại toàn bộ bảng theo lịch sử migration hiện hành.

**KHÔNG áp dụng cho:** Production, Staging, database test dùng chung, hoặc bất cứ database nào chứa dữ liệu cần giữ. Với các môi trường đó, phải có quy trình backup, review và triển khai riêng.

> [!CAUTION]
> `dotnet ef database drop` **xóa database và toàn bộ dữ liệu trong database đó**. Không có lệnh `undo`. Nếu chưa backup thì không thể khôi phục dữ liệu cũ bằng Migration hay Seeder.

---

## 2. Drop ảnh hưởng đến những gì?

| Thành phần | Sau khi `database drop` |
|---|---|
| PostgreSQL database đang trỏ đến | **Bị xóa** |
| Tables, constraints, foreign keys, indexes, views... nằm trong database | **Bị xóa** |
| User accounts, profiles, projects, chat, dữ liệu nghiệp vụ | **Bị xóa** |
| Bảng `__EFMigrationsHistory` trong database | **Bị xóa** |
| File Migration `.cs` trong repository | **Giữ nguyên** |
| `TindyDbContext`, Entities, Configurations | **Giữ nguyên** |
| User Secrets / Connection String local | **Giữ nguyên** |

**Phân biệt:**

- **Migration** khôi phục cấu trúc schema, không tự phục hồi dữ liệu người dùng đã xóa.
- **Seeding** (nếu đã triển khai) chỉ thêm lại **dữ liệu khởi tạo được định nghĩa trong Seeder**, ví dụ Roles, Permissions, Skills.
- Dữ liệu người dùng thật chỉ có thể khôi phục từ backup/nguồn dữ liệu riêng.

---

## 3. Điều kiện trước khi reset

Thực hiện các lệnh bên dưới ở **thư mục gốc repository** (cùng cấp `Tindy.slnx`).

```text
<repository-root>/
├── Tindy.slnx
├── src/
│   ├── Tindy.Api/
│   ├── Tindy.Application/
│   └── Tindy.Infrastructure/
│       └── ... (TindyDbContext, Migrations)
├── DATABASE_SETUP.md
├── DATABASE_RESET.md
└── DATABASE_SEED.md
```

Checklist bắt buộc:

- [ ] Tôi đang thao tác với **database PostgreSQL LOCAL của riêng tôi**.
- [ ] Connection String `DefaultConnection` thực sự trỏ tới database định xóa.
- [ ] Môi trường đang chạy là `Development`.
- [ ] Không có thành viên/dịch vụ nào khác đang dùng database này.
- [ ] Tôi **chấp nhận mất toàn bộ dữ liệu** hoặc đã backup dữ liệu cần giữ.
- [ ] Các file Migration đầy đủ và project có thể build.

Kiểm tra .NET và EF Core CLI:

```powershell
dotnet --version
dotnet ef --version
dotnet build
```

Kiểm tra User Secrets của API (không chia sẻ kết quả vì có thể chứa mật khẩu):

```powershell
dotnet user-secrets list --project .\src\Tindy.Api\Tindy.Api.csproj
```

> Nếu dùng cấu hình ghi đè bằng biến môi trường, cấu hình khác hoặc `IDesignTimeDbContextFactory`, cần kiểm tra **Connection String thực sự được EF CLI sử dụng**; User Secrets không phải lúc nào cũng là nguồn cấu hình cuối cùng.

---

## 4. Quy trình reset chuẩn — Development LOCAL

### STEP 1 — Backup (tùy chọn nhưng khuyến nghị)

Nếu có dữ liệu muốn giữ, có thể backup bằng **pgAdmin → Backup** hoặc PostgreSQL CLI. Ví dụ:

```powershell
pg_dump -h localhost -p 5432 -U postgres -F c -f .\tindy-local-before-reset.backup tindy
```

- Thay user/database cho đúng máy của mình.
- PostgreSQL sẽ yêu cầu mật khẩu theo cơ chế xác thực đã thiết lập.
- Không commit các file `.backup`, `.dump`, `.sql` có dữ liệu thật hoặc thông tin nhạy cảm lên Git.
- Chỉ backup chưa đảm bảo khôi phục thành công; dữ liệu quan trọng nên được **kiểm tra khả năng restore** trước khi xóa.

### STEP 2 — Xem trước database sẽ bị xóa (BẮT BUỘC)

`--dry-run` chỉ in thông tin, **không xóa database**:

```powershell
dotnet ef database drop `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    --dry-run `
    -- --environment Development
```

**Đọc kỹ tên database và server** được hiển thị. Nếu sai hoặc không xác định được, **dừng tại đây** và kiểm tra Connection String.

### STEP 3 — Drop database local

Sau khi đã xác nhận đúng database:

```powershell
dotnet ef database drop `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    --force `
    -- --environment Development
```

`--force` bỏ qua câu hỏi xác nhận của công cụ, **không tự đảm bảo** database là local. Vì vậy chỉ dùng sau STEP 2.

### STEP 4 — Tạo lại database và tables từ Migration

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

EF Core sẽ:

1. Kết nối PostgreSQL bằng cấu hình Development.
2. Tạo database mới nếu cần và tài khoản PostgreSQL **có quyền phù hợp**.
3. Chạy các Migration theo thứ tự để tạo tables/keys/indexes.
4. Tạo và ghi lịch sử vào `__EFMigrationsHistory`.
5. Chạy **`UseSeeding()` nếu dự án đã đăng ký seeding trên DbContext dùng bởi EF CLI**.

> Nếu user PostgreSQL không có quyền tạo database, tạo database rỗng bằng pgAdmin hoặc tài khoản quản trị rồi chạy lại `database update`. Không cần tự tạo bảng thủ công.

### STEP 5 — Kiểm tra kết quả

Trong pgAdmin: refresh database → **Schemas → public → Tables** (hoặc schema thực tế của dự án).

Hoặc dùng SQL:

```sql
-- Xem migration đã áp dụng
SELECT *
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

-- Liệt kê bảng trong schema public
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE'
ORDER BY table_name;
```

Nếu Seeder đã có, kiểm tra dữ liệu Roles/Permissions/Skills theo tên bảng, cột **thực tế của dự án**. Nếu bảng trống, hãy đọc [DATABASE_SEED.md](./DATABASE_SEED.md): có thể Seeder chưa được triển khai/đăng ký.

---

## 5. Khi nào KHÔNG cần reset?

| Tình huống | Cách làm |
|---|---|
| Thành viên mới clone repo | Cấu hình PostgreSQL local → `dotnet ef database update` |
| Pull code có Migration mới | `dotnet ef database update` |
| Thêm/sửa Column hoặc Table | Người phụ trách tạo/commit Migration mới → mọi người `database update` |
| Chỉ muốn thêm dữ liệu mặc định | Cập nhật Seeder → `database update` khi đã cấu hình `UseSeeding()` |
| Muốn cập nhật một bản ghi hiện có | Viết logic cập nhật/seed có kiểm soát, **không Drop** |
| Muốn xóa sạch cả schema + data của local | `database drop` → `database update` |

**Không dùng Drop như cách cập nhật schema thông thường.** Migration bình thường sẽ bảo toàn dữ liệu nếu không có thao tác phá hủy dữ liệu; luôn review `Up()` trước khi apply.

---

## 6. Quy định team Tindy

1. **Không xóa folder `Migrations`** khi reset. Migration files phải được commit lên GitHub.
2. **Không tạo lại `InitialCreate`** sau mỗi lần reset.
3. Chỉ người đang làm task thay đổi schema mới cần chạy `dotnet ef migrations add ...` và commit migration.
4. Thành viên chỉ clone/pull code **không tạo migration mới** từ schema đã có.
5. Không bật cơ chế **tự động Drop database mỗi lần chạy ứng dụng**.
6. Không tùy tiện xóa bảng `__EFMigrationsHistory` để xử lý lỗi.
7. Trước khi reset, xác nhận lại database bằng `--dry-run` và kiểm tra cấu hình.
8. Dữ liệu seed phải **idempotent**: chạy lại không tạo bản ghi trùng hoặc phá dữ liệu đã có.

### Vì sao không dùng `database update 0` để reset hoàn toàn?

`dotnet ef database update 0` là **rollback các Migration thông qua `Down()`**, không phải thao tác xóa toàn bộ database; nó không đảm bảo xử lý những object ngoài migration. Ngoài ra, `UseSeeding()` có thể được gọi sau thao tác migration/rollback và **thất bại khi bảng cần seed không còn tồn tại**. Với nhu cầu làm lại toàn bộ local DB, team thống nhất dùng `database drop` → `database update`.

---

## 7. Lỗi thường gặp

### `database is being accessed by other users`

Có kết nối còn mở vào PostgreSQL (API đang chạy, pgAdmin, query tool...). Dừng API và các kết nối liên quan, rồi thử lại. Không ngắt kết nối người khác nếu đó không phải DB của mình.

### `permission denied` / `must be owner of database`

Tài khoản PostgreSQL thiếu quyền để xóa hoặc tạo database. Kiểm tra owner/quyền, không dùng user production để thao tác local.

### `No migrations were found`

Kiểm tra branch, folder Migrations, đúng `--project`, `--startup-project` và `--context`. Nếu chưa có migration, người phụ trách cần tạo/commit migration trước.

### Drop thành công nhưng `database update` lỗi

Kiểm tra PostgreSQL đang chạy, quyền tạo database, build errors và từng migration. Database có thể đang ở trạng thái dở dang: chỉ lặp lại reset sau khi đã hiểu lỗi và xác nhận vẫn là local DB.

### Schema đã tạo, nhưng không có Roles/Permissions/Skills

Đây là kết quả bình thường nếu **Seeder chưa được code hoặc chưa đăng ký**. `database update` không tự sinh dữ liệu nghiệp vụ từ Entity.

### Muốn khôi phục User/Profile/Chat đã bị xóa

Migration và Seeder **không thể khôi phục dữ liệu cũ không được seed**. Cần backup hợp lệ (ví dụ từ `pg_dump`/pgAdmin), và nên restore vào DB riêng trước khi làm bất cứ thao tác ghi đè nào.

---

## 8. Quick Reference — Reset Local (PowerShell 7)

> **DANGER:** Các lệnh này xóa toàn bộ database đang trỏ tới. Chỉ chạy với PostgreSQL local Development của riêng bạn.

```powershell
# 1. Dry-run: xem database sẽ bị xóa
dotnet ef database drop `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    --dry-run `
    -- --environment Development

# 2. Drop: thực sự xóa database
dotnet ef database drop `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    --force `
    -- --environment Development

# 3. Apply migrations; đồng thời gọi UseSeeding nếu đã cấu hình
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

**Kết luận:** Reset chỉ dành cho trường hợp **chủ động chấp nhận mất sạch dữ liệu local**. Cập nhật schema/dữ liệu hàng ngày thì dùng Migration và Seeder, không Drop.

**Tham khảo chính thức:** [EF Core CLI: `database drop` và `database update`](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) · [EF Core: Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding).
