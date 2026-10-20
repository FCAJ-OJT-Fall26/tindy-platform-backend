# Codex Frontend Prompt — Tindy Profile V1

Sao chép nội dung bên dưới và gửi Codex khi **đã mở source FE mới nhất**; cung cấp kèm `PROFILE_API.md` và tốt nhất `PROFILE_OPENAPI.yaml`. Không giả định FE ZIP ngày 20/10 là phiên bản mới nhất.

---

Bạn đang làm việc trong repository Frontend Tindy (React/TypeScript/Vite). Backend Profile V1 đã được triển khai trên nhánh `feature/profile` của repository `tindy-backend-platform`; contract chuẩn là file `docs/api/PROFILE_API.md` và `docs/api/PROFILE_OPENAPI.yaml` đính kèm. **Hãy đọc source frontend hiện tại rồi mới sửa; không tự suy diễn field hoặc endpoint.**

## Goal

Chỉ tích hợp Profile Module FE với Backend hiện có. Giữ nguyên Authentication/JWT/OAuth ticket exchange và flow Onboarding; không sửa Backend. Không tích hợp file upload/S3, certificate verification, auto AI summary hay Project Management CRUD.

## API & security

- Prefix `/api`; token gửi `Authorization: Bearer <access_token>` qua API client hiện hữu; snake_case JSON; success `{success,message,data}`; errors `application/problem+json`, `error_code`, `errors`.
- Account `GET /api/me` **khác** owner profile `GET /api/me/profile`; Profile yêu cầu active + completed onboarding.
- Owner: GET/PATCH `/api/me/profile`; GET/POST `/api/me/profile/experiences`; PUT/DELETE `/api/me/profile/experiences/{experience_id}`; GET/POST `/api/me/profile/certificates`; PUT/DELETE `/api/me/profile/certificates/{certificate_id}`.
- Member (read only): GET `/api/users/{user_id}/profile`, `/experiences`, `/certificates` tương ứng; route UI `/users/:userId`, yêu cầu đăng nhập.
- POST trả 201 + data; PUT/PATCH trả 200 + data; DELETE trả 204 **không body**; không có GET-by-ID Experience/Certificate.

## Key validation/semantics

- Profile PATCH partial: omitted unchanged; nullable field `null` clears; required fields không null/blank; reject unknown Account/security fields. Không gửi `avatar_url`/`cover_image_url`.
- `preferred_role_id`: UUID từ Catalog. `skill_ids`: array 1..30 UUID, chỉ gửi khi thay đổi, replace toàn bộ selected Profile Skills. availability 1..40.
- Experience POST/PUT (full replacement): `project_name` required <=150, `role_title` <=150, `description` required <=5000, `repo_url` optional HTTP(S), `start_date`/`end_date` optional `YYYY-MM`, **`is_ongoing` boolean bắt buộc**, **`skill_ids` array bắt buộc (0..20 UUID, `[]` được)**; when ongoing end_date=null. Response field `technologies: {skill_id,name}[]`, không gửi `technologies` trong request.
- Certificate POST/PUT: `title`,`issuer` bắt buộc <=255, optional `credential_id`,`credential_url`, `issued_at`,`expires_at` là `YYYY-MM-DD|null`.
- Public/member DTO **không có** `phone_number` và `resume_url`; không hiển thị login email. `public_email` chỉ từ Profile do user chủ động khai báo.
- Google/GitHub Avatar qua GET `avatar_url` nếu có; fallback initials, cover mặc định CSS; không upload.

## Work steps

1. Kiểm tra `src/App.tsx`, `src/pages/profile/ProfileScreen.tsx`, `src/store.tsx`, `src/screens.tsx`, `src/services/apiClient.ts`, `src/services/catalog.service.ts`, `src/types/*` và các màn AI Studio/Settings. Các tên file chỉ là gợi ý từ FE prototype cũ; dùng cấu trúc FE mới nhất nếu đã đổi.
2. Đề xuất danh sách file cần sửa và phân chia owner/member views, types, services, state. Sau đó triển khai trong phạm vi FE.
3. Tạo types `MyProfileResponse`, `MemberProfileResponse`, `ProfilePatch`, `ExperienceResponse/Upsert`, `CertificateResponse/Upsert`; re-use API client/token/Error handling.
4. Owner Profile load ba API riêng (Profile, Experiences, Certificates) và hiển thị loading/error riêng; sửa Profile bằng PATCH dirty-fields, lấy response server làm state mới; không dùng mock/localStorage làm source of truth.
5. CRUD Experiences/Certificates (list/create/edit/delete) dùng payload thực; không auto tạo từ Onboarding summary hoặc Tindy Projects; chỉ dùng Catalog UUID.
6. Member profile route `/users/:userId` có ba GET, read-only, không có phần Phone/Resume, Edit/Delete.
7. Loại bỏ các demo cards, upload certificate/avatar và verified fake; giữ trang thái empty đúng khi data `[]`; đảm bảo user avatar fallback.
8. Giải quyết nguy cơ `authUser` ghi đè Profile sau PATCH trong global store; các trang Settings/AI Studio liên quan Profile phải không ghi local-only data gây mất đồng bộ server.
9. Kiểm tra scenarios: null clearing, omitted field giữ nguyên, 401/403, 404 owner violation, invalid URL/dates, 201/200/204, network errors, expired token, empty collections. Không chỉnh BE contract để hợp FE mock.
10. Chạy lint/typecheck/build/test scripts đang có, kiểm tra diff và báo cáo file thay đổi, test evidence, giới hạn do BE/DB chưa chạy. Không claim E2E PASS nếu chưa chạy BE/PostgreSQL thật.

Output: source FE tương thích API V1, giải thích mappings quan trọng, danh sách thay đổi, kết quả kiểm tra, những phần còn blocked bởi Backend environment. Không thay đổi file code thuộc Backend. Không tạo route upload/verify chưa có trong contract.
