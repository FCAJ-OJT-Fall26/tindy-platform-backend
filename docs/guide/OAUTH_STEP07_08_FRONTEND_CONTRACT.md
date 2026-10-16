# OAuth STEP 07–08: Frontend contract và security review

Phạm vi: Local Register/Login, Google/GitHub OAuth Login, explicit account linking, stateless logout và authorization của onboarding. Tài liệu đối chiếu DTO, controllers, services, PostgreSQL stores và `../tindy-platform-frontend/src/services/apiClient.ts`; frontend chỉ được đọc để xác minh contract. Repository không có `Tindy_OAuth2_Authentication_Implementation_Plan.md`, nên phạm vi theo acceptance criteria của yêu cầu STEP 07–08.

## 1. Cấu hình kết nối và quy ước JSON

| Thành phần | Development |
| --- | --- |
| Frontend | `http://localhost:8443` hoặc `http://127.0.0.1:8443` |
| API dùng cho frontend/OAuth | `https://localhost:7190` |
| HTTP listener | `http://localhost:5217`; pipeline chuyển sang HTTPS |
| Frontend API base | `VITE_API_BASE_URL=https://localhost:7190` (không thêm `/api`) |
| Provider callbacks | `https://localhost:7190/signin-google`, `https://localhost:7190/signin-github` |
| Frontend callback | Origin đầu tiên trong `OAuth:Frontend:AllowedOrigins` + `/auth/oauth/callback` |

Gửi JSON bằng `Content-Type: application/json`; endpoint yêu cầu đăng nhập nhận `Authorization: Bearer <tindy_access_token>`. Không đưa JWT vào query string, fragment hoặc `authorization_url`. OAuth provider access token chỉ dùng trong backend và không trả frontend.

JSON success luôn có ba trường, kể cả khi không có dữ liệu:

```json
{ "success": true, "message": "Human-readable message", "data": null }
```

DTO và dictionary dùng `snake_case` (`access_token`, `display_name`, `is_onboarding_completed`, `authorization_url`, `has_local_password`). `traceId` là extension chẩn đoán của ProblemDetails, giữ nguyên tên này để tương thích wrapper. Không dùng nội dung `message` làm điều kiện nghiệp vụ. Wrapper hiện tại từ chối success thiếu `message` hoặc `data`.

## 2. Danh sách endpoint

`Active JWT` nghĩa là JWT hợp lệ của tài khoản còn tồn tại, `active`, chưa soft-delete; không yêu cầu hoàn thành onboarding.

| Method, URL | Authentication | Request | Success |
| --- | --- | --- | --- |
| `POST /api/auth/register` | Public | Register JSON bên dưới | `201`, token + user + `redirect_to` |
| `POST /api/auth/login` | Public | Login JSON bên dưới | `200`, token + user + `redirect_to` |
| `POST /api/auth/logout` | Active JWT | Không cần body | `200`, `data: null` |
| `GET /api/auth/google` | Public, browser navigation | Không có | `302` tới Google |
| `GET /api/auth/github` | Public, browser navigation | Không có | `302` tới GitHub |
| `POST /api/auth/oauth/exchange` | Public | `{ "ticket": "<one-time-ticket>" }` | `200`, token + user + `redirect_to` |
| `GET /api/auth/connections` | Active JWT | Không có | `200`, `has_local_password` + `providers` |
| `POST /api/auth/connections/{provider}/start` | Active JWT | Không cần body | `200`, `authorization_url` |
| `POST /api/auth/connections/confirm` | Active JWT, cùng UserId đã start | `{ "link_ticket": "<one-time-link-ticket>" }` | `200`, `linked` + `created` |
| `DELETE /api/auth/connections/{provider}` | Active JWT | Không cần body | `200`, `data: null` |
| `GET /api/me` | Active JWT | Không có | `200`, current user |

`{provider}` chỉ nhận `google` hoặc `github`, không phân biệt hoa/thường. Trong response `providers[].provider`, giá trị chuẩn là `Google` hoặc `GitHub`.

### Register và Login

```json
{
  "display_name": "Nguyen An",
  "university": "FPT University",
  "email": "an@example.com",
  "password": "<user-supplied-password>",
  "accept_terms": true
}
```

Register: `display_name` sau trim dài 1–100 ký tự; `university` 1–255; email hợp lệ tối đa 255 ký tự; password ít nhất 8 ký tự, tối đa 72 UTF-8 bytes, có chữ hoa, chữ thường, số và ký tự đặc biệt; phải chấp nhận điều khoản. Đây là request minh họa, placeholder password không phải dữ liệu để chạy thử.

```json
{ "identifier": "an@example.com", "password": "<user-supplied-password>" }
```

Login nhận email hoặc username trong `identifier`, trim và so sánh theo dạng chuẩn hóa không phân biệt hoa/thường. Username được thiết lập trong onboarding, không phải field của Register.

Register (`201`), Login (`200`) và OAuth Exchange (`200`) có cùng cấu trúc `data`:

```json
{
  "success": true,
  "message": "Logged in successfully.",
  "data": {
    "access_token": "<tindy-jwt>",
    "token_type": "Bearer",
    "expires_in": 3600,
    "user": {
      "user_id": "00000000-0000-0000-0000-000000000001",
      "display_name": "Nguyen An",
      "university": "FPT University",
      "email": "an@example.com",
      "username": null,
      "status": "active",
      "roles": ["member"],
      "is_onboarding_completed": false
    },
    "redirect_to": "/onboarding"
  }
}
```

Register có `message: "Account registered successfully."`; user mới luôn `redirect_to: "/onboarding"`. Login/Exchange trả `/discover` khi onboarding đã hoàn thành. `expires_in` là giây theo cấu hình JWT (3600 trong cấu hình mặc định), không hardcode ở frontend. OAuth `email` có thể `null`, `username` có thể `null`; OAuth chưa khai báo trường đại học trả `university: ""`. Tài khoản OAuth mới tạo User, Profile, member role và ExternalAuthAccount, không tạo local credential hoặc password giả.

### Connections và current user

`GET /api/auth/connections`:

```json
{
  "success": true,
  "message": "Account connections retrieved successfully.",
  "data": {
    "has_local_password": true,
    "providers": [
      { "provider": "Google", "provider_username": null, "linked_at": "2026-10-16T00:00:00Z" }
    ]
  }
}
```

`providers` có thể là `[]`; `provider_username` có thể `null`. `has_local_password` cho biết có local credential, không được suy từ email.

`POST /api/auth/connections/google/start`:

```json
{
  "success": true,
  "message": "Account linking authorization is ready.",
  "data": {
    "authorization_url": "https://localhost:7190/api/auth/connections/google/challenge?request=<one-time-start-token>"
  }
}
```

`POST /api/auth/connections/confirm`:

```json
{ "success": true, "message": "Account linked.", "data": { "linked": true, "created": true } }
```

Nếu identity đã thuộc cùng tài khoản, trả `created: false`, `linked: true`, `message: "Account was already linked."`. Mỗi link ticket vẫn chỉ được consume một lần; đây không phải quyền replay ticket.

`DELETE /api/auth/connections/google`:

```json
{ "success": true, "message": "Provider disconnected.", "data": null }
```

`GET /api/me`:

```json
{
  "success": true,
  "message": "Account information retrieved successfully.",
  "data": {
    "user_id": "00000000-0000-0000-0000-000000000001",
    "username": null,
    "display_name": "Nguyen An",
    "university": null,
    "email": null,
    "status": "active",
    "roles": ["member"],
    "is_onboarding_completed": false
  }
}
```

Trong `/me`, `university`, `email` và `username` đều nullable. Frontend dùng trạng thái hiện tại từ API, không suy account state từ JWT cũ.

## 3. OAuth Login và Linking redirects

Login:

1. Điều hướng toàn trang đến `GET /api/auth/google` hoặc `/api/auth/github`; không dùng `apiRequest` để parse challenge thành JSON.
2. Middleware xử lý provider code tại `/signin-google` hoặc `/signin-github`, kiểm tra protected state/correlation, rồi chuyển nội bộ đến `/api/auth/oauth/complete`.
3. Backend redirect đến `<frontend-origin>/auth/oauth/callback#ticket=<opaque-ticket>`.
4. Frontend đọc fragment và xóa ngay bằng `history.replaceState`, sau đó POST `/api/auth/oauth/exchange` với `{ "ticket": "..." }`, `requiresAuth: false`.
5. Lưu Tindy JWT từ HTTPS response body, cập nhật auth state và chuyển theo `redirect_to`.

Login ticket là 32 random bytes, Base64Url 43 ký tự, TTL **90 giây**, chỉ hash SHA-256 được lưu PostgreSQL. Invalid/expired/replayed ticket bị từ chối. Conditional delete chỉ cho một exchange thắng khi gọi đồng thời. Backend đọc lại account state, profile và roles sau khi consume; ticket thất bại sau consume cũng không dùng lại.

Linking:

1. Đã đăng nhập: POST `/api/auth/connections/{provider}/start` bằng Bearer JWT.
2. Điều hướng toàn trang tới đúng `authorization_url` backend trả; URL chứa opaque start token, không chứa JWT. Endpoint nội bộ `GET /api/auth/connections/{provider}/challenge?request=...` consume start token theo provider rồi challenge OAuth. Start transaction TTL **5 phút** và không thể khởi chạy lại.
3. Middleware provider callback chuyển đến `/api/auth/connections/complete`; backend chỉ tạo completion ticket, chưa gắn identity vào tài khoản.
4. Redirect `<frontend-origin>/auth/oauth/callback#link_ticket=<opaque-ticket>`; frontend đọc và xóa fragment ngay.
5. POST `/api/auth/connections/confirm` bằng JWT của **cùng UserId đã start** và `{ "link_ticket": "..." }`. Completion ticket TTL **90 giây** kể từ provider completion; chỉ hash được lưu, consume một lần.
6. Reload connections. JWT của User khác không confirm được; identity đã thuộc tài khoản khác hoặc khác identity cùng provider trả conflict. Email trùng không tự động link. Unlink phải giữ ít nhất một phương thức đăng nhập, kể cả concurrent requests.

Redirect lỗi ở bước hoàn tất login dùng `/auth/oauth/callback?error=oauth_failed`, `account_link_required` hoặc `account_unavailable`. Linking dùng `/auth/oauth/callback?flow=link&error=oauth_link_failed` hoặc `oauth_link_expired`. Các giá trị này là UI redirect codes, khác JSON `error_code`. Không có JWT/provider access token trong các URL này.

Provider cancellation, callback failure hoặc invalid state/correlation do middleware bắt sẽ trả HTTP `400` ProblemDetails `OAUTH_HANDSHAKE_FAILED` tại callback backend; không phải mọi lỗi đều redirect frontend. UI cần có khả năng quay lại trang đăng nhập và bắt đầu lại.

## 4. Error contract và rate limits

JSON lỗi dùng ProblemDetails, không phải success envelope:

```json
{
  "type": "about:blank",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Missing, invalid or expired access token.",
  "instance": "/api/me",
  "error_code": "AUTH_UNAUTHORIZED",
  "traceId": "<request-trace-id>"
}
```

`type` có thể vắng hoặc là framework status URI; parse theo status và `error_code`. Validation bổ sung `errors: { "field_name": ["..."] }`; onboarding completion thiếu dữ liệu bổ sung `missing_steps: [1, 2]`. Không log request chứa password/ticket hay toàn bộ token response ở frontend.

| HTTP | `error_code` | Trường hợp |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | Body/field không hợp lệ, thiếu ticket |
| 400 | `OAUTH_PROVIDER_UNSUPPORTED` | Provider ngoài Google/GitHub |
| 400 | `OAUTH_LINK_REQUEST_INVALID` | Link start token invalid, expired, đã dùng hoặc sai provider |
| 400 | `OAUTH_HANDSHAKE_FAILED` | Provider cancellation/failure hoặc state/correlation không hợp lệ |
| 401 | `AUTH_UNAUTHORIZED` | Missing/invalid/expired JWT tại protected endpoint |
| 401 | `AUTH_INVALID_CREDENTIALS` | Sai local credentials; invalid/expired/replayed login hoặc completion ticket; confirm sai UserId |
| 403 | `AUTH_FORBIDDEN` | JWT hợp lệ nhưng active account policy không thỏa (suspended/deleted/missing account) |
| 403 | `ONBOARDING_REQUIRED` | Tài khoản active chưa hoàn thành onboarding gọi business endpoint |
| 404 | `NOT_FOUND` | Unlink provider chưa liên kết |
| 409 | `AUTH_EMAIL_ALREADY_EXISTS`, `USERNAME_ALREADY_EXISTS` | Email/username đã được dùng |
| 409 | `CONFLICT` | Identity thuộc user khác, cùng provider khác identity, unlink phương thức cuối hoặc concurrent account changes |
| 422 | `ONBOARDING_INCOMPLETE`, `BUSINESS_RULE_VIOLATION` | Onboarding thiếu/bất hợp lệ hoặc vi phạm rule ứng dụng |
| 429 | `AUTH_TOO_MANY_ATTEMPTS` | Vượt rate limit theo IP |
| 503 | `OAUTH_UNAVAILABLE` | OAuth disabled hoặc provider scheme chưa có |
| 500 | `INTERNAL_SERVER_ERROR` | Unexpected failure, thông báo chung không chứa exception detail |

Rate limits hiện tại: local login **10/phút/IP**, OAuth start/challenge **20/phút/IP**, OAuth exchange/link confirm **10/phút/IP**, không queue. Các endpoint cùng policy chia sẻ quota theo IP; linking start và challenge đều tính lượt. Register không được thêm rate limit trong phạm vi thay đổi này.

## 5. Logout và onboarding

POST `/api/auth/logout` chỉ yêu cầu Active JWT, dùng được trước khi hoàn thành onboarding. Success:

```json
{ "success": true, "message": "Logged out successfully.", "data": null }
```

Logout dọn cookie OAuth tạm `Tindy.External` nếu scheme đã được đăng ký. Cookie này không phải API session; JWT Bearer vẫn là default authentication. Logout **không revoke JWT**, không blacklist/refresh token, và JWT còn dùng được tới expiry nếu account vẫn thỏa policy. Không hứa hẹn “logout mọi thiết bị”.

Frontend phải xóa token trong storage **và** reset auth/user state trong `finally`, kể cả logout lỗi, JWT hết hạn, server không truy cập được hoặc CORS/network lỗi. `apiClient.ts` hiện clear stored token khi protected request trả 401; điều đó chưa thay thế việc reset auth state trong logout. Register/Login/Logout/Exchange/Connections và OAuth handshakes dùng `Cache-Control: no-store`; redirects nhạy cảm có `Referrer-Policy: no-referrer`.

`/api/me`, `/api/me/onboarding`, lưu các bước, `/api/auth/connections` và logout dùng `AuthenticatedOnly` (JWT + active account). Default/fallback authorization của business APIs còn yêu cầu completed onboarding; token cũ không bỏ qua kiểm tra database. Giữ nguyên 8 bước: GET `/api/me/onboarding`, PUT `/api/me/onboarding/1` đến `/7`, POST `/api/me/onboarding/complete` với `{ "confirmed": true }` (optional `bio`). Completion không hợp lệ trả 422 `ONBOARDING_INCOMPLETE`; completion thành công cập nhật `is_onboarding_completed` và `redirect_to: "/discover"`. Không cần đổi cơ chế JWT để qua gate sau khi hoàn thành.

## 6. CORS, allowlist và environment

Mẫu committed `src/Tindy.Api/appsettings.Development.example.json` có cả `http://localhost:8443` và `http://127.0.0.1:8443` trong `Cors:AllowedOrigins` và `OAuth:Frontend:AllowedOrigins`. Merge hai section của mẫu vào `appsettings.Development.json` local; file local này bị gitignore, không ghi đè các cấu hình đang có. Hai host là hai origin khác nhau; frontend callback dùng **entry đầu tiên** (`localhost`) chứ không chọn từ request. Chạy flow nhất quán trên một host để JWT/storage không nằm ở origin khác.

CORS policy `TindyFrontend` dùng exact origins, `AllowAnyHeader` và `AllowAnyMethod`, cho OPTIONS preflight của `Authorization`/JSON; pipeline đặt CORS trước rate limiting/authentication/authorization. Không bật `AllowAnyOrigin` hoặc `AllowCredentials`. API fetch dùng Bearer header, không yêu cầu `credentials: "include"`; cookie tạm được browser xử lý trong OAuth top-level navigation. Dùng trực tiếp HTTPS API base để tránh HTTP preflight redirect và bảo đảm Secure cookies hoạt động.

OAuth phải được bật riêng bằng `OAuth:Enabled=true` và cung cấp Google/GitHub ClientId/ClientSecret, JWT signing key, database connection bằng User Secrets/environment/secret manager. Không đưa secret vào committed appsettings. Mặc định OAuth vẫn disabled; local register/login và JWT tiếp tục hoạt động. Origin allowlist khi bật OAuth chỉ nhận HTTPS, hoặc HTTP loopback development; từ chối path/query/fragment/userinfo. Không có arbitrary `returnUrl`. Production settings không thay đổi; deployment cần cấu hình origin và registered provider callback riêng.

## 7. Thay đổi STEP 07–08 và security review

| File/nhóm | Thay đổi và lý do |
| --- | --- |
| `src/Tindy.Api/Controllers/OAuthConnectionsController.cs` | Bổ sung `message` cho List/Start và `data: null` cho Unlink; giữ URL và payload cũ; chuẩn hóa OAuth lỗi thành ProblemDetails |
| `src/Tindy.Api/Controllers/AuthController.cs` | Logout async dọn external cookie khi scheme tồn tại, giữ Active JWT và stateless semantics; no-store cho register/login/logout |
| `src/Tindy.Api/Controllers/OAuthController.cs`, `Extensions/OAuthAuthenticationExtensions.cs` | Chuẩn hóa unavailable/handshake failure thành safe ProblemDetails; lọc log GitHub từ Warning để tránh log upstream error body; giữ callbacks, state/correlation, fragment tickets |
| `src/Tindy.Api/ExceptionHandlers/GlobalExceptionHandler.cs` | Log unexpected failure bằng exception type + trace ID, tránh ghi toàn bộ exception chứa upstream credential/token/code |
| `src/Tindy.Api/appsettings.Development.example.json` | Mẫu CORS + OAuth frontend origin allowlist local để merge vào development config bị gitignore; không bật OAuth hay đưa secrets vào config |
| `tests/Tindy.Tests/Integration/OAuthHttpApiFactory.cs`, `OAuthHttpContractTests.cs` | HTTP contract với real JWT/OAuth middleware và offline provider backchannel; kiểm tra CORS, logout, rate limit, callback và token/log canary |
| `tests/Tindy.Tests/Integration/OAuthStoreSecurityTests.cs` | PostgreSQL ticket expiry/replay/hash/TTL, concurrent consume và concurrent unlink; đồng bộ read để buộc requests cạnh tranh |
| `tests/Tindy.Tests/Security/AuthServiceTests.cs` | Local auth chạy thực tế qua AuthService + BCrypt, repository giả không cần Docker |
| `tests/Tindy.Tests/Security/ExternalAuthServiceTests.cs`, `OAuthLoginFlowServiceTests.cs` | Bổ sung Local→Google/GitHub, first/repeat login và inactive/ticket validation |
| `tests/Tindy.Tests/Integration/TindyApiFactory.cs` | Cấp JWT/database test settings trước startup, tắt OAuth thật và dùng ephemeral Data Protection; không phụ thuộc User Secrets hay key ring của máy developer |
| Tài liệu này | Contract bàn giao frontend, security findings, test matrix, manual checklist và giới hạn xác minh |

Findings được xử lý: success envelope thiếu field làm `apiClient` reject response; OAuth ad hoc error không tương thích ProblemDetails; logout chưa dọn temporary cookie; response token local chưa no-store; OAuth development allowlist thiếu; logging nguyên exception có nguy cơ lộ upstream detail; test canary phát hiện GitHub handler log upstream error body ở Information và đã bổ sung filter riêng. Không đổi API URLs, schema, migration hoặc production settings; giữ API → Application → Infrastructure.

Các bảo vệ đã có và được giữ: provider identity khóa bằng provider + immutable subject (không email); không auto-link email; OAuth-only account không local credential; provider tokens không persist (`SaveTokens=false`) hoặc xuất ra frontend; fixed allowlisted redirects; external cookie HttpOnly/Secure/SameSite=Lax, correlation cookie HttpOnly/Secure/SameSite=None; temporary cookie cleanup sau callback; hashed short-lived tickets, atomic consume, link user/provider binding; serializable unlink giữ phương thức cuối; re-check active account/onboarding từ database; HS256 issuer/audience/lifetime/signature validation; safe error codes và rate limits.

Giữ cấu hình log `Microsoft.AspNetCore` ở `Warning`: [ASP.NET Core RemoteAuthenticationHandler](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.8/src/Security/Authentication/Core/src/RemoteAuthenticationHandler.cs) có gọi logger khi remote authentication thất bại, và [LoggingExtensions](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.8/src/Security/Authentication/Core/src/LoggingExtensions.cs) định nghĩa log failure ở mức Information. Handler GitHub thuộc category `AspNet.Security.OAuth.GitHub`, nên code đăng ký OAuth bổ sung filter Warning riêng cho category này. Không bật request/response body logging cho credentials/ticket/token.

Giới hạn review: logging type/trace trong handler và tests không chứng minh toàn bộ reverse proxy/APM/provider logs đều được redacted. Start token bắt buộc nằm trong backend authorization URL; cần tránh ghi query strings của flow này ở hạ tầng. Browser fragment cần frontend xóa ngay. Stateless logout không bảo vệ nếu JWT đã bị sao chép; JWT storage hiện tại là localStorage nên vẫn phụ thuộc XSS defenses của frontend. Không thay đổi token storage, bổ sung revoke, refresh token, MFA hay email verification trong STEP 07–08. Restore phát hiện cảnh báo `NU1902`/`NU1903` ở dependency hiện có (`System.Security.Cryptography.Pkcs` 10.0.0/10.0.10 và `System.Security.Cryptography.Xml` 10.0.10); đây là security follow-up riêng, chưa nâng package trong phạm vi này.

## 8. Test matrix và verification

Tests không gọi Google/GitHub thật bằng credentials thật. Unit/service tests dùng fake repositories/stores; simulated HTTP OAuth tests dùng provider backchannel giả; PostgreSQL integration tests dùng `TindyApiFactory` với Testcontainers `postgres:16-alpine`, random test signing key và database tạm riêng, không dùng database developer.

| Yêu cầu | Evidence/test file | Trạng thái thực thi |
| --- | --- | --- |
| Local register/login email + username; 8-step onboarding/gate | `Security/AuthServiceTests.cs`, `Integration/AuthenticationOnboardingTests.cs`, `AuthenticationDatabaseCompatibilityTests.cs` | Local service 10/10 Pass; PostgreSQL onboarding/compatibility Blocked |
| OAuth first/repeat login, no credential, hidden/null/unverified email, email conflict, suspended/deleted | `Security/ExternalAuthServiceTests.cs`, `OAuthLoginFlowServiceTests.cs` | Pass (unit/service) |
| Invalid/expired/replayed ticket, atomic concurrent exchange, stored hashes/TTL | `Integration/OAuthStoreSecurityTests.cs`, `Security/OAuthLoginFlowServiceTests.cs` | Unit Pass; PostgreSQL store Blocked |
| Local→Google/GitHub, Google→GitHub, other identity owner | `Security/ExternalAuthServiceTests.cs`, `AccountConnectionsServiceTests.cs` | Pass (unit/service) |
| Provider/user-bound link start/completion, expiry/replay/concurrency; final-method/concurrent unlink | `Integration/OAuthStoreSecurityTests.cs`, `Security/AccountConnectionsServiceTests.cs` | Unit Pass; PostgreSQL concurrency Blocked |
| Active account before/after onboarding; suspended/deleted rejection | `Security/ActiveAccountHandlerTests.cs`, `Integration/OAuthHttpContractTests.cs`, `AuthenticationOnboardingTests.cs` | Unit + simulated HTTP Pass; PostgreSQL Blocked |
| HTTP envelope, no-store, logout stateless/cookie cleanup, CORS, actual OAuth middleware với simulated provider, cancellation/state/correlation/failure, safe URL/log, rate limits | `Integration/OAuthHttpContractTests.cs` + `OAuthHttpApiFactory.cs`, `Integration/RateLimitTests.cs` | Simulated HTTP 26/26 Pass; existing PostgreSQL rate-limit suite Blocked |
| Password hashing/JWT validation and request validation | `Security/CryptoTests.cs`, `Validation/RequestValidationTests.cs` | Pass |

Chạy từ backend root:

```powershell
dotnet restore .\Tindy.slnx
dotnet build .\Tindy.slnx
dotnet test .\Tindy.slnx
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter "FullyQualifiedName!~Tindy.Tests.Integration"
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter "Category=DockerFree"
dotnet test .\Tindy.slnx --no-build --no-restore --filter "FullyQualifiedName~Tindy.Tests.Security|FullyQualifiedName~Tindy.Tests.Validation|FullyQualifiedName~OAuthHttpContractTests"
git diff --check
git status --short
```

Kết quả thực thi ngày **2026-10-16**, .NET SDK **10.0.302**:

| Kiểm tra | Kết quả thực tế |
| --- | --- |
| `dotnet restore .\Tindy.slnx` | Pass sau khi cấp quyền mạng NuGet; lần sandbox ban đầu bị NU1301 |
| `dotnet build .\Tindy.slnx --no-restore` | Pass, 0 errors, 17 warnings NU1902/NU1903 từ dependency hiện có |
| `dotnet test .\Tindy.slnx --no-restore` (có TRX logger) | Exit 1: 150 test, 100 Pass, 50 Fail do cùng lỗi constructor Docker fixture; 50 test PostgreSQL chưa tới bước chạy assertions |
| Filtered solution test không cần Docker (lệnh trên, có TRX logger) | Pass, 100/100: 74 unit/security/validation + 26 HTTP simulated-provider; 0 Fail, 0 Skip |
| PostgreSQL/OAuth store security mới | 19 cases đã compile; Blocked bởi Docker fixture |
| `git diff --check` | Pass |

Reports: `TestResults/step07-08-full.trx`, `TestResults/step07-08-full.console.log` và `TestResults/step07-08-docker-free.trx` (gitignored). Cả 50 failures có cùng nguyên nhân `TindyApiFactory` constructor không kết nối được Docker; kiểm tra `docker info` ngoài sandbox cũng xác nhận pipe `dockerDesktopLinuxEngine` không tồn tại. Không reset database developer hoặc khởi chạy provider thật. Không khẳng định real PostgreSQL concurrency tests Pass; in-memory/service doubles không thay thế bằng chứng atomic consume/serializable transaction.

## 9. Manual testing checklist

- [ ] Chạy frontend `http://localhost:8443` và API HTTPS; OPTIONS từ đúng origin cho `Authorization, Content-Type` thành công; origin lạ không được CORS grant.
- [ ] Local register/login email, complete onboarding để có username, login username; xác minh envelope, snake_case, no-store và `redirect_to`.
- [ ] Google/GitHub first login tạo đúng aggregate, không local credential; GitHub hidden email/null email vẫn hoạt động; repeat login giữ UserId.
- [ ] Verify provider callbacks giữ `/signin-google`, `/signin-github`; browser fragment chỉ có ticket, không JWT/access token; frontend xóa fragment ngay, exchange lấy JWT trong HTTPS body.
- [ ] Ticket invalid, quá 90 giây, replay và concurrent exchange: tối đa một thành công. Account suspended/deleted sau issue không nhận JWT.
- [ ] Provider email trùng account local không tự link; UI hướng người dùng login account hiện có rồi explicit link.
- [ ] Local→Google, Local→GitHub, Google→GitHub: start bằng JWT, navigate URL, callback link ticket, confirm bằng cùng JWT và reload connections.
- [ ] Confirm bằng UserId khác, replay/expired link ticket, identity thuộc account khác, khác identity cùng provider đều bị từ chối; callback đơn thuần chưa link.
- [ ] Unlink khi còn local password/another provider thành công; final-method unlink bị chặn; concurrent unlink còn ít nhất một phương thức.
- [ ] New OAuth user gọi `/me`, onboarding, connections và logout được; business gate 403 `ONBOARDING_REQUIRED`; đủ 8 bước chuyển `/discover`, JWT hiện có qua gate; inactive account bị chặn.
- [ ] Logout trước/sau onboarding trả `200` và `data: null`; external cookie bị dọn khi đã đăng ký; OAuth disabled logout vẫn hoạt động; JWT cũ còn dùng được tới expiry nếu chủ động gửi lại.
- [ ] Frontend logout luôn xóa token/auth state khi request thành công, 401, 403 hoặc network error.
- [ ] Cancel provider, callback/provider failure và invalid state/correlation trả safe errors; vượt login/start/exchange quota trả 429 ProblemDetails.
- [ ] Inspect response URL/browser history/application logs: không JWT/provider token/secret; fragment được xóa, infra không ghi start-token query; unexpected exception không lộ message/stack trong response hoặc handler log.

Chưa xác minh bằng manual checklist: live Google/GitHub consent và provider app registrations, browser Secure/SameSite behavior, frontend callback/state implementation, production reverse proxy/TLS/CORS/APM và secret-manager deployment. Đây là các bước chạy thật riêng, không được suy từ provider simulation.

Commit đề xuất (chưa commit/push):

- `feat(auth): integrate oauth frontend contract and logout`
- `test(auth): cover oauth flows linking and security scenarios`
