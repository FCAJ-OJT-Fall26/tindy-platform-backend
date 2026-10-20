# Tindy Profile — Frontend Handoff (Backend First)

> Nguồn BE: `tindy-backend-platform(20261020-131618).zip`, nhánh metadata `feature/profile`, HEAD `eaac2ba`.  
> FE prototype tham khảo gần nhất được cung cấp trong cuộc trao đổi: `tindy-platform-frontend(6).zip` (20/10/2026). **Chưa có FE ZIP mới hơn trong STEP 12**. Codex phải mở source FE mới nhất trước khi sửa.  
> Contract chi tiết: `../api/PROFILE_API.md`; OpenAPI tham chiếu: `../api/PROFILE_OPENAPI.yaml`.

## 1. Kết quả mong muốn

- `/profile` (owner) gọi riêng ba nhóm Profile, Experiences, Certificates, không dùng `initialProfile` hay localStorage làm source of truth.
- `/users/:userId` (member, authenticated) là màn hình **read-only**; lấy Member Profile và hai collection theo ID từ route; **không** hiển thị Phone/Resume/account email.
- Form edit Profile sử dụng `PATCH`, gửi **chỉ field đã thay đổi**; muốn xóa optional gửi null; kỹ năng `skill_ids` gửi array UUID khi thay đổi, sẽ replace toàn bộ selected Profile Skills.
- Add/Edit/Delete Experience/Certificate phản ánh database, không upload file và không hiển thị verified giả.
- FE dùng Avatar URL nếu hợp lệ, fallback initials/default; Cover nền CSS; không có upload controls.
- Các màn AI Studio/Settings không ghi đè Profile server bằng localStorage, nhất là Bio/Skills, DisplayName/University.

## 2. Backend calls FE phải hỗ trợ

| FE action | HTTP |
|---|---|
| Load owner header & details | GET `/api/me/profile` |
| Edit owner profile | PATCH `/api/me/profile` |
| Load owner project experiences | GET `/api/me/profile/experiences` |
| Add/edit/delete owner Experience | POST `/api/me/profile/experiences`; PUT/DELETE `/api/me/profile/experiences/{experience_id}` |
| Load owner certificates | GET `/api/me/profile/certificates` |
| Add/edit/delete owner Certificate | POST `/api/me/profile/certificates`; PUT/DELETE `/api/me/profile/certificates/{certificate_id}` |
| Load member profile | GET `/api/users/{userId}/profile` |
| Load member collections | GET `/api/users/{userId}/profile/experiences`, `/certificates` |
| Load skill/role selectors | GET `/api/catalog/skills`, `/professional-roles` |
| Load current account state | GET `/api/me` (không thay bằng Profile) |

Các `POST` đều trả `201 + data`, `PUT/PATCH` trả `200 + data`, `DELETE` trả `204` body rỗng. Không gọi GET by experience_id/certificate_id (không có).

## 3. Kiểu dữ liệu TypeScript đề xuất

- `MyProfileResponse`: fields như `PROFILE_API.md`, bao gồm `phone_number`, `resume_url` và `skills: {skill_id,name}[]`.
- `MemberProfileResponse`: DTO tách biệt, **không** có phone_number/resume_url; đừng cast từ MyProfileResponse.
- `ExperienceResponse`: id, project/role/description, `technologies: {skill_id,name}[]`, `YYYY-MM|null`, `is_ongoing`, timestamps.
- `ExperienceUpsertRequest`: bắt buộc `project_name`, `role_title`, `description`, `is_ongoing`, `skill_ids` (ít nhất `[]`), optional `repo_url/start_date/end_date`.
- `CertificateResponse`: title/issuer, optional credential_id/credential_url, `YYYY-MM-DD|null`, timestamps.
- `CertificateUpsertRequest`: bắt buộc `title`, `issuer`; optional fields được phép null.
- `ProfilePatch`: optional payload fields; tính presence riêng, **không** gửi toàn bộ một DTO với `undefined`/null ngẫu nhiên; unknown fields bị 400.

## 4. Những chỗ FE prototype cũ cần tìm và refactor

1. `src/pages/profile/ProfileScreen.tsx`: hiện dùng `profile/setProfile`, có demo cards, file upload UI và mock experience/certificate; thay bằng server data, các form phù hợp contract.
2. `src/store.tsx`: `profile` đang dùng localStorage và đồng bộ lại `name/university` từ `authUser`; tránh GET/PATCH Profile thành công rồi bị state cũ ghi đè.
3. `src/App.tsx`: có `/profile`, chưa có `/users/:userId`; thêm member route dưới auth/onboarding gate.
4. `src/services/apiClient.ts`, `apiConfig.ts` và `src/types/`: tái sử dụng `apiRequest<T>` và `ApiError`/token management có sẵn; bổ sung services/DTO types mới, tránh client HTTP thứ hai.
5. `src/screens.tsx`, AI Studio và Settings: kiểm tra chỗ gọi `setProfile`, hiển thị profile, link member; không tự thay đổi account email bằng Profile PATCH.

Các đường dẫn trên dựa FE prototype đã xem, **không khẳng định FE hiện tại vẫn giống như vậy**. Codex phải đọc FE branch mới nhất.

## 5. Quy trình tích hợp an toàn

1. Kiểm tra FE source mới nhất, route, state, API client, auth/onboarding, Catalog và UI mẫu.
2. Thêm TypeScript types và 3 service modules (hoặc tổ chức theo convention thực tế), dùng cùng `Authorization: Bearer`.
3. Thay Profile owner data source và form PATCH: dirty fields, loading/error, refetch hoặc update cache từ response.
4. Thêm Experience/Certificate collection + modal/forms thật (GET, POST, PUT, DELETE); lưu ý `skill_ids` bắt buộc kể cả danh sách rỗng và `is_ongoing` bắt buộc.
5. Thêm read-only member route `/users/:userId`, gọi 3 GET song song, không render private fields.
6. Rà lại Settings/AI Studio để đồng bộ Bio/Skills và Account/Onboarding state; giữ Auth/OAuth session flow.
7. Kiểm tra `data: []`, null values, fallback avatar, 401/403/404/409/422, CRUD thật và loading per section.
8. Chạy `npm run build` và scripts test/lint hiện có; chỉ xác nhận FE->BE end-to-end sau khi BE Migration+PostgreSQL+JWT sẵn sàng.

## 6. Acceptance criteria

- Owner có thể GET/PATCH mà không mất optional fields khác; patch `{"bio":null}` xóa đúng Bio.
- Chọn Role/Skill qua Catalog UUID, không hardcode ID. 1–40 hours, 1–30 Profile Skills, 0–20 Experience Skills.
- Owner Experience và Certificate CRUD trả đúng 201/200/204, 404 khi owner không hợp lệ.
- Member view không có nút edit/delete, không truy cập phone/resume/login email, chưa onboarding caller phải bị 403.
- Empty state chân thực; không project card hoặc certificate upload/demo data mặc định.
- Không thêm AWS S3/file upload, verified badges, AI summary generation hoặc frontend auth redesign trong scope.

## 7. Hạn chế trước khi handoff thật

Bản ZIP BE xác nhận source nhưng chưa cung cấp kết quả STEP 11 đầy đủ trên HEAD hiện tại. Nên kiểm tra DB migrations apply, JWT HTTP contract và regression trước khi nói đã tích hợp production-ready. Nếu FE source đã đổi từ ZIP cũ, điều chỉnh các file paths theo FE hiện tại, không chỉnh Backend chỉ để khớp mock.
