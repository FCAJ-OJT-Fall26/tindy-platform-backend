# AI Profile Extraction — manual local test

Công cụ console .NET 10 độc lập để gửi PDF từ URL và/hoặc bio, README, mô tả text thật tới API extraction hiện có. Kết quả được in ra terminal để duyệt và đối chiếu skill/role ID với catalog active từ database của backend.

Project không nằm trong solution, không chạy cùng `dotnet test`, không thêm package hoặc tham chiếu production project. Công cụ không sinh bio và không lưu profile.

## Chuẩn bị backend và JWT

Chạy các lệnh từ thư mục `backend/`. Cấu hình database, JWT và Bedrock theo [hướng dẫn setup](../../../docs/guide/AI_PROFILE_EXTRACTION_SETUP.md), rồi khởi động API:

```bash
dotnet run --project src/Tindy.Api --launch-profile http
```

Đăng nhập qua Swagger (`http://localhost:5217/swagger`) bằng `POST /api/auth/login`, body gồm `identifier` và `password`. Lấy `data.access_token` của tài khoản active; tài khoản chưa hoàn thành onboarding vẫn dùng được. Nhập JWT vào biến môi trường, không kèm `Bearer`.

Trong Bash/Zsh, lệnh sau cho phép dán token mà không hiển thị khi nhập hay lưu token vào lịch sử lệnh:

```bash
read -rs TINDY_ACCESS_TOKEN
export TINDY_ACCESS_TOKEN
```

Nhấn Enter sau khi dán. Nếu dùng PowerShell, nhập ẩn rồi gán biến:

```powershell
$env:TINDY_ACCESS_TOKEN = [System.Net.NetworkCredential]::new('', (Read-Host 'Tindy JWT' -AsSecureString)).Password
```

API key Bedrock chỉ nằm ở backend trong .NET User Secrets. Chạy live sẽ gửi nguồn tài liệu tới Bedrock qua backend và có thể tính phí; chỉ dùng tài liệu được phép gửi.

## Chạy với dữ liệu thật

Đặt bio/text UTF-8 ở `tests/Manual/AiProfileExtraction/private/bio.txt` (Git ignore), hoặc dùng đường dẫn ngoài repository.

```bash
# Text-only
dotnet run --project tests/Manual/AiProfileExtraction -- \
  --text-file tests/Manual/AiProfileExtraction/private/bio.txt

# PDF-only: thay URL ví dụ bằng URL tải file trực tiếp
dotnet run --project tests/Manual/AiProfileExtraction -- \
  --pdf-url "https://example.com/cv.pdf"

# Kết hợp PDF và text
dotnet run --project tests/Manual/AiProfileExtraction -- \
  --pdf-url "https://example.com/cv.pdf" \
  --text-file tests/Manual/AiProfileExtraction/private/bio.txt
```

PowerShell: viết các lệnh trên một dòng hoặc thay dấu nối dòng `\` bằng backtick.

| Tham số | Ý nghĩa |
| --- | --- |
| `--pdf-url` | HTTPS, không có thông tin đăng nhập trong URL; file tải trực tiếp, không phải trang preview/login. |
| `--text-file` | Bio/text/README, tối đa 20.000 ký tự; cần ít nhất một nguồn không rỗng. |
| `--base-url` | Mặc định `http://localhost:5217`; URL gốc không path/query. HTTP chỉ cho loopback, server khác phải HTTPS. |
| `--self-test` | Chạy kiểm tra offline, dùng riêng, không cần JWT/backend/database/AWS. |
| `--help` | Hiện cú pháp, dùng riêng. |

PDF được tải trong bộ nhớ, tối đa 4 MiB, kiểm tra chữ ký `%PDF-`, timeout tổng 30 giây và tối đa 3 redirect HTTPS. Client tải PDF không mang JWT. Request tới backend không theo redirect, timeout 90 giây mỗi request; JSON response giới hạn 4 MiB. Không có retry tự động. Nhấn Ctrl+C để hủy.

## Đọc kết quả

Luồng chạy: kiểm tra input → đọc hai catalog → tải PDF nếu có → gọi extraction một lần → in draft/warnings → đối chiếu ID.

- `GET /api/catalog/skills` và `GET /api/catalog/professional-roles` đọc database mà backend đang kết nối. Công cụ không kết nối SQL trực tiếp, không cần connection string riêng.
- Kiểm tra `profile.skill_ids`, `experiences[].skill_ids`, `profile.preferred_role_id`: UUID hợp lệ, thuộc catalog active; skill ID không trùng trong từng danh sách. Cùng skill xuất hiện ở profile và experience là hợp lệ.
- Mỗi ID in kèm đường dẫn field, tên catalog và PASS/FAIL. Danh sách rỗng/role null là hợp lệ. Warnings vẫn hiển thị và không tự làm test fail.
- Exit code `0`: các nhóm response và ID đã kiểm tra hợp lệ; `1`: input, download, HTTP, cấu trúc response hoặc catalog ID không hợp lệ.
- HTTP 401: lấy JWT mới. HTTP 429: chờ rồi tự chạy lại; backend giới hạn 5 request/phút/người dùng. HTTP 503: kiểm tra cấu hình/quyền Bedrock.

Đây là kiểm tra cấu trúc cơ bản và đối chiếu ID, không thay thế toàn bộ validator backend. Catalog được đọc trước extraction; nếu catalog thay đổi giữa lượt chạy, kết quả đối chiếu có thể khác. Tự so sánh draft với PDF/bio gốc để phát hiện nội dung sai/thiếu, kỹ năng suy diễn và cảnh báo không phù hợp. ID đúng không chứng minh thông tin trích xuất đúng.

## Kiểm thử offline và dữ liệu riêng tư

```bash
dotnet build tests/Manual/AiProfileExtraction
dotnet run --project tests/Manual/AiProfileExtraction -- --self-test
```

Self-test dùng HTTP handler giả và file text tạm, không mở kết nối mạng. Bao phủ các nguồn đầu vào, multipart, catalog ID, lỗi HTTP/JSON và tải PDF/redirect; không đánh giá độ chính xác model thật.

Công cụ không tự ghi PDF, draft hoặc log xuống đĩa; terminal sẽ chứa thông tin cá nhân trong draft. Không commit dữ liệu thật/output hoặc chia sẻ bản ghi terminal chưa kiểm tra. `private/` chỉ giữ `.gitkeep` trong Git; các file khác bị ignore. Không lưu JWT/API key vào project. Signed PDF URL có thể chứa thông tin nhạy cảm trong lịch sử lệnh của shell dù công cụ không in URL.
