# DATABASE_SETUP — Tindy Backend

> Hướng dẫn thiết lập PostgreSQL và đồng bộ database bằng Entity Framework Core (Code First) cho các thành viên của team Tindy.
>
> **Stack:** .NET 10 · EF Core 10 · PostgreSQL · PowerShell 7

## 1. Mục tiêu

Sau khi clone repository, mỗi thành viên có thể:

- Kết nối ứng dụng với **PostgreSQL local** của riêng mình.
- Tạo/cập nhật cấu trúc database từ các **EF Core Migrations đã được commit**.
- Đồng bộ schema khi có migration mới trên GitHub mà không phải tự viết SQL tạo bảng.
- Phân biệt **Migration (schema)** và **Seeding (dữ liệu khởi tạo)**.

> **Quan trọng:** `dotnet ef database update` đồng bộ **cấu trúc bảng** và dữ liệu được quy định trong migration/seeding đã triển khai; lệnh này **không tự sao chép dữ liệu** từ PostgreSQL trên máy người khác.

---

## 2. Cấu trúc project liên quan

Chạy các lệnh trong tài liệu này tại **thư mục gốc repository**, nơi chứa file `Tindy.slnx`.

```text
<repository-root>/
├── Tindy.slnx
├── src/
│   ├── Tindy.Api/                    # Startup project; đọc config và User Secrets
│   ├── Tindy.Application/
│   └── Tindy.Infrastructure/         # TindyDbContext, EF configurations, migrations
└── DATABASE_SETUP.md
```

Các tham số dùng chung:

| Tham số | Giá trị |
|---|---|
| Target project | `./src/Tindy.Infrastructure/Tindy.Infrastructure.csproj` |
| Startup project | `./src/Tindy.Api/Tindy.Api.csproj` |
| DbContext | `TindyDbContext` |
| Connection string key | `ConnectionStrings:DefaultConnection` |
| Môi trường local | `Development` |

**Điều kiện:** Ứng dụng phải đăng ký `TindyDbContext` bằng đúng key `DefaultConnection`. Nếu tên key trong code thay đổi, hãy cập nhật lệnh User Secrets tương ứng.

---

## 3. Setup lần đầu — dành cho thành viên mới

### STEP 1 — Cài các công cụ cần thiết

- **.NET SDK 10**.
- **PostgreSQL** (đang chạy và truy cập được từ máy local).
- **Git**.
- **EF Core CLI (`dotnet-ef`)** tương thích với EF Core 10 của repository.

Kiểm tra:

```powershell
dotnet --version
psql --version
git --version
dotnet ef --version
```

`psql` là công cụ tùy chọn nếu bạn dùng pgAdmin hoặc một PostgreSQL client khác. Nếu `dotnet ef` chưa có:

```powershell
dotnet tool install --global dotnet-ef --version 10.*
```

Nếu đã cài nhưng cần cập nhật:

```powershell
dotnet tool update --global dotnet-ef --version 10.*
```

> Nếu repository có tool manifest (`.config/dotnet-tools.json`), ưu tiên `dotnet tool restore` và dùng phiên bản do team khóa trong manifest, thay vì cài tool global. Đồng bộ phiên bản EF CLI với các package EF Core của dự án.

### STEP 2 — Clone repository

```powershell
git clone https://github.com/FCAJ-OJT-Fall26/tindy-fcaj-platform.git
cd tindy-fcaj-platform
```

Nếu đã clone sẵn, chuyển về nhánh làm việc và cập nhật code theo Git workflow của team. Trước khi pull/merge, bảo đảm các thay đổi local đã được commit hoặc stash phù hợp.

### STEP 3 — Restore dependencies

```powershell
dotnet restore
```

Có thể kiểm tra source build được:

```powershell
dotnet build
```

### STEP 4 — Chuẩn bị PostgreSQL local

1. Khởi động PostgreSQL.
2. Đảm bảo có tài khoản PostgreSQL được phép kết nối và tạo/cập nhật database cần thiết.
3. Sử dụng các thông số local, ví dụ:

| Cấu hình | Ví dụ |
|---|---|
| Host | `localhost` |
| Port | `5432` |
| Database | `tindy` |
| Username | `postgres` |
| Password | Mật khẩu PostgreSQL trên **máy của bạn** |

Có thể tạo database rỗng trước bằng pgAdmin hoặc SQL:

```sql
CREATE DATABASE tindy;
```

> Nếu database chưa tồn tại, EF Core/Npgsql *có thể* tạo database khi chạy migration, miễn tài khoản kết nối có quyền phù hợp. Tạo database rỗng trước là lựa chọn dễ kiểm soát hơn khi dùng tài khoản có quyền hạn chế.

**Không tự tạo các bảng nghiệp vụ bằng SQL**; để EF Core Migrations quản lý schema.

### STEP 5 — Cấu hình User Secrets

Tại thư mục gốc repository, chạy PowerShell:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
    "Host=localhost;Port=5432;Database=tindy;Username=postgres;Password=YOUR_LOCAL_PASSWORD" `
    --project .\src\Tindy.Api\Tindy.Api.csproj
```

Thay `YOUR_LOCAL_PASSWORD` bằng mật khẩu máy bạn. Kiểm tra key đã tồn tại:

```powershell
dotnet user-secrets list --project .\src\Tindy.Api\Tindy.Api.csproj
```

**Lưu ý bảo mật:**

- User Secrets dùng cho **local development**, không phải kho lưu trữ bí mật production.
- Không commit mật khẩu hoặc connection string có mật khẩu vào GitHub, `appsettings.json`, file `.md`, hay ảnh chụp terminal.
- Nếu `dotnet user-secrets` báo project chưa có `UserSecretsId`, cần kiểm tra cấu hình project `Tindy.Api` với người phụ trách. Chỉ chạy `dotnet user-secrets init --project .\src\Tindy.Api\Tindy.Api.csproj` khi thực sự chưa khởi tạo và commit thay đổi `UserSecretsId` vào repo nếu team thống nhất.

### STEP 6 — Apply các Migration đã có

**Đây là bước tạo các bảng từ Code First.**

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Lệnh này sẽ:

1. Build project (nếu cần).
2. Kết nối đến PostgreSQL bằng cấu hình `Development`.
3. Kiểm tra bảng lịch sử `__EFMigrationsHistory`.
4. Áp dụng **những migration chưa chạy**, theo đúng thứ tự.

Nếu thành công, database local của bạn sẽ có các bảng được định nghĩa trong những migration hiện có. Tên/số lượng bảng phụ thuộc migration thực tế được commit, không phụ thuộc danh sách ví dụ trong tài liệu.

### STEP 7 — Kiểm tra kết quả

Trong pgAdmin, refresh database `tindy` → `Schemas` → `public` → `Tables`.

Hoặc chạy:

```sql
-- Kiểm tra các migration đã được áp dụng
SELECT * FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

-- Liệt kê các bảng trong schema public
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE'
ORDER BY table_name;
```

Kiểm tra migration có trong source:

```powershell
dotnet ef migrations list `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

> `migrations list` liệt kê migration; `database update` là lệnh thực sự cập nhật database.

---

## 4. Workflow hằng ngày — khi team có Migration mới

Ví dụ người phụ trách thay đổi Entity/Configuration và đã commit migration mới lên nhánh được merge vào `main`.

**Các thành viên còn lại:**

```powershell
# 1. Cập nhật code theo workflow Git của team
git pull origin main

# 2. Restore nếu dependencies có thay đổi
dotnet restore

# 3. Đồng bộ schema local
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

> Ví dụ trên áp dụng khi đang làm việc ở nhánh `main`. Nếu đang ở feature branch, hãy cập nhật/merge/rebase `main` theo quy định của team trước khi chạy migration. **Không pull `main` trực tiếp một cách máy móc** nếu đang có thay đổi chưa lưu hoặc xung đột Git.

**Không cần chạy `dotnet ef migrations add` khi clone/pull**. Lệnh đó chỉ dành cho người thực sự tạo một thay đổi schema mới.

Chạy `database update` lại nhiều lần thường an toàn: EF Core chỉ áp dụng migration chưa có trong lịch sử. Dù vậy, vẫn cần review migration phá hủy dữ liệu (drop table/column, chuyển kiểu dữ liệu...) trước khi chạy trên database có dữ liệu quan trọng.

---

## 5. Workflow dành cho người thay đổi schema

Khi task **thực sự yêu cầu sửa database**:

1. Sửa Entity và EF Core Entity Configurations.
2. Build để kiểm tra lỗi.
3. Tạo **một migration mới với tên có ý nghĩa**.
4. Review mã migration (`Up`, `Down`, snapshot) để tránh thay đổi ngoài dự kiến.
5. Apply migration lên database local và test.
6. Commit cả Entity, Configurations, file migration và model snapshot.
7. Push feature branch, tạo Pull Request để leader review/merge.

Ví dụ tạo migration:

```powershell
dotnet ef migrations add UpdateUserProfileSchema `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Sau đó chạy `database update` như STEP 6.

Ví dụ commit:

```powershell
git add src/Tindy.Application src/Tindy.Infrastructure
git commit -m "feat(db): update user profile schema"
git push -u origin feature/update-user-profile-schema
```

> `git add` chỉ là ví dụ. Hãy review `git status` và `git diff --staged` trước khi commit để không đưa nhầm file vào PR. Nếu Entity nằm ở project khác hoặc có thay đổi cần thiết ở API, stage thêm đúng đường dẫn đó.

**Quy định:**

- Chỉ tạo migration khi schema thay đổi.
- Commit **toàn bộ thư mục/file migration có liên quan**, bao gồm `*ModelSnapshot.cs`.
- Không tạo lại migration trùng từ cùng một thay đổi schema đã được người khác commit.
- Nếu hai nhánh cùng sửa schema, người phụ trách phải xử lý xung đột và xác nhận migration history/snapshot sau khi merge.
- Không sửa/xóa migration **đã áp dụng trên database dùng chung hoặc môi trường khác** nếu chưa có kế hoạch phối hợp. Thường tạo migration tiếp theo để điều chỉnh.
- Không chạy `database update` lên production chỉ vì vừa pull code; production phải có quy trình deploy, backup và review migration riêng.

---

## 6. Migration khác gì Seeding?

| Nội dung | Migration | Seeding |
|---|---|---|
| Mục đích | Tạo/sửa schema | Thêm dữ liệu khởi tạo |
| Ví dụ | Tạo `users`, `roles`, FK, index | Thêm role `Admin`, `User`, danh mục skill |
| Khi clone | Chạy `database update` | Chỉ có dữ liệu nếu project **đã triển khai** seeding tương ứng |
| Dữ liệu người dùng thật | Không tự đồng bộ | Không nên đưa vào seed mặc định |

**Đối với Tindy:** Có thể cân nhắc seed các bảng dữ liệu dùng chung như `roles`, `permissions`, `skills` khi đã chốt danh sách chuẩn. Nên triển khai seed theo cách **idempotent** (chạy lại không tạo bản ghi trùng lặp).

EF Core hỗ trợ các cơ chế seed như `UseSeeding` / `UseAsyncSeeding`; team cũng có thể thiết kế một quy trình seed riêng. **Tài liệu hiện tại không giả định Tindy đã có Seeder**, nên không cung cấp lệnh seed chưa tồn tại trong code.

Nếu cần **dữ liệu mẫu cho giao diện/API**, nên có một bộ development seed tách biệt, không bao gồm dữ liệu nhạy cảm hoặc dữ liệu thật của người dùng.

---

## 7. FAQ và xử lý lỗi thường gặp

### `dotnet ef` không được nhận diện

Kiểm tra `dotnet ef --version`, sau đó cài/restore tool theo STEP 1. Kiểm tra `PATH` và mở lại terminal nếu vừa cài công cụ global.

### `Connection refused` / `Failed to connect to 127.0.0.1:5432`

PostgreSQL chưa chạy, sai host/port, hoặc server không lắng nghe địa chỉ này. Kiểm tra dịch vụ PostgreSQL và cấu hình connection string.

### `password authentication failed for user "postgres"`

Sai username/password. Kiểm tra User Secrets trên **máy hiện tại**, không lấy password của người khác.

### `database "tindy" does not exist` / lỗi quyền tạo database

Tạo database rỗng trước bằng tài khoản có quyền, hoặc cấp đúng quyền cho user kết nối. Sau đó chạy lại `database update`.

### `No migrations were found`

Kiểm tra migration files đã được commit/pull hay chưa, đúng target project và đúng DbContext. Nếu repository chưa có migration đầu tiên, người phụ trách schema phải tạo và commit migration trước.

### `Build failed`

Chạy `dotnet restore` và `dotnet build`, sửa lỗi build trước khi áp dụng migration. Kiểm tra .NET SDK và phiên bản package/tool có tương thích không.

### `relation ... already exists` / `table already exists`

Database có thể đã bị tạo bảng thủ công hoặc lịch sử migration không khớp. **Không tự ý xóa `__EFMigrationsHistory`** hay drop bảng nếu có dữ liệu cần giữ. Báo người phụ trách database để so sánh schema với lịch sử migration.

### `database update` xong nhưng bảng không có dữ liệu

Điều này là bình thường nếu mới có migration mà chưa có seed. Nhờ team triển khai/seeding dữ liệu cần thiết; `database update` không copy record từ máy người tạo migration.

### Muốn làm lại database local từ đầu?

Chỉ thực hiện nếu **được phép mất toàn bộ dữ liệu local**. Backup nếu cần. Sau khi tạo một database mới/rỗng, chạy lại `database update` để tái tạo schema. Không làm thao tác này trên database dùng chung hoặc production.

---

## 8. Checklist dành cho thành viên mới

- [ ] Đã clone/pull đúng repository, branch.
- [ ] Đã cài .NET 10 và PostgreSQL.
- [ ] `dotnet restore` và `dotnet build` thành công.
- [ ] PostgreSQL local đang chạy.
- [ ] Đã cấu hình `ConnectionStrings:DefaultConnection` trong User Secrets của `Tindy.Api`.
- [ ] Đã chạy `dotnet ef database update` thành công.
- [ ] Đã thấy các bảng và `__EFMigrationsHistory` trong PostgreSQL.
- [ ] Đã hiểu dữ liệu seed chỉ tồn tại nếu dự án đã triển khai và chạy quy trình seed.

---

## 9. Quick commands — PowerShell

```powershell
# Restore
dotnet restore

# Build
dotnet build

# Xem các migration
dotnet ef migrations list `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development

# Apply các migration chưa chạy
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

**Kết luận:** Thành viên mới **không cần nhận file SQL từ người tạo database**. Chỉ cần source code có đầy đủ EF Core migrations, PostgreSQL local và cấu hình kết nối chính xác. Migration đồng bộ **schema**; còn dữ liệu chung cần có **seeding** được team triển khai và quản lý riêng.
