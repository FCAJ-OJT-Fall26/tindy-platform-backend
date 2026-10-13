# STEP 12 - Onboarding - Implement Profile Completion APIs

Nguồn đối chiếu: `AUTHENTICATION_API.md` (STEP 01), `ONBOARDING_SCHEMA.md` (STEP 11), `tindy-backend-platform(7).zip`.

## Điều kiện trước khi triển khai

- STEP 11 Entity, Configuration, DbContext, migration `Profile_AddOnboardingDataAndSkills` đã được áp dụng thành công.
- PostgreSQL Development có các bảng `skills`, `user_skills`, `user_onboarding_steps` và 5 cột Profile mới.
- STEP 04–10 JWT Bearer, Register/Login/Logout hoạt động.
- Code này dựa trên snapshot ZIP số 7 + file STEP 11 từ đợt hướng dẫn trước; nếu local có thay đổi khác, review patch và reconcile trước khi áp dụng.

## Endpoint

| Method | Endpoint | Action |
|---|---|---|
| GET | `/api/me` | Đọc current user từ DB |
| GET | `/api/me/onboarding` | Đọc trạng thái và step_data |
| PUT | `/api/me/onboarding/1` | Username, display name, university |
| PUT | `/api/me/onboarding/2` | Skills full replace |
| PUT | `/api/me/onboarding/3` | PreferredRole (KHÔNG phải RBAC member) |
| PUT | `/api/me/onboarding/4` | Professional interests + learning goals |
| PUT | `/api/me/onboarding/5` | Availability 1–40 |
| PUT | `/api/me/onboarding/6` | Projects & experience |
| PUT | `/api/me/onboarding/7` | Github/Portfolio optional, bắt buộc submit |
| POST | `/api/me/onboarding/complete` | Xác nhận Step 8 |

Tất cả yêu cầu `Authorization: Bearer <access_token>`.

## JSON mẫu trong Postman

### Step 1
```json
{"username":"tindy_test_01","display_name":"Nguyen Van A","university":"FPT University"}
```
### Step 2
```json
{"skills":[".NET","AWS","PostgreSQL"]}
```
### Step 3
```json
{"preferred_role":"Backend Developer"}
```
### Step 4
```json
{"professional_interests":"Cloud computing, AI applications","learning_goals":"Build production-ready cloud applications"}
```
### Step 5
```json
{"available_hours_per_week":10}
```
### Step 6
```json
{"previous_projects_and_contribution":"Built a RAG chatbot","technologies_to_learn":"Docker, AWS Lambda"}
```
### Step 7
```json
{"github_url":null,"personal_website_url":null}
```
### Step 8
```json
{"confirmed":true}
```

Response chuẩn: `{ "success":true, "message":"...", "data":{...} }`.

## Error contract

| Điều kiện | HTTP | error_code |
|---|---|---|
| Missing/invalid JWT | 401 | AUTH_UNAUTHORIZED |
| Invalid request body | 400 | VALIDATION_FAILED |
| Username trùng | 409 | USERNAME_ALREADY_EXISTS |
| Thiếu bước hoặc dữ liệu step chưa hợp lệ | 422 | ONBOARDING_INCOMPLETE + missing_steps |
| Account inactive/soft-deleted | 401 | AUTH_INVALID_CREDENTIALS |

## Invariants được giữ

- `user_id` lấy từ JWT `sub` và DB, không bao giờ nhận user ID trong body.
- Đánh dấu tiến độ sau khi lưu dữ liệu hợp lệ và trong **cùng một transaction**. Những PUT lặp lại không thêm marker trùng.
- PUT Step 2 thay thế toàn bộ danh sách link, dùng PostgreSQL `ON CONFLICT DO NOTHING` khi thêm skills mới.
- `user_onboarding_steps` chỉ chứa số 1–7; Step 8 = `User.IsOnboardingCompleted`.
- `POST /complete` kiểm tra cả marker và dữ liệu bắt buộc từ DB, không tin client.
- `UserProfile.Bio` không bị ghi vào, không phụ thuộc AI.
- Sau khi hoàn tất, PUT cập nhật một step với dữ liệu hợp lệ vẫn giữ `IsOnboardingCompleted = true` (không tự huỷ hoàn tất).
- Access gate chặn business APIs khi chưa hoàn tất thuộc STEP 13, chưa triển khai ở đây.

## Build

```powershell
dotnet restore
dotnet build
```

Không tạo thêm EF Migration ở STEP 12.

## Kiểm tra PostgreSQL

```sql
SELECT user_id, username, username_normalized, is_onboarding_completed
FROM users WHERE username_normalized = 'tindy_test_01';

SELECT us.user_id, s.name, s.name_normalized
FROM user_skills us JOIN skills s ON s.skill_id = us.skill_id
ORDER BY s.name;

SELECT user_id, step_number, completed_at
FROM user_onboarding_steps
ORDER BY user_id, step_number;
```

Kiểm tra test cases: GET draft của user mới `completed_steps=[]`, lưu Step 7 với URLs null vẫn mark Step 7, gọi `/complete` trước khi đủ 1–7 trả 422, sau khi đủ trả 200 và `Bio` còn NULL, PUT Step 2 hai lần không tạo liên kết trùng, username trùng trả 409, no-auth trả 401, retry `/complete` trả 200.

## Git

```powershell
git status
git diff --check
git add src/Tindy.Application/DTOs/Onboarding/ src/Tindy.Application/Interfaces/Services/IOnboardingService.cs src/Tindy.Application/Interfaces/Repositories/IOnboardingRepository.cs src/Tindy.Application/Exceptions/UsernameAlreadyExistsException.cs src/Tindy.Application/Exceptions/OnboardingIncompleteException.cs src/Tindy.Application/Services/OnboardingService.cs src/Tindy.Infrastructure/Repositories/OnboardingRepository.cs src/Tindy.Infrastructure/Extensions/ServiceCollectionExtensions.cs src/Tindy.Api/Controllers/MeController.cs src/Tindy.Api/Extensions/ServiceCollectionExtensions.cs src/Tindy.Api/ExceptionHandlers/GlobalExceptionHandler.cs docs/guide/ONBOARDING_API_IMPLEMENTATION.md
git commit -m "feat(onboarding): implement eight-step completion api"
```

Trên PowerShell, khi nhập `git add`, dùng một dòng hoặc dấu backtick ` thay cho dấu `\\` nối dòng (dấu `\\` trong đoạn minh họa trên chỉ dành cho Bash).

## Đánh giá phạm vi

Chưa có .NET SDK/PostgreSQL trong môi trường soạn source nên đây là patch đã rà soát tĩnh, không phải build/run được xác nhận. Hãy chạy build/Postman trên máy và gửi log nếu có lỗi.
