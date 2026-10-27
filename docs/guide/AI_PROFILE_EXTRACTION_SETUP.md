# Cấu hình AI Profile Extraction

Chạy các lệnh dưới đây từ `backend/`. Hợp đồng HTTP nằm tại [AI_PROFILE_EXTRACTION.md](../api/AI_PROFILE_EXTRACTION.md). Bio chưa được triển khai; xem [kế hoạch riêng](../../../BIO_IMPLEMENTATION_PLAN.md).

## Provider được chọn

Backend gọi Amazon Bedrock Converse trực tiếp bằng `HttpClient` và Bearer API key, không cần AWS SDK hoặc dependency mới. Model ban đầu là Amazon Nova 2 Lite (`global.amazon.nova-2-lite-v1:0`) qua endpoint vùng `ap-southeast-1`; không dùng Claude và không có model fallback.

Đây là **global inference profile**: endpoint Singapore không bảo đảm dữ liệu chỉ được xử lý tại Singapore. Trước khi gửi CV thật, cần hiểu phạm vi xử lý dữ liệu và chỉ dùng tài liệu được phép gửi tới AWS. Không có OCR service riêng; mỗi lượt Nova nhận cùng PDF/text, xử lý theo prompt và catalog riêng.

## Profile và danh sách chạy song song

`POST /api/ai/extract-profile` chạy song song `extract_profile_scalars` và `extract_lists`. Lượt profile nhận role catalog mã `r1`, `r2`, …; lượt danh sách nhận skill catalog mã `s1`, `s2`, …. Schema công khai giữ nguyên: trường đơn có bằng chứng được điền, trường chưa rõ giữ null; danh sách cung cấp skills/experiences/certificates. Không tự ghi database, không sửa frontend. Current role không tự trở thành preferred role; role mong muốn cần câu nêu rõ nguyện vọng và khớp catalog.

Mỗi lượt sử dụng một forced tool tương ứng, catalog mã ngắn theo request và PDF trước hướng dẫn text. Mỗi skill cần một trích đoạn bằng chứng khớp tên hoặc tên tương đương; gợi ý thiếu bằng chứng phù hợp bị bỏ và có warning, không tự chấp nhận chỉ vì UUID tồn tại. Backend đổi mã về UUID active, giữ tên công ty trong mô tả và chuẩn hóa chuỗi ngày nguồn theo độ chính xác thật. `SQL` được giữ nếu catalog có mục SQL; không suy diễn PostgreSQL/MySQL/SQL Server từ SQL chung.

## Cấu hình không nhạy cảm

`src/Tindy.Api/appsettings.json` và `appsettings.Development.example.json` khai báo:

```json
{
  "Bedrock": {
    "Enabled": false,
    "Region": "ap-southeast-1",
    "ModelId": "global.amazon.nova-2-lite-v1:0",
    "TimeoutSeconds": 60
  }
}
```

`Enabled` mặc định tắt. `TimeoutSeconds` cho phép 1–120. Implementation chỉ chấp nhận model đã chọn; đổi model là thay đổi cần đánh giá lại contract/khả năng PDF, không phải cách khắc phục lỗi quyền truy cập.

Các giá trị cố định trong code, **không phải config keys**: `maxTokens=4096`, `temperature=0.1`, reasoning tắt, tối đa 2 calls đồng thời/process và không xếp hàng. API giới hạn 5 request/phút/người dùng. Không retry, repair call hoặc fallback tự động; mỗi request đầy đủ giữ đồng thời hai slot trước khi gọi hai lượt model. Nếu thiếu slot, không lượt nào được khởi chạy và request trả `AI_BUSY`. List-only manual mode vẫn giữ một slot từ cùng bộ giới hạn. Nếu một lượt lỗi, backend hủy/chờ lượt kia rồi trả lỗi gốc, không có HTTP 200 một phần. Deadline tổng của endpoint là 120 giây, gồm đọc multipart/catalog; timeout mỗi lượt vẫn theo `TimeoutSeconds`. Hai lượt gửi cùng PDF/text riêng biệt nên mỗi request đầy đủ có hai lần tính input và chi phí tương ứng. Khi chạy nhiều replica, giới hạn trong bộ nhớ áp dụng riêng từng process, không phải quota phân tán.

## API key trong .NET User Secrets

`Tindy.Api.csproj` đã có `UserSecretsId`; không cần `dotnet user-secrets init`. Dùng đúng project API, không đặt key vào appsettings, frontend, playground hoặc Git:

```bash
dotnet user-secrets set "Bedrock:ApiKey" "<BEDROCK_API_KEY>" --project src/Tindy.Api
dotnet user-secrets set "Bedrock:Enabled" "true" --project src/Tindy.Api
```

`<BEDROCK_API_KEY>` là placeholder, không phải key mẫu dùng được. Không chia sẻ output `user-secrets list`; tránh để key thật trong shell history hoặc bản ghi terminal. User Secrets chỉ dành cho development và không mã hóa trên ổ đĩa. Khi deploy, inject `Bedrock__ApiKey` qua secret store của môi trường; không commit file chứa secret.

Key phải còn hiệu lực và có quyền gọi model/inference profile tương ứng. Không cần cấu hình Access Key/Secret Access Key song song. Bật tính năng không tự tạo request tới AWS, nhưng gọi endpoint extraction sẽ phát sinh Bedrock invocation và có thể tính phí.

API vẫn cần cấu hình database/JWT hiện có vì xác thực tài khoản và đọc Catalog diễn ra trong backend. Xem [DATABASE_SETUP.md](DATABASE_SETUP.md); không có migration mới cho extraction.

## Chạy và kiểm thử

```bash
dotnet build Tindy.slnx
dotnet test Tindy.slnx
dotnet run --project src/Tindy.Api --launch-profile https
```

Launch profile `https` chạy `https://localhost:7190` và `http://localhost:5217`, nạp User Secrets và phù hợp luồng OAuth hiện tại. Trên máy local có lỗi kết nối IPv6, chạy cùng `DOTNET_SYSTEM_NET_DISABLEIPV6=1`; không cần sửa code production. Dùng JWT của tài khoản active; chưa hoàn thành onboarding vẫn gọi được extraction. Xem ví dụ multipart trong contract API. Không gửi Bedrock key từ client.

Unit/provider tests dùng HTTP giả, không cần AWS key và không được gọi mạng AWS. HTTP/database regression dùng các fixture tương ứng; bộ test tích hợp PostgreSQL hiện tại cần Docker và database disposable, không dùng database chung. Chạy test thành công không chứng minh độ chính xác model thật trên mọi CV.

Chỉ thực hiện live smoke test khi được cho phép gọi AWS. Bộ mẫu đánh giá nên có PDF có text, PDF scan, tiếng Việt/Anh, text-only, README không xác nhận tác giả, dữ liệu mâu thuẫn, thông tin thiếu, phủ định/nguyện vọng học, prompt injection và PDF không đọc được. Ghi nhận cả field sai/thiếu, warning, độ trễ và chi phí; không đưa CV thật hoặc API key vào fixtures/logs.

Kiểm thử PDF có sẵn trong folder bằng manual runner riêng: xem [Local PDF extraction verification](../../tests/Manual/AiListExtraction/README.md). Runner mặc định dùng production list service; thêm `--full-profile` để chạy production combined service (hai model calls), key trong User Secrets và catalog database read-only; không cần sao chép JWT trình duyệt. `--self-test` hoàn toàn offline, `--live` mới gọi AWS. Full-profile mode yêu cầu các kỳ vọng `profile` trong file ngoài repo; không tự retry. Kỳ vọng và báo cáo nằm ngoài repo; không commit PDF thật hoặc raw output.

## Chẩn đoán lỗi

- `AI_UNAVAILABLE`: kiểm tra cờ bật, key còn hạn, region/model và quyền AWS; không xuất key ra log để kiểm tra.
- `AI_DOCUMENT_REJECTED`: thử PDF không mã hóa, có nội dung đọc được; PDF không phải mọi định dạng đồ họa đều được model xử lý.
- `AI_BUSY` / `AI_RATE_LIMITED`: chờ rồi chủ động thử lại; không tạo retry loop trên client.
- `AI_INVALID_OUTPUT`: tool result/schema/reference không hợp lệ hoặc bị cắt ngắn. Backend từ chối toàn draft. Ở Development, xem `ai_diagnostics.stage`, `ai_diagnostics.stop_reason`, `failure_reason`, `validation_reason` và raw tool input. `max_tokens` xác nhận hết token; tăng timeout không giải quyết nguyên nhân đó.
- `AI_TIMEOUT`: provider hoặc toàn request quá timeout; cancellation hủy/chờ cả hai lượt và giải phóng slot. Không tăng timeout vô hạn.

Development có thể trả raw output lỗi tối đa 128 KiB và metadata trong response `no-store`; Production/Staging không trả các trường đó. Thành công ở Development có header `X-AI-Diagnostics` với tổng token/thời gian/cap và `stages.profile`/`stages.lists`; lỗi chỉ rõ lượt thất bại và raw output của lượt đó. Không thay cấu trúc `data`.

Chỉ log trace, loại lỗi, thời gian và số token; không log key, nguồn tài liệu, raw provider response hoặc draft. Không lưu draft/file lâu dài hay tự cập nhật profile; người dùng kiểm tra kết quả rồi lưu qua API hiện có.

## Tham chiếu AWS

- [Dùng Bedrock API keys](https://docs.aws.amazon.com/bedrock/latest/userguide/api-keys-use.html).
- [Nova 2 Lite model card và inference profiles](https://docs.aws.amazon.com/bedrock/latest/userguide/model-card-amazon-nova-2-lite.html).
- [Nova 2 multimodal/PDF input](https://docs.aws.amazon.com/nova/latest/nova2-userguide/using-multimodal-models.html).
- [Nova 2 reasoning configuration](https://docs.aws.amazon.com/nova/latest/nova2-userguide/extended-thinking.html).

Provider/model capabilities có thể thay đổi; kiểm tra lại nguồn chính thức trước khi thay model, region hoặc format request.

## Kiểm chứng triển khai profile + danh sách

Build solution đã qua, không warning/lỗi. Bộ regression không cần Docker: 530/530 test qua (máy local cần `DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` để tránh giới hạn inotify khi tạo nhiều host test). Trong đó có 234 test AI/provider/HTTP. Manual reviewer self-test offline qua; frontend hiện có qua typecheck, build và 13/13 kiểm tra trình duyệt AI mà không sửa file frontend.

Đây là bằng chứng offline/contract. Kiểm thử hai lượt thật với PDF đang chờ quyền gửi tài liệu cá nhân tới Amazon Bedrock; chưa có kết quả live cho giai đoạn profile. Kỳ vọng và tiến độ được giữ ngoài repository, không ghi báo cáo thành công khi chưa gọi model.
