# Chạy ClamAV Docker cho Tindy Backend

## Cấu hình đã chuẩn bị

Backend chạy trên Windows bằng .NET; ClamAV chạy trong Docker Linux và nhận INSTREAM qua `127.0.0.1:3310`. Không mount thư mục uploads/database application vào container. Backend tự gửi stream cần quét.

- `compose.clamav.yml`: official ClamAV **1.5.4**, ghim digest `sha256:751df8dc649cc977696f107c80db4960b6317a7cb511aebc8066f3b91db6b631`, restart `unless-stopped`, tối đa 4 GiB RAM và 2 CPU.
- `deploy/local/clamd.conf`: chỉ nhận PDF tối đa 10.000.000 bytes; bật PDF scan và alerts cho encrypted/scan limits. Mount config read-only.
- Named volume `tindy-media-local_clamav-signatures` lưu virus definitions. FreshClam cập nhật definitions; dữ liệu tồn tại khi container được tạo lại hoặc chạy `down` thông thường.
- Port chỉ publish trên `127.0.0.1`, không mở clamd ra mạng LAN/Internet. Health check dùng `nPING` qua IPv4 rõ ràng, tránh `localhost` trỏ sang IPv6 trong image này.
- Log xoay vòng 3 file, tối đa 10 MB/file. Không có AWS credentials hay thay đổi database người dùng.

Docker Desktop phải đang chạy với Linux containers và đủ RAM. Dung lượng 4 GiB là giới hạn riêng cho container, cần RAM thêm cho Windows, Docker, PostgreSQL và backend. Lần đầu cần mạng để tải image/definitions.

## Khởi động

Chạy PowerShell tại root `tindy-backend-platform`:

```powershell
docker compose -f compose.clamav.yml config --quiet
docker compose -f compose.clamav.yml up -d --wait --wait-timeout 180
docker compose -f compose.clamav.yml ps
docker compose -f compose.clamav.yml logs -f --tail 50
```

Lần đầu tải definitions có thể lâu hơn 180 giây tùy mạng. Nếu lệnh wait hết hạn, kiểm tra logs rồi chạy lại; không coi container `running` là scanner đã sẵn sàng. Chỉ thử PDF khi status `healthy`. `Ctrl+C` ở lệnh logs chỉ dừng theo dõi logs.

Cấu hình mặc định đã có trong `src/Tindy.Api/appsettings.json`, file example cũng có cùng values:

```json
{
  "ClamAv": { "Host": "127.0.0.1", "Port": 3310, "TimeoutSeconds": 30 },
  "MediaLifecycle": { "Enabled": true },
  "GuardDuty": { "Enabled": false }
}
```

Giữ nguyên JWT/connection string đang dùng. Bảo đảm `MediaStorage:Local:RootPath` là đường dẫn tuyệt đối ngoài application content root, rồi chạy:

```powershell
dotnet run --project .\src\Tindy.Api --environment Development
```

PDF upload trả `202 processing`; background worker quét theo chu kỳ mặc định 30 giây. Clean → active/clean; EICAR/threat → rejected; encrypted/scan limits → rejected/unsupported. Scanner không sẵn sàng → vẫn processing/failed, không cho download, retry không sớm hơn 1 phút sau lần thất bại. Ảnh Certificate không cần ClamAV.

## Dừng và khởi động lại

```powershell
docker compose -f compose.clamav.yml stop clamav
docker compose -f compose.clamav.yml up -d --wait --wait-timeout 180

# Xóa container/network của riêng Compose project này, giữ definitions volume:
docker compose -f compose.clamav.yml down
```

Không thêm `--volumes` nếu muốn giữ definitions đã tải. Docker Desktop cần khởi động trước; restart policy không tự bật Docker Desktop khi Windows chưa chạy Docker.

## Kiểm thử thật bằng xUnit

Tests ở `ClamAvLiveIntegrationTests.cs` dùng implementation production `ClamAvMalwareScanner`, HTTP TestServer, Local Storage tạm và PostgreSQL Testcontainers riêng. Không có fake scanner. PDF mẫu hợp lệ được tạo trong memory; trường hợp EICAR nhúng chuỗi thử antivirus vô hại vào PDF attachment. Test cleanup xóa dữ liệu test sau khi chạy. Không tắt antivirus của Windows để chạy test.

```powershell
$env:TINDY_CLAMAV_LIVE = '1'
Remove-Item Env:TINDY_CLAMAV_EXPECT_UNAVAILABLE -ErrorAction SilentlyContinue
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter 'Category=ClamAvLive'
```

Kiểm thử outage chủ động chỉ dừng service ClamAV của project này:

```powershell
docker compose -f compose.clamav.yml stop clamav
$env:TINDY_CLAMAV_EXPECT_UNAVAILABLE = '1'
try {
    dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj --filter 'Category=ClamAvLive'
} finally {
    Remove-Item Env:TINDY_CLAMAV_EXPECT_UNAVAILABLE -ErrorAction SilentlyContinue
    docker compose -f compose.clamav.yml up -d --wait --wait-timeout 180
}

# Kiểm tra sau khi bật lại và chạy toàn bộ regression:
dotnet test .\tests\Tindy.Tests\Tindy.Tests.csproj
Remove-Item Env:TINDY_CLAMAV_LIVE -ErrorAction SilentlyContinue
```

Khi không bật `TINDY_CLAMAV_LIVE`, nhóm live được đánh dấu skipped rõ ràng, các tests trước đó vẫn chạy bình thường. Flag `TINDY_CLAMAV_EXPECT_UNAVAILABLE` chỉ điều khiển **expected assertions trong test**, không thay đổi behavior scanner/backend. Tests gọi lifecycle service trực tiếp để xác định thời điểm quét; lịch/cancellation của Background Worker đã có tests riêng.

## Cập nhật image và chẩn đoán

Digest giúp tránh tự đổi version ngoài ý muốn. Khi nâng ClamAV, tải image official mới, kiểm tra version, cập nhật digest có review và chạy lại clean/EICAR/outage tests. Virus definitions vẫn được FreshClam cập nhật độc lập với digest image.

Nếu unhealthy: kiểm tra `docker compose ... logs`, RAM/OOM, kết nối tới máy chủ definitions và cổng 3310 có bị chiếm. Không chuyển PDF thành clean bằng tay khi scanner lỗi. Nếu backend cũng chạy trong Docker sau này, Host cần đổi sang tên service/network phù hợp; cấu hình này dành cho backend chạy trên Windows host.

Tham khảo: [ClamAV Docker, volumes và RAM](https://docs.clamav.net/manual/Installing/Docker.html), [INSTREAM/PING protocol](https://docs.clamav.net/manual/Usage/ClamdProtocol.html).

## Kết quả thực thi ngày 23/10/2026

| Kiểm tra | Kết quả |
|---|---|
| Compose config và readiness | Hợp lệ; container healthy trên 127.0.0.1:3310 |
| Scanner thật | ClamAV 1.5.4, signatures version 28161 tại thời điểm kiểm thử |
| Live tests với container đang chạy | 3/3 PASS: clean PDF active/clean, PDF nhúng EICAR rejected/threat_detected, availability clean |
| Tắt container thật | 3/3 PASS: cả PDF sạch/EICAR vẫn processing/failed, HTTP download bị chặn, scanner unavailable |
| Bật container lại và chạy toàn bộ suite | 458/458 PASS, 0 failed, 0 skipped; bao gồm 3 live cases |
| Restore/build | Thành công, 0 warning, 0 error |
| Git whitespace check | PASS |

TRX local (Git ignored): `TestResults/clamav-live.trx`, `TestResults/clamav-offline.trx`, `TestResults/clamav-docker-full.trx`. Tất cả PostgreSQL integration dùng container test tạm và được dọn sau tests. Container ClamAV và signature volume được giữ chạy để backend sử dụng. Không thay đổi JWT/secrets, schema hoặc dữ liệu application; không commit/push.

Phạm vi bằng chứng: đã kiểm tra lifecycle service production + HTTP thật với scanner thật, outage bằng cách stop service thật, rồi kiểm tra lại sau restart. Tests điều khiển thời điểm gọi lifecycle, không chờ polling của Background Worker. Các bài cancellation/retry/worker khác vẫn nằm trong regression suite.
