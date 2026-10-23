# Tindy Profile Module — STEP 03: Avatar & Cover Upload + Private Media Read

> Ngôn ngữ: tiếng Việt; tên class, API, database field và Git commit giữ tiếng Anh.
> Cơ sở: `tindy-backend-platform(20261023-023055).zip`.
> **Không chạy migration hoặc database drop ở STEP 03.** Schema Full Media Assets V3 đã tồn tại từ STEP 01.

## 1. Phạm vi

Triển khai upload/replace/remove Avatar (5 MB), Cover (10 MB); ảnh JPEG, PNG, WebP được kiểm tra bằng **magic bytes + codec**, giới hạn decoded pixels (20 MP), crop trung tâm và re-encode sang WebP. Avatar 512×512; Cover 1500×500 (đề xuất kích thước xử lý được chọn trong STEP 03). Crop trên frontend nếu cần lựa chọn bố cục khác; backend vẫn áp dụng center crop.

Local Storage là provider Development. AWS S3 provider từ STEP 02 vẫn sẵn code nhưng **không yêu cầu bucket/IAM/credentials hoặc S3 tests ở bước này**.

`media_assets` chỉ lưu metadata; binary lưu ngoài PostgreSQL. Các `storage_key` không bao giờ xuất hiện trong response HTTP.

## 2. Thay đổi source

| Nhóm | Thay đổi |
| --- | --- |
| Application | `IProfileMediaService`, `ProfileMediaState`, `PrivateMediaRead`, `MediaRequestException` |
| Infrastructure | `ProfileImageProcessor` (SkiaSharp), `ProfileMediaService`, DI/HttpClient |
| API | `ProfileMediaController`, `PrivateMediaController`, `ProblemDetails` và per-user rate limits |
| OAuth | Link provider **không tự thay Avatar**; lưu URL provider vào `provider_avatar_url`; unlink provider đang được chọn chuyển về default |
| Profile | `MyProfileResponse` và `MemberProfileResponse` bổ sung `avatar_source`, `avatar_media_asset_id`, `cover_media_asset_id`; giữ `avatar_url` và `cover_image_url` cũ trong giai đoạn tương thích |
| Tests | `ProfileImageProcessorTests`, `ProfileMediaServiceIntegrationTests`, điều chỉnh unit test OAuth theo quy tắc Avatar mới |

**Package mới:** `SkiaSharp 3.119.2`, `SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.2` (để có thể chạy Linux sau này) và `Microsoft.Extensions.Http 10.0.8`. SkiaSharp có giấy phép MIT. Không dùng ImageSharp vì cần rà riêng license thương mại.

## 3. API Contract

Tất cả endpoint mới dùng JWT Bearer và `[Authorize]` mặc định (Active + Completed Onboarding). Chỉ owner được upload/replace/remove; member hợp lệ được đọc avatar/cover của profile Active + completed onboarding.

| Method | Endpoint | Payload | Thành công |
| --- | --- | --- | --- |
| POST | `/api/me/profile/avatar` | multipart `file` | 200 JSON |
| DELETE | `/api/me/profile/avatar` | — | 204 |
| PUT | `/api/me/profile/avatar/source` | JSON `{ "source":"oauth", "provider":"google" }` hoặc `{ "source":"default" }` | 200 JSON |
| POST | `/api/me/profile/cover-image` | multipart `file` | 200 JSON |
| DELETE | `/api/me/profile/cover-image` | — | 204 |
| GET | `/api/media/{media_asset_id}/content` | Bearer JWT | 200 binary `image/webp` |

Ví dụ response POST:

```json
{
  "success": true,
  "message": "Avatar updated successfully.",
  "data": {
    "avatar_source": "uploaded",
    "avatar_media_asset_id": "550e8400-e29b-41d4-a716-446655440001",
    "cover_media_asset_id": null
  }
}
```

`GET /api/media/.../content` chỉ phục vụ Avatar/Cover `active` **đang được UserProfile tham chiếu**; không mở quyền đọc PDF/Certificate ở STEP 03. Media ID cũ sau replace và media không được tham chiếu đều trả 404; anonymous hoặc pre-onboarding không có quyền. Kết quả binary có `Cache-Control: private, no-store` và `X-Content-Type-Options: nosniff`.

Frontend lấy ảnh bằng fetch mang JWT Bearer, rồi `URL.createObjectURL(blob)` và `URL.revokeObjectURL(...)` khi không dùng nữa. `<img src="/api/media/...">` trực tiếp **không gửi Authorization Bearer** nên không sử dụng kiểu này.

## 4. Xử lý replace và remove

1. Kiểm tra size, magic bytes, pixel dimensions và decode ảnh; luôn re-encode WebP.
2. Lưu file mới bằng `IFileStorageService` từ STEP 02 (object key do backend sinh).
3. PostgreSQL transaction, khóa hàng `users` (`FOR UPDATE`), kiểm tra profile và owner, thêm `MediaAsset status=active`, đổi Profile reference.
4. Asset cũ (nếu có) chuyển `pending_delete`, thu hồi quyền đọc ngay lập tức.
5. Nếu transaction thất bại, file mới được xóa best-effort; profile cũ không thay đổi. Nếu storage cleanup thất bại, orphan reconciliation bền vững nằm ở STEP 05.

DELETE không xóa file vật lý ngay trong transaction để tránh làm hỏng rollback. File không còn được đọc qua API sau khi Profile gỡ reference.

## 5. Quy tắc OAuth

- **Link Google/GitHub không được tự fill hoặc thay `user_profiles.avatar_url`.** URL do provider cung cấp được lưu riêng vào `external_auth_accounts.provider_avatar_url`.
- Người dùng chủ động gọi `PUT /api/me/profile/avatar/source` với provider `google`/`github`: backend chỉ cho phép URL HTTPS từ hostname tin cậy (`lh3.googleusercontent.com`, `avatars.githubusercontent.com`), cấm redirect, giới hạn file và decode/re-encode trước khi lưu private.
- Remove Avatar luôn trả về `default`, không tự động dùng OAuth.
- Unlink provider đang được chọn: chuyển `default` trong transaction, sau đó unlink; không unlink phương thức đăng nhập cuối cùng.
- Khi OAuth signup lần đầu, mặc định Tindy avatar (`avatar_source=default`) và lưu provider URL; muốn hiển thị avatar provider thì người dùng chọn sau onboarding. Đây là cách an toàn, vì URL public từ provider không bảo đảm chính sách members-only.
- Legacy URLs hiện vẫn được giữ để tương thích dữ liệu. Chuyển đổi toàn bộ legacy avatar/cover sang private storage là việc migration dữ liệu riêng; không coi legacy URL là private.

**Giới hạn còn lại:** URL cũ đã có của OAuth account trước STEP 03 có thể đang để NULL trong `provider_avatar_url`; đăng nhập lại bằng OAuth để refresh metadata rồi chọn source. Không tự import dữ liệu URL chưa xác minh.

## 6. Rate limiting và lỗi

- Avatar upload / chọn ảnh provider: 10 lần/giờ/user.
- Cover upload: 6 lần/giờ/user.
- Media read: 300 lần/phút/user.
- Số upload avatar/cover/OAuth image đồng thời: tối đa 2/user trên **một API instance**.
- Rate limiting đang lưu trong bộ nhớ instance. Khi scale nhiều API instance, phải chuyển sang distributed limiter để bảo đảm giới hạn toàn cụm.
- `400`: ảnh rỗng/không decode/dimensions; `413`: quá size; `415`: không phải JPEG/PNG/WebP; `404`: media không active hoặc không tham chiếu; `409`: provider avatar không khả dụng; `429`: rate/concurrency; `401/403`: auth.
- `ProblemDetails` bao gồm `error_code` và `traceId`.

## 7. Áp dụng Git Patch

**Trên PowerShell 7, từ root Backend:**

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
git status
git apply --check .\Tindy_Profile_STEP03_Avatar_Cover.patch
git apply .\Tindy_Profile_STEP03_Avatar_Cover.patch
git status
git -c core.whitespace=cr-at-eol diff --check
```

Patch được xây trên đúng ZIP ngày 23/10/2026. Nếu repository đã thay đổi sau ZIP, `git apply --check` có thể báo conflict; **không dùng `--force` và không tự ghi đè code thành viên khác**.

Không commit file `.patch` và không đưa media từ `D:\FPT\Semester 6\OJT\aws\TindyData\private-media` lên Git.

## 8. Build & Tests

```powershell
dotnet restore
dotnet build .\Tindy.slnx

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~ProfileImageProcessorTests" `
    --verbosity normal

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~ExternalAuthServiceTests" `
    --verbosity normal
```

**PostgreSQL Integration Tests (Docker Desktop + Testcontainers):**

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~ProfileMediaServiceIntegrationTests" `
    --verbosity normal
```

Sau đó chạy đầy đủ `dotnet test .\Tindy.slnx`. Không coi bước hoàn thành khi chưa PASS; environment tạo patch chưa có dotnet SDK và Docker nên chưa có log build/xUnit thực tế.

## 9. Test API với Postman

1. Chạy backend bằng `dotnet run --project .\src\Tindy.Api --environment Development`.
2. Dùng JWT của một user đang **active** và đã `is_onboarding_completed=true`.
3. POST `/api/me/profile/avatar`, tab `Body > form-data`, field `file` chọn một `.png` dung lượng nhỏ.
4. Xem `data.avatar_media_asset_id`; GET `/api/me/profile` kiểm tra `avatar_source=uploaded`.
5. GET `/api/media/{id}/content` với JWT: nhận `image/webp`.
6. Upload Avatar lần nữa: id thay đổi, id cũ GET trả 404 (nhưng file còn đợi STEP 05 cleanup).
7. DELETE Avatar: GET Profile trả `avatar_source=default`, id null.
8. Thử file `.txt` => 415, ảnh hỏng => 400, file quá 5MB => 413.
9. Thử không JWT => 401, chưa onboarding => 403, user suspended => 403.
10. Cover tương tự, tối đa 10MB. Member hợp lệ có thể GET Avatar/Cover, không thể POST/DELETE thay user khác.
11. Link Google/GitHub: avatar hiện tại **không đổi**. Chọn OAuth source chủ động mới thay; unlink provider đang được chọn trả default.

> HTTPS: vì frontend dùng Vite và backend có `UseHttpsRedirection`, gọi URL/port theo launchSettings thực tế. Không dùng `http://` trong Production.

## 10. Điều kiện nghiệm thu

- [ ] `dotnet restore`, `dotnet build` PASS không errors.
- [ ] `ProfileImageProcessorTests` và OAuth tests PASS.
- [ ] PostgreSQL integration tests PASS trên môi trường Docker thử nghiệm.
- [ ] Avatar/Cover upload, replace, delete, private GET qua JWT hoạt động.
- [ ] 401/403/404, quota per-file 413, 415 và 429 hoạt động đúng.
- [ ] Link OAuth không đổi Avatar; unlink provider đang chọn về default.
- [ ] Profile DTO giữ thông tin cũ, đồng thời trả Media Asset IDs.
- [ ] Không có migration mới ở STEP 03; Database không bị drop.

## 11. Commit đề xuất

```powershell
git add src/Tindy.Application/ src/Tindy.Infrastructure/ src/Tindy.Api/ tests/Tindy.Tests/ docs/guide/PROFILE_MEDIA_STEP03_AVATAR_COVER_HANDOFF_VI.md
git -c core.whitespace=cr-at-eol diff --cached --check
git commit -m "feat(profile): add private avatar and cover media APIs"
```

**Chưa triển khai ở bước này:** Certificate Attachments (STEP 04), ClamAV/GuardDuty, persistent cleanup worker (STEP 05), Frontend Integration đầy đủ (STEP 06), S3 thật.
