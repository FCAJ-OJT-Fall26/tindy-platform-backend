# Tindy — STEP 06: Integrate JWT Authorization And Onboarding

> **Scope:** Bản rà soát/chỉnh sửa nhỏ, tiếp nối STEP 04–05. Giữ nguyên JWT Bearer và 8 bước onboarding của Tindy. **Không migration, không sửa Local Register/Login, không thêm OAuth endpoint.**

## 1. Kết quả audit source hiện tại

| Quy tắc | Source hiện tại | Kết luận |
|---|---|---|
| Local & OAuth cấp JWT dùng chung `IJwtTokenService` | `AuthService` / `OAuthLoginFlowService` | Đã đạt; không thay token service |
| JWT `sub`, `role`, issuer, audience, thời hạn | `JwtTokenService` + `AddJwtBearer` | Đã đồng nhất; giữ nguyên |
| Chặn inactive/deleted và thiếu roles **trước OAuth ticket exchange phát JWT** | `OAuthLoginFlowService.ExchangeAsync` | Đã đạt; giữ nguyên |
| Chặn inactive/deleted trước Local Login phát JWT | `AuthService.LoginAsync` | Đã đạt; giữ nguyên |
| OAuth-only không cần Local Credential | `OnboardingService.GetCurrentUserAsync` (`PrimaryEmail ?? Credential?.Email`) | Đã đạt; giữ nguyên |
| OAuth User mới đi `/onboarding`, User cũ hoàn thành đi `/discover` | `ExternalAuthService` và `OAuthLoginFlowService` | Đã đạt; giữ nguyên |
| Bio chưa có AI để `null` | `ExternalAuthService` và `OnboardingService` | Đã đạt; giữ nguyên |
| Default / fallback policy chặn User chưa hoàn thành onboarding | `ServiceCollectionExtensions` / `OnboardingCompletedHandler` | Đã đạt; giữ nguyên |
| `AuthenticatedOnly` kiểm tra **User vẫn active** | Policy cũ chỉ kiểm tra JWT | **Bổ sung `ActiveAccountRequirement` và handler** |
| OAuth Exchange response giống Local Login | OAuth response chưa có `university` | **Bổ sung trường `university`**, giữ trường cũ |

Trong Tindy, onboarding gồm **7 API lưu bước 1–7 và API Complete (bước 8)**. Không cần tạo bản ghi `user_onboarding_steps` số 8; kiểm tra hoàn thành dựa trên `users.is_onboarding_completed`.

## 2. Thay đổi mã nguồn

| File | Thay đổi |
|---|---|
| `src/Tindy.Api/Authorization/ActiveAccountRequirement.cs` | Điều kiện account đang hoạt động (mới) |
| `src/Tindy.Api/Authorization/ActiveAccountHandler.cs` | Đọc trạng thái thực từ DB bằng `IOnboardingAccessReader`; pending onboarding vẫn được vào các API setup; blocked/deleted thì không (mới) |
| `src/Tindy.Api/Authorization/OnboardingPolicies.cs` | Giải thích lại ý nghĩa `AuthenticatedOnly` |
| `src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs` | Thêm requirement vào named policy và register authorization handler; **không thay JWT default**, default/fallback onboarding policies |
| `src/Tindy.Application/Models/Authentication/OAuthExchangeResponse.cs` | Bổ sung `University` vào `OAuthExchangeUser` |
| `src/Tindy.Application/Services/OAuthLoginFlowService.cs` | Điền `University` từ `UserProfile`, dùng `string.Empty` nếu người dùng chưa onboarding |
| `tests/Tindy.Tests/Security/ActiveAccountHandlerTests.cs` | Unit tests cho trạng thái active/onboarding/blocked/bad claims |
| `tests/Tindy.Tests/Security/OAuthLoginFlowServiceTests.cs` | Test OAuth-only thiếu email + trường University + thiếu Profile |

### Thay đổi API có chủ ý

- OAuth exchange **bổ sung** trường `data.user.university` (chuỗi trống khi chưa có). Không xóa/đổi kiểu các trường cũ; `data.user.email` vẫn nullable.
- API dùng `AuthenticatedOnly` (`/api/me`, onboarding, logout, các API account connections) từ nay yêu cầu **JWT hợp lệ + User active trong database**, nhưng **không yêu cầu** hoàn thành onboarding. JWT của tài khoản suspended/deleted/missing sẽ bị từ chối (thường là HTTP 403 do đã authenticate nhưng không đủ điều kiện). Đây là thay đổi bảo mật có chủ ý, không phải đổi schema.
- JWT hiện có **không bị thu hồi/blacklist** khi logout; các API có policy DB sẽ kiểm tra trạng thái hiện tại. Không thay token lifetime/issuer/audience/signature.

### Giữ nguyên

- Endpoint/DTO `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`.
- Google/GitHub callback, ticket exchange, linking/unlinking.
- `JwtTokenService`, `JwtBearerDefaults.AuthenticationScheme`, `NameClaimType = "sub"`, `RoleClaimType = "role"`.
- Onboarding yêu cầu 8 bước; `Bio = null` hợp lệ khi chưa có AI.
- Các API `[AllowAnonymous]` cho Register/Login/OAuth start + callback/exchange và public catalogs như source đang cấu hình.

## 3. Áp dụng patch từ root backend trong PowerShell 7

**Điều kiện:** Source của bạn đã có STEP 05 (và migration STEP 05 đã được xử lý nếu bạn sử dụng linking). Nếu STEP 05 chưa được áp dụng, **không** ép patch này vào source cũ; hãy kiểm tra lại thứ tự trước.

```powershell
cd "D:\FPT\Semester 6\OJT\aws\tindy-platform\tindy-backend-platform"
git status
git -c core.whitespace=cr-at-eol apply --check .\Tindy_STEP06_JWT_Authorization_Onboarding.patch
git -c core.whitespace=cr-at-eol apply .\Tindy_STEP06_JWT_Authorization_Onboarding.patch

dotnet build .\Tindy.slnx
```

Patch không chứa migration. **Không chạy** `dotnet ef migrations add`, `database drop` hoặc `database update` cho riêng STEP 06.

## 4. Kiểm thử không cần Docker

```powershell
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj `
    --filter "FullyQualifiedName~ActiveAccountHandlerTests|FullyQualifiedName~OAuthLoginFlowServiceTests|FullyQualifiedName~CryptoTests"

git -c core.whitespace=cr-at-eol diff --check
git status
git diff --stat
```

### Manual API smoke tests qua Postman

1. **Local login:** `POST https://localhost:7190/api/auth/login` xác nhận JWT vẫn có, `redirect_to` phù hợp; không gửi token vào chat.
2. **OAuth exchange:** đăng nhập Google/GitHub lấy ticket, `POST /api/auth/oauth/exchange` với ticket mới, xác nhận `data.user.university` có mặt, `email` có thể null, `redirect_to=/onboarding` với User mới.
3. **`GET /api/me`:** sử dụng Bearer JWT OAuth-only khi chưa onboarding. Phải trả 200, kể cả không có `user_credentials`.
4. **`GET /api/dev/auth-check`:** với JWT User active chưa onboarding trả 200 trong Development (policy `AuthenticatedOnly`).
5. **`GET /api/dev/onboarding-gate-check`:** với JWT User chưa onboarding trả 403 và `ONBOARDING_REQUIRED`; sau khi hoàn thành onboarding 8 bước trả 200 (Dev only).
6. **User bị khóa/xóa mềm:** dùng JWT đã cấp trước đó đến `/api/me` hoặc `/api/auth/connections`: phải bị từ chối sau khi status đổi trong DB, ngay cả JWT chưa hết hạn. *Chỉ thử trên tài khoản test* và khôi phục trạng thái sau đó.
7. **No-token:** gọi `/api/me` trả 401. Account Connections chỉ chạy được sau khi STEP 05 có migration/database đúng.

Lưu ý: Test manual trong Development không chứng minh mọi race condition. Integration Tests/Testcontainers/PostgreSQL concurrency tests giữ lại cho STEP 08 theo thỏa thuận.

## 5. Definition of Done

- [ ] Patch apply thành công trên source sau STEP 05, không đè code local team đang chỉnh.
- [ ] `dotnet build .\Tindy.slnx` thành công.
- [ ] Scoped Unit Tests xanh (không cần Docker).
- [ ] Local register/login JWT không đổi.
- [ ] OAuth exchange trả cùng Bearer JWT scheme; response thêm `university` mà không mất các trường cũ.
- [ ] OAuth-only User (không Credential, không email) đọc `/api/me` không null-reference.
- [ ] `AuthenticatedOnly`: User chưa onboarding nhưng active được vào API setup, User blocked/deleted không được vào.
- [ ] Default & fallback policy vẫn chặn API business khi onboarding chưa xong.
- [ ] Git diff đã review, commit chỉ chứa STEP 06.

## 6. Commit

```powershell
git add src/Tindy.Api/Authorization/ActiveAccountRequirement.cs `
        src/Tindy.Api/Authorization/ActiveAccountHandler.cs `
        src/Tindy.Api/Authorization/OnboardingPolicies.cs `
        src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs `
        src/Tindy.Application/Models/Authentication/OAuthExchangeResponse.cs `
        src/Tindy.Application/Services/OAuthLoginFlowService.cs `
        tests/Tindy.Tests/Security/ActiveAccountHandlerTests.cs `
        tests/Tindy.Tests/Security/OAuthLoginFlowServiceTests.cs `
        docs/guide/OAUTH_STEP06_JWT_ONBOARDING.md

git diff --cached --stat
git commit -m "feat(auth): unify oauth jwt and onboarding behavior"
```

**Không commit** file `.patch`, token, Client Secret hay file SQL/ log tạm ở root.
