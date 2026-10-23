# Tindy Profile Module — STEP 01: Database Schema & Migration

> **Phạm vi:** Chuẩn bị Entity, EF Core Configuration, quan hệ Database, ràng buộc và kiểm thử schema. **Chưa triển khai API upload, File Storage, PDF Scanner hoặc Frontend.**
>
> **Source đối chiếu:** `tindy-backend-platform(20261022-065605).zip` (.NET 10, EF Core, PostgreSQL).
>
> **Trạng thái bàn giao:** Đã chuẩn bị source C# và test; **chưa sinh EF migration và chưa chạy build/test thực tế** vì môi trường chuẩn bị không có `dotnet` và PostgreSQL. Vui lòng hoàn thành phần kiểm chứng bên dưới trước khi commit.

## 1. Mục tiêu STEP 01

Thiết lập cấu trúc dữ liệu **Full Media Assets V3**, có khả năng mở rộng cho Avatar, Cover Image và Certificate Attachments mà không phá vỡ dữ liệu Profile/OAuth/Certificate đang tồn tại.

Ở STEP này:

- Giữ nguyên các field cũ `user_profiles.avatar_url` và `user_profiles.cover_image_url`.
- Không tự động tải ảnh OAuth, thay Avatar hoặc chỉnh sửa luồng đăng nhập.
- Không upload hay xóa file vật lý.
- Không thực thi quota, giới hạn ba Attachment hoặc antivirus scanner. Chỉ chuẩn bị schema và mô tả quy tắc cho STEP sau.
- Không chạy `database drop`, không reset dữ liệu hiện tại.

## 2. Các file đã chuẩn bị

| Khu vực | Thay đổi |
|---|---|
| `src/Tindy.Application/Entities/` | Tạo `MediaAsset.cs`, `UserCertificateAttachment.cs`; cập nhật navigation cho `User`, `UserCertificate`, `UserProfile`; thêm `ExternalAuthAccount.ProviderAvatarUrl` |
| `src/Tindy.Infrastructure/Persistence/Configurations/` | Tạo `MediaAssetConfiguration.cs`, `UserCertificateAttachmentConfiguration.cs`; bổ sung cấu hình Profile/OAuth/Certificate |
| `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | Thêm `DbSet<MediaAsset>` và `DbSet<UserCertificateAttachment>` |
| `tests/Tindy.Tests/Profile/` | Tạo `MediaSchemaModelTests.cs` kiểm tra EF model, quan hệ và constraints |
| `tests/Tindy.Tests/Integration/` | Tạo `MediaSchemaMigrationTests.cs` kiểm tra migration và FK trên PostgreSQL Testcontainers |
| `docs/guide/PROFILE_MEDIA_STEP01_HANDOFF.md` | Hướng dẫn tiếng Việt này |

**Chú ý:** ZIP và patch đi kèm chứa **15 file mới/chỉnh sửa**, không chứa migration do EF sinh. Tên migration sẽ được tạo bằng CLI ở mục 6.

## 3. Database & Constraints cần review

### 3.1. Bảng `media_assets` (Full V3)

- **Identity:** `media_asset_id`, `owner_user_id`, `purpose`.
- **Storage:** `storage_provider`, `storage_container`, `storage_key`, `object_version_id`, `object_etag`.
- **File:** `original_file_name`, `content_type`, `file_size_bytes`, `reserved_bytes`, `width`, `height`.
- **Trạng thái:** `status`, `scan_status`, `scan_provider`, `scan_completed_at`, `upload_expires_at`, `activated_at`, `delete_requested_at`.
- **Cleanup và audit:** `cleanup_attempts`, `next_cleanup_at`, `last_cleanup_error`, `created_at`, `updated_at`.

Các ràng buộc quan trọng:

| Nhóm | Quy tắc |
|---|---|
| `purpose` | Chỉ `avatar`, `cover`, `certificate` |
| `storage_provider` | Chỉ `local`, `s3` |
| `status` | `pending_upload`, `processing`, `active`, `rejected`, `pending_delete` |
| `scan_status` | `pending`, `clean`, `threat_detected`, `unsupported`, `access_denied`, `failed`, `not_required` |
| Kích thước | `file_size_bytes > 0` nếu đã có giá trị; `reserved_bytes >= 0`; `width` và `height` dương nếu đã có |
| File `active` | Phải có `file_size_bytes`, `content_type`, `reserved_bytes = 0`; nếu PDF phải có `scan_status = clean` |
| Storage duy nhất | Unique `(storage_provider, storage_container, storage_key)` |
| Ownership | `owner_user_id` tham chiếu `users`, không tự cascade xóa Media |

Indexes chỉ tập trung vào owner/purpose, file đang xử lý, file chờ cleanup và upload hết hạn. Không index mọi metadata không cần thiết.

### 3.2. Bảng `user_profiles`

Thêm bốn cột nullable:

- `avatar_source`: `default`, `uploaded`, `oauth`; **NULL tạm thời để giữ dữ liệu cũ chưa phân loại**.
- `avatar_asset_id`: tham chiếu Avatar trong `media_assets`.
- `avatar_oauth_provider`: dùng `Google`/`GitHub` (đúng chữ hoa chữ thường đang lưu trong source).
- `cover_asset_id`: tham chiếu Cover trong `media_assets`.

Sử dụng **composite FK** `(avatar_asset_id, user_id)` và `(cover_asset_id, user_id)` để đảm bảo ảnh được tham chiếu thuộc đúng chủ sở hữu.

Ảnh OAuth đang được chọn sử dụng quan hệ `(user_id, avatar_oauth_provider)` đến `(user_id, provider)` của `external_auth_accounts`. Quy tắc CHECK bảo đảm tổ hợp `avatar_source`, `avatar_asset_id`, `avatar_oauth_provider` nhất quán.

**Không xóa hoặc tự phân loại `avatar_url`/`cover_image_url` cũ.** NULL ở `avatar_source` trong STEP này mang nghĩa *legacy/chưa phân loại*, không tự đồng nhất với `default`.

### 3.3. Bảng `user_certificate_attachments`

Các trường: `attachment_id`, `certificate_id`, `owner_user_id`, `media_asset_id`, `visibility`, `created_at`, `updated_at`.

- `visibility` mặc định `owner_only`, giá trị hợp lệ: `owner_only`, `members`.
- `media_asset_id` là UNIQUE, tránh dùng cùng một file cho nhiều Attachment.
- Composite FK `(certificate_id, owner_user_id)` → `user_certificates(certificate_id, user_id)`.
- Composite FK `(media_asset_id, owner_user_id)` → `media_assets(media_asset_id, owner_user_id)`.
- Không tự cascade xóa metadata Media từ Certificate/Attachment.

**Giới hạn của CHECK:** PostgreSQL CHECK không tự kiểm tra quan hệ chéo rằng asset thật sự có `purpose = certificate` hay `status = active`. Giới hạn **3 Attachments/Certificate**, **200 MB/User** và quyền đọc file phải được thực thi trong các STEP nghiệp vụ sau bằng transaction/authorization tương ứng.

### 3.4. OAuth và khóa duy nhất

Bổ sung `external_auth_accounts.provider_avatar_url` (nullable, tối đa 1000 ký tự). **Không sửa hành vi đăng nhập/link/unlink OAuth ở STEP 01**.

Composite FK OAuth sử dụng một alternate key `(user_id, provider)`; source đã có unique index cùng cặp cột này, nên EF Core có thể sinh thêm một UNIQUE structure tương đương. **Cần review migration được sinh và cân nhắc trùng index trước khi đưa lên database dùng chung.**

## 4. Quy tắc `reserved_bytes` và Quota

Quota cho **Certificate Attachments** là **200.000.000 bytes / user (200 MB hệ thập phân)**. Avatar/Cover không tính vào quota này. Một Certificate tối đa **3 attachments**, tính cả Attachment đang upload/processing để không vượt giới hạn khi có nhiều request đồng thời.

| Trạng thái Media | Dung lượng tính vào quota |
|---|---|
| `pending_upload`, `processing` | Ưu tiên `reserved_bytes` nếu > 0; nếu không thì tính `file_size_bytes` đã biết |
| `active` | `file_size_bytes` |
| `rejected`, `pending_delete` | 0 bytes quota logic; file vật lý vẫn có thể chờ cleanup |

**Không bao giờ cộng `reserved_bytes + file_size_bytes` cho cùng một file.** Ví dụ file PDF 8 MB đang chờ quét chỉ giữ quota 8 MB, không phải 16 MB. Khi file `active`, giải phóng giữ chỗ bằng cách đưa `reserved_bytes` về 0 và xác nhận dung lượng thật.

Kiểm tra quota và số lượng attachment ở STEP 04 phải sử dụng PostgreSQL transaction và cơ chế khóa phù hợp để chặn hai request đồng thời cùng vượt hạn mức. **STEP 01 chỉ lưu metadata và tài liệu quy tắc.**

## 5. Áp dụng Git Patch (PowerShell 7 — Windows)

**Cách khuyến nghị:** Dùng file `Tindy_Profile_STEP01_VI.patch` ở phiên bản bàn giao này. Patch áp dụng từ **thư mục gốc repository** `tindy-backend-platform` (nơi có `Tindy.slnx`).

### 5.1. Kiểm tra branch và local changes

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"

git branch --show-current
git status --short
```

Nên commit/stash các thay đổi cá nhân trước khi áp dụng để dễ review và tránh patch conflict. **Không chạy `git reset --hard`.**

### 5.2. Kiểm tra và áp dụng Patch

Giả sử file patch vừa tải nằm trong `Downloads`:

```powershell
$patch = Join-Path $HOME "Downloads\Tindy_Profile_STEP01_VI.patch"

# Kiểm tra patch có áp dụng được không (chưa sửa bất kỳ file nào)
git apply --check $patch

# Chỉ chạy nếu lệnh kiểm tra không báo lỗi
git apply $patch

# Xem những file vừa thay đổi
git status --short
git diff --stat
git diff --check
```

Nếu `git apply --check` báo lỗi, **dừng lại**, chưa chạy `git apply`; kiểm tra đúng source ZIP ngày 22/10, branch và các thay đổi cục bộ. Nếu bạn đã áp dụng patch STEP 01 cũ, **không áp dụng lần hai**; chỉ thay nội dung file `docs/guide/PROFILE_MEDIA_STEP01_HANDOFF.md` bằng bản tiếng Việt rồi tiếp tục kiểm thử.

Nếu cần **hoàn tác đúng bản patch vừa áp dụng** khi chưa sửa thêm các file liên quan:

```powershell
# Kiểm tra khả năng hoàn tác trước
git apply --reverse --check $patch

# Chỉ chạy nếu kiểm tra thành công
git apply --reverse $patch
```

Không dùng lệnh hoàn tác trên nếu các file đã được tiếp tục chỉnh sửa hoặc đã có migration mới: hãy review thay đổi Git trước.

## 6. Tạo EF Migration theo quy ước của Tindy

Tuân theo `docs/guide/MIGRATION_NAMING_CONVENTION.md`. **Không đánh số migration thủ công, không tự sửa timestamp.**

Đứng ở **repo root** sau khi áp dụng patch:

```powershell
dotnet restore

dotnet ef migrations add Profile_CreateMediaAssetsAndAttachments `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

EF Core sẽ sinh `*.cs`, `*.Designer.cs` và cập nhật `TindyDbContextModelSnapshot.cs` trong `src/Tindy.Infrastructure/Persistence/Migrations/`.

**Review kỹ trước khi cập nhật Database:**

```powershell
git status --short
git diff -- src/Tindy.Infrastructure/Persistence/Migrations
```

Vì migration mới là file **untracked**, `git diff` có thể chưa hiển thị toàn bộ. Mở trực tiếp file migration mới để kiểm tra `Up()`/`Down()`, sau đó kiểm tra cả ModelSnapshot.

Migration hợp lệ phải:

- Tạo `media_assets` và `user_certificate_attachments`.
- Thêm bốn cột mới cho `user_profiles` và `provider_avatar_url` cho OAuth.
- Tạo đúng Composite FK, alternate key, CHECK, indexes và delete behaviors.
- **Không drop bảng** `users`, `user_profiles`, `user_certificates` hay cột `avatar_url`/`cover_image_url` cũ.
- `Down()` chỉ hoàn tác những thay đổi STEP 01; rollback trên Database chứa media mới sẽ làm mất metadata đó — chỉ thử rollback ở DB test.

## 7. Build và chạy Tests

### 7.1. Build solution

```powershell
dotnet build .\Tindy.slnx
```

### 7.2. EF Model Tests (không cần Docker)

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~MediaSchemaModelTests"
```

### 7.3. PostgreSQL Migration Tests (cần Docker Desktop)

Bật Docker Desktop trước khi chạy; test dùng **PostgreSQL Testcontainers tách biệt** với DB Development.

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~MediaSchemaMigrationTests"
```

Test nhằm kiểm chứng Migration Up/Down, giữ dữ liệu User/Profile/Certificate cũ và lỗi FK cross-owner (PostgreSQL SQLSTATE `23503`). Nếu test fail, **chưa commit**; gửi log và migration vừa sinh để kiểm tra.

### 7.4. Chỉ áp dụng vào DB Development sau khi kiểm tra xong

Đảm bảo connection string trỏ đúng DB Development riêng được phép chỉnh sửa:

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

**Tuyệt đối không dùng `dotnet ef database drop`** cho STEP này. Nên sao lưu DB có dữ liệu quan trọng trước khi cập nhật, kể cả ở Development.

## 8. Checklist nghiệm thu trước khi commit

- [ ] `git apply --check` và `git apply` thành công trên source đúng phiên bản.
- [ ] `dotnet build .\Tindy.slnx` thành công.
- [ ] EF Model Tests thành công.
- [ ] PostgreSQL Migration Tests thành công (Docker Desktop).
- [ ] Migration `Up()`/`Down()` và ModelSnapshot được review đầy đủ.
- [ ] Dữ liệu `users`, `user_profiles`, `user_certificates` cũ được giữ nguyên.
- [ ] `avatar_url`, `cover_image_url` cũ không bị xóa hay tự thay đổi.
- [ ] Composite FK từ chối media/certificate của owner khác.
- [ ] Không phát sinh API upload, media read hoặc thay đổi OAuth flow ở STEP này.

### Commit sau khi tất cả đã pass

```powershell
git add src/Tindy.Application/Entities/ `
    src/Tindy.Infrastructure/Persistence/ `
    tests/Tindy.Tests/ `
    docs/guide/PROFILE_MEDIA_STEP01_HANDOFF.md

git diff --cached --check
git diff --cached --stat

git commit -m "feat(profile): add media assets and certificate attachment schema"
```

## 9. Bàn giao sang STEP 02

Chỉ chuyển sang **STEP 02 – File Storage Foundation** sau khi xác minh build, migration và tests. STEP 02 sẽ triển khai Local/S3 Storage; **chưa xem STEP 01 là hoàn thành chỉ vì patch đã áp dụng**.
