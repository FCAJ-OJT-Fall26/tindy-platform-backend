# AI Profile Extraction API

API trích xuất profile, kỹ năng, kinh nghiệm và chứng chỉ từ CV PDF, README được dán hoặc mô tả tự do. Hai lượt model chạy song song: một lượt các trường đơn của profile, một lượt danh sách. Endpoint và schema response giữ nguyên; không sinh bio và không tự lưu dữ liệu.

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

Với Gemma 4 31B, tổng JSON gửi provider còn bị giới hạn 3.500.000 byte, gồm PDF base64, prompt và catalog. Request upload hợp lệ nhưng vượt giới hạn provider vẫn trả 413 `AI_PAYLOAD_TOO_LARGE` trước khi gửi AWS; không tự chuyển ảnh hay cắt PDF.

Mỗi part chỉ xuất hiện một lần; `file` phải là file part và `text` phải là text part. Duplicate/unknown fields hoặc tráo kiểu part trả 400. PDF rỗng trả 400 ngay cả khi có text đi kèm.

Ví dụ text-only (chỉ chạy khi đã bật provider và được phép gọi AWS):

```bash
curl https://localhost:7190/api/ai/extract-profile \
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

Lượt `extract_profile_scalars` chỉ trích các trường đơn của profile; lượt `extract_lists` nhận skill/role catalog và sở hữu `profile.skill_ids`, `experiences` và `certificates`. Backend ghép và kiểm tra toàn bộ draft trước khi trả HTTP 200. Một lượt lỗi sẽ hủy và chờ lượt còn lại kết thúc, rồi trả lỗi của lượt thất bại; không trả kết quả một phần và không tự gọi sửa output.

Scalar không có bằng chứng trả `null`; collection trả `[]`. `is_ongoing` cũng nullable, không đoán `false` khi không rõ. Không có `bio`, `resume_url`, ID của bản ghi, trường bảo mật, timestamp hoặc trạng thái xác minh. `skill_ids` chỉ chứa UUID thuộc catalog active; `preferred_role_id` chỉ có giá trị khi nguồn nêu rõ vai trò mong muốn và khớp catalog active. Chức danh hiện tại không tự trở thành vai trò mong muốn. Không tạo skill/role mới.

Ngày experience dùng `YYYY-MM`; ngày certificate dùng `YYYY-MM-DD`. Thiếu thành phần ngày không được tự thêm. Tối đa 20 experiences, 20 certificates, 50 warnings; profile có tối đa 30 skill IDs, mỗi experience tối đa 20. Các giới hạn độ dài scalar, URL/email và ngày tương thích với API profile hiện có, nhưng trường bắt buộc khi lưu vẫn có thể `null` trong draft.

Model nhận catalog mã ngắn theo request: skill `s1`, `s2`, … và role `r1`, `r2`, …, không phải UUID. Lượt profile nhận role catalog cho nguyện vọng; lượt danh sách nhận cả skill và role catalog cho kinh nghiệm. Backend đọc skill rồi role tuần tự trước khi bắt đầu hai lời gọi model để tránh dùng chung DbContext đồng thời. Riêng `preferred_role_id` chỉ được chọn khi trích đoạn nêu rõ mong muốn nhận chính vai trò đó; chỉ tên chức danh hoặc mong muốn một môi trường kỹ thuật không đủ bằng chứng. Backend kiểm tra mã, trùng lặp, số lượng rồi ánh xạ về đúng UUID trong snapshot catalog đã gửi. Không fuzzy-match mã sai. Tool input dùng map `{mã: bằng chứng}` để mỗi skill chỉ có một khóa; JSON có khóa trùng vẫn bị từ chối trước khi deserialize. Mỗi mã kỹ năng phải có trích đoạn bằng chứng ngắn chứa tên kỹ năng hoặc tên tương đương rõ ràng. Mã hợp lệ nhưng bằng chứng không khớp bị bỏ kèm `INSUFFICIENT_EVIDENCE`; mã lạ/trùng/vượt giới hạn vẫn là lỗi. Đây là kiểm tra nhất quán tên–bằng chứng, không phải OCR độc lập để chứng minh model đã chép đúng từng chữ từ PDF hoặc gắn đúng kỹ năng vào từng công việc. Các gợi ý gắn với experience vẫn cần người dùng đối chiếu với nguồn trước khi sử dụng. CV ghi SQL và catalog có SQL thì SQL vẫn được trả; không được tự thay SQL bằng PostgreSQL/MySQL/SQL Server khi nguồn không nêu các sản phẩm đó. Nếu catalog chưa có SQL tương đương, bỏ mục chưa khớp và trả `UNMATCHED_CATALOG`.

Mỗi `experiences[].role_title` chỉ là tên chính xác trong catalog role active hoặc null. Model chọn `role_key`; backend kiểm tra mã rồi lấy tên từ cùng snapshot database, không chấp nhận model tự viết public title. Có thể chọn cùng nhóm nghề nghiệp cho tên tiếng Việt hoặc cấp Junior/Senior/Intern khi tương đương về chức năng; không tự đổi sang nghề khác. Chức danh/cấp bậc nguyên gốc được giữ trong `description` khi khác tên catalog. Không có role tương đương thì `role_title=null`, giữ chức danh gốc trong mô tả và cảnh báo `UNMATCHED_CATALOG`. Đây là vai trò của kinh nghiệm, độc lập với role mong muốn; public field vẫn là string/null, không thêm ID/field mới.

Với kinh nghiệm làm việc, tên công ty được giữ trong `description`; `project_name` không được tự lấy từ tên công ty. Model sao chép ngày nguyên gốc vào tool input nội bộ; backend chuẩn hóa tháng/năm cho experience và ngày/tháng/năm cho certificate. Ngày thiếu thành phần hoặc mơ hồ giữ null kèm `INCOMPLETE_DATE`, không tự thêm ngày 01/01. Warning giữ nguyên chuỗi ngày nguồn (ví dụ `2019`, `2021`) để không mất thông tin năm của chứng chỉ. Không thêm field năm vào response hoặc cột mới vào DB.

Warning dùng các code `CONFLICT`, `INSUFFICIENT_EVIDENCE`, `INCOMPLETE_DATE`, `UNMATCHED_CATALOG`, `MISSING_INFORMATION`, `LIMIT_EXCEEDED`. `field` là đường dẫn hợp lệ trong draft, như `profile.display_name`, `experiences[0].start_date`, tên collection khi bỏ một mục, hoặc `$` cho toàn nguồn. Backend từ chối mục experience/certificate hoàn toàn rỗng, đường dẫn/code lạ và cảnh báo `CONFLICT` đi kèm scalar vẫn có giá trị. Việc xác định dữ kiện có đúng với nguồn hay không vẫn phụ thuộc model và bước duyệt của người dùng.

Frontend hiện có tiếp tục đọc `data.profile.skill_ids`, các warning và raw response như trước; các trường đơn được trả thêm giá trị trong chính các field đã có. Tích hợp này không thay UI và không tự điền tab Profile builder đang dùng template local.

## Cách hiểu bản nháp

- Model chỉ được trích xuất sự thật có trong nguồn. Dữ liệu mâu thuẫn phải để trống trường tương ứng và kèm cảnh báo để người dùng xử lý.
- Công nghệ liệt kê trong README không tự chứng minh người dùng có kỹ năng đó. Cần bằng chứng người dùng đã sử dụng; không suy ra kỹ năng từ phủ định, yêu cầu tuyển dụng, ví dụ hay mục tiêu muốn học.
- Nguồn hợp lệ nhưng không có dữ kiện cá nhân trả draft rỗng với cảnh báo. Output sai schema hoặc bị model cắt ngắn là lỗi, không trả draft nửa chừng.
- Người dùng phải kiểm tra, sửa và bổ sung dữ liệu trước khi lưu. Client không gửi nguyên object `data` vào PATCH profile; tách trường được chọn sang các API profile/experience/certificate hiện có và tuân thủ validation của từng API.

## Vận hành và lỗi

Quota 5 request/phút/người dùng, tối đa 2 Bedrock call đồng thời mỗi process, không có hàng đợi. Một request đầy đủ giữ đủ hai slot trước khi gọi bất kỳ lượt nào; thiếu slot trả `AI_BUSY` mà không khởi chạy nửa request. Hai lượt chạy song song, mỗi lượt tối đa 8192 output tokens (gồm reasoning) và dùng cùng timeout provider đã cấu hình. Deadline toàn HTTP request là 120 giây, gồm đọc multipart/catalog; tương thích timeout 130 giây hiện có của frontend. Lượt list-only trong manual runner vẫn dùng một slot cùng bộ giới hạn. Không tự retry/fallback. PDF/text được gửi riêng tới mỗi lượt, do đó mỗi request đầy đủ có hai lần xử lý input và chi phí tương ứng.

Lỗi dùng `application/problem+json` với `error_code` và `traceId`; không bọc `{success:false}`.

Riêng môi trường `Development`, lỗi `AI_INVALID_OUTPUT` có thể kèm `raw_model_output`, `raw_output_kind` (`tool_input`, `model_text`, `provider_response` hoặc `merged_draft`) và `raw_output_truncated` để xem nội dung bị từ chối. Nội dung tối đa 128 KiB UTF-8, chỉ nằm trong response `no-store`; không được ghi log hoặc lưu lâu dài. Production/Staging không trả các trường chẩn đoán này. Raw output vẫn là dữ liệu không hợp lệ, không phải draft được chấp nhận. Provider đọc trực tiếp đúng một function call hoàn tất của Gemma; giữ nguyên chuỗi arguments để validator phát hiện JSON trùng khóa. Reasoning không được trả trong raw output; nếu provider envelope không phải JSON hợp lệ thì không trả nguyên envelope.

Ở Development, lỗi AI có thể kèm `ai_diagnostics` (`stop_reason`, `input_tokens`, `output_tokens`, `max_output_tokens`, `elapsed_ms`, `failure_reason`) và `validation_reason`. `max_tokens`/`output_token_limit` phân biệt hết token với HTTP 504 `AI_TIMEOUT`; mã lạ/trùng/vượt giới hạn được phân biệt bằng validation reason. Lỗi có thêm `stage` trong `ai_diagnostics` (`profile`, `lists`, `merge` hoặc `request`). Response thành công có header `X-AI-Diagnostics` ở Development: số token tổng, thời gian thực tế toàn request, cap tổng 16384 và `stages.profile`/`stages.lists` chứa metadata từng lượt. Không thêm nhóm mới vào `data`. Warnings được gộp, bỏ trùng theo field/code/message; nếu quá 50 thì giữ 49 và thêm một `LIMIT_EXCEEDED`. Cảnh báo thiếu dữ kiện của lượt danh sách được giới hạn về danh sách, không mô tả sai rằng profile không có thông tin. Production không trả metadata chẩn đoán này hoặc raw output.

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
| 504 | `AI_TIMEOUT` | Provider hoặc toàn request quá thời gian chờ. |

PDF/text và draft không được lưu vào database hoặc kho file lâu dài. Request được gửi tới Bedrock khi tính năng bật. Gemma mặc định được gọi tại `us-east-1`, nhận nguyên PDF và dùng `store=false`; reasoning không được trả trong draft/raw output; xem [hướng dẫn cấu hình](../guide/AI_PROFILE_EXTRACTION_SETUP.md). Kiểm thử offline không chứng minh độ chính xác OCR/model thật trên mọi tài liệu.
