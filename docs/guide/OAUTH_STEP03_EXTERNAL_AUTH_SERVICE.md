# STEP 03 — Implement External Authentication Repository And Service

> Dự án: Tindy .NET 10, Clean Architecture, EF Core / PostgreSQL.
> Phạm vi: **STEP 03 duy nhất**; chưa xây dựng controller callback, one-time ticket, JWT exchange hoặc account linking API.
> Nguồn đối chiếu: `tindy-backend-platform(20261015-134150).zip`, áp dụng lần lượt patch STEP 01 + STEP 02 để dựng baseline. **Các migration STEP 01 đã sinh trực tiếp trên máy developer không nằm trong ZIP cũ**.

## 1. Các thay đổi và lý do

| File | Thay đổi | Lý do |
|---|---|---|
| `src/Tindy.Application/Constants/ExternalAuthProviders.cs` | Hằng số tên provider và canonicalization | `Google`/`GitHub` nhất quán cho index `(provider, provider_user_id)` |
| `src/Tindy.Application/Models/Authentication/AuthenticatedExternalIdentity.cs` | Model đầu vào chỉ dành cho provider identity đã xác thực | Không đưa `authorization_code` / `access_token` vào Application |
| `src/Tindy.Application/Models/Authentication/ExternalSignInResult.cs` | Kết quả resolve user (không JWT) | STEP 04 dùng UserId/roles và trạng thái onboarding để tạo ticket an toàn |
| `src/Tindy.Application/Interfaces/Repositories/IExternalAuthRepository.cs` | Repository abstraction | Application không phụ thuộc EF Core |
| `src/Tindy.Application/Interfaces/Services/IExternalAuthService.cs` | Business contract cho resolve/login và liên kết nội bộ | STEP 04 và STEP 05 sử dụng sau |
| `src/Tindy.Application/Services/ExternalAuthService.cs` | Nghiệp vụ tìm/tạo OAuth-only User, kiểm tra conflict và status, linking nội bộ | Không sửa Local Auth |
| `src/Tindy.Infrastructure/Repositories/ExternalAuthRepository.cs` | EF implementation + chuyển lỗi unique DB thành `ConflictException` | Có chốt ở database khi race-condition |
| `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Đăng ký `IExternalAuthRepository` | DI |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | Đăng ký `IExternalAuthService` | DI |
| `tests/Tindy.Tests/Security/ExternalAuthServiceTests.cs` | Unit tests dùng fake repository, không Docker | Test nghiệp vụ không cần Testcontainers |

**Không sửa:** `AuthController`, `AuthService`, Local DTOs, `JwtTokenService`, JwtBearer scheme, Onboarding Policies, migrations hoặc entities/EF mappings.

## 2. Chính sách nghiệp vụ đã mã hóa — chốt trước khi tích hợp STEP 04

1. **1 User — nhiều phương thức.** OAuth-only User không có `UserCredential`.
2. **Stable provider identity**: lookup bằng `(provider, provider_user_id)`, không phải email hay username provider.
3. **Không tự động link theo email**. Khi email verified của provider đã bị chiếm bởi tài khoản Tindy khác, trả `ConflictException`. Người dùng phải đăng nhập Tindy và tự liên kết về sau.
4. **Email liên hệ chỉ nhận từ email đã được provider xác minh**: nếu provider thiếu email hoặc chưa xác minh, `users.primary_email` / normalized / verified_at đều `NULL`. `external_auth_accounts.provider_email` có thể lưu email hợp lệ dù chưa verified, nhưng **không dùng để chứng minh quyền sở hữu**.
5. **Profile** mới bắt buộc có `DisplayName`; fallback là provider username, rồi `Tindy Member`. `University`, `Bio` ban đầu `NULL` (không giả lập AI summary).
6. **Role** `member` phải tồn tại do seeding. User được tạo với `Status=active`, `IsOnboardingCompleted=false` và một `UserRole` `member`.
7. **Existing User** phải active, không soft-deleted, có Profile và Role hợp lệ. Không tự động cập nhật email chính của User khi provider email thay đổi.
8. **Một provider một account / user**: theo unique index `(user_id, provider)` của STEP 01. `LinkAsync` là **nghiệp vụ nội bộ** để STEP 05 sử dụng, chưa phải endpoint và **chỉ được gọi khi caller đã xác minh JWT Tindy, OAuth correlation/state và UserId của chủ thể**.
9. **Transaction và races**: tạo aggregate qua **một** `SaveChangesAsync`. Npgsql sẽ từ chối identity/email trùng bằng unique index; repository chuyển các lỗi 23505 đã biết thành conflict HTTP 409 khi controller xử lý sau này. Một callback bị race có thể cần người dùng thử lại; chưa tự động retry.
10. **Không cấp Tindy JWT ở STEP 03**. STEP 04: backend callback xác thực identity -> service resolve User -> server tạo one-time ticket -> frontend exchange -> mới phát JWT, tránh rủi ro phát JWT trước khi giao ticket.

> **LƯU Ý BẢO MẬT:** `AuthenticatedExternalIdentity` là dữ liệu tin cậy nội bộ do adapter OAuth dựng từ verified provider principal. Không bao giờ tạo nó từ JSON do client tự gửi. Với GitHub, `EmailVerified=true` chỉ khi xác minh được email qua provider; không mặc định `true` chỉ vì có email string. Với Google, sử dụng claim/email_verified đúng nguồn. Middleware STEP 02 chưa tự động làm toàn bộ kiểm tra này.

## 3. Cách áp dụng patch (PowerShell, root backend)

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
git status
git apply --check .\Tindy_STEP03_External_Authentication_Service.patch
git apply .\Tindy_STEP03_External_Authentication_Service.patch

dotnet build .\Tindy.slnx
git status
git -c core.whitespace=cr-at-eol diff --check
```

**Không được áp dụng patch nếu `git apply --check` báo lỗi.** Khi đó gửi `git status` cùng thông báo lỗi và ZIP source mới nhất để điều chỉnh, tránh ghi đè STEP 01–02 chưa có trong bản ZIP trước.

## 4. Cách test (không yêu cầu Docker)

Bạn đã quyết định **hoãn Docker/Integration Tests**. Riêng bài test nghiệp vụ STEP 03 nằm trong `tests/Tindy.Tests/Security/ExternalAuthServiceTests.cs` và không dùng Docker:

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter FullyQualifiedName~ExternalAuthServiceTests
```

Các tình huống test thuần Application gồm: lần đầu Google tạo user OAuth-only, Google lần hai cùng UserId, GitHub không có email, email unverified, email đã thuộc Local, user bị khóa/soft-delete, không có role, onboarding, linking thành công/idempotent và conflict. Các unit tests này **không thay thế** kiểm tra PostgreSQL constraints/concurrency (hoãn STEP 08).

Để xác minh không có migration mới trong STEP 03:

```powershell
git diff --name-only
```

**Manual test bằng Postman**: chưa có endpoint Login/Register OAuth mới ở STEP 03. Các `GET /api/dev/oauth/*/start` thuộc STEP 02 chỉ test handshake. STEP 04 mới expose OAuth login/register và ticket exchange.

## 5. Tiêu chí Done

- [ ] Patch áp dụng lên source **sau STEP 01–02** không có conflict.
- [ ] `dotnet build .\Tindy.slnx` thành công.
- [ ] Unit Tests `ExternalAuthServiceTests` pass (không cần Docker).
- [ ] `git -c core.whitespace=cr-at-eol diff --check` không báo lỗi.
- [ ] Không sửa Local Register/Login/JWT/Onboarding và không tạo migration.
- [ ] Chốt rõ chính sách verified provider email / email conflict, nhất là GitHub private email.
- [ ] Chưa có HTTP endpoint OAuth hoặc JWT issuance mới.
- [ ] Commit riêng STEP 03.

```powershell
git add src/ tests/Tindy.Tests/Security/ExternalAuthServiceTests.cs docs/guide/OAUTH_STEP03_EXTERNAL_AUTH_SERVICE.md
git diff --cached --stat
git commit -m "feat(auth): implement external identity service and repository"
```

Không `git add .` nếu patch `.patch`, file log/test tạm, SQL kiểm tra hoặc Client Secrets vẫn đang ở root. Không đưa GitHub/Google Client Secret vào Git.

## 6. Giới hạn đánh giá

Bộ patch được tạo theo source ZIP mới nhất được cung cấp trong hội thoại và kiểm tra áp dụng trên baseline giả lập đã ghép patch STEP 01/02. Không có .NET SDK trong môi trường tạo patch nên không thể thực thi `dotnet build` hoặc `dotnet test` tại đây; các lệnh trên phải chạy trên máy bạn.
