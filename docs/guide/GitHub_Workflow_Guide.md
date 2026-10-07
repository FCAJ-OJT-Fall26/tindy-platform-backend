# GitHub Workflow Guide cho Team Project

## 1. Mục tiêu

Team sử dụng GitHub theo workflow:

```text
main
  │
  ├── feature/authentication
  ├── feature/user-profile
  ├── feature/project-management
  └── fix/login-validation
```

Quy định chính:

```text
Member:
Pull main
→ Tạo branch local
→ Code
→ Commit
→ Push branch
→ Tạo Pull Request

Leader:
Review
→ Approve
→ Merge vào main
```

---

## 2. Quy định chung

### Thành viên

- Không code trực tiếp trên `main`.
- Không push trực tiếp lên `main`.
- Không tự merge code vào `main`.
- Mỗi chức năng hoặc task phải làm trên branch riêng.
- Sau khi hoàn thành phải tạo Pull Request.
- Leader hoặc reviewer sẽ kiểm tra và merge.

### Leader

Leader có trách nhiệm:

- Review Pull Request.
- Kiểm tra conflict.
- Kiểm tra code có build được hay không.
- Kiểm tra coding convention.
- Yêu cầu sửa nếu cần.
- Merge code vào `main`.

---

## 3. Quy tắc đặt tên branch

### Feature mới

```text
feature/<ten-chuc-nang>
```

Ví dụ:

```text
feature/jwt-authentication
feature/user-profile
feature/project-management
feature/google-oauth
```

### Sửa lỗi

```text
fix/<ten-loi>
```

Ví dụ:

```text
fix/login-validation
fix/expired-token
fix/user-email-duplicate
```

### Refactor code

```text
refactor/<noi-dung>
```

Ví dụ:

```text
refactor/auth-service
refactor/user-repository
```

### Documentation

```text
docs/<noi-dung>
```

Ví dụ:

```text
docs/update-readme
docs/api-documentation
```

---

## 4. Clone project lần đầu

Nếu chưa có project ở máy:

```powershell
git clone <repository-url>
```

Ví dụ:

```powershell
git clone https://github.com/team/tindy-backend-platform.git
```

Sau đó:

```powershell
cd tindy-backend-platform
```

Kiểm tra branch:

```powershell
git branch
```

---

## 5. Workflow khi bắt đầu một task mới

Trước khi làm task mới, phải cập nhật `main` mới nhất:

```powershell
git switch main
git pull origin main
```

Sau đó tạo branch local.

Ví dụ làm chức năng User Profile:

```powershell
git switch -c feature/user-profile
```

Kiểm tra:

```powershell
git branch
```

Kết quả:

```text
* feature/user-profile
  main
```

Dấu `*` cho biết branch hiện tại.

---

## 6. Không cần tạo branch trước trên GitHub

Không cần vào GitHub để tạo branch.

Chỉ cần tạo ở local:

```powershell
git switch -c feature/user-profile
```

Sau đó khi push:

```powershell
git push -u origin feature/user-profile
```

GitHub sẽ tự tạo branch `feature/user-profile` trên remote repository.

---

## 7. Kiểm tra code trước khi commit

Kiểm tra trạng thái:

```powershell
git status
```

Nếu là project .NET, nên build trước:

```powershell
dotnet build
```

Nếu có test:

```powershell
dotnet test
```

Chỉ nên commit khi code build thành công.

---

## 8. Git Add

### Add toàn bộ file thay đổi

```powershell
git add .
```

Kiểm tra lại:

```powershell
git status
```

### Add một file cụ thể

```powershell
git add src/Tindy.Api/Controllers/UserController.cs
```

---

## 9. Quy tắc Commit

Team sử dụng format:

```text
<type>: <description>
```

Ví dụ:

```text
feat: add user profile endpoint
```

---

## 10. Các loại commit

### `feat`

Thêm chức năng mới.

```text
feat: add JWT authentication
feat: add user profile endpoint
feat: add Google OAuth login
```

### `fix`

Sửa lỗi.

```text
fix: handle expired access token
fix: validate duplicate email
fix: handle null user profile
```

### `refactor`

Thay đổi cấu trúc code nhưng không thay đổi chức năng.

```text
refactor: extract authentication service
refactor: move user logic to application layer
```

### `docs`

Thay đổi tài liệu.

```text
docs: update README
docs: add API documentation
```

### `test`

Thêm hoặc chỉnh sửa test.

```text
test: add login service unit tests
test: add user repository tests
```

### `chore`

Các thay đổi liên quan package, dependency, configuration hoặc tooling.

```text
chore: add Entity Framework Core packages
chore: configure Swagger
chore: update NuGet dependencies
```

### `style`

Format code nhưng không thay đổi logic.

```text
style: format authentication controller
```

### `perf`

Tối ưu performance.

```text
perf: optimize user query
```

---

## 11. Commit đúng cách

Ví dụ vừa thêm User Entity:

```powershell
git add .
git commit -m "feat: add user entity"
```

Tiếp tục làm User Service:

```powershell
git add .
git commit -m "feat: add user service"
```

Sau đó thêm API:

```powershell
git add .
git commit -m "feat: add user profile endpoint"
```

---

## 12. Không nên commit như thế này

Không sử dụng:

```text
update
fix
code
code moi
done
final
final2
final-final
test
abc
123
push code
```

Ví dụ sai:

```powershell
git commit -m "update"
```

Thay bằng:

```powershell
git commit -m "feat: add refresh token service"
```

---

## 13. Một commit chỉ nên chứa một thay đổi logic

Không nên làm rất nhiều chức năng rồi commit:

```text
feat: update project
```

Nên chia:

```text
feat: add authentication entity
feat: add password hashing service
feat: add JWT token service
feat: add login endpoint
feat: add refresh token
```

Lợi ích:

- Dễ review.
- Dễ tìm lỗi.
- Dễ rollback.
- Git history rõ ràng.
- Biết ai thay đổi phần nào.

---

## 14. Push branch lần đầu

Sau khi commit:

```powershell
git push -u origin feature/user-profile
```

`-u` sẽ liên kết local branch với remote branch.

Sau lần đầu tiên, các lần sau chỉ cần:

```powershell
git push
```

---

## 15. Workflow push hoàn chỉnh

Ví dụ:

```powershell
git status
git add .
git commit -m "feat: add user profile endpoint"
git push -u origin feature/user-profile
```

Sau lần đầu:

```powershell
git add .
git commit -m "fix: validate user profile input"
git push
```

---

## 16. Sau khi push

Sau khi push branch lên GitHub, thành viên tạo Pull Request:

```text
feature/user-profile
        ↓
       main
```

Pull Request không được merge bởi member.

Leader sẽ review.

---

## 17. Pull Request

Tên Pull Request nên rõ ràng.

Ví dụ:

```text
feat: implement user profile
```

Description:

```text
Changes:
- Add UserProfile entity
- Add UserProfile service
- Add UserProfile repository
- Add UserProfile API endpoints

Testing:
- Create profile
- Get profile
- Update profile
```

---

## 18. Leader review Pull Request

Leader kiểm tra:

```text
Code
 ↓
Architecture
 ↓
Naming
 ↓
Build
 ↓
Conflict
 ↓
Security
 ↓
Approve
 ↓
Merge
```

Nếu code chưa ổn, leader chọn `Request Changes`.

Member sửa code trên branch hiện tại:

```powershell
git add .
git commit -m "fix: update user profile validation"
git push
```

Pull Request sẽ tự cập nhật.

Không cần tạo Pull Request mới.

---

## 19. Sau khi leader merge

Sau khi branch đã được merge vào `main`:

```powershell
git switch main
git pull origin main
```

Xóa branch local đã hoàn thành:

```powershell
git branch -d feature/user-profile
```

Branch trên GitHub có thể cấu hình để tự xóa sau khi merge.

---

## 20. Bắt đầu task tiếp theo

Sau khi update `main`:

```powershell
git switch main
git pull origin main
```

Tạo branch mới:

```powershell
git switch -c feature/project-management
```

Không tiếp tục code task mới trên branch cũ.

---

## 21. Khi branch đang làm bị `main` thay đổi

Ví dụ bạn đang ở:

```text
feature/user-profile
```

Trong lúc đó leader merge code người khác vào `main`.

Bạn nên lấy `main` mới nhất:

```powershell
git switch main
git pull origin main

git switch feature/user-profile
git merge main
```

Nếu không conflict thì tiếp tục bình thường:

```powershell
git push
```

---

## 22. Nếu có conflict

Ví dụ:

```text
CONFLICT (content): Merge conflict in UserService.cs
```

Mở file sẽ thấy:

```text
<<<<<<< HEAD
Code của bạn
=======
Code từ main
>>>>>>> main
```

Chọn code đúng hoặc kết hợp hai bên.

Sau khi xử lý xong:

```powershell
git add .
git commit -m "fix: resolve merge conflict"
git push
```

---

## 23. Kiểm tra branch hiện tại

```powershell
git branch
```

Hoặc:

```powershell
git status
```

Luôn kiểm tra branch trước khi code hoặc commit.

---

## 24. Xem lịch sử commit

```powershell
git log
```

Dạng ngắn:

```powershell
git log --oneline
```

Ví dụ:

```text
73ad821 feat: add user profile endpoint
83412ad feat: add user profile service
992ada1 feat: add user profile entity
```

---

## 25. Xem remote repository

```powershell
git remote -v
```

---

## 26. Pull code

Để cập nhật `main`:

```powershell
git switch main
git pull origin main
```

Không nên pull `main` khi đang có code chưa commit.

Trước tiên kiểm tra:

```powershell
git status
```

Nếu đang có code chưa commit thì nên commit hoặc stash trước.

---

## 27. Quy trình chuẩn mỗi ngày

### Khi bắt đầu làm việc

```powershell
git switch main
git pull origin main
git switch -c feature/<task-name>
```

Ví dụ:

```powershell
git switch -c feature/jwt-authentication
```

### Trong lúc code

```powershell
git status
git add .
git commit -m "feat: add JWT token service"
```

Có thể commit nhiều lần.

### Khi hoàn thành

```powershell
dotnet build
git status
git push -u origin feature/jwt-authentication
```

Sau đó tạo Pull Request.

---

## 28. Workflow tổng thể

```text
                 GitHub
                   │
                  main
                   │
                   │ pull
                   ▼
             Local main
                   │
                   │ git switch -c
                   ▼
       feature/user-profile
                   │
                   │ Code
                   ▼
                Commit
                   │
                   │ git push
                   ▼
        GitHub feature branch
                   │
                   │ Pull Request
                   ▼
                Leader
                   │
             Review / Test
                   │
                   ▼
                 Merge
                   │
                   ▼
                  main
```

---

## 29. Quy định bắt buộc cho member

1. Luôn pull `main` trước khi bắt đầu task.
2. Không code trực tiếp trên `main`.
3. Mỗi task phải tạo branch riêng.
4. Branch được tạo ở local bằng `git switch -c <branch-name>`.
5. Không cần tạo branch trước trên GitHub.
6. Commit phải có ý nghĩa.
7. Không dùng commit kiểu `update`, `fix`, `done`, `final`, `abc`.
8. Push branch riêng lên GitHub.
9. Sau khi push phải tạo Pull Request.
10. Không tự merge vào `main`.
11. Leader hoặc reviewer merge.
12. Sau khi merge, pull `main` mới nhất rồi mới bắt đầu task tiếp theo.

---

## 30. Quy định commit của team

Format:

```text
type: description
```

Ví dụ chuẩn:

```text
feat: add JWT authentication
feat: add user profile endpoint

fix: handle expired token
fix: validate duplicate email

refactor: extract authentication service

docs: update README

test: add authentication tests

chore: configure Swagger
```

---

## 31. Cheat Sheet

### Clone

```powershell
git clone <repository-url>
```

### Pull main

```powershell
git switch main
git pull origin main
```

### Tạo branch

```powershell
git switch -c feature/user-profile
```

### Kiểm tra

```powershell
git status
```

### Add

```powershell
git add .
```

### Commit

```powershell
git commit -m "feat: add user profile"
```

### Push lần đầu

```powershell
git push -u origin feature/user-profile
```

### Push những lần sau

```powershell
git push
```

### Xem branch

```powershell
git branch
```

### Đổi branch

```powershell
git switch main
```

Hoặc:

```powershell
git switch feature/user-profile
```

### Xóa branch local

```powershell
git branch -d feature/user-profile
```

### Xem lịch sử commit

```powershell
git log --oneline
```

---

## 32. Workflow ngắn gọn nhất

Member chỉ cần nhớ:

```text
Pull
↓
Branch
↓
Code
↓
Commit
↓
Push
↓
Pull Request
```

Command:

```powershell
git switch main
git pull origin main

git switch -c feature/user-profile

# Code...

git add .
git commit -m "feat: add user profile"

git push -u origin feature/user-profile
```

Sau đó:

```text
Member tạo Pull Request
        ↓
Leader Review
        ↓
Leader Merge
        ↓
main
```

Đây là workflow mặc định của team.
