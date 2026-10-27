# AI Profile Extraction API

API độc lập để trích xuất dữ kiện từ CV PDF, README được dán hoặc mô tả tự do thành bản nháp hồ sơ. Không yêu cầu gọi từ onboarding hay chỉnh sửa profile; không sinh bio và không tự lưu dữ liệu.

## Endpoint và quyền truy cập

`POST /api/ai/extract-profile`

Gửi `Authorization: Bearer <access_token>`. Policy `AuthenticatedOnly` kiểm tra JWT và tài khoản active/chưa xóa; người dùng chưa hoàn thành onboarding vẫn dùng được. Không nhận `user_id` hoặc thay đổi trạng thái onboarding.

## Request

Content type: `multipart/form-data` với boundary do HTTP client tạo.

| Part | Quy tắc |
| --- | --- |
| `file` | Tùy chọn, một PDF với MIME `application/pdf`, tối đa 4 MiB (4.194.304 byte); nội dung phải có PDF signature. |
| `text` | Tùy chọn, tối đa 20.000 ký tự; mô tả cá nhân/dự án hoặc README được dán. |

Cần ít nhất một nguồn không rỗng; có thể gửi cả hai. Tổng HTTP body tối đa 5 MiB (5.242.880 byte), bao gồm multipart overhead. Không hỗ trợ DOCX, ảnh upload riêng, URL cần tải xuống, nhiều file hoặc field lạ. Các giới hạn này là giới hạn của ứng dụng, không phải tuyên bố giới hạn tối đa của Bedrock.

Mỗi part chỉ xuất hiện một lần; `file` phải là file part và `text` phải là text part. Duplicate/unknown fields hoặc tráo kiểu part trả 400. PDF rỗng trả 400 ngay cả khi có text đi kèm.

Ví dụ text-only (chỉ chạy khi đã bật provider và được phép gọi AWS):

```bash
curl http://localhost:5217/api/ai/extract-profile \
  -H "Authorization: Bearer $TINDY_ACCESS_TOKEN" \
  --form-string 'text=Tôi là sinh viên CNTT. Tôi đã viết API bằng C# trong dự án nhóm.'
```

Thêm `--form 'file=@./cv.pdf;type=application/pdf'` để kết hợp PDF. Không đặt thủ công header multipart thiếu boundary. Token trong ví dụ là JWT của Tindy; Bedrock API key chỉ nằm trên server.

## Response

HTTP 200 dùng envelope `{ "success": true, "message": "Profile extracted. Review the suggestions before saving.", "data": ... }` và `Cache-Control: no-store`. `data` luôn có bốn nhóm `profile`, `experiences`, `certificates`, `warnings`.

| Nhóm | Fields |
| --- | --- |
| `profile` | `display_name`, `university`, `phone_number`, `headline`, `location`, `current_role`, `preferred_role_id`, `available_hours_per_week`, `professional_interests`, `learning_goals`, `previous_projects_and_contribution`, `technologies_to_learn`, `github_url`, `linkedin_url`, `personal_website_url`, `public_email`, `skill_ids` |
| `experiences[]` | `project_name`, `role_title`, `description`, `repo_url`, `start_date`, `end_date`, `is_ongoing`, `skill_ids` |
| `certificates[]` | `title`, `issuer`, `credential_id`, `credential_url`, `issued_at`, `expires_at` |
| `warnings[]` | `field`, `code`, `message` |

Scalar không có bằng chứng trả `null`; collection trả `[]`. `is_ongoing` cũng nullable, không đoán `false` khi không rõ. Không có `bio`, `resume_url`, ID của bản ghi, trường bảo mật, timestamp hoặc trạng thái xác minh. `preferred_role_id` và `skill_ids` chỉ chứa UUID thuộc Catalog active; không tạo skill/role mới.

Ngày experience dùng `YYYY-MM`; ngày certificate dùng `YYYY-MM-DD`. Thiếu thành phần ngày không được tự thêm. Tối đa 20 experiences, 20 certificates, 50 warnings; profile có tối đa 30 skill IDs, mỗi experience tối đa 20. Các giới hạn độ dài scalar, URL/email và ngày tương thích với API profile hiện có, nhưng trường bắt buộc khi lưu vẫn có thể `null` trong draft.

Warning dùng các code `CONFLICT`, `INSUFFICIENT_EVIDENCE`, `INCOMPLETE_DATE`, `UNMATCHED_CATALOG`, `MISSING_INFORMATION`, `LIMIT_EXCEEDED`. `field` là đường dẫn hợp lệ trong draft, như `profile.display_name`, `experiences[0].start_date`, tên collection khi bỏ một mục, hoặc `$` cho toàn nguồn. Backend từ chối mục experience/certificate hoàn toàn rỗng, đường dẫn/code lạ và cảnh báo `CONFLICT` đi kèm scalar vẫn có giá trị. Việc xác định dữ kiện có đúng với nguồn hay không vẫn phụ thuộc model và bước duyệt của người dùng.

## Cách hiểu bản nháp

- Model chỉ được trích xuất sự thật có trong nguồn. Dữ liệu mâu thuẫn phải để trống trường tương ứng và kèm cảnh báo để người dùng xử lý.
- Công nghệ liệt kê trong README không tự chứng minh người dùng có kỹ năng đó. Cần bằng chứng người dùng đã sử dụng; không suy ra kỹ năng từ phủ định, yêu cầu tuyển dụng, ví dụ hay mục tiêu muốn học.
- Nguồn hợp lệ nhưng không có dữ kiện cá nhân trả draft rỗng với cảnh báo. Output sai schema hoặc bị model cắt ngắn là lỗi, không trả draft nửa chừng.
- Người dùng phải kiểm tra, sửa và bổ sung dữ liệu trước khi lưu. Client không gửi nguyên object `data` vào PATCH profile; tách trường được chọn sang các API profile/experience/certificate hiện có và tuân thủ validation của từng API.

## Vận hành và lỗi

Quota 5 request/phút/người dùng, tối đa 2 Bedrock call đồng thời mỗi process, không có hàng đợi. Một extraction gọi model một lần, không tự retry/fallback; lỗi ở request mới có thể cần người dùng chủ động thử lại.

Lỗi dùng `application/problem+json` với `error_code` và `traceId`; không bọc `{success:false}`.

Riêng môi trường `Development`, lỗi `AI_INVALID_OUTPUT` có thể kèm `raw_model_output`, `raw_output_kind` (`model_text` hoặc `provider_response`) và `raw_output_truncated` để xem nội dung bị từ chối. Nội dung tối đa 128 KiB UTF-8, chỉ nằm trong response `no-store`; không được ghi log hoặc lưu lâu dài. Production/Staging không trả các trường chẩn đoán này. Raw output vẫn là dữ liệu không hợp lệ, không phải draft được chấp nhận.

| HTTP | `error_code` | Tình huống |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | Thiếu nguồn, text quá dài, PDF rỗng, multipart/fields không hợp lệ. |
| 401 / 403 | `AUTH_UNAUTHORIZED` / `AUTH_FORBIDDEN` | JWT hoặc trạng thái tài khoản không hợp lệ. |
| 413 | `AI_PAYLOAD_TOO_LARGE` | File hoặc toàn request quá giới hạn. |
| 415 | `AI_UNSUPPORTED_MEDIA` | Sai content type request hoặc file MIME. |
| 422 | `AI_DOCUMENT_REJECTED` | PDF signature không hợp lệ hoặc provider không xử lý được tài liệu. |
| 429 | `AI_RATE_LIMITED` / `AI_BUSY` | Hết quota người dùng hoặc không còn slot/provider bận. |
| 502 | `AI_PROVIDER_ERROR` / `AI_INVALID_OUTPUT` | Provider lỗi, output sai schema hoặc bị cắt ngắn. |
| 503 | `AI_UNAVAILABLE` | Tính năng tắt, thiếu hoặc sai cấu hình/quyền provider. |
| 504 | `AI_TIMEOUT` | Provider quá thời gian chờ. |

PDF/text và draft không được lưu vào database hoặc kho file lâu dài. Request được gửi tới Bedrock khi tính năng bật. Global inference có thể xử lý ngoài Singapore; xem [hướng dẫn cấu hình](../guide/AI_PROFILE_EXTRACTION_SETUP.md). Kiểm thử offline không chứng minh độ chính xác OCR/model thật trên mọi tài liệu.
