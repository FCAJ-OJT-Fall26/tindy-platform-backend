# Phát hành backend có migration trên AWS

## Chuẩn bị bản phát hành

1. Merge bản backend đã qua CI vào `main`. Kiểm tra migration mới trên PostgreSQL và rà các thay đổi với image API đang chạy. API cũ phải chạy được với schema mới trong lúc ECS chuyển phiên bản và nếu ECS rollback. Với thay đổi không tương thích, chia thành các bản phát hành tương thích rồi thực hiện từng bản.
2. Trong GitHub Actions của backend, chạy **Deploy backend** trên `main` với mode `prepare-schema` và full SHA của bản phát hành. Ghi lại **Release commit**, **Migration tree** và **ECR image** từ Summary. Dùng URI có digest cho cả task migration và bản phát hành.
3. Chọn thời điểm thực hiện và phối hợp để tránh deploy, migration hoặc teardown đồng thời. Ghi lại revision ECS và image URI theo digest của API đang chạy.

## Tạo snapshot rồi chạy migration

1. Trong AWS Console tại `us-east-1`, mở **Aurora and RDS → Databases → tindy-database-postgres → Actions → Take snapshot**. Tạo manual snapshot mới cho đợt này, ví dụ `tindy-before-schema-<short-sha>-<yyyymmddhhmm>`. Mở **Snapshots → Manual** và chờ `Available`. Ghi lại snapshot ID hoặc ARN, thời điểm tạo và xác nhận snapshot được mã hóa, thuộc đúng DB instance hiện tại. Hoàn tất bước này trước khi khởi chạy task migration.
2. Trong **ECS → Task definitions**, lấy revision theo output `MigrationTaskDefinitionArn` của stack `tindy-runtime`. Tạo revision mới từ revision đó và chỉ thay **Image URI** của container `migration` bằng ECR image lấy từ Summary. Kiểm tra command vẫn là `migrate-db` và secret kết nối dành cho migration trỏ đến đúng RDS hiện tại. Kiểm tra thông tin kết nối trong Console mà không ghi hoặc chia sẻ mật khẩu.
3. Trong **ECS → Clusters → tindy-runtime → Tasks → Run new task**, chọn revision vừa tạo, **Fargate** và **1 task**. Dùng VPC, `PublicTaskSubnetIds` và `MigrationSecurityGroupId` từ outputs của `tindy-network`. Bật public IP như task migration của STEP 09. Kiểm tra cấu hình rồi chạy task.
4. Chờ task `Stopped`. Trong **Containers → migration → Details**, xác nhận **Exit code** bằng `0` và **Image URI** đúng digest. Trong **Logs**, kiểm tra `Database migration complete` và migration mới nhất đúng với bản phát hành. Ghi lại task ARN, log hoàn tất và migration ID. Nếu bất kỳ kiểm tra nào thất bại, dừng quy trình và kiểm tra schema trước khi thử lại.
5. Kiểm tra API cũ vẫn trả `200` tại `/api/health/ready` và chạy smoke test các chức năng chạm vào schema mới.

## Cập nhật baseline và deploy

1. Mở PR cập nhật hai giá trị trong `scripts/deploy-backend.sh`:
   - `known_migrations` lấy đúng **Migration tree** của bản phát hành.
   - `baseline_fallback_image` lấy image URI theo digest của revision API cũ đã kiểm tra tương thích với schema mới. Gate dùng giá trị này để cho phép chuyển từ API hiện tại sang bản phát hành mới.
2. Đính kèm snapshot ID, thời điểm snapshot, release SHA, ECR digest, task ARN và migration ID vào PR để người duyệt đối chiếu. Merge sau khi kiểm tra bằng chứng và CI.
3. Chạy **Deploy backend** với mode `deploy`, điền chính xác release SHA đã dùng trong `prepare-schema`. Kiểm tra Summary ghi nhận đúng digest và revision ECS mới, rồi smoke test trên CloudFront.
4. Nếu rollout thất bại, kiểm tra workflow đã xác minh revision ECS trước đó. Việc rollback ECS chỉ khôi phục phiên bản API. Nếu cần khôi phục dữ liệu hoặc schema, xử lý riêng theo quy trình restore snapshot.
