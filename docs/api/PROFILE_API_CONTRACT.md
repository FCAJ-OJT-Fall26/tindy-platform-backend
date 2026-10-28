# Tindy Profile Backend API Contract — V1 (STEP 01)

> **Phần mở rộng Media (23/10/2026):** [OpenAPI STEP 04/05](PROFILE_MEDIA_OPENAPI.yaml), [Certificate Attachments](../guide/PROFILE_MEDIA_STEP04_CERTIFICATE_ATTACHMENTS.md), [PDF Scanning/Cleanup](../guide/PROFILE_MEDIA_STEP05_PDF_SCANNING_CLEANUP.md). Các routes media đã triển khai; phần V1 bên dưới giữ bối cảnh contract ban đầu. `PROFILE_OPENAPI.yaml` tham chiếu các path media mới.

> **Trạng thái:** Contract thiết kế dành cho Backend MVP — chưa có endpoint mới nào được triển khai bởi STEP 01.  
> **Cơ sở:** `tindy-backend-platform(20261020-101054).zip`, `TINDY_PROFILE_BACKEND_IMPLEMENTATION_PLAN(2).md`.  
> **Mục đích:** Làm nguồn hợp đồng duy nhất để triển khai STEP 02–12; không yêu cầu thay đổi Frontend ở bước này.  
> **Lưu ý:** Các mức giới hạn chi tiết đánh dấu **[STEP01 default]** là quyết định kỹ thuật đề xuất đóng băng ở bước này, không phải giới hạn đã tồn tại trong source.

## 1. Code baseline và không gian thay đổi

- Framework: ASP.NET Core / .NET 10, EF Core/PostgreSQL, Clean Architecture (`Tindy.Api`, `Tindy.Application`, `Tindy.Infrastructure`, `Tindy.Tests`).
- Route hiện có: `GET /api/me` + `/api/me/onboarding/**` do `MeController` xử lý; `GET /api/catalog/skills` và `/api/catalog/professional-roles` do `CatalogController` xử lý. Không thay thế các route này.
- `MeController` đang có `[Authorize(Policy = OnboardingPolicies.AuthenticatedOnly)]`, dành cho cả tài khoản **chưa** hoàn thành onboarding. **Không** gắn controller Profile mới dưới policy này. Profile APIs dùng default policy JWT + `OnboardingCompletedRequirement`.
- JWT lấy user ID từ claim `sub` đã parse ra `Guid` không rỗng; không nhận `user_id` trong request ghi dữ liệu.
- `user_profiles` hiện có `is_public`, `bio`, `avatar_url`, `cover_image_url`, `preferred_role_id`, các text và links. `is_public` sẽ bị xóa ở STEP 02, không có trong contract này.
- `user_skills` đã liên kết với `skills` theo composite key; `professional_roles` đã có UUID Catalog. Không tạo bảng Skill/Role mới.

## 2. Conventions chung

| Quy ước | Contract V1 |
|---|---|
| Base path | `/api` (không có `/v1`) |
| Auth | `Authorization: Bearer <access_token>`; caller active, không soft-deleted, đã hoàn thành onboarding |
| Content type | `application/json; charset=utf-8` (request body) |
| JSON | `snake_case` theo `JsonNamingPolicy.SnakeCaseLower` hiện có |
| IDs | UUID thực từ DB/Catalog; `Guid.Empty` không hợp lệ |
| Thành công | `200/201 {"success":true,"message":"...","data":...}` |
| Xóa thành công | `204 No Content`, response **không** có body/envelope **[STEP01 default]** |
| Danh sách rỗng | `data: []`; optional scalar `null` |
| Lỗi | `application/problem+json`, `error_code` theo exception handler/API validation đang có |
| Time JSON | `YYYY-MM` đối với Experiences; `created_at`/`updated_at` là UTC ISO 8601 |
| Pagination | Các collection ban đầu trả array, **chưa** phân trang **[STEP01 default]** |
| Tên field | Ưu tiên tên Backend đã dùng; không đổi schema chỉ để khớp FE mock |

### HTTP status và error_code

| Status | Tình huống | `error_code` thực tế / mục tiêu |
|---|---|---|
| 400 | DTO/model binding/format/range/field không được phép | `VALIDATION_FAILED` |
| 401 | Thiếu/không hợp lệ/hết hạn JWT | `AUTH_UNAUTHORIZED` |
| 403 | Caller chưa xong Onboarding | `ONBOARDING_REQUIRED` |
| 403 | Caller inactive/không đủ quyền | `AUTH_FORBIDDEN` theo auth flow |
| 404 | Không tìm thấy Profile/Experience/Certificate, hoặc bản ghi không thuộc caller | `NOT_FOUND` |
| 409 | Ghi lại Onboarding Step sau khi đã completed | `CONFLICT` (giữ handler hiện tại; có thể thêm mã chuyên biệt ở STEP 03 nếu có tests) |
| 422 | Chọn Catalog Role/Skill không tồn tại/inactive hoặc business rule | `BUSINESS_RULE_VIOLATION` |
| 500 | Lỗi ngoài dự kiến | `INTERNAL_SERVER_ERROR` (không leak dữ liệu nhạy cảm) |

> Không hứa `PROFILE_NOT_FOUND`/`SKILL_NOT_FOUND` khi chưa triển khai custom exception mapping. Kiểm tra response lỗi JSON thực tế qua integration tests; không có `{success:false}` làm wrapper cho lỗi.

**Ví dụ 400:**

```json
{
  "title": "Bad Request",
  "status": 400,
  "detail": "The input data is invalid.",
  "instance": "/api/me/profile",
  "errors": { "university": ["University is required."] },
  "error_code": "VALIDATION_FAILED",
  "traceId": "<server-trace-id>"
}
```

## 3. Danh sách endpoint cố định

| Method | Endpoint | Request | Result |
|---|---|---|---|
| GET | `/api/me/profile` | — | 200 `MyProfileResponse` |
| PATCH | `/api/me/profile` | `UpdateProfileRequest` | 200 `MyProfileResponse` mới |
| GET | `/api/users/{user_id}/profile` | — | 200 `MemberProfileResponse` |
| GET | `/api/me/profile/experiences` | — | 200 `ExperienceResponse[]` |
| POST | `/api/me/profile/experiences` | `CreateExperienceRequest` | 201 `ExperienceResponse` |
| PUT | `/api/me/profile/experiences/{experience_id}` | `UpdateExperienceRequest` | 200 `ExperienceResponse` |
| DELETE | `/api/me/profile/experiences/{experience_id}` | — | 204 |
| GET | `/api/users/{user_id}/profile/experiences` | — | 200 `ExperienceResponse[]` |
| GET | `/api/me/profile/certificates` | — | 200 `CertificateResponse[]` |
| POST | `/api/me/profile/certificates` | `CreateCertificateRequest` | 201 `CertificateResponse` |
| PUT | `/api/me/profile/certificates/{certificate_id}` | `UpdateCertificateRequest` | 200 `CertificateResponse` |
| DELETE | `/api/me/profile/certificates/{certificate_id}` | — | 204 |
| GET | `/api/users/{user_id}/profile/certificates` | — | 200 `CertificateResponse[]` |

**Ngoài phạm vi Profile CRUD:** verification; Project CRUD; AI-generated Bio; Refresh Token. Media upload đã triển khai qua các endpoint riêng, xem `PROFILE_MEDIA_OPENAPI.yaml` và media guides.

## 4. Profile DTOs

### 4.1. `MyProfileResponse`

- Identity read-only: `user_id: uuid`, `username: string|null`.
- Required profile: `display_name: string`, `university: string|null` (DB pre-onboarding cho phép null nhưng hoàn thành onboarding yêu cầu có dữ liệu).
- Basic nullable: `headline`, `bio`, `location`, `current_role`, `phone_number`.
- Career: `preferred_role: PreferredRoleSummary|null`, `available_hours_per_week: integer|null`, `professional_interests`, `learning_goals`, `previous_projects_and_contribution`, `technologies_to_learn`.
- Links: `github_url`, `linkedin_url`, `personal_website_url`, `resume_url`, `public_email`; đều nullable.
- Assets: `avatar_url`, `cover_image_url`; nullable, **read-only** trong PATCH V1.
- `skills: SkillSummary[]`, `experiences_count: integer`, `certificates_count: integer`.
- `PreferredRoleSummary = {professional_role_id: uuid, name: string}`. `SkillSummary = {skill_id: uuid, name: string}`.
- **Không** trả `primary_email`/credential secrets/OAuth connections/account status trong Profile response; thông tin Account hiện đã có endpoint riêng `GET /api/me`.
- Không nhúng `experiences[]` hoặc `certificates[]`.

**Ví dụ GET /api/me/profile 200:**

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
    "bio": null,
    "location": null,
    "current_role": "Student",
    "preferred_role": {
      "professional_role_id": "22222222-2222-4222-8222-222222222222",
      "name": "Backend Developer"
    },
    "available_hours_per_week": 10,
    "professional_interests": null,
    "learning_goals": null,
    "previous_projects_and_contribution": null,
    "technologies_to_learn": null,
    "github_url": null,
    "linkedin_url": null,
    "personal_website_url": null,
    "resume_url": null,
    "public_email": null,
    "avatar_url": null,
    "cover_image_url": null,
    "skills": [
      {"skill_id":"33333333-3333-4333-8333-333333333333","name":".NET"}
    ],
    "experiences_count": 0,
    "certificates_count": 0
  }
}
```

UUID trong ví dụ chỉ có tính minh họa; client phải lấy ID thật từ `GET /api/catalog/skills` và `GET /api/catalog/professional-roles`.

### 4.2. `MemberProfileResponse`

Được dùng bởi cả user khác và owner khi xem qua link `/api/users/{user_id}/profile`; **luôn dùng DTO đã lọc** ngay cả khi ID trùng owner, tránh hai schema tùy caller:

- Có: `user_id`, `username`, `display_name`, `university`, `headline`, `bio`, `location`, `current_role`, `preferred_role`, `available_hours_per_week`, `professional_interests`, `learning_goals`, `previous_projects_and_contribution`, `technologies_to_learn`, `github_url`, `linkedin_url`, `personal_website_url`, `public_email`, `avatar_url`, `cover_image_url`, `skills`, `experiences_count`, `certificates_count`.
- Không có (key **không xuất hiện**, không chỉ gán null): `primary_email`, `phone_number`, `resume_url`, `password_hash`, `email_verified_at`, `external_auth_accounts`, `user_roles`, `status`, `is_onboarding_completed`, `is_public`.
- `public_email` chỉ chứa email do người dùng nhập vào Profile, **không** tự fallback sang email đăng nhập.
- Chỉ cho xem target `active`, chưa xóa, đã hoàn thành onboarding; target khác trạng thái này trả 404 để không lộ hồ sơ.

### 4.3. `UpdateProfileRequest` — PATCH object (không phải JSON Patch RFC 6902)

Endpoint dùng **JSON object partial update** với `Content-Type: application/json`. Không dùng mảng operation của JSON Patch.

| Field | Null | Quy tắc nhập |
|---|---|---|
| `display_name` | Không | Trim, 1–100 ký tự |
| `university` | Không | Trim, 1–255 ký tự |
| `phone_number` | Có | Trim, tối đa 20 ký tự; rỗng thành null |
| `headline` | Có | Trim, tối đa 255 |
| `bio` | Có | Trim, tối đa 2000 (theo OnboardingCompleteRequest) |
| `location` | Có | Trim, tối đa 255 |
| `current_role` | Có | Trim, tối đa 255 |
| `preferred_role_id` | Không | UUID khác `Guid.Empty`, FK catalog hợp lệ; role mới phải active |
| `available_hours_per_week` | Không | Integer 1–40 |
| `professional_interests` | Có | Text, trim; **[STEP01 default]** tối đa 5000 ký tự khi field được gửi |
| `learning_goals` | Có | Text, trim; **[STEP01 default]** tối đa 5000 |
| `previous_projects_and_contribution` | Có | Text, trim; **[STEP01 default]** tối đa 5000 |
| `technologies_to_learn` | Có | Text, trim; **[STEP01 default]** tối đa 5000 |
| `github_url` | Có | HTTP/HTTPS absolute URL, tối đa 1000 |
| `linkedin_url` | Có | HTTP/HTTPS absolute URL, tối đa 1000 |
| `personal_website_url` | Có | HTTP/HTTPS absolute URL, tối đa 1000 |
| `resume_url` | Có | HTTP/HTTPS absolute URL, tối đa 1000; chỉ trong My Profile |
| `public_email` | Có | Email hợp lệ, tối đa 255; không thay đổi login email |
| `skill_ids` | Không | Array 1–30 UUID khác Empty, không trùng; nếu gửi **replace toàn bộ** user_skills |

**Bộ quy tắc partial update:**

1. Field không có trong JSON → không thay đổi.
2. Field optional được gửi `null` hoặc chuỗi toàn whitespace → xóa thành `NULL`.
3. Field required được gửi `null`/rỗng → 400 `VALIDATION_FAILED`.
4. `skill_ids` vắng → không thay đổi skills; `skill_ids: null` hoặc `[]` → 400; `skill_ids` có giá trị → replace cả list trong **cùng transaction** với scalar fields.
5. Unknown/forbidden field như `username`, `primary_email`, `is_public`, `user_id`, `avatar_url`, `cover_image_url`, `created_at`, `is_onboarding_completed` → 400 (không silently ignore).
6. Empty patch `{}` → 400 **[STEP01 default]**. Duplicate JSON keys nên bị reject để tránh ambiguous update **[STEP01 default]**.
7. Validate role/skill mới active. Nếu hiện tại đã lưu inactive role/skill và không thay đổi liên kết đó thì **giữ nguyên**. Khi gửi lại full skill list, ID inactive đang thuộc profile trước đó được giữ, nhưng ID inactive *mới thêm* không hợp lệ.
8. Không reset onboarding state, không cấp JWT mới. Sau transaction, trả `MyProfileResponse` mới.
9. Validation với field được gửi: không bắt người dùng sửa trường khác chỉ vì dữ liệu text lịch sử dài hơn giới hạn mới. Dữ liệu hoàn thành onboarding phải duy trì các điều kiện bắt buộc quan trọng nếu bị cập nhật.

**Ví dụ PATCH:**

```json
{
  "bio": "I build .NET APIs and RAG applications.",
  "learning_goals": null,
  "preferred_role_id": "22222222-2222-4222-8222-222222222222"
}
```

> **Lưu ý C#:** nullable properties thông thường (`string?`) không phân biệt JSON omitted và explicit null. Sử dụng `Optional<T>` có field-presence, setter + `...WasProvided` tương tự `OnboardingCompleteRequest.BioWasProvided`, hoặc parser/validation có field-presence tương đương. Cần test deserialization HTTP, không chỉ test service.

## 5. Experience DTOs

**Schema dự kiến STEP 02:** `profile_experiences` (`experience_id`, `user_id`, `project_name`, `role_title`, `description`, `repo_url`, `start_date date?`, `end_date date?`, `is_ongoing`, `created_at`, `updated_at`) và `profile_experience_skills` (`experience_id`, `skill_id`, composite PK).

| Property (Create/PUT) | Validation V1 |
|---|---|
| `project_name` | Required, trim 1–150 |
| `role_title` | Required, trim 1–150 |
| `description` | Required, trim 1–5000 **[STEP01 default]** |
| `skill_ids` | **Required array**, có thể `[]`; tối đa 20 UUID distinct **[STEP01 default]** |
| `repo_url` | Nullable, HTTP/HTTPS absolute URL, <=1000 |
| `start_date` | Nullable string `YYYY-MM` |
| `end_date` | Nullable string `YYYY-MM` |
| `is_ongoing` | Required bool, `false` hợp lệ |

- POST và PUT đều dùng cấu trúc trên, PUT nghĩa là **full replacement**. `skill_ids` luôn gửi rõ, kể cả `[]`. Các optional field có thể `null` hoặc omitted (trong PUT omitted coi như null). Unknown fields → 400.
- `is_ongoing=true` → bắt buộc `end_date=null`. Nếu cả `start_date` và `end_date` khác null → end >= start.
- `YYYY-MM` được parse chính xác theo tháng/năm, lưu DB ngày đầu tháng (`DateOnly(year, month, 1)`), **không diễn giải là ngày thực tế**. Trả về `YYYY-MM` theo tháng gốc; không đổi sang timestamp ISO 8601.
- Skills sử dụng ID có thật từ Catalog. ID mới được thêm phải active; ID đang gắn với chính experience nhưng inactive được phép giữ khi PUT; không tự sync vào `user_skills`.
- `technologies` trong `ExperienceResponse` là `[{"skill_id":uuid,"name":string}]`; request **không** nhận field này mà dùng `skill_ids`.
- DELETE do owner thực hiện; khi xóa experience xóa liên kết skills theo cascade. Không được cập nhật/xóa experience thuộc user khác: trả 404.
- Danh sách sort **[STEP01 default]**: `start_date` giảm dần, null cuối, `created_at` giảm dần, `experience_id` làm tie-breaker. Không phân trang MVP.

**Ví dụ response item:**

```json
{
  "experience_id": "44444444-4444-4444-8444-444444444444",
  "project_name": "RAG Knowledge Chatbot",
  "role_title": "Backend Developer",
  "description": "Built an API with .NET and pgvector.",
  "technologies": [{"skill_id":"33333333-3333-4333-8333-333333333333","name":".NET"}],
  "repo_url": null,
  "start_date": "2025-09",
  "end_date": "2025-12",
  "is_ongoing": false,
  "created_at": "2026-10-20T08:00:00Z",
  "updated_at": null
}
```

`created_at`/`updated_at` trong response là UTC ISO 8601; không cho client gửi/set audit fields.

## 6. Certificate DTOs

**Schema hiện tại (23/10/2026):** `user_certificates` (`certificate_id`, `user_id`, `title`, `issuer`, `description?`, `credential_url?`, `created_at`, `updated_at`). File lưu qua Media Asset và Certificate Attachment riêng. Xem [migration/preflight](../guide/PROFILE_CERTIFICATES_SIMPLIFICATION.md).

| Property (Create/PUT) | Validation V1 |
|---|---|
| `title` | Required, trim 1–255 |
| `issuer` | Required, trim 1–255 |
| `description` | Nullable, trim <=2000 |
| `credential_url` | Nullable, absolute HTTP/HTTPS URL <=1000 |

- Description/URL optional; omitted, null hoặc blank thành null, kể cả PUT full replacement. Certificate là thông tin self-declared.
- JSON chỉ nhận bốn field ở bảng trên; unknown/duplicate properties trả 400, gồm mọi field đã loại bỏ.
- POST 201; PUT full replacement 200; DELETE 204. Không được tự chỉnh certificate thuộc người khác (404).
- Không unique theo `title`/`issuer`, vì cùng loại chứng chỉ có thể được cấp lại.
- Không có `is_verified`, `verification_status` hay file bytes/public URL trong CertificateResponse. Metadata file được đọc qua attachment endpoints riêng.
- Tối đa một attachment liên kết, gồm pending_upload/processing/active/rejected; file thứ hai trả `409 MEDIA_ATTACHMENT_LIMIT`. Owner phải DELETE file cũ trước khi upload file khác. Giữ giới hạn 10.000.000 bytes/file, quota 200.000.000 bytes/user và PDF scan clean trước khi đọc.
- Danh sách owner/member sort `created_at DESC`, `certificate_id ASC`.

**Ví dụ response item:**

```json
{
  "certificate_id": "55555555-5555-4555-8555-555555555555",
  "title": "AWS Certified Cloud Practitioner",
  "issuer": "Amazon Web Services",
  "description": "Foundational knowledge of AWS Cloud services.",
  "credential_url": "https://www.credly.com/badges/example",
  "created_at": "2026-10-20T08:00:00Z",
  "updated_at": null
}
```

## 7. Quyền xem profile và collections

- `GET /api/me/profile`: owner đã onboard; có thông tin liên hệ riêng như `phone_number`, `resume_url`.
- `GET /api/users/{user_id}/profile`: chỉ member có JWT active và onboard; target active, not deleted, onboard; dùng `MemberProfileResponse` đã lọc. Không có anonymous public access.
- Member Experiences/Certificates GET dùng cùng target rule; chỉ public professional data, không expose `user_id` của credential/account.
- Chỉ owner ghi vào `/api/me/profile` / `/api/me/profile/experiences` / `/api/me/profile/certificates`.
- `is_public` không xuất hiện trong Request/Response. Không hỗ trợ bật/tắt visibility riêng từng Experience/Certificate ở MVP.
- `previous_projects_and_contribution` (summary) **không** tự đổi thành các experience records; Tindy Projects là hệ thống riêng.
- `avatar_url` là dữ liệu **đọc** từ profile: STEP 10 có thể lưu URL ảnh OAuth nếu provider trả về đáng tin cậy; frontend tự fallback initials khi null hoặc ảnh lỗi. `cover_image_url` read-only nullable, cover CSS mặc định.

## 8. Validation/security edge cases bắt buộc test

1. PATCH `{}` / unknown keys / restricted fields / invalid JSON values / duplicate properties.
2. PATCH optional null clear, empty-string -> null; missing unchanged; repeated PATCH không ghi đè dữ liệu khác.
3. University/display_name null/blank; availability `0`, `41`, non-integer; invalid preferred role GUID.
4. `skill_ids`: omitted, null, [], >30, duplicate IDs, Guid.Empty, inactive new ID, inactive already-selected ID, transaction rollback.
5. Experiences: `YYYY-MM`, invalid `2026-13`, end before start, ongoing with end date, 0/20/21 skills, unexpected `technologies` payload, duplicate IDs.
6. Certificates: description 2000/2001 ký tự, invalid URL, optional description/URL null hoặc omitted, field đã loại bỏ/unknown/duplicate bị từ chối; attachment thứ hai 409 ở mọi trạng thái còn liên kết.
7. Anonymous request 401; incomplete caller 403 `ONBOARDING_REQUIRED`; inactive/soft-deleted caller cannot pass authorization.
8. Inactive/uncompleted/deleted target member profile GET → 404; cross-user PUT/DELETE resources → 404; no sensitive fields in serialized member JSON.
9. Local and OAuth users after completed onboarding; profile scalar/skill consistency with existing Onboarding data.
10. 200/201 success envelope, empty array, 204 no body, 400 ProblemDetails `errors`, 422 business rule error, UTC audit times, snake_case.

## 9. Bản đồ triển khai (không thực hiện ở STEP 01)

| STEP | Thành phần |
|---|---|
| 02 | EF entities + 3 tables, migration xóa `is_public` |
| 03 | OnboardingService guard SaveStep 1–7 sau complete; không thay đổi GET/complete idempotent |
| 04 | My Profile GET, DTO mapping, counts |
| 05 | PATCH field-presence + transactions, skill sync |
| 06 | Member Profile GET (đóng băng collection routes, triển khai ở STEP 08–09) |
| 07–08 | Experiences service/repository + HTTP CRUD & member list |
| 09 | Certificates service/repository + HTTP CRUD & member list |
| 10 | OAuth avatar reader/identity + persistence theo quy tắc không overwrite |
| 11 | Test toàn diện security/regression |
| 12 | API docs/handoff Codex FE theo implementation thực tế |

**STEP 01 chỉ tạo/review tài liệu này.** Không tạo DB migration, controller/service/DTO code thật, không sửa Auth/OAuth/Onboarding/Catalog, không thay Frontend.

## 10. Những điểm có thể điều chỉnh trước code nhưng không thay đổi quyết định nghiệp vụ

**[STEP01 defaults cần review]:** Experience `description <=5000`, optional profile text `<=5000` khi PATCH, tối đa 20 skills/experience, `skill_ids` là required array (cho phép `[]`) trong Experience POST/PUT, `DELETE=204`, collection chưa phân trang, sort mặc định, empty PATCH bị từ chối. Đây là mặc định cụ thể để code không phải đoán. Nếu nhóm muốn đổi, cập nhật contract trước STEP 02 và ghi commit docs tương ứng.

**Tình trạng runtime:** Chưa apply migration, chưa thử PostgreSQL instance của người dùng; không suy ra trạng thái database chạy từ source ZIP.
