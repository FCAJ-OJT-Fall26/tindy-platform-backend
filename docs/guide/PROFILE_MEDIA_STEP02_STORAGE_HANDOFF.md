# Tindy Profile Module – STEP 02: File Storage Foundation

> Tài liệu bàn giao kỹ thuật (tiếng Việt). Phạm vi: nền tảng lưu trữ file nội bộ, **chưa mở API upload/download**, chưa thay đổi database.

## 1. Kết quả của STEP 02

- `IFileStorageService` được định nghĩa tại Application; **Application không phụ thuộc AWS SDK**.
- Local Storage ghi file ngoài application content root và web root; key sinh bởi Backend.
- S3 Storage lưu object vào bucket private; AWS SDK sử dụng default credential chain (role/profile/environment), **không hardcode credentials**.
- Cả hai provider có `SaveAsync`, `OpenReadAsync`, `DeleteAsync` và thống nhất kiểu kết quả cho `media_assets` V3.
- File không chứa binary trong PostgreSQL; STEP 03/04 mới thêm nghiệp vụ validate ảnh/PDF và API có JWT.

## 2. Danh sách file thay đổi

| File/thư mục | Vai trò |
|---|---|
| `src/Tindy.Application/Interfaces/Services/IFileStorageService.cs` | Contract lưu, đọc, xóa |
| `src/Tindy.Application/Models/Storage/` | Request, enum loại media, response metadata, read handle |
| `src/Tindy.Infrastructure/Storage/LocalFileStorageService.cs` | Local private storage |
| `src/Tindy.Infrastructure/Storage/S3FileStorageService.cs` | Amazon S3 private storage |
| `src/Tindy.Infrastructure/Storage/StorageObjectKeys.cs` | Key do Backend tạo, chặn path traversal |
| `src/Tindy.Infrastructure/Storage/BoundedStorageContent.cs` | Đọc stream giới hạn dung lượng |
| `src/Tindy.Infrastructure/Storage/MediaStorageServiceRegistration.cs` | Chọn provider theo config |
| `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Đăng ký storage trong DI |
| `src/Tindy.Infrastructure/Tindy.Infrastructure.csproj` | Thêm AWSSDK.S3 và các package liên quan |
| `src/Tindy.Api/appsettings*.json` | Cấu trúc cấu hình provider (không có secrets) |
| `tests/Tindy.Tests/Media/LocalFileStorageTests.cs` | Test Local lưu/đọc/xóa và các lỗi |

**Không có entity, migration hoặc `dotnet ef database update` trong STEP 02.**

## 3. Áp dụng Git Patch (PowerShell)

Đặt file `Tindy_Profile_STEP02_FileStorage.patch` tại thư mục gốc của Backend:

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"

git status
git apply --check .\Tindy_Profile_STEP02_FileStorage.patch
git apply .\Tindy_Profile_STEP02_FileStorage.patch

git status
git -c core.whitespace=cr-at-eol diff --check
```

**Điều kiện:** source đang ở phiên bản tương ứng với ZIP `tindy-backend-platform(20261023-020135).zip` và các thay đổi hiện có không xung đột patch. Nếu `git apply --check` báo lỗi, không tiếp tục `git apply`: gửi log để kiểm tra. Không áp dụng patch hai lần. File `.patch` nằm ở root không cần commit.

## 4. Cấu hình Local – Development

Không lưu file vào `wwwroot`, thư mục source hoặc `bin`.

Gợi ý thư mục riêng trên Windows: `D:\TindyData\private-media` (cần quyền ghi). Ví dụ trong `src/Tindy.Api/appsettings.Development.json` (file này đã được Git ignore):

```json
{
  "MediaStorage": {
    "Provider": "local",
    "Local": {
      "RootPath": "D:/TindyData/private-media"
    }
  }
}
```

**Không thay toàn bộ nội dung appsettings Development hiện tại:** chỉ thêm phần `MediaStorage` vào JSON đang có để giữ cấu hình CORS/OAuth/database khác. Có thể dùng environment variables để tránh sửa file:

```powershell
$env:MediaStorage__Provider = "local"
$env:MediaStorage__Local__RootPath = "D:\TindyData\private-media"
```

`appsettings.json` chỉ có cấu trúc config với `RootPath` trống; cần thiết lập đường dẫn hợp lệ trước khi gọi storage. Config được kiểm tra khi Service được resolve, không phá vỡ khởi động các API cũ chưa dùng media.

## 5. Cấu hình S3 – Production

Dùng bucket riêng, bật **S3 Block Public Access cho cả bucket** và bật mã hóa mặc định tại bucket. Bucket private không có public ACL và không tạo presigned URL công khai. Ví dụ env vars:

```powershell
$env:MediaStorage__Provider = "s3"
$env:MediaStorage__S3__BucketName = "<your-private-media-bucket>"
$env:MediaStorage__S3__Region = "ap-southeast-1"
```

App trên ECS/EC2 sử dụng **Task Role/Instance Profile** thay vì AWS Access Key ghi trong config hoặc mã nguồn. Khi chạy local để thử S3, có thể dùng AWS profile/SSO đã cấu hình bằng AWS CLI. Cần AWS IAM tối thiểu trên chính bucket/prefix:

- `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject` trên `arn:aws:s3:::<bucket>/profiles/*` và `arn:aws:s3:::<bucket>/certificates/*`.
- Nếu bật versioning và thao tác một `VersionId` cụ thể: `s3:GetObjectVersion`, `s3:DeleteObjectVersion` tương ứng.
- Không cần `s3:PutObjectAcl` và không cho public access.

S3 Provider dùng `object_etag` và `object_version_id` từ kết quả PUT để lưu vào `media_assets` ở STEP tiếp theo. Nếu bucket bật versioning, caller cần cung cấp `object_version_id` khi đọc/xóa phiên bản đó. Chỉ test S3 integration trên môi trường/bucket thử nghiệm riêng. **Không dùng Production bucket cho kiểm thử.**

## 6. Contract và lưu ý khi gọi ở STEP 03/04

```csharp
// Ví dụ bên trong Application Service, KHÔNG phải Controller và chưa triển khai trong STEP 02.
var stored = await storage.SaveAsync(
    new StorageSaveRequest(userId, MediaStorageKind.Avatar, processedWebPStream), cancellationToken);

await using var readHandle = await storage.OpenReadAsync(
    stored.StorageKey, stored.ObjectVersionId, cancellationToken);
// Sử dụng readHandle.Content khi thật sự cần đọc, rồi tự đóng handle.

await storage.DeleteAsync(stored.StorageKey, stored.ObjectVersionId, cancellationToken);
```

- Caller **sở hữu upload stream** và phải dispose. SaveAsync không đóng stream được truyền vào.
- Caller **phải dispose `StorageReadHandle`** (`await using`) để đóng stream + response HTTP của S3.
- `SaveAsync` tự sinh GUID/object key và chỉ cho phép 4 loại storage: Avatar, Cover, Certificate Image, Certificate PDF. Output ảnh thống nhất WebP theo kế hoạch. **STEP 02 không xác minh file thực sự là WebP/PDF**; các bước sau bắt buộc decode/re-encode/scan trước khi công khai.
- Giới hạn file dưới dạng byte thập phân: Avatar 5,000,000 B; Cover/PDF/Certificate Image 10,000,000 B; stream được đọc có giới hạn dù không có Content-Length.
- File `.pdf` trong `certificates/pdf/` **chưa được cho phép download bằng API**; việc kiểm tra malware và trạng thái `active` là yêu cầu bắt buộc trước khi mở API media read trong các bước sau.
- Application phải kiểm tra **JWT, ownership, status và visibility** trước khi gọi `OpenReadAsync`. `IFileStorageService` là internal service, không tự cấp quyền truy cập.
- `StorageKey` và đường dẫn ổ đĩa **không được trả từ public DTO**.

## 7. Lỗi, cleanup và giới hạn STEP 02

- Local: ghi vào file `.partial` rồi rename trong cùng thư mục; gặp exception thì xóa file tạm. File xóa nhiều lần không gây lỗi.
- S3: PUT dùng `IfNoneMatch = "*"` để ngăn overwrite key; nếu upload có lỗi, thử xóa object còn sót (best-effort). Không xóa object đang có nếu S3 trả `412 Precondition Failed`.
- Nếu mất điện/crash giữa chừng hoặc cleanup S3 cũng thất bại, có thể còn file rác. **Retry bền vững và orphan reconciliation thuộc STEP 05**, không giả vờ đã giải quyết ở STEP 02.
- Local không cho truyền VersionId; S3 hỗ trợ VersionId để xử lý bucket có versioning.
- Hiện tại mỗi file được buffer tối đa 10 MB trong memory để xử lý kể cả stream không seekable. Cần tính đến giới hạn đồng thời/rate limiting ở STEP 03/04; chưa phù hợp cho upload file lớn tùy ý.

## 8. Build & Tests

Từ Backend root:

```powershell
dotnet restore
dotnet build .\Tindy.slnx

dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~LocalFileStorageTests" `
    --verbosity normal

# Kiểm tra toàn bộ tests nếu môi trường đã có các dịch vụ phụ thuộc (PostgreSQL/Docker).
dotnet test .\Tindy.slnx
```

Các tests Local không cần PostgreSQL, AWS credentials hoặc Internet, sau khi package NuGet được restore. Test gồm:

1. Save → Read → Delete cho Avatar/Cover/Certificate Image/PDF.
2. Delete hai lần và đọc file đã xóa.
3. Chống traversal/key giả mạo.
4. Giới hạn file 5 MB, file rỗng và không để lại orphan.
5. Concurrent upload tạo key khác nhau.
6. Storage root phải nằm ngoài application content root.
7. Cancellation không để lại file.

S3 integration test chạy **riêng** sau khi tạo bucket/IAM test; trong STEP 02 chưa claim PASS cho S3 thực tế.

## 9. Điều kiện nghiệm thu và commit

- [ ] `git apply --check` và `git apply` thành công trên repo thật.
- [ ] `dotnet restore`, `dotnet build` thành công.
- [ ] Tất cả `LocalFileStorageTests` PASS.
- [ ] Cấu hình Local path nằm ngoài repo, lưu/đọc/xóa thật PASS (đã được cover bởi unit/integration-style tests sử dụng filesystem).
- [ ] Config switch sang S3 và xác minh với **test bucket riêng** trước khi đánh giá S3 end-to-end PASS.
- [ ] Không tạo API upload/media public, không tạo migration.
- [ ] Không thay đổi logic Auth/Profile/Certificate có sẵn.

```powershell
git add src/Tindy.Application/Interfaces/Services/IFileStorageService.cs
git add src/Tindy.Application/Models/Storage/
git add src/Tindy.Infrastructure/Storage/
git add src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs
git add src/Tindy.Infrastructure/Tindy.Infrastructure.csproj
git add src/Tindy.Api/appsettings.json
git add src/Tindy.Api/appsettings.Development.example.json
git add tests/Tindy.Tests/Media/
git add docs/guide/PROFILE_MEDIA_STEP02_STORAGE_HANDOFF.md

git -c core.whitespace=cr-at-eol diff --cached --check
git commit -m "feat(media): add local and s3 storage providers"
```

> Nếu test fail, gửi log terminal và giữ STEP 02 chưa commit. Không chạy `dotnet ef database update` trong bước này.
