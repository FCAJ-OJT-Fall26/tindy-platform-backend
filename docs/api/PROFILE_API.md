# Tindy Profile API — Backend V1 (STEP 12)

> **Certificate cập nhật 23/10/2026:** Contract hiện tại tại mục 6; mỗi certificate có tối đa một file riêng tư qua [attachment endpoints](PROFILE_MEDIA_OPENAPI.yaml). [Preflight và migration thủ công](../guide/PROFILE_CERTIFICATES_SIMPLIFICATION.md). Các checkpoint ZIP bên dưới là lịch sử.

> **Bản đối chiếu:** Backend ZIP `tindy-backend-platform(20261020-131618).zip`, branch metadata `feature/profile`, HEAD `eaac2ba` (`test(profile): cover API security and regression cases`). Ngày đối chiếu: 20/10/2026.  
> **Trạng thái:** Contract xác minh từ code Controller/DTO/parser/repository, **không phải** xác nhận đã chạy HTTP end-to-end. Quy trình xác nhận trước khi phát hành xem `../guide/PROFILE_STEP12_RELEASE_AUDIT.md`.  
> **Mục tiêu:** Tài liệu bàn giao FE cho Profile, Experiences, Certificates. Nguồn thực thi cuối cùng là code BE hiện tại; mọi thay đổi contract sau này phải cập nhật tài liệu và test tương ứng.

## 1. Địa chỉ API, OpenAPI & security

- Prefix: `/api` (không có `/v1` ở source này).
- Dev launch profile: `http://localhost:5217` hoặc `https://localhost:7190` (xem `Properties/launchSettings.json`; URL thực tế tùy môi trường).
- **Runtime OpenAPI** trong Development: `GET /openapi/tindy.json`; Swagger UI khi chạy Development: `/swagger`.
- Bản OpenAPI **tham chiếu cho FE**: `PROFILE_OPENAPI.yaml` đặt cùng thư mục tài liệu. Source thực tế dùng `[FromBody] JsonElement`, vì vậy OpenAPI tự sinh có thể không biểu diễn đầy đủ ràng buộc parser; đọc thêm các quy tắc validation trong file này.
- Header cho mọi API của Profile/Experience/Certificate: `Authorization: Bearer <access_token>`; request có body: `Content-Type: application/json`.
- Default `[Authorize]` yêu cầu JWT hợp lệ, caller `active`, chưa bị soft delete, và `is_onboarding_completed=true`.
- **Không nhầm:** `GET /api/me` thuộc **Account/Onboarding** và dùng policy `AuthenticatedOnly` cho cả user trước khi onboarding xong. `GET /api/me/profile` là **Profile**, yêu cầu onboarding hoàn tất. `GET /api/catalog/skills`, `/api/catalog/professional-roles` có policy `AuthenticatedOnly` để onboarding dùng được.
- `{user_id}` trên Member GET phải là UUID. Đây là hồ sơ *member-visible*, không phải endpoint công khai anonymous.
- JSON field names luôn là `snake_case`; ID là UUID từ DB và Catalog (không dùng `skill_dotnet`/`role_backend`).

## 2. Endpoints đang có trong source — 13 endpoints

| Method | Route | 2xx | Data | Quyền |
|---|---|---|---|---|
| GET | `/api/me/profile` | 200 | `MyProfileResponse` | owner |
| PATCH | `/api/me/profile` | 200 | `MyProfileResponse` mới | owner |
| GET | `/api/users/{user_id}/profile` | 200 | `MemberProfileResponse` | member read |
| GET | `/api/me/profile/experiences` | 200 | `ExperienceResponse[]` | owner |
| POST | `/api/me/profile/experiences` | 201 | `ExperienceResponse` | owner |
| PUT | `/api/me/profile/experiences/{experience_id}` | 200 | `ExperienceResponse` | owner |
| DELETE | `/api/me/profile/experiences/{experience_id}` | 204 | không có body | owner |
| GET | `/api/users/{user_id}/profile/experiences` | 200 | `ExperienceResponse[]` | member read |
| GET | `/api/me/profile/certificates` | 200 | `CertificateResponse[]` | owner |
| POST | `/api/me/profile/certificates` | 201 | `CertificateResponse` | owner |
| PUT | `/api/me/profile/certificates/{certificate_id}` | 200 | `CertificateResponse` | owner |
| DELETE | `/api/me/profile/certificates/{certificate_id}` | 204 | không có body | owner |
| GET | `/api/users/{user_id}/profile/certificates` | 200 | `CertificateResponse[]` | member read |

Không có GET-by-ID route cho Experience/Certificate ở V1. Sau POST/PUT dùng `data` trả về hoặc gọi lại GET list. POST 201 hiện không phát `Location` trỏ tới route GET-by-ID.

## 3. Response và lỗi chung

### Success envelope

- Mọi GET/POST/PUT/PATCH thành công trả `{ "success": true, "message": "...", "data": ... }`.
- Collection chưa phân trang, dữ liệu rỗng trả `data: []`.
- DELETE thành công: HTTP `204 No Content`, **không** gọi `response.json()` trên body rỗng.
- Ngày tạo/cập nhật (`created_at`, `updated_at`) do .NET serialize kiểu `DateTime` thành ISO-8601; `updated_at` có thể null. Đây là timestamp khác với tháng/năm hoặc ngày cấp nghiệp vụ.

### ProblemDetails (không có `success:false` envelope)

| HTTP | Nguyên nhân điển hình | `error_code` nguồn thực thi |
|---|---|---|
| 400 | JSON invalid/unknown/duplicate field, thiếu required, format ngày/URL sai | `VALIDATION_FAILED` |
| 401 | JWT thiếu/invalid/hết hạn | `AUTH_UNAUTHORIZED` |
| 403 | Chưa hoàn thành Onboarding | `ONBOARDING_REQUIRED` |
| 403 | Caller inactive/không đủ quyền | `AUTH_FORBIDDEN` (thông thường) |
| 404 | target chưa đủ điều kiện/không có; Experience/Certificate không thuộc caller | `NOT_FOUND` |
| 409 | SaveStep Onboarding sau completed | `ONBOARDING_ALREADY_COMPLETED` |
| 422 | Role/Skill Catalog mới không tồn tại/inactive hoặc business invariants | `BUSINESS_RULE_VIOLATION` |
| 500 | Unhandled error | `INTERNAL_SERVER_ERROR` |

Ví dụ `400` (field cụ thể thay đổi theo validation):

```json
{
  "type": "about:blank",
  "title": "Bad Request",
  "status": 400,
  "detail": "The input data is invalid.",
  "instance": "/api/me/profile",
  "errors": {
    "university": [
      "This field cannot be empty or null."
    ]
  },
  "error_code": "VALIDATION_FAILED",
  "traceId": "server-generated-trace-id"
}
```

FE nên đọc `status` + `error_code` + `errors` (nếu có) và không hiển thị raw stack traces. Một số lỗi ModelState và authentication có path serialization riêng; kiểm tra response thật trước khi chốt UI error mapping.

## 4. Profile — GET owner / GET member / PATCH

### 4.1 GET `/api/me/profile`

`data` chứa đúng các field đã khai báo trong `MyProfileResponse`:

```json
{
  "success": true,
  "message": "User profile retrieved successfully.",
  "data": {
    "user_id": "11111111-1111-4111-8111-111111111111",
    "username": "alexle",
    "display_name": "Alex Le",
    "university": "FPT University",
    "phone_number": null,
    "headline": "Backend Developer",
    "bio": "Interested in .NET, AWS, and RAG.",
    "location": "Ho Chi Minh City",
    "current_role": "Student",
    "preferred_role": {
      "professional_role_id": "22222222-2222-4222-8222-222222222222",
      "name": "Backend Developer"
    },
    "available_hours_per_week": 10,
    "professional_interests": "Cloud and AI Agents",
    "learning_goals": null,
    "previous_projects_and_contribution": null,
    "technologies_to_learn": null,
    "github_url": "https://github.com/example",
    "linkedin_url": null,
    "personal_website_url": null,
    "resume_url": null,
    "public_email": null,
    "avatar_url": null,
    "cover_image_url": null,
    "skills": [
      {
        "skill_id": "33333333-3333-4333-8333-333333333333",
        "name": ".NET"
      }
    ],
    "experiences_count": 1,
    "certificates_count": 1
  }
}
```

- `preferred_role` là `{professional_role_id, name} | null`.
- `skills` là array `{skill_id, name}` từ catalog; `experiences_count`, `certificates_count` là số lượng, **không** nhúng `experiences` hoặc `certificates`.
- `username` đọc được nhưng **không sửa qua PATCH**; login email thuộc `GET /api/me`, không nằm trong Profile DTO.
- Dữ liệu optional là `null` nếu không có; `avatar_url` từ provider OAuth khi có, còn fallback hình mặc định/initials nằm ở FE.

### 4.2 GET `/api/users/{user_id}/profile`

`MemberProfileResponse` là DTO riêng, gồm các field hiển thị cho thành viên:

```json
{
  "success": true,
  "message": "Member profile retrieved successfully.",
  "data": {
    "user_id": "44444444-4444-4444-8444-444444444444",
    "username": "alexle",
    "display_name": "Alex Le",
    "university": "FPT University",
    "headline": "Backend Developer",
    "bio": "Interested in .NET, AWS, and RAG.",
    "location": "Ho Chi Minh City",
    "current_role": "Student",
    "preferred_role": {
      "professional_role_id": "22222222-2222-4222-8222-222222222222",
      "name": "Backend Developer"
    },
    "available_hours_per_week": 10,
    "professional_interests": "Cloud and AI Agents",
    "learning_goals": null,
    "previous_projects_and_contribution": null,
    "technologies_to_learn": null,
    "github_url": "https://github.com/example",
    "linkedin_url": null,
    "personal_website_url": null,
    "public_email": null,
    "avatar_url": null,
    "cover_image_url": null,
    "skills": [
      {
        "skill_id": "33333333-3333-4333-8333-333333333333",
        "name": ".NET"
      }
    ],
    "experiences_count": 1,
    "certificates_count": 1
  }
}
```

**Không bao gồm** `phone_number`, `resume_url`, `primary_email`, password, OAuth connections, RBAC, internal status. `public_email` chỉ là email *tự khai báo trong Profile*, không suy ra từ login email. Nếu gọi member route với chính ID của mình vẫn nhận DTO đã lọc. Target inactive/deleted/chưa onboarding xong trả 404.

### 4.3 PATCH `/api/me/profile`

**Partial update theo field presence**:
- Field **không gửi**: giữ nguyên.
- Optional field gửi **`null`** hoặc chuỗi trắng: chuẩn hóa thành null.
- Required field gửi **`null`/rỗng**: 400.
- `skill_ids` nếu xuất hiện sẽ **thay toàn bộ** Profile Skills, 1–30 UUID duy nhất; nếu vắng mặt không đụng tới danh sách Skills.
- `{}`: 400. JSON field lạ hoặc trùng tên: 400. Field Account/Security như `user_id`, `username`, `primary_email`, `status`, `is_public`, `is_onboarding_completed`, `avatar_url`, `cover_image_url`, `created_at` đều bị từ chối.

Ví dụ PATCH:

```json
{
  "bio": "Backend Developer interested in AWS and RAG.",
  "learning_goals": null,
  "skill_ids": [
    "33333333-3333-4333-8333-333333333333"
  ]
}
```

Trả `200` với **toàn bộ `MyProfileResponse` mới**, trong envelope message `User profile updated successfully.`.

**Whitelist + validation theo `ProfilePatchParser`**:

| Field | Max/rule |
|---|---|
| `display_name` | non-empty, ≤100 |
| `university` | non-empty, ≤255 |
| `phone_number` | optional ≤20 |
| `headline` | optional ≤255 |
| `bio` | optional ≤2000 |
| `location` | optional ≤255 |
| `current_role` | optional ≤255 |
| `preferred_role_id` | valid non-empty UUID, role active khi chọn mới |
| `available_hours_per_week` | integer 1..40 |
| `professional_interests` | optional ≤5000 |
| `learning_goals` | optional ≤5000 |
| `previous_projects_and_contribution` | optional ≤5000; Experience Summary, **khác CRUD** |
| `technologies_to_learn` | optional ≤5000 |
| `github_url`, `linkedin_url`, `personal_website_url`, `resume_url` | optional URL absolute HTTP/HTTPS, ≤1000 |
| `public_email` | optional email ≤255 |
| `skill_ids` | array 1..30 UUID unique, catalog; chỉ skill mới được thêm phải active |

Sau PATCH, dữ liệu đã hoàn thành onboarding vẫn phải giữ required invariants: `display_name`, `university`, `preferred_role_id`, `available_hours_per_week`, 1–30 Profile Skills hợp lệ. Role/Skill inactive **đã lưu trước đó** được giữ nếu không thay bằng mục mới.

## 5. Experiences — collection độc lập

### 5.1 GET list (owner hoặc member)

- Owner: `GET /api/me/profile/experiences` → message `Experiences retrieved successfully.`.
- Member: `GET /api/users/{user_id}/profile/experiences` → message `Member experiences retrieved successfully.`.
- Ví dụ body với một item:

```json
{
  "success": true,
  "message": "Experiences retrieved successfully.",
  "data": [
    {
      "experience_id": "55555555-5555-4555-8555-555555555555",
      "project_name": "RAG Knowledge Chatbot",
      "role_title": "Backend Developer",
      "description": "Built retrieval and chat APIs.",
      "technologies": [
        {
          "skill_id": "33333333-3333-4333-8333-333333333333",
          "name": ".NET"
        }
      ],
      "repo_url": "https://github.com/example/rag",
      "start_date": "2025-09",
      "end_date": null,
      "is_ongoing": true,
      "created_at": "2026-10-20T10:00:00Z",
      "updated_at": null
    }
  ]
}
```

- `technologies` là danh sách `{skill_id, name}` lấy từ **Skill Catalog**, không phải free text. Profile Skills và Experience Skills độc lập.
- Sắp xếp từ mới tới cũ theo start date (sau đó created_at, ID); chưa pagination.

### 5.2 POST `/api/me/profile/experiences` — 201

Request JSON:

```json
{
  "project_name": "RAG Knowledge Chatbot",
  "role_title": "Backend Developer",
  "description": "Built retrieval and chat APIs.",
  "repo_url": "https://github.com/example/rag",
  "start_date": "2025-09",
  "end_date": null,
  "is_ongoing": true,
  "skill_ids": [
    "33333333-3333-4333-8333-333333333333"
  ]
}
```

Response:

```json
{
  "success": true,
  "message": "Experience created successfully.",
  "data": {
    "experience_id": "55555555-5555-4555-8555-555555555555",
    "project_name": "RAG Knowledge Chatbot",
    "role_title": "Backend Developer",
    "description": "Built retrieval and chat APIs.",
    "technologies": [
      {
        "skill_id": "33333333-3333-4333-8333-333333333333",
        "name": ".NET"
      }
    ],
    "repo_url": "https://github.com/example/rag",
    "start_date": "2025-09",
    "end_date": null,
    "is_ongoing": true,
    "created_at": "2026-10-20T10:00:00Z",
    "updated_at": null
  }
}
```


### 5.3 PUT `/api/me/profile/experiences/{experience_id}` — 200

PUT **full replacement**, body có cùng schema như POST; trả `Experience updated successfully.` + ExperienceResponse. `skill_ids` mới sẽ thay toàn bộ liên kết; skill mới phải tồn tại và active, skill inactive đang liên kết trước đó có thể giữ nếu vẫn chọn. Chỉ chủ sở hữu được sửa; không thuộc owner → 404.

### 5.4 DELETE `/api/me/profile/experiences/{experience_id}` — 204

Không cần request body; không có response body; không thuộc owner → 404. DB cấu hình cascade Experience → experience-skills, không xóa Skill Catalog hoặc Profile Skills.

### Validation Experience

| Field | Rule |
|---|---|
| `project_name` | bắt buộc, trimmed, 1..150 chars |
| `role_title` | bắt buộc, 1..150 |
| `description` | bắt buộc, 1..5000 |
| `repo_url` | optional null, absolute HTTP/HTTPS ≤1000 |
| `start_date`, `end_date` | optional `YYYY-MM` hoặc null; năm hợp lệ, tháng 01..12 |
| `is_ongoing` | **bắt buộc** `true`/`false` |
| `skill_ids` | **bắt buộc** array 0..20 UUID unique; `[]` hợp lệ |

`is_ongoing=true` yêu cầu `end_date=null`; nếu có cả hai ngày thì `end_date >= start_date`. Backend lưu tháng/năm bằng PostgreSQL `date` ngày **01** của tháng. Không tự tạo Experience từ `previous_projects_and_contribution` hay từ Tindy Projects.

## 6. Certificates — collection độc lập

### 6.1 GET list (owner hoặc member)

- Owner: `GET /api/me/profile/certificates` → message `Certificates retrieved successfully.`.
- Member: `GET /api/users/{user_id}/profile/certificates` → message `Member certificates retrieved successfully.`.
- Cả hai danh sách sắp xếp `created_at DESC`, sau đó `certificate_id ASC` để ổn định khi trùng thời gian.
- Ví dụ:

```json
{
  "success": true,
  "message": "Certificates retrieved successfully.",
  "data": [
    {
      "certificate_id": "66666666-6666-4666-8666-666666666666",
      "title": "AWS Certified Cloud Practitioner",
      "issuer": "Amazon Web Services",
      "description": "Foundational knowledge of AWS Cloud services.",
      "credential_url": "https://www.credly.com/badges/example",
      "created_at": "2026-10-20T10:00:00Z",
      "updated_at": null
    }
  ]
}
```


### 6.2 POST `/api/me/profile/certificates` — 201

Request:

```json
{
  "title": "AWS Certified Cloud Practitioner",
  "issuer": "Amazon Web Services",
  "description": "Foundational knowledge of AWS Cloud services.",
  "credential_url": "https://www.credly.com/badges/example"
}
```

Response:

```json
{
  "success": true,
  "message": "Certificate created successfully.",
  "data": {
    "certificate_id": "66666666-6666-4666-8666-666666666666",
    "title": "AWS Certified Cloud Practitioner",
    "issuer": "Amazon Web Services",
    "description": "Foundational knowledge of AWS Cloud services.",
    "credential_url": "https://www.credly.com/badges/example",
    "created_at": "2026-10-20T10:00:00Z",
    "updated_at": null
  }
}
```


### 6.3 PUT `/api/me/profile/certificates/{certificate_id}` — 200

PUT full replacement; body cùng schema POST; trả `Certificate updated successfully.` + CertificateResponse. Optional field bị bỏ qua hoặc gửi null đều được xóa về null. Không thuộc owner → 404.

### 6.4 DELETE `/api/me/profile/certificates/{certificate_id}` — 204

Không có body. Không thuộc owner → 404. Attachment được gỡ và media được đánh dấu `pending_delete` trong cùng transaction; worker xóa file vật lý an toàn.

### Validation Certificate

| Field | Rule |
|---|---|
| `title` | bắt buộc, trimmed, 1..255 chars |
| `issuer` | bắt buộc, 1..255 chars |
| `description` | optional null, trimmed, ≤2000 |
| `credential_url` | optional null, absolute HTTP/HTTPS ≤1000 |

Text được trim; optional blank thành null. JSON phải là object; unknown field, duplicate property và các field certificate đã loại bỏ đều trả `400 VALIDATION_FAILED`. Ví dụ description quá dài có `errors.description = ["The maximum length is 2000 characters."]`; field không hỗ trợ có message `This field is not supported.`; field trùng có `Duplicate JSON properties are not allowed.`.

CertificateResponse chỉ chứa metadata ở ví dụ trên, không nhúng file bytes, storage path hay public file URL. Certificate vẫn là thông tin tự khai báo, không có API xác minh hoặc verified state.

### File tùy chọn — tối đa một attachment

Dùng các [attachment endpoints hiện có](PROFILE_MEDIA_OPENAPI.yaml): POST multipart `file` (ảnh `201`, PDF `202`), GET metadata, PATCH visibility và DELETE theo attachment ID. Mọi row còn liên kết chiếm slot, gồm `pending_upload`, `processing`, `active`, `rejected`. Upload file thứ hai trả `409 MEDIA_ATTACHMENT_LIMIT`, detail `Maximum one attachment per certificate. Delete the existing attachment before uploading another.`. Xóa attachment thành công (`204`) rồi mới upload file thay thế, kể cả PDF bị từ chối. Không có Replace File API.

File tối đa 10.000.000 bytes, quota certificate vẫn 200.000.000 bytes/user. File riêng tư; PDF chỉ tải được khi `active` và scan `clean`. Visibility `owner_only`/`members`, polling và cleanup giữ nguyên.

## 7. OAuth avatar, Account, Onboarding và Catalog

- `avatar_url`/`cover_image_url` chỉ đọc qua GET. Không có PATCH/POST upload ảnh hay body chỉnh `avatar_url`.
- Google OAuth lấy `picture`; GitHub OAuth lấy `avatar_url` và lưu qua identity/registration/linking nếu hợp lệ. Avatar hiện có không bị ghi đè tự động khi đăng nhập hoặc liên kết provider khác. Nếu null hoặc ảnh lỗi, FE fallback initials.
- `GET /api/me` dành cho Account (`email`, status/onboarding metadata) — không thay bằng Profile GET.
- Onboarding 8 bước; sau khi hoàn thành, SaveStep 1–7 trả 409 `ONBOARDING_ALREADY_COMPLETED`, phải chỉnh qua Profile PATCH.
- Skills và preferred roles phải được chọn từ `GET /api/catalog/skills`, `GET /api/catalog/professional-roles`; không dùng tên skill làm ID và không tạo Skill tự do trong Experience.

## 8. Những khác biệt so với tài liệu `API_Profile.md` prototype

| Prototype FE/tài liệu cũ | Backend V1 đang implement |
|---|---|
| `PUT /api/me/profile` | `PATCH /api/me/profile` |
| `bio_summary` | `bio` |
| `portfolio_url` | `personal_website_url` |
| `is_public` | Không tồn tại (migration xóa) |
| Upload avatar/cover/certificate | Qua các endpoint media riêng; certificate tối đa một file, xem `PROFILE_MEDIA_OPENAPI.yaml` |
| Certificate verified state | Không hỗ trợ |
| Skills `skill_dotnet`, free-text technologies | UUID từ Catalog; `skill_ids` request |
| `experiences[]`/`certificates[]` nhúng trong Profile | 2 GET collection độc lập |
| Profile availability 1–60 | 1–40 |

## 9. Bằng chứng và giới hạn release

- 13 route có trong source, DTO/parser/EF mappings/migrations có trong ZIP; 2 migration 20261020... được kiểm tra nội dung bằng đọc source.
- Các test report `.trx` trong ZIP là từ giai đoạn STEP 07–08: không Docker **100/100 PASS**, full suite **100 PASS/50 FAIL** do DockerUnavailableException. Chúng **không** xác minh toàn bộ STEP 09–11.
- Chưa có bằng chứng trong ZIP về HTTP end-to-end/migration đã apply PostgreSQL trên máy hiện tại; không thể coi BE V1 production-ready chỉ nhờ source.
- Chi tiết checkpoint, SHA, test, deploy nằm ở `../guide/PROFILE_STEP12_RELEASE_AUDIT.md`.
