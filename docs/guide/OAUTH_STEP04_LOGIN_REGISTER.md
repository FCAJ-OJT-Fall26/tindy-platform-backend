# Tindy — STEP 04: Google / GitHub OAuth Login & Register

**Mục tiêu:** Kết nối các OAuth providers đã cấu hình ở STEP 02 với `ExternalAuthService` (STEP 03), đăng nhập/đăng ký OAuth-only, chuyển tiếp phiên đăng nhập bằng one-time ticket, sau đó phát JWT Bearer Tindy qua endpoint exchange.

> STEP 04 **không triển khai** Account Linking (STEP 05) hay giao diện callback (STEP 07). Không cần Docker hay integration tests ở giai đoạn này theo yêu cầu; tuy nhiên trước khi merge production vẫn phải chạy test an toàn.

## A. Các quyết định kiến trúc cần xác nhận

1. **Vẫn dùng JWT Bearer**, không chuyển toàn bộ hệ thống sang cookie authentication. Cookie `Tindy.External` chỉ sống trong khoảng thời gian thực hiện OAuth handshake.
2. **Dùng PostgreSQL cho one-time ticket** (hỗ trợ nhiều ECS/Fargate instances). Cần migration `Auth_CreateOAuthLoginTickets` vì thêm entity/table `oauth_login_tickets`.
3. Ticket có **32 byte ngẫu nhiên**, **TTL 90 giây**, lưu **SHA-256 hash** phía server; bước exchange tiêu thụ ticket bằng PostgreSQL `DELETE` có điều kiện, đảm bảo một request thắng.
4. Redirect frontend cố định bằng `OAuth:Frontend:AllowedOrigins:0` + `/auth/oauth/callback#ticket=...`, không lấy `returnUrl` do client gửi. Fragment `#ticket=` không gửi đến frontend server trong HTTP request, nhưng vẫn cần frontend xóa khỏi history ngay khi xử lý (STEP 07).
5. Email OAuth dùng làm `PrimaryEmail` **chỉ khi provider xác nhận verified**. Google đọc `verified_email`/`email_verified`; GitHub sử dụng API `/user/emails` với scope `user:email` để kiểm tra `verified`. Thiếu email verified: OAuth-only vẫn có thể đăng ký với `PrimaryEmail = null`.
6. Nếu provider identity chưa liên kết mà email verified trùng Local/Tindy User: **conflict**, yêu cầu linking thủ công ở STEP 05. Không tự động merge.
7. Callback ngoài middleware: `/signin-google`, `/signin-github` **giữ nguyên**. Controller chỉ xử lý `/api/auth/oauth/complete` sau callback middleware.
8. Frontend hiện chưa có `/auth/oauth/callback`: khi test thủ công cần **copy ticket trong fragment từ browser URL** hoặc tạo trang test tạm ở frontend (không thuộc STEP 04). Không có JWT hoặc access token Google/GitHub trong redirect URL.

## B. Các file thay đổi

| Layer | Đường dẫn (tương đối root) | Lý do |
|---|---|---|
| API | `src/Tindy.Api/Controllers/OAuthController.cs` | challenge, xử lý remote auth result, ticket exchange |
| API | `src/Tindy.Api/Extensions/OAuthAuthenticationExtensions.cs` | map Google verification / gọi GitHub verified emails bằng server-to-server |
| API | `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | DI OAuth flow service và rate-limit exchange |
| Application | `src/Tindy.Application/Interfaces/Repositories/IOAuthLoginTicketStore.cs` | contract lưu/tiêu thụ ticket |
| Application | `src/Tindy.Application/Interfaces/Services/IOAuthLoginFlowService.cs` | contract nghiệp vụ exchange |
| Application | `src/Tindy.Application/Services/OAuthLoginFlowService.cs` | phối hợp ExternalAuthService, ticket, JWT, trạng thái User |
| Application | `src/Tindy.Application/Models/Authentication/OAuthExchangeResponse.cs` | response cùng kiểu JWT Login của Tindy, email nullable |
| Application | `src/Tindy.Application/DTOs/Authentication/OAuthExchangeRequest.cs` | validate ticket request |
| Infrastructure | `src/Tindy.Infrastructure/Persistence/Models/OAuthLoginTicket.cs` | transient persistent identity for ticket hash |
| Infrastructure | `src/Tindy.Infrastructure/Persistence/Configurations/OAuthLoginTicketConfiguration.cs` | EF mapping |
| Infrastructure | `src/Tindy.Infrastructure/Repositories/OAuthLoginTicketStore.cs` | PostgreSQL atomic one-time ticket |
| Infrastructure | `src/Tindy.Infrastructure/Persistence/TindyDbContext.cs` | thêm DbSet ticket |
| Infrastructure | `src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs` | đăng ký ticket store |
| Unit tests | `tests/Tindy.Tests/Security/OAuthLoginFlowServiceTests.cs` | kiểm tra flow không cần Docker |

Không sửa `AuthController`, Local Register/Login/Logout, `JwtTokenService`, JWT Bearer scheme, các Authorization policies hoặc schema User/Credential. **Không sửa migrations cũ đã được apply/shared**.

## C. Áp dụng patch tại PowerShell 7

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
git status
git -c core.whitespace=cr-at-eol apply --check .\Tindy_STEP04_OAuth_Login_Register.patch
git -c core.whitespace=cr-at-eol apply .\Tindy_STEP04_OAuth_Login_Register.patch
dotnet build .\Tindy.slnx
```

Nếu `git apply --check` có conflict, **dừng lại và gửi output**; STEP 04 được dựng trên baseline STEP 01 + STEP 02 + STEP 03 đã áp dụng, không nên `--force` hoặc ghi đè file.

## D. Tạo migration mới (BẮT BUỘC trước khi chạy endpoint OAuth)

Sau khi build thành công, tạo migration bằng EF Core trên source thật (để EF Core tự đồng bộ Designer/Snapshot và các migrations riêng có từ team):

```powershell
dotnet ef migrations add Auth_CreateOAuthLoginTickets `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    --output-dir Persistence/Migrations `
    -- --environment Development
```

Xem `Up()` phải tạo `oauth_login_tickets` có:

- `ticket_hash` (varchar(64), PK), **không lưu raw ticket**.
- `user_id` (uuid, FK tới `users`), `created_at`, `expires_at` (timestamptz).
- Index `ix_oauth_login_tickets_expires_at`.

Xem `Down()` drop đúng bảng/index. **Không chạy `database drop` vì STEP 04 không cần reset DB.** Sau khi review migration:

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Trong PostgreSQL:

```sql
\d oauth_login_tickets
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

> Vì bản ZIP backend được tải trước khi bạn tự chạy migrations STEP 01–03, patch này **không chứa migration được scaffold sẵn**. Để tránh làm hỏng snapshot thực tế trên máy bạn, nên tạo migration bằng câu lệnh ở trên. Gửi lại `Up()` và `Down()` trước khi update nếu muốn mình review.

## E. API contract và flow

| Method | Endpoint | Mô tả |
|---|---|---|
| GET | `/api/auth/google` | Bắt đầu Challenge Google |
| GET | `/api/auth/github` | Bắt đầu Challenge GitHub |
| GET | `/signin-google` | OAuth middleware callback, không phải controller |
| GET | `/signin-github` | OAuth middleware callback, không phải controller |
| GET | `/api/auth/oauth/complete` | Nhận external temporary cookie, resolve User, redirect với ticket |
| POST | `/api/auth/oauth/exchange` | Consume ticket, kiểm tra User mới nhất, trả JWT Tindy |

Ví dụ Request exchange (không dùng ticket đã hết hạn):

```http
POST https://localhost:7190/api/auth/oauth/exchange
Content-Type: application/json

{"ticket":"<ticket-from-url-fragment>"}
```

Response mẫu:

```json
{
  "success": true,
  "message": "Logged in successfully.",
  "data": {
    "access_token": "<tindy-jwt>",
    "token_type": "Bearer",
    "expires_in": 3600,
    "user": {
      "user_id": "<uuid>",
      "display_name": "Example",
      "email": null,
      "username": null,
      "status": "active",
      "roles": ["member"],
      "is_onboarding_completed": false
    },
    "redirect_to": "/onboarding"
  }
}
```

Local JWT và OAuth JWT đều do `IJwtTokenService` tạo ra.

## F. Manual test không cần Docker

1. Kiểm tra `OAuth:Enabled=true`, Client ID, Client Secret đã đặt bằng User Secrets; callback GitHub `https://localhost:7190/signin-github` và Google `https://localhost:7190/signin-google` khớp chính xác với provider settings.
2. Trong Development `OAuth:Frontend:AllowedOrigins:0 = http://localhost:8443` (hoặc đúng Vite origin trên máy); backend HTTPS chạy ở 7190, `https://localhost:7190/api/auth/github` mở trong **browser**.
3. Sau khi login provider, backend redirect về `http://localhost:8443/auth/oauth/callback#ticket=...` (frontend chưa có trang này thì có thể báo 404, song URL trong address bar vẫn có ticket). Copy ticket **riêng**, không gửi lên Git hoặc chat, không chia sẻ ảnh có ticket.
4. Postman: `POST /api/auth/oauth/exchange` body JSON `{"ticket":"..."}` → kỳ vọng 200 và `data.access_token`.
5. Gọi exchange lại cùng ticket → 401. Chờ 90 giây rồi đổi ticket chưa dùng → 401.
6. Login cùng GitHub ID lần 2 → nhận cùng `user_id`; User OAuth-only không có record `user_credentials`.
7. User lần đầu nhận `redirect_to: "/onboarding"`; đã hoàn thành onboarding nhận `"/discover"`.
8. Local Register/Login và JWT onboarding vẫn hoạt động. Tài khoản disabled/deleted không exchange được.
9. Khi email verified trùng tài khoản khác chưa link → frontend nhận `?error=account_link_required`, không tự động merge.

Không paste ticket/JWT/client secret vào ảnh hoặc logs gửi người khác. Ticket nằm trong fragment chỉ 90 giây, nhưng vẫn là credential trong thời gian đó.

## G. Unit tests không cần Docker

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~OAuthLoginFlowServiceTests"
```

Test trên kiểm tra phát JWT sau tiêu thụ ticket, replay, invalid/expired, status user, roles, onboarding, BeginTicket. **Chưa thay thế** được integration test kiểm tra PostgreSQL atomic deletion với 2 requests đồng thời; hoãn các bài test đó sang STEP 08 như đã thống nhất.

## H. Tiêu chí DONE / Commit

- [ ] `dotnet build .\Tindy.slnx` thành công.
- [ ] Migration mới chính xác và áp dụng thành công lên **Development** DB.
- [ ] Google/GitHub browser handshake thành công với `OAuth:Enabled=true`.
- [ ] OAuth Register tạo `User + UserProfile + UserRole + ExternalAuthAccount`, **không** tạo `UserCredential`.
- [ ] OAuth Login lần 2 ánh xạ cùng `UserId`.
- [ ] Ticket chỉ dùng 1 lần, quá hạn bị từ chối, JWT không đi qua URL.
- [ ] 401/409/conflict được xử lý an toàn; missing GitHub email không gây lỗi.
- [ ] Local Register/Login/JWT/Onboarding không regress (manual smoke test).
- [ ] Review `git -c core.whitespace=cr-at-eol diff --check`, `git status`, không commit `.patch`, secret, output SQL debug.

```powershell
git status
git -c core.whitespace=cr-at-eol diff --check
git add src/ tests/Tindy.Tests/Security/OAuthLoginFlowServiceTests.cs docs/guide/OAUTH_STEP04_LOGIN_REGISTER.md
git diff --cached --stat
git commit -m "feat(auth): implement oauth login registration and ticket exchange"
```

Chưa làm STEP 05 (link/unlink) hoặc STEP 07 (frontend). **Bổ sung chia sẻ DataProtection key ring** khi deploy nhiều ECS/Fargate instances để OAuth correlation và temporary external cookie hoạt động khi redirect qua instance khác; PostgreSQL chia sẻ ticket **không tự giải quyết vấn đề DataProtection**.
