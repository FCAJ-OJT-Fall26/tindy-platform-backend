# STEP 13 - Onboarding - Enforce Completion Access Policy

## Mục tiêu

Backend kiểm tra **trạng thái thật trong PostgreSQL trên mỗi lần gọi API nghiệp vụ**: JWT hợp lệ **và** tài khoản active, chưa bị soft-delete, `users.is_onboarding_completed = true` mới được phép truy cập. Không tin claim trạng thái onboarding trong JWT hoặc localStorage.

Phạm vi: không thay đổi Entity, EF migrations, JWT signing, Registration/Login/Logout nghiệp vụ hay Onboarding STEP 12 (bao gồm `bio` tùy chọn do user sửa).

## Thiết kế

- `Application/Interfaces/Repositories/IOnboardingAccessReader`: hợp đồng đọc trạng thái.
- `Application/Models/Authorization/OnboardingAccessStatus`: `Completed`, `Incomplete`, `InactiveOrMissing`.
- `Infrastructure/Repositories/OnboardingAccessReader`: projection `AsNoTracking` từ `users`, một lần truy vấn DB/request được bảo vệ.
- `Api/Authorization/OnboardingCompletedRequirement`: custom authorization requirement.
- `Api/Authorization/OnboardingCompletedHandler`: đọc `sub` từ JWT, truy vấn DB và cấp/không cấp quyền.
- `Api/Authorization/OnboardingAuthorizationResultHandler`: tạo lỗi `403 ONBOARDING_REQUIRED` khi đúng nguyên nhân; các trường hợp khác giữ nguyên 401/403 đã có.
- `AuthorizationOptions.DefaultPolicy` và `FallbackPolicy`: bảo vệ các API bằng JWT + Onboarding Completed khi không có policy miễn trừ rõ ràng.
- `OnboardingPolicies.AuthenticatedOnly`: yêu cầu JWT nhưng **không** yêu cầu hoàn thành onboarding.

## Ngoại lệ được phép

| Endpoint | Policy | Đặc điểm |
| --- | --- | --- |
| `POST /api/auth/register` | `[AllowAnonymous]` | Công khai |
| `POST /api/auth/login` | `[AllowAnonymous]` | Công khai, giữ rate limiting |
| `POST /api/auth/logout` | `AuthenticatedOnly` | Đã login, chưa xong onboarding vẫn logout được |
| `GET /api/me` | `AuthenticatedOnly` | Lấy thông tin user để frontend điều hướng |
| `GET /api/me/onboarding` | `AuthenticatedOnly` | Lấy draft |
| `PUT /api/me/onboarding/1..7` | `AuthenticatedOnly` | Lưu bước |
| `POST /api/me/onboarding/complete` | `AuthenticatedOnly` | Hoàn thành bước 8 |
| `GET /api/health` | `[AllowAnonymous]` | Health check |
| `/openapi/tindy.json` | `.AllowAnonymous()` | Development only |
| `GET /api/dev/auth-check` | `AuthenticatedOnly` | JWT smoke test, DEV only |
| `GET /api/dev/onboarding-gate-check` | DefaultPolicy | Gate smoke test, DEV only |
| API nghiệp vụ mới | Default/Fallback policy | JWT + hoàn tất onboarding |

**Cẩn thận:** `[AllowAnonymous]` bỏ qua **cả JWT và onboarding gate**. Chỉ đặt trên endpoint công khai được review, không đặt trên controller nghiệp vụ. Một policy cụ thể khác có thể thay thế DefaultPolicy; khi khai báo `[Authorize(Roles = "...")]` hoặc policy RBAC riêng, cần kết hợp với Onboarding requirement để tránh bypass. Không lạm dụng `AuthenticatedOnly` cho API nghiệp vụ.

## HTTP errors

- Không có token / token sai / hết hạn: **401** `AUTH_UNAUTHORIZED` (JWT Bearer OnChallenge).
- Có JWT hợp lệ, account active nhưng chưa hoàn thành onboarding: **403** `ONBOARDING_REQUIRED`.
- Có JWT hợp lệ nhưng tài khoản không active / soft-deleted / không tồn tại: **403** `AUTH_FORBIDDEN` tại gate (không phải `ONBOARDING_REQUIRED`).
- Có JWT hợp lệ và đã hoàn thành: endpoint được chạy; kết quả của endpoint quyết định HTTP 200/201/...
- Lỗi DB: không fail-open; middleware không cấp quyền khi truy vấn thất bại.

Ví dụ lỗi onboarding:

```json
{
  "title": "Forbidden",
  "status": 403,
  "detail": "You must complete all 8 onboarding steps to access this feature.",
  "instance": "/api/dev/onboarding-gate-check",
  "error_code": "ONBOARDING_REQUIRED",
  "traceId": "..."
}
```

## Áp dụng patch

> Chạy trong thư mục gốc `tindy-backend-platform` trên Windows PowerShell 7.

```powershell
git status
git apply --check .\STEP13_Onboarding_Changes.patch
git apply .\STEP13_Onboarding_Changes.patch
dotnet restore
dotnet build
```

Nếu `git apply --check` báo conflict, **không ghi đè**: so sánh file local với source hướng dẫn. Patch dựa trên snapshot STEP 12 trước cập nhật Bio gần nhất; patch không sửa DTO, Service và Repository xử lý Bio nên các thay đổi Bio đang có được giữ nguyên.

Không cần `dotnet ef migrations add`, `dotnet ef database update` riêng ở STEP 13.

## Postman test

Chạy `dotnet run --project .\src\Tindy.Api\Tindy.Api.csproj --launch-profile https` và dùng `base_url=https://localhost:7190` nếu launch profile chưa đổi.

1. Register tài khoản **mới** hoặc Login tài khoản test `is_onboarding_completed=false`, lưu token A.
2. `GET /api/dev/auth-check` với token A → **200** (JWT hợp lệ, không kiểm tra onboarding).
3. `GET /api/dev/onboarding-gate-check` với token A → **403** `ONBOARDING_REQUIRED`.
4. `GET /api/dev/onboarding-gate-check` không token → **401** `AUTH_UNAUTHORIZED`.
5. `GET /api/me` và `GET /api/me/onboarding` với token A → **200**.
6. `PUT /api/me/onboarding/{1..7}` và `POST /api/me/onboarding/complete` với **cùng token A** → các request hợp lệ thành công (Step 8 có `confirmed=true`; `bio` tùy chọn theo thay đổi STEP 12 của bạn).
7. Gọi lại `GET /api/dev/onboarding-gate-check` bằng **token A ban đầu** → **200** ngay sau completion, không cần Login lại hay cấp JWT mới.
8. `POST /api/auth/logout` với token A → **200** dù chưa complete; logout không revoke JWT trên server.
9. Gọi API business (khi có) với user incomplete → **403**; completed → vào action bình thường.

**Lưu ý:** Không test bằng cách trỏ vào `/api/dev/auth-check` cho onboarding gate vì endpoint này cố ý chỉ yêu cầu JWT. Nếu đang test bằng một account đã complete, hãy dùng account test mới thay vì tự xóa dữ liệu đang có.

### SQL xác minh trạng thái

```sql
SELECT
    uc.email_normalized,
    u.status,
    u.deleted_at,
    u.is_onboarding_completed
FROM users u
JOIN user_credentials uc ON uc.user_id = u.user_id
WHERE uc.email_normalized = 'YOUR_TEST_EMAIL';
```

## Production considerations

- Chính sách này truy vấn PostgreSQL mỗi request bảo vệ; cần cân nhắc throughput, DB pool và latency khi tăng tải. Không thêm cache quyền dài hạn vì cần hiệu lực ngay sau Onboarding complete và khóa tài khoản.
- Trạng thái user có thể đổi ngay sau truy vấn check và trước nghiệp vụ (TOCTOU). Với thao tác nghiệp vụ đặc biệt nhạy cảm, vẫn cần transaction/business rule riêng.
- Fallback policy cũng ảnh hưởng endpoint không đánh dấu auth (kể cả route không có handler có thể không trả 404 như mong đợi). Định nghĩa public endpoint/404 behavior khi hoàn thiện API production.
- Khi frontend khác origin, cấu hình CORS đáng tin cậy và cho preflight đi qua middleware theo thứ tự chuẩn; không dựa vào AllowAnonymous hàng loạt để sửa lỗi CORS.
- Không tạo hoặc cung cấp DEV test endpoints ở Production.
- Nếu bổ sung `[Authorize(Roles = "admin")]` hoặc policy cụ thể mới, phải đảm bảo requirement Onboarding cũng được gắn vào policy đó; các named policies không mặc nhiên tự kết hợp với DefaultPolicy.

## Git commit

```powershell
git status
git diff --check
git add src/Tindy.Application/Models/Authorization/ src/Tindy.Application/Interfaces/Repositories/IOnboardingAccessReader.cs
git add src/Tindy.Infrastructure/Repositories/OnboardingAccessReader.cs src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs
git add src/Tindy.Api/Authorization/ src/Tindy.Api/Controllers/ src/Tindy.Api/Extensions/
git add docs/guide/ONBOARDING_ACCESS_POLICY.md
git diff --cached --check
git commit -m "feat(auth): enforce onboarding completion for protected features"
```

## Definition of Done

- Build pass.
- Existing Authentication + Onboarding API works.
- Valid token + incomplete: 403 `ONBOARDING_REQUIRED` at protected features.
- No/invalid token: 401.
- Same valid token immediately gains access after completion (reads DB, no claim caching).
- Inactive/deleted account blocked at gate.
- No DB migration or auth token schema change.
