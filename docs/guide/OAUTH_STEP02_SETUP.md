# STEP 02 — Configure Google And GitHub OAuth (Tindy)

> Dự án: Tindy Backend Platform — .NET 10 / ASP.NET Core / JWT Bearer.
> Phạm vi: **STEP 02**; chưa triển khai OAuth Register/Login, linking, JWT exchange hoặc migration.
> Source tham chiếu: `tindy-backend-platform(20261015-134150).zip`, sau STEP 01 là code hiện hành trên máy developer.

## 1. Kiến trúc và những gì thay đổi

- JWT Bearer vẫn là scheme mặc định (`AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`).
- Thêm `Google` và `GitHub` dưới dạng remote OAuth schemes độc lập, chỉ khi `OAuth:Enabled = true`.
- Thêm cookie xác thực tạm `Tindy.External` (`__Host-Tindy.External`, `HttpOnly`, `Secure`, `SameSite=Lax`, TTL 10 phút); không thay thế JWT hoặc trở thành cookie session chính của Tindy.
- Middleware dùng các backend callback `/signin-google` và `/signin-github`. Callback được xử lý tự động bởi authentication middleware; không tạo controller action cạnh tranh cùng path.
- Correlation cookies là `Secure`, `HttpOnly`, `SameSite=None`; state OAuth được bảo vệ bởi ASP.NET Core. Phải sử dụng HTTPS cho backend khi thử bằng trình duyệt.
- Không lưu provider tokens (`SaveTokens=false`). GitHub yêu cầu `user:email` để có khả năng đọc email không public. **Quyết định sử dụng email verified** và xử lý email thiếu thuộc STEP 03–04.
- Khai báo allowlist nguồn frontend; **chỉ kiểm tra tính hợp lệ cấu hình ở STEP 02**. Việc enforce redirect vào origin cụ thể sẽ triển khai ở STEP 04, tuyệt đối không nhận arbitrary `returnUrl`.
- Nếu `OAuth:Enabled=false`, Local Register/Login/JWT/Onboarding vẫn hoạt động như trước, không yêu cầu Google/GitHub secrets.
- Chỉ bật OAuth khi đã khai báo đầy đủ secrets và frontend allowlist, nếu thiếu ứng dụng báo lỗi cấu hình rõ ràng (không in giá trị secret).

Các file thay đổi:

| File | Lý do |
|---|---|
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | Nối OAuth schemes sau JwtBearer, giữ JWT default |
| `src/Tindy.Api/Extensions/OAuthAuthenticationExtensions.cs` *(mới)* | Cấu hình provider, temporary cookie, correlation, xử lý lỗi từ provider |
| `src/Tindy.Api/Extensions/WebApplicationExtensions.cs` | Development-only OAuth challenge + callback smoke test |
| `src/Tindy.Api/appsettings.json` | Bổ sung flags/allowlist không chứa secrets |
| `docs/guide/OAUTH_STEP02_SETUP.md` *(mới)* | Hướng dẫn vận hành và test |

Không sửa: `Tindy.Application`, `Tindy.Infrastructure`, EF entities, migrations, Local Auth, `AuthController`, `Program.cs`, CORS hoặc JWT policies.

## 2. Tạo OAuth Credentials

### Google

1. Truy cập **Google Cloud Console → Google Auth Platform → Clients**; cấu hình branding/consent nếu hệ thống yêu cầu.
2. Tạo OAuth client, loại **Web application**.
3. Đăng ký **Authorized redirect URI** (đường dẫn backend):
   ```text
   https://localhost:7190/signin-google
   ```
4. Lưu Client ID và Client Secret **tại máy**. Nếu đang ở trạng thái Testing, thêm tài khoản thử nghiệm tương ứng nếu Google yêu cầu.

Tài liệu: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0

### GitHub

1. Truy cập GitHub **Settings → Developer settings → OAuth Apps → New OAuth App**.
2. Homepage URL có thể dùng URL frontend Development, ví dụ `http://localhost:8443`.
3. **Authorization callback URL** (backend):
   ```text
   https://localhost:7190/signin-github
   ```
4. Tạo Client Secret; lưu an toàn.

Tài liệu: https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app

> Phân biệt: provider redirect URI là **backend callback** (`/signin-*`); frontend callback URL là đường dẫn tiếp nhận one-time ticket sẽ triển khai ở STEP 04.

## 3. Cấu hình User Secrets (PowerShell 7)

Mở terminal tại root:

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
```

Project API đã có `UserSecretsId`, **không cần chạy** `dotnet user-secrets init`.

```powershell
$api = ".\src\Tindy.Api\Tindy.Api.csproj"

dotnet user-secrets set "OAuth:Google:ClientId" "YOUR_GOOGLE_CLIENT_ID" --project $api
dotnet user-secrets set "OAuth:Google:ClientSecret" "YOUR_GOOGLE_CLIENT_SECRET" --project $api
dotnet user-secrets set "OAuth:GitHub:ClientId" "YOUR_GITHUB_CLIENT_ID" --project $api
dotnet user-secrets set "OAuth:GitHub:ClientSecret" "YOUR_GITHUB_CLIENT_SECRET" --project $api

dotnet user-secrets set "OAuth:Frontend:AllowedOrigins:0" "http://localhost:8443" --project $api
dotnet user-secrets set "OAuth:Frontend:AllowedOrigins:1" "http://127.0.0.1:8443" --project $api

dotnet user-secrets set "OAuth:Enabled" "true" --project $api
```

Không gửi Client Secret trong chat, không commit User Secrets vào Git. Cấu hình production phải dùng dịch vụ quản lý secrets, HTTPS và danh sách origin production thực tế.

## 4. Kiểm tra thủ công không cần Docker

### 4.1. Kiểm tra cấu hình và build

```powershell
dotnet restore .\Tindy.slnx
dotnet build .\Tindy.slnx
git diff --check
```

Bật trust local HTTPS development certificate nếu trình duyệt báo chứng chỉ:

```powershell
dotnet dev-certs https --trust
```

### 4.2. Chạy backend **HTTPS**

```powershell
dotnet run --project .\src\Tindy.Api\Tindy.Api.csproj --launch-profile https
```

Theo `launchSettings.json` source hiện tại: `https://localhost:7190` (API HTTPS), `http://localhost:5217` (HTTP). **Sử dụng HTTPS 7190** cho OAuth, vì correlation và external cookies là `Secure`.

### 4.3. Test Google/GitHub Challenge

Mở trực tiếp trên trình duyệt, không gọi bằng Postman hoặc `fetch` từ frontend:

```text
https://localhost:7190/api/dev/oauth/google/start
https://localhost:7190/api/dev/oauth/github/start
```

Kỳ vọng: chuyển hướng đến Google/GitHub, hoàn thành consent/login, provider gọi `/signin-google` hoặc `/signin-github`, middleware kết thúc tại:

```text
https://localhost:7190/api/dev/oauth/result
```

Response kỳ vọng:

```json
{
  "success": true,
  "message": "External OAuth handshake completed. No Tindy user or JWT was created."
}
```

**Quan trọng:** Đây chỉ là test handshake. Không tạo User, không liên kết account, không lưu Tindy JWT. Sau khi test, external cookie được xóa. Các route `/api/dev/oauth/*` chỉ tồn tại khi `ASPNETCORE_ENVIRONMENT=Development`, không xuất hiện trong production. Đến STEP 04 sẽ thay thế bằng API nghiệp vụ thực và có thể xóa các test routes tạm thời.

Nếu trả `503`, xem `OAuth:Enabled` hoặc credentials. Nếu Google/GitHub báo `redirect_uri_mismatch`, đối chiếu callback URI trong OAuth App với HTTPS 7190 + `/signin-*`. Nếu mất correlation, kiểm tra cùng một hostname (không đổi qua `127.0.0.1` giữa các bước), HTTPS/secure cookie, browser cookies và reverse proxy headers.

### 4.4. Kiểm tra Local Authentication không bị phá

- `POST /api/auth/register` và `POST /api/auth/login` vẫn giữ request/response trước đó.
- `POST /api/auth/logout` vẫn dùng Bearer token và stateless.
- Một API protected không có token vẫn trả `401` dạng JWT ProblemDetails (không bị redirect đến Google hoặc trang login cookie).
- `GET /api/health` hoạt động.
- Nếu `OAuth:Enabled = false` API vẫn khởi động mà không có Google/GitHub secrets.

> Team đang hoãn Docker/Integration Tests sang STEP 08. Điều này không tương đương với việc đã chạy toàn bộ automated tests.

## 5. Mục tiêu Done và Git commit

- [ ] Có Google OAuth Client và GitHub OAuth App cho Development (không đẩy secrets lên repository).
- [ ] Config đầy đủ qua User Secrets; backend khởi động bằng HTTPS.
- [ ] Google và GitHub challenge/callback Development thành công (hoặc ghi rõ provider chưa được cấp credential).
- [ ] JWT Bearer vẫn là default, không đổi `AuthController`, Local Auth, Onboarding Policies.
- [ ] `dotnet build` pass; review thay đổi; không có migration trong STEP 02.
- [ ] Không phát sinh token trên URL; smoke test không tạo account.

```powershell
git status
git diff --check
git add src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs `
        src/Tindy.Api/Extensions/OAuthAuthenticationExtensions.cs `
        src/Tindy.Api/Extensions/WebApplicationExtensions.cs `
        src/Tindy.Api/appsettings.json `
        docs/guide/OAUTH_STEP02_SETUP.md

git diff --cached --stat
git commit -m "feat(auth): configure google and github oauth providers"
```

**Chưa commit** nếu code không build hoặc secrets/credentials chưa được cấu hình để test. File `.patch` ở root là file hỗ trợ triển khai, không nên commit.

## 6. Những phần cố ý hoãn

- STEP 03: OAuth repository và domain/business service.
- STEP 04: Login/Register thật, xử lý verified email, callback exchange one-time ticket và frontend redirect allowlist enforcement.
- STEP 05: Account linking/unlinking.
- STEP 06: JWT và onboarding integration cho OAuth user.
- STEP 07: frontend và logout UX.
- STEP 08: Docker/Integration Tests và security verification toàn diện.

## 7. Chú ý triển khai trên ECS/Fargate

OAuth cookies và `state` phụ thuộc ASP.NET Core Data Protection. Khi scale nhiều backend instances, cần chia sẻ key ring và cấu hình trusted `ForwardedHeaders`/HTTPS termination đúng cách; **không** cấu hình proxy tin mọi `X-Forwarded-*` từ internet. Đây là một phần production deployment chưa thực hiện ở STEP 02.
