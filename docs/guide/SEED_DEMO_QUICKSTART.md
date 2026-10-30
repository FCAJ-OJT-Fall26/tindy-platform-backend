# Tindy — Hướng dẫn seed dữ liệu và đăng nhập user demo

Tài liệu dành cho thành viên trong nhóm sử dụng Backend .NET 10, PostgreSQL và EF Core Code First. Dữ liệu được ghi vào database thật để kiểm thử; không phải mock data của Frontend.

## 1. Chuẩn bị

- Pull code Backend mới nhất, bao gồm các file `DevelopmentDataSeeder`, `DevelopmentSeedData` và `DevelopmentSeedOptions`.
- Cài .NET 10 SDK, công cụ `dotnet ef` phù hợp với phiên bản EF Core của repository.
- Khởi động PostgreSQL và cấu hình connection string Development theo hướng dẫn của Backend (User Secrets hoặc cấu hình local).
- Mở PowerShell tại thư mục `tindy-backend-platform`.

GitHub chỉ lưu code seed. Mỗi người dùng database riêng cần chạy seed trên database của mình. Nếu nhóm dùng chung database thì chỉ cần một người chạy.

## 2. Chạy migrations và seed demo

Đoạn lệnh dưới đây không drop/reset database:

```powershell
$env:DevelopmentSeed__Enabled = "true"
$env:DevelopmentSeed__Password = "TindyDemo!2026"

try {
    dotnet ef database update `
        --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
        --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
        --context TindyDbContext `
        -- --environment Development

    if ($LASTEXITCODE -ne 0) {
        throw "Migration/seed thất bại. Kiểm tra log phía trên."
    }
}
finally {
    Remove-Item Env:DevelopmentSeed__Enabled -ErrorAction SilentlyContinue
    Remove-Item Env:DevelopmentSeed__Password -ErrorAction SilentlyContinue
}
```

`DevelopmentSeed` mặc định tắt và chỉ được bật trong môi trường `Development`. EF vẫn chạy pipeline seed khi thông báo `No migrations were applied. The database is already up to date.`; thông báo đó chỉ nói schema không cần migration mới.

Khối `finally` xóa hai biến môi trường trong phiên PowerShell sau khi thực hiện. Dữ liệu đã lưu trong PostgreSQL vẫn còn. Sau đó chạy Backend và Frontend bằng cách thông thường để đăng nhập và kiểm thử.

## 3. Dữ liệu được tạo

- Catalog sau seed: 84 skills, 10 nhóm skills; 35 skills và 3 nhóm được bổ sung so với catalog trước đó.
- 20 tài khoản, có profile, skills, preferred role, availability và hoàn tất Onboarding.
- 40 Experiences (hai cho mỗi người) và 30 Certificates dạng metadata demo; không có file chứng chỉ đính kèm.
- 50 Projects, 150 Positions. Mỗi Project ban đầu có một Leader, capacity 7 và ba Positions, mỗi Position có hai slots.
- Sáu tài khoản đầu dẫn dắt các Projects. Tất cả vẫn mang RBAC Member, không có quyền Admin; Leader có thể Apply vào Project của Leader khác.
- Không tạo sẵn Applications, Saved hoặc Skip để nhóm tự kiểm thử các thao tác.

Các con số trên mô tả bộ seed ban đầu; database có thể còn dữ liệu khác hoặc đã thay đổi qua quá trình test.

## 4. Đăng nhập và danh sách tài khoản

- Username: `demo01` đến `demo20`.
- Email tương ứng: `demo01@tindy.test` đến `demo20@tindy.test`.
- Mật khẩu khi tạo tài khoản mới bằng lệnh trên: **`TindyDemo!2026`**.
- Có thể nhập username hoặc email tại màn hình đăng nhập.
- Đây là mật khẩu mẫu được chia sẻ cho dữ liệu demo Development; không dùng cho tài khoản thật.

| Username | Email | Tên hiển thị | Preferred role | Skills | Giờ/tuần | Projects dẫn dắt | Certificates |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| demo01 | demo01@tindy.test | Nguyễn Minh An (Demo) | Backend Developer | C#, .NET, ASP.NET Core, Entity Framework Core, PostgreSQL, xUnit | 5 | 9 | 1 |
| demo02 | demo02@tindy.test | Trần Thu Hà (Demo) | Frontend Developer | React, TypeScript, Tailwind CSS, Vite, Vitest, Accessibility | 8 | 9 | 2 |
| demo03 | demo03@tindy.test | Lê Hoàng Nam (Demo) | Cloud Engineer | AWS, Docker, Terraform, Linux, GitHub Actions, Prometheus | 10 | 8 | 1 |
| demo04 | demo04@tindy.test | Phạm Ngọc Linh (Demo) | Mobile Developer | Flutter, Dart, REST API, SQLite, Git | 12 | 8 | 2 |
| demo05 | demo05@tindy.test | Võ Gia Huy (Demo) | AI Engineer | Python, Machine Learning, scikit-learn, pandas, NumPy, FastAPI | 15 | 8 | 1 |
| demo06 | demo06@tindy.test | Đặng Bảo Trâm (Demo) | UI/UX Designer | Figma, Wireframing, Prototyping, User Research, Accessibility | 20 | 8 | 2 |
| demo07 | demo07@tindy.test | Bùi Đức Anh (Demo) | Backend Developer | Java, Spring Boot, MySQL, Redis, RabbitMQ, Docker | 25 | 0 | 1 |
| demo08 | demo08@tindy.test | Ngô Khánh Vy (Demo) | Frontend Developer | Vue.js, TypeScript, HTML, CSS, Vite, Cypress | 5 | 0 | 2 |
| demo09 | demo09@tindy.test | Dương Quốc Bảo (Demo) | Full-stack Developer | Node.js, NestJS, React, TypeScript, PostgreSQL, JWT | 8 | 0 | 1 |
| demo10 | demo10@tindy.test | Hồ Phương Nhi (Demo) | QA Engineer | Playwright, Postman, SQL, Test Automation, Test Planning, Agile | 10 | 0 | 2 |
| demo11 | demo11@tindy.test | Đỗ Tuấn Kiệt (Demo) | DevOps Engineer | AWS, Kubernetes, Docker, CI/CD, Grafana, Linux | 12 | 0 | 1 |
| demo12 | demo12@tindy.test | Nguyễn Mai Anh (Demo) | Data Engineer | SQL, Python, pandas, Apache Airflow, dbt, PostgreSQL | 15 | 0 | 2 |
| demo13 | demo13@tindy.test | Trần Hải Đăng (Demo) | Mobile Developer | React Native, TypeScript, REST API, Jest, Git | 20 | 0 | 1 |
| demo14 | demo14@tindy.test | Lê Thanh Thảo (Demo) | Business Analyst | Requirements Analysis, User Research, Agile, Scrum, SQL | 25 | 0 | 2 |
| demo15 | demo15@tindy.test | Phan Nhật Minh (Demo) | Backend Developer | Python, Django, FastAPI, PostgreSQL, Docker, OpenAPI | 5 | 0 | 1 |
| demo16 | demo16@tindy.test | Vũ Ngọc Ánh (Demo) | Frontend Developer | Angular, TypeScript, CSS, Jest, Accessibility | 8 | 0 | 2 |
| demo17 | demo17@tindy.test | Hoàng Việt Long (Demo) | Full-stack Developer | Next.js, React, Node.js, TypeScript, OAuth 2.0, MongoDB | 10 | 0 | 1 |
| demo18 | demo18@tindy.test | Đinh Yến Nhi (Demo) | AI Engineer | Python, PyTorch, TensorFlow, Machine Learning, NumPy, Docker | 12 | 0 | 2 |
| demo19 | demo19@tindy.test | Trương Quang Vinh (Demo) | QA Engineer | C#, xUnit, Postman, OpenAPI, SQL, Test Planning | 15 | 0 | 1 |
| demo20 | demo20@tindy.test | Lý Minh Châu (Demo) | UI/UX Designer | Figma, User Research, Prototyping, HTML, CSS, Scrum | 20 | 0 | 2 |

Mỗi người có hai Experiences mô phỏng tại Tindy Demo Lab: Community Platform và Team Delivery Workshop. Certificates do Tindy Demo Academy giả lập, gồm chứng chỉ nền tảng theo role; các tài khoản số chẵn có thêm Agile Team Delivery Workshop. Đây không phải kinh nghiệm hoặc chứng chỉ thực tế. Experiences/Certificates không cộng điểm Match.

## 5. Gợi ý kiểm thử

- Dùng `demo20` để lướt 50 Projects demo ban đầu vì tài khoản này không dẫn dắt Project nào.
- Dùng `demo01` hoặc `demo02` để xem Applications của Projects mình dẫn dắt và Accept/Reject.
- Dùng hai tài khoản khác nhau để thử Member Apply → Leader xử lý → Member xem kết quả.
- Có thể Apply lại ngay sau Rejected hoặc Withdrawn, kể cả cùng Position. Mỗi Member chỉ có một Application Pending trong cùng Project.
- Leader không thấy Project mình dẫn dắt trong Discovery. Pending Application, membership và Skip có thể làm số card hiển thị giảm theo quy tắc Backend.

## 6. Chạy lại, drop database và lỗi thường gặp

- **Chạy seed lại:** chỉ thêm các root aggregates còn thiếu theo ID ổn định. Không reset mật khẩu/profile đã sửa, không phục hồi mọi child row đã xóa, không xóa Applications và không gia hạn deadline cũ.
- **Từng seed bằng mật khẩu khác:** vẫn đăng nhập bằng mật khẩu lúc tài khoản được tạo; đổi `DevelopmentSeed__Password` rồi chạy lại không reset tài khoản đã tồn tại.
- **Drop database:** dữ liệu trong database đó mất. Chạy lại migrations và lệnh seed trên để tạo lại bộ demo.
- **Không đăng nhập được:** kiểm tra seed thành công, Backend đang trỏ đúng database và mật khẩu lúc tạo tài khoản. Chỉ chạy migrations mà không bật `DevelopmentSeed` sẽ không tạo 20 user demo.
- **Too many login attempts:** chờ hết giới hạn theo thông báo trước khi thử lại; tránh thử nhiều tài khoản liên tục.
- **Projects hết hạn:** chạy lại seed không kéo dài deadline. Deadline ban đầu tính từ lần tạo Project (+7/+14/+30/+45 ngày); kiểm tra trạng thái và thời hạn tuyển dụng khi test Discovery.

Chi tiết cơ chế seed: [DEVELOPMENT_DEMO_SEED.md](DEVELOPMENT_DEMO_SEED.md).

Nguồn dữ liệu: `src/Tindy.Infrastructure/Persistence/Seeding/DevelopmentSeedData.cs` và `DevelopmentDataSeeder.cs`. File hướng dẫn nằm trong `docs/guide` để có thể commit/push cùng Backend cho nhóm sử dụng.
