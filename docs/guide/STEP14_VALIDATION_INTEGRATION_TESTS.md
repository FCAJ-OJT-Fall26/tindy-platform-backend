# STEP 14 - Authentication - Validation & Integration Tests

## 1. Mục tiêu

Thêm automated tests cho Authentication (Register, Login, Logout, PasswordHash, JWT), DTO validation, Onboarding Step 1–8 và Authorization Gate của Tindy. Mã nguồn chính không đổi nghiệp vụ.

**Kiến trúc test:**

- `tests/Tindy.Tests/Validation`: xUnit + `System.ComponentModel.DataAnnotations`, không cần Docker.
- `tests/Tindy.Tests/Security`: BCrypt + JWT thực tế, không cần Docker.
- `tests/Tindy.Tests/Integration`: ASP.NET Core `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql` + EF Core `MigrateAsync` + role seed.
- Integration tests gọi HTTP thật bằng `HttpClient` trên TestServer (không cần Postman hay port localhost:7190).
- PostgreSQL container tạm được tự động tạo và hủy; **không sử dụng database Development/Production**.

## 2. Điều kiện

1. .NET SDK 10.
2. Docker Desktop chạy Linux containers (hoặc Docker Engine tương thích).
3. NuGet restore được; khi chạy integration lần đầu Docker có thể cần tải `postgres:16-alpine`.
4. Không cần thiết lập connection string hoặc JWT Signing Key thật: test fixture tự cung cấp các giá trị riêng.

## 3. File mới và chỉnh sửa

```text
tests/Tindy.Tests/
├── Tindy.Tests.csproj
├── AssemblyInfo.cs
├── Validation/RequestValidationTests.cs
├── Security/CryptoTests.cs
└── Integration/
    ├── TindyApiFactory.cs
    ├── AuthenticationOnboardingTests.cs
    └── RateLimitTests.cs

src/Tindy.Api/Program.cs                  # thêm public partial class Program
Tindy.slnx                                # thêm test project
```

## 4. Lệnh chạy (PowerShell 7, tại root repo)

```powershell
# Kiểm tra SDK và Docker
dotnet --version
docker info

# Restore toàn solution
dotnet restore

# Build toàn solution
dotnet build

# Unit tests: không cần Docker
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter "FullyQualifiedName~Validation|FullyQualifiedName~Security"

# Integration tests: cần Docker
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter "FullyQualifiedName~Integration"

# Toàn bộ
dotnet test .\Tindy.slnx
```

**Chú ý:** Không thay `MigrateAsync()` bằng `EnsureCreated()`: tests phải chạy tất cả migration thật, bao gồm `Profile_AddOnboardingDataAndSkills`.

## 5. Test cases quan trọng

| Nhóm | Tình huống | Kết quả |
|---|---|---|
| Validation | Register hợp lệ | Pass |
| Validation | Email sai, password yếu, không đồng ý điều khoản | Reject |
| Validation | Step 2 rỗng, availability ngoài 1–40 | Reject |
| Validation | Step 7 cả hai URL null | Pass |
| Validation | Step 8 có/không có Bio, Bio > 2.000 ký tự | Đúng contract |
| BCrypt | Hash khác salt, verify đúng/sai, giới hạn 72 bytes | Pass |
| JWT | Role, sub, issuer, signature, expiration settings | Pass |
| Register | 201, User/Profile/Credential/Role persisted | Pass |
| Duplicate email | 409 và không tạo record thứ hai | Pass |
| Concurrent register | 1 request 201; 1 request 409 | Pass |
| Login | Email và Username đều 200; password sai 401 | Pass |
| Onboarding | Incomplete complete → 422; PUT 1–7 → 200 | Pass |
| Skills | Danh sách trùng không tạo link trùng | Pass |
| Bio | Step 8 lưu Bio do user sửa | Pass |
| Access gate | Chưa hoàn thành → 403 | Pass |
| Access gate | JWT cũ sau khi Complete → 200 | Pass |
| Disabled account | Không vượt qua access gate | 403 |
| Logout | 200; JWT còn hạn vẫn có thể được xác minh | Đúng Stateless JWT |
| Rate limiting | Quá 10 login/phút cùng partition → 429 | Pass |

## 6. Troubleshooting

- **Docker daemon unavailable**: bật Docker Desktop, chọn Linux containers, chạy `docker info` lại.
- **NuGet restore failed**: kiểm tra kết nối `nuget.org` và version package; không thay đổi code production để workaround.
- **Port localhost:7190 đang dùng**: TestServer không sử dụng cổng này; không cần tắt API local.
- **Integration test trả 500**: xem exception log và test output; chú ý Migration, EF relationship, Docker logs.
- **Test fail do shape response**: đối chiếu snake_case contract, `error_code` và status code thực tế; không cập nhật expected result chỉ để cho test xanh.
- **Rate limit test 429 sớm hơn**: nó dùng fixture riêng nên thông thường bắt đầu từ quota mới; nếu thay đổi policy giới hạn, đồng bộ lại contract và test.
- **Docker resource cleanup**: nếu test bị kill, kiểm tra container còn tồn tại bằng `docker ps -a` trước khi dọn thủ công.

## 7. Git commit

```powershell
git status
git diff --check
git add Tindy.slnx src/Tindy.Api/Program.cs tests/Tindy.Tests/ docs/guide/STEP14_VALIDATION_INTEGRATION_TESTS.md
git diff --cached --stat
git commit -m "test(auth): add validation and integration coverage"
```

Chỉ đánh dấu **STEP 14 DONE** khi build và test chạy thành công trên máy có .NET SDK và Docker. Khi test fail, gửi đầy đủ tên failing test và stack trace (không gửi Signing Key/Access Token thật).
