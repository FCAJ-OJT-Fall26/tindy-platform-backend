# ONBOARDING_API_IMPLEMENTATION — Tindy Backend (Catalog V1)

> **Trạng thái tài liệu:** API contract hiện tại sau STEP 01–06, đối chiếu từ source `tindy-backend-platform(20261015-022026).zip`. Thay thế hướng dẫn STEP 12 cũ có payload `skills: string[]` và `preferred_role: string`.
>
> **Stack:** .NET 10, ASP.NET Core, EF Core 10, PostgreSQL. Ví dụ chạy bằng PowerShell 7.  
> **Các file nguồn cần đối chiếu nếu API thay đổi:** `src/Tindy.Api/Controllers/{CatalogController,MeController}.cs`, `src/Tindy.Application/DTOs/Onboarding/OnboardingRequests.cs`, `src/Tindy.Application/Services/OnboardingService.cs`.

## 1. Phân quyền và quy ước chung

- Mọi Catalog API và `api/me` trong tài liệu đều yêu cầu JWT: `Authorization: Bearer <access_token>`.
- User **chưa hoàn tất Onboarding** vẫn được gọi `GET /api/catalog/*`, `GET /api/me` và các endpoint Onboarding. Các business API có policy `OnboardingCompleted` bị chặn trước khi hoàn tất.
- `user_id` lấy từ JWT đã xác thực và database, **không truyền trong body**.
- JSON dùng `snake_case` ở request và response. Phản hồi thành công theo dạng `{"success":true,"message":"...","data":...}`.
- `professional_roles` là **nghề nghiệp mong muốn**, không phải bảng RBAC `roles`.
- Người dùng **không tự tạo Skills**. Backend chỉ lưu liên kết đến Skill có sẵn, đang active trong catalog.

## 2. Catalog Read APIs

| Method | Endpoint | Query | Ý nghĩa |
|---|---|---|---|
| GET | `/api/catalog/skill-categories` | — | Categories `is_active=true`, sắp xếp theo `display_order`, tên |
| GET | `/api/catalog/skills` | `category_id` (UUID, tùy chọn), `search` (tùy chọn, tối đa 100 ký tự) | Skills active, có thể lọc Category và tìm theo tên |
| GET | `/api/catalog/professional-roles` | `search` (tùy chọn, tối đa 100 ký tự) | Các Professional Roles active |

Ví dụ:

```http
GET /api/catalog/skills?category_id=<category_uuid>&search=python
Authorization: Bearer <access_token>
```

Response (rút gọn):

```json
{
  "success": true,
  "message": "Skills retrieved successfully.",
  "data": [
    { "skill_id": "837fc321-7450-5259-8d83-015e6a0824f2", "name": "Python" }
  ]
}
```

- `search` được trim, gom khoảng trắng và chuyển về chữ thường trước khi lọc.
- Một Skill thuộc nhiều Categories nhưng API không trả duplicate.
- `category_id` không tồn tại hoặc Category inactive trả mảng rỗng; sai định dạng UUID trả 400.
- Tên Role, Skill và Category chỉ là dữ liệu hiển thị; frontend cần dùng **UUID nhận từ API**, không hardcode ID hay dùng tên để PUT.

## 3. Onboarding APIs

| Method | Endpoint | Dữ liệu | Kết quả |
|---|---|---|---|
| GET | `/api/me` | — | Thông tin tài khoản đang đăng nhập |
| GET | `/api/me/onboarding` | — | Draft và tiến độ 8 bước |
| PUT | `/api/me/onboarding/1` | `username`, `display_name`, `university` | Lưu thông tin cơ bản |
| PUT | `/api/me/onboarding/2` | `skill_ids: UUID[]` | **Thay toàn bộ** User Skills |
| PUT | `/api/me/onboarding/3` | `preferred_role_id: UUID` | Lưu nghề nghiệp mong muốn |
| PUT | `/api/me/onboarding/4` | `professional_interests`, `learning_goals` | Lưu sở thích và mục tiêu |
| PUT | `/api/me/onboarding/5` | `available_hours_per_week` | Lưu số giờ rảnh/tuần |
| PUT | `/api/me/onboarding/6` | `previous_projects_and_contribution`, `technologies_to_learn` | Kinh nghiệm và công nghệ muốn học |
| PUT | `/api/me/onboarding/7` | `github_url`, `personal_website_url` | Lưu links (được null, vẫn phải submit step) |
| POST | `/api/me/onboarding/complete` | `confirmed: true`, `bio` tùy chọn | Xác nhận hoàn tất Step 8 |

### 3.1. Payload chuẩn từng bước

**Step 1:**

```json
{"username":"tindy_test_01","display_name":"Nguyen Van A","university":"FPT University"}
```

Username dài 3–50 ký tự, chỉ gồm A–Z, a–z, 0–9, `_`, `.`; `display_name` và `university` bắt buộc.

**Step 2:**

```json
{
  "skill_ids": [
    "119c99fb-fe58-53dc-9414-8bdbf8fbbeb2",
    "07c390fa-bed8-5666-a7a3-4d285146ab5a"
  ]
}
```

- Tối thiểu 1 và tối đa 30 UUID; UUID rỗng (all zeros) không hợp lệ.
- Backend tự loại trùng UUID; nếu có ID không tồn tại hoặc Skill inactive, trả 422 và **không ghi đè `user_skills` hiện có**.
- Chỉ thay `user_skills`; không gọi `INSERT INTO skills` hoặc tạo Custom Skill.

**Step 3:**

```json
{"preferred_role_id":"e8e72cb0-e5b2-515a-92b3-1abf59733d97"}
```

- `preferred_role_id` tham chiếu `professional_roles.professional_role_id`.
- Missing/null/UUID rỗng: 400; Role không tồn tại/inactive: 422.
- Không còn trường lưu `user_profiles.preferred_role` dạng string.

**Step 4:**

```json
{"professional_interests":"Cloud computing, AI applications","learning_goals":"Build production-ready cloud applications"}
```

**Step 5:**

```json
{"available_hours_per_week":10}
```

Giá trị phải nằm trong khoảng **1–40**.

**Step 6:**

```json
{"previous_projects_and_contribution":"Built a RAG chatbot","technologies_to_learn":"Docker, AWS Lambda"}
```

**Step 7:**

```json
{"github_url":null,"personal_website_url":null}
```

Cả hai URL được để trống, nhưng **phải gửi PUT Step 7**. Nếu có giá trị phải là URL HTTP(S), tối đa 1.000 ký tự.

**Step 8:**

```json
{"confirmed":true,"bio":"Tôi quan tâm đến Backend và Cloud."}
```

Hoặc `{ "confirmed": true }` để không thay đổi `Bio`. `bio` có thể null/để trống, tối đa 2.000 ký tự; hiện chưa bắt buộc tạo AI summary. Người dùng có thể tự viết hoặc chỉnh sửa bản đề xuất AI; endpoint `complete` chỉ lưu Bio khi **chưa hoàn thành** và body có trường `bio`.

### 3.2. Draft Response

```http
GET /api/me/onboarding
Authorization: Bearer <access_token>
```

Response minh họa (rút gọn, chỉ trích `data`):

```json
{
  "total_steps": 8,
  "completed_steps": [1, 2, 3],
  "current_step": 4,
  "is_onboarding_completed": false,
  "step_data": {
    "2": {
      "skill_ids": ["119c99fb-fe58-53dc-9414-8bdbf8fbbeb2"],
      "skills": [
        { "skill_id": "119c99fb-fe58-53dc-9414-8bdbf8fbbeb2", "name": ".NET" }
      ]
    },
    "3": {
      "preferred_role_id": "e8e72cb0-e5b2-515a-92b3-1abf59733d97",
      "preferred_role": "Backend Developer"
    }
  }
}
```

`step_data` trên được rút gọn; thực tế có khóa `"1"` đến `"8"`. `preferred_role` trong Draft **chỉ là tên hiển thị**, không phải cột string trong PostgreSQL.

## 4. Quy tắc hoàn tất

- `user_onboarding_steps` chỉ lưu marker **1–7**; PostgreSQL có constraint `step_number BETWEEN 1 AND 7`.
- Step 8 = `POST /api/me/onboarding/complete` với `confirmed=true`, và lưu `users.is_onboarding_completed=true`.
- Để complete: cần đủ marker Step 1–7 **và dữ liệu bắt buộc còn hợp lệ**. Marker tồn tại nhưng User Skills không còn thì chưa hoàn thành.
- Lựa chọn Role/Skill inactive **sau khi người dùng đã lưu** không tự khiến marker đã lưu mất hiệu lực; lựa chọn **mới** phải active.
- Lặp lại `/complete` sau khi hoàn tất trả thành công và không tạo marker Step 8; flow này cũng không cập nhật Bio lần nữa.
- Khi hoàn thành, backend trả `redirect_to: "/discover"` trong `data`. Phần cập nhật Bio sau Onboarding nên được triển khai bằng API Profile riêng nếu cần.
- Các thay đổi step sau khi đã hoàn thành không tự động đặt `is_onboarding_completed=false` trong phiên bản hiện tại.

## 5. Lỗi thường gặp

| Trường hợp | HTTP | `error_code` |
|---|---:|---|
| Thiếu JWT, token không hợp lệ/hết hạn | 401 | `AUTH_UNAUTHORIZED` (authentication middleware) |
| Request sai định dạng/vi phạm DataAnnotations | 400 | `VALIDATION_FAILED` |
| Username đã được sử dụng | 409 | `USERNAME_ALREADY_EXISTS` |
| Skill hoặc Preferred Role không tồn tại/inactive | 422 | `BUSINESS_RULE_VIOLATION` |
| Chưa hoàn tất các bước bắt buộc khi gọi `/complete` | 422 | `ONBOARDING_INCOMPLETE` (có `missing_steps`) |
| Account inactive/soft-deleted | 401 | `AUTH_INVALID_CREDENTIALS` |
| Business API yêu cầu completed onboarding | 403 | `ONBOARDING_REQUIRED` |

Với lỗi nghiệp vụ, backend sử dụng `application/problem+json`, không dùng wrapper `success/data` như phản hồi thành công.

## 6. Kiểm thử local không cần Docker

Điều kiện: PostgreSQL local đã có 6 migrations và Catalog Seed Data, Backend đang chạy tại địa chỉ của bạn (ví dụ `http://localhost:5217`).

```powershell
# Root thư mục backend (chứa Tindy.slnx)
dotnet build .\Tindy.slnx
dotnet run --project .\src\Tindy.Api\Tindy.Api.csproj --launch-profile http
```

Ở một terminal PowerShell khác:

```powershell
$base = 'http://localhost:5217'
$token = 'PASTE_YOUR_ACCESS_TOKEN'
$headers = @{ Authorization = "Bearer $token" }

$roles = Invoke-RestMethod "$base/api/catalog/professional-roles" -Headers $headers
$skills = Invoke-RestMethod "$base/api/catalog/skills" -Headers $headers
$categories = Invoke-RestMethod "$base/api/catalog/skill-categories" -Headers $headers

$roles.data | Select-Object -First 5
$skills.data | Select-Object -First 5
$categories.data | Select-Object -First 5

# Sử dụng token của tài khoản chưa hoàn tất Onboarding
$selectedIds = @($skills.data | Select-Object -First 2 | ForEach-Object { $_.skill_id })
$body = @{ skill_ids = $selectedIds } | ConvertTo-Json -Depth 3
Invoke-RestMethod "$base/api/me/onboarding/2" -Method Put -Headers $headers -ContentType 'application/json' -Body $body

$roleId = ($roles.data | Where-Object name -EQ 'Backend Developer' | Select-Object -First 1).professional_role_id
$bodyRole = @{ preferred_role_id = $roleId } | ConvertTo-Json
Invoke-RestMethod "$base/api/me/onboarding/3" -Method Put -Headers $headers -ContentType 'application/json' -Body $bodyRole
```

Kiểm thử tiếp: Draft phản ánh đúng UUID; gửi Skill giả trả 422 không thay dữ liệu; Step 7 cho phép URL null; `/complete` trước khi đủ bước trả 422, đủ bước trả 200. **Không cần Dockerize ứng dụng để chạy các bước này.**

## 7. Tài liệu và các bước liên quan

- [DATABASE_SEED.md](./DATABASE_SEED.md): Seed dữ liệu hệ thống.
- [DATABASE_SETUP.md](./DATABASE_SETUP.md): Khởi tạo PostgreSQL local và chạy migration.
- [ONBOARDING_ACCESS_POLICY.md](./ONBOARDING_ACCESS_POLICY.md): Authorization gate.
- `CatalogReadApiTests`, `OnboardingSkillCatalogTests`, `OnboardingPreferredRoleCatalogTests`, `OnboardingCompletionRulesTests`: Integration Tests dùng PostgreSQL Testcontainers, cần Docker Engine khi chạy tự động.

> Không tạo migration mới cho việc sửa tài liệu. Nếu có thay đổi API contract trong source về sau, sửa lại tài liệu này cùng commit code tương ứng.
