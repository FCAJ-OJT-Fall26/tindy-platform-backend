# Tindy Backend — Migration Naming Convention

> **Áp dụng cho:** `tindy-backend-platform` — .NET 10, Entity Framework Core (Code First), PostgreSQL.  
> **Mục tiêu:** Cả team đặt tên Migration nhất quán, dễ review và giảm xung đột khi làm việc trên nhiều Git branch.

## 1. Quy tắc chính

**Tên Migration do developer nhập:**

```text
<Feature>_<Action><Target>
```

**Ví dụ:**

```text
Auth_CreateCredentialTables
Auth_AddExternalAuthAccounts
User_MakeUsernameNullable
Profile_CreateUserSkills
Role_AddRolePermissions
Chat_CreateMessageTables
```

**Không đánh số thứ tự thủ công** (`001_`, `002_`, …). EF Core tự thêm timestamp vào tên file:

```text
20261012172030_Auth_CreateCredentialTables.cs
20261012172030_Auth_CreateCredentialTables.Designer.cs
TindyDbContextModelSnapshot.cs
```

Timestamp thể hiện thời điểm tạo Migration; phần sau thể hiện ý nghĩa thay đổi. **Không tự sửa timestamp, tên file hay Migration ID sau khi đã tạo.**

## 2. Quy ước đặt tên

| Thành phần | Quy tắc | Ví dụ |
|---|---|---|
| `Feature` | Tên module, viết PascalCase | `Auth`, `User`, `Profile`, `Role`, `Chat` |
| `_` | Dấu phân tách feature và nội dung | `Auth_...` |
| `Action` | Động từ mô tả thay đổi schema | `Create`, `Add`, `Remove`, `Rename`, `Update`, `Make` |
| `Target` | Entity, bảng, cột hoặc nhóm thay đổi | `CredentialTables`, `UsernameNullable`, `UserSkills` |

**Nguyên tắc:**

1. Dùng **tiếng Anh**, ngắn gọn, thể hiện đúng thay đổi trong database.
2. Dùng **PascalCase** cho từng thành phần; chỉ dùng một dấu `_` để tách `Feature` và phần mô tả (trừ khi có lý do đặc biệt).
3. Mỗi Migration nên gắn với **một thay đổi schema có liên quan về mặt nghiệp vụ**; không cần một Migration cho từng cột nhỏ nếu chúng thuộc cùng một tính năng.
4. Tên phải nói rõ **đã thay đổi gì**, không chỉ nói “cập nhật database”.
5. Nếu một Migration thực sự sửa nhiều module, hãy ưu tiên tách thành các Migration hợp lý; nếu không thể, dùng tên rõ ràng như `Database_AddInitialAuthAndProfileSchema`.

### Ví dụ tên nên dùng

| Tình huống | Tên Migration |
|---|---|
| Tạo các bảng credential | `Auth_CreateCredentialTables` |
| Thêm liên kết Google/GitHub | `Auth_AddExternalAuthAccounts` |
| Cho phép `Username` null | `User_MakeUsernameNullable` |
| Thêm cột `Status` cho user | `User_AddStatusColumn` |
| Tạo quan hệ user–skill | `Profile_CreateUserSkills` |
| Thêm bảng phân quyền | `Role_CreatePermissionTables` |
| Tạo các bảng chat | `Chat_CreateConversationAndMessageTables` |
| Đổi tên một cột | `User_RenameDisplayNameColumn` |

### Ví dụ tên không nên dùng

```text
001_UpdateDatabase              # Số thứ tự tự quản lý, không mô tả rõ thay đổi
Migration2                      # Không thể hiện mục đích
FixDb                           # Quá chung chung
UpdateSomething                 # Không rõ feature / target
Auth_Update                     # Không mô tả thay đổi
20261012172030_Auth_Create...   # Không tự nhập timestamp vào tên migration
```

## 3. Cách tạo Migration

Chạy tại **thư mục gốc repository** `tindy-backend-platform` (PowerShell):

```powershell
dotnet ef migrations add Auth_CreateCredentialTables `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Để áp dụng Migration vào database đang cấu hình:

```powershell
dotnet ef database update `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

Để xem lịch sử Migration theo thứ tự EF Core nhận diện:

```powershell
dotnet ef migrations list `
    --project .\src\Tindy.Infrastructure\Tindy.Infrastructure.csproj `
    --startup-project .\src\Tindy.Api\Tindy.Api.csproj `
    --context TindyDbContext `
    -- --environment Development
```

> **Lưu ý:** `database update` sử dụng connection string của môi trường đang chạy. Trước khi thực thi, đảm bảo bạn đang trỏ đến **database được phép thay đổi**, không phải database production hoặc database dùng chung ngoài dự kiến.

## 4. Cấu trúc file và cách commit

Ví dụ thư mục được EF Core sinh ra:

```text
src/
└── Tindy.Infrastructure/
    └── Persistence/
        └── Migrations/
            ├── 20261012172030_Auth_CreateCredentialTables.cs
            ├── 20261012172030_Auth_CreateCredentialTables.Designer.cs
            └── TindyDbContextModelSnapshot.cs
```

Khi tạo Migration, cần commit **đầy đủ các file do EF Core sinh ra** và những thay đổi model/configuration liên quan; không chỉ commit file `.cs` chính.

Ví dụ Git:

```text
Branch: feature/auth-credential-tables
Migration: Auth_CreateCredentialTables
Commit: feat(db): create authentication credential tables
```

## 5. Tránh xung đột khi nhiều người cùng làm Migration

**Tên Migration khác nhau không có nghĩa là không conflict.** Nhiều Migration cùng dùng chung file `TindyDbContextModelSnapshot.cs`. Ngoài xung đột Git, các Migration tạo song song từ cùng một schema ban đầu cũng có thể gây sai lệch trạng thái khi chạy lần lượt.

### Workflow khuyến nghị cho team Tindy

1. **Trước khi làm:** cập nhật `main`; thống nhất với leader ai đang phụ trách tạo/merge Migration.
2. **Trong branch riêng:** cập nhật Entity và Fluent API Configuration, sau đó tạo Migration với tên đúng quy ước.
3. **Trước khi tạo PR:** kiểm tra `Up()`, `Down()`, file Designer và `TindyDbContextModelSnapshot.cs`; chạy build và thử Migration trên **database phát triển riêng**.
4. **Khi merge:** leader ưu tiên merge **từng PR có Migration một**. Nếu `main` đã có Migration mới, thành viên còn lại phải đồng bộ và kiểm tra lại Migration của mình với schema mới.
5. **Nếu bị conflict Snapshot:** không chọn nguyên file `ours` hoặc `theirs` một cách máy móc. Cần đối chiếu model hiện tại, lịch sử Migration và trạng thái database; khi cần thì tạo lại Migration **chưa được chia sẻ/chưa áp dụng** trên nền `main` mới.
6. **Sau merge:** cả team pull `main`, kiểm tra connection string rồi chạy `dotnet ef database update` vào database dev tương ứng.

### Các điều cần tránh

- Không để hai người tự tạo Migration trên các branch cũ rồi merge liên tiếp mà không kiểm tra Snapshot và thứ tự Migration.
- Không chỉ đổi số/tên file để “sửa” conflict.
- Không tự xóa hoặc sửa Migration **đã merge và đã áp dụng** trên môi trường được chia sẻ; tạo Migration mới để điều chỉnh schema.
- Không commit `TindyDbContextModelSnapshot.cs` bị mất thay đổi của thành viên khác.
- Không tự chạy migration/reset trên database chung nếu team chưa thống nhất.

## 6. Checklist trước khi tạo Pull Request

- [ ] Tên Migration đúng `<Feature>_<Action><Target>`.
- [ ] Không có `001_`, `002_`, timestamp nhập tay hoặc tên chung chung.
- [ ] Đã cập nhật từ `main` và kiểm tra có Migration mới nào vừa được merge không.
- [ ] `Up()` đúng thay đổi mong muốn; `Down()` có hành vi rollback phù hợp (lưu ý rollback đôi khi mất dữ liệu).
- [ ] File Migration, Designer và `TindyDbContextModelSnapshot.cs` nhất quán.
- [ ] `dotnet build` thành công.
- [ ] Đã thử áp dụng trên database dev phù hợp.
- [ ] PR mô tả những thay đổi schema và tác động đến dữ liệu hiện có.

---

## Kết luận

**Chuẩn chung cho Tindy:**

```text
<Feature>_<Action><Target>
```

**Ví dụ chuẩn:** `Auth_AddExternalAuthAccounts`.

**Không đánh số thứ tự thủ công** vì EF Core đã có timestamp. Muốn hạn chế conflict thực sự, team cần **điều phối thứ tự merge Migration, kiểm tra ModelSnapshot và thử trên database dev**.
