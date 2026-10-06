# AI-Powered Smart Restaurant Platform

Backend theo hướng microservice; phạm vi hiện tại là Identity Service, ASP.NET Core Web API controller, **.NET 9 / EF Core 9 / SQL Server / Database First**. Dockerfile dùng image .NET 9 và Linux containers.

## Database First

Nguồn schema là [`database/identity.sql`](database/identity.sql); mapping đầy đủ tại [`docs/identity-schema-proposal.md`](docs/identity-schema-proposal.md). Script chỉ tạo bốn bảng `dbo.Roles`, `dbo.Users`, `dbo.UserSessions`, `dbo.AuditLogs`. Không seed role/tài khoản, không có bảng Identity framework hoặc migrations. Nếu bảng Identity đã tồn tại, script báo lỗi và không thay thế bảng/dữ liệu.

### Chạy bằng SQL Server Management Studio

1. Kết nối SQL Server bằng tài khoản có quyền tạo database/bảng; mở `database/identity.sql`.
2. Bật **Query → SQLCMD Mode**. Thêm dòng sau lên đầu cửa sổ query, đổi tên DB nếu cần:

   ```sql
   :setvar IdentityDatabase "Identity"
   ```

3. Kiểm tra tên DB rồi Execute. Database chưa tồn tại sẽ được tạo; database rỗng đã tồn tại có thể nhận bốn bảng này. Nếu đã có schema ứng dụng, đối chiếu DDL trước; không xóa bảng hoặc chạy lại để thay thế dữ liệu.
4. Kiểm tra bốn bảng, khóa ngoại NO ACTION, unique index trên UserName/RoleName. UserName không phân biệt hoa thường; RoleName dùng collation BIN2 và ứng dụng đối chiếu tên chính xác.

Có thể dùng `sqlcmd` thay cho SSMS, với Windows authentication:

```powershell
sqlcmd -S '<SQL_SERVER>' -E -b -i database/identity.sql -v IdentityDatabase=Identity
```

Script không cần mật khẩu được ghi vào file. Dùng quyền triển khai schema riêng với tài khoản ứng dụng khi cấu hình môi trường thực tế.

### Scaffold / scaffold lại

Các file `IdentityService.API/Models/*.cs` và `Data/IdentityDbContext.cs` đã được sinh thật bằng EF Core **9.0.14** từ DDL trên SQL Server 2022 test riêng. Không có OnConfiguring hoặc connection string trong file sinh. Nghiệp vụ, DTO và validation nằm ngoài các file đó.

Cài SDK .NET 9.0.3xx. Cấu hình `ConnectionStrings:Identity` bằng User Secrets hoặc `ConnectionStrings__Identity` trong môi trường hiện tại, rồi chạy từ root:

```powershell
dotnet tool restore
dotnet ef dbcontext scaffold Name=ConnectionStrings:Identity Microsoft.EntityFrameworkCore.SqlServer --project IdentityService.API --context IdentityDbContext --context-dir Data --output-dir Models --table dbo.Roles --table dbo.Users --table dbo.UserSessions --table dbo.AuditLogs --no-onconfiguring --force
```

Hoặc `./database/scaffold.ps1 -Force`. `--force` ghi đè các file được sinh: xem diff trước khi nhận thay đổi. Khi schema cần thay đổi, duyệt SQL thay đổi trước, chạy SQL có kiểm soát rồi scaffold lại. Dùng partial class/method ở file riêng khi cần mở rộng. Không dùng `dotnet ef database update`, `Database.Migrate` hoặc `EnsureCreated`, kể cả trong test.

## Cấu hình và chạy API

`appsettings.json` chỉ có issuer/audience/thời hạn và logging; **không chứa key hoặc connection string**. UserSecretsId đã có trong API project. Trong Visual Studio dùng **Manage User Secrets** và lưu cấu hình ngoài repository:

```json
{
  "ConnectionStrings": {
    "Identity": "Server=<server>;Database=Identity;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
  },
  "Jwt": {
    "Key": "<random-secret-at-least-32-bytes>"
  }
}
```

Nếu dùng SQL authentication, lưu thông tin xác thực trong secrets hoặc biến môi trường, không commit. Các tên biến: `ConnectionStrings__Identity`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__LifetimeMinutes`. Key cần ngẫu nhiên mạnh, tối thiểu 32 byte; mặc định issuer `IdentityService`, audience `RestaurantStaff`, thời hạn 60 phút. Ứng dụng kiểm tra cấu hình lúc khởi động, không tự tạo schema hoặc tài khoản.

```powershell
dotnet restore AIRestaurantPlatform.Backend.slnx
dotnet build AIRestaurantPlatform.Backend.slnx -c Release --no-restore
dotnet dev-certs https --trust
dotnet run --project IdentityService.API --launch-profile https
```

API phát triển: `https://localhost:7032`. Khi chạy Docker, cấp secrets qua môi trường và cấu hình HTTPS/certificate hoặc HTTPS reverse proxy. Không bật logging request body, Authorization header, EF sensitive data logging hoặc IdentityModel PII.

### Test trực tiếp bằng Swagger UI (chỉ Development)

Chọn profile **https** rồi F5 trong Visual Studio: trình duyệt mở [https://localhost:7032/swagger](https://localhost:7032/swagger). Có thể dùng lệnh `dotnet run` với profile https phía trên và mở URL này thủ công. Tài liệu JSON: `https://localhost:7032/swagger/v1/swagger.json`. Swagger UI/JSON không được bật trong Production hoặc Staging.

1. Mở **POST /api/auth/login → Try it out**. Nhập UserName/password của tài khoản đã có, rồi **Execute**. Login không yêu cầu token.
2. Copy giá trị **accessToken** trong response, bấm **Authorize** và chỉ dán chuỗi token. **Không thêm chữ Bearer**: security scheme HTTP Bearer/JWT tự gửi `Authorization: Bearer <token>`.
3. Dùng **GET /api/auth/me → Try it out → Execute** để xem user hiện tại. **POST /api/admin/users** chỉ được backend cho phép khi role hiện tại là **System Administrator**; Swagger mô tả quyền này nhưng không thay thế kiểm tra quyền.
4. Gọi **POST /api/auth/logout → Execute**, kết quả thành công là 204. Dùng lại token đó gọi **GET /api/auth/me** phải nhận **401** vì session đã kết thúc. Đăng nhập lại và Authorize bằng token mới nếu cần tiếp tục.

Swagger dùng đúng request/response DTO của API, không có PasswordHash trong response. Token không được persist vào browser storage; đóng/reload trang sẽ mất Authorize. Không lưu mật khẩu, hash hoặc token thật vào file ví dụ, Git, screenshots hoặc logs. Các quy tắc JWT/User/Role/UserSession vẫn được kiểm tra ở backend như khi gọi API bằng PowerShell.

## Role và Admin do chủ DB tạo thủ công

Chỉ những tên sau được đăng nhập; quyền không dựa vào thứ tự RoleId:

| RoleName chính xác | Được tạo tài khoản nhân sự |
|---|---|
| System Administrator | Có; chỉ tạo năm role phía dưới |
| Restaurant Manager | Không |
| Waiter | Không |
| Cashier | Không |
| Kitchen Staff | Không |
| Inventory Staff | Không |

Sau khi tạo schema, bạn tự thêm các role trên trong SSMS. Ví dụ thao tác thủ công cho database mới:

```sql
USE [Identity];
INSERT dbo.Roles (RoleName, Description) VALUES
 (N'System Administrator', NULL),
 (N'Restaurant Manager', NULL),
 (N'Waiter', NULL),
 (N'Cashier', NULL),
 (N'Kitchen Staff', NULL),
 (N'Inventory Staff', NULL);
```

Không chạy đoạn insert role này trên database đã có role mà chưa đối chiếu. Không cần thêm Customer; Customer quét QR không đăng nhập nhân sự.

Tạo hash bằng tiện ích không có dependency DB, không tạo tài khoản:

```powershell
dotnet run --project tools/Identity.PasswordHash -c Release
```

Nhập mật khẩu ở prompt ẩn. Tiện ích dùng PasswordHasher IdentityV3, 100,000 iterations, giống ứng dụng. Hash in ra stdout để bạn tự chèn; không đưa hash, mật khẩu hoặc JWT vào Git/log/CI artifacts. Không truyền mật khẩu trên command line. Tài khoản mới qua API cần mật khẩu 12–256 ký tự; Admin thủ công nên dùng cùng giới hạn để tương thích validation đăng nhập.

Trong SSMS, tự thay placeholder hash và thông tin của bạn, rồi chạy đoạn sau (không lưu bản có hash vào repository):

```sql
USE [Identity];
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @AdminRoleId int;
SELECT @AdminRoleId = RoleId FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK)
WHERE RoleName = N'System Administrator';
IF @AdminRoleId IS NULL
    THROW 51001, 'Create the System Administrator role manually first.', 1;
IF EXISTS (SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE RoleId = @AdminRoleId)
    THROW 51002, 'An Admin already exists. No additional Admin may be created.', 1;
DECLARE @PasswordHash nvarchar(512) = N'PASTE_HASH_FROM_UTILITY';
IF @PasswordHash = N'PASTE_HASH_FROM_UTILITY'
    THROW 51003, 'Replace the hash placeholder before executing.', 1;
INSERT dbo.Users (RoleId, UserName, Email, PhoneNumber, FullName, AvatarUrl,
                   PasswordHash, IsActive, CreatedAt, UpdatedAt)
VALUES (@AdminRoleId, N'admin', NULL, NULL, N'System Administrator', NULL,
        @PasswordHash, 1, SYSUTCDATETIME(), NULL);
COMMIT;
```

PK dùng IDENTITY, không tự chèn RoleId/UserId. Chủ DB chịu trách nhiệm giữ duy nhất một Admin; ứng dụng không seed tài khoản và API luôn từ chối tạo thêm Admin/Customer.

## API trong phạm vi

Ví dụ request nằm trong [`IdentityService.API/IdentityService.API.http`](IdentityService.API/IdentityService.API.http). Đặt biến request bằng môi trường của HTTP client; không ghi mật khẩu/token thật vào file.

| Endpoint | Quyền | Kết quả chính |
|---|---|---|
| POST /api/auth/login | Anonymous | UserName/password → 200 JWT, expiresAt, user; 401 nếu bị từ chối |
| POST /api/auth/logout | Phiên hợp lệ | 204, đặt ExpiresAt về hiện tại; token phiên đó bị từ chối |
| GET /api/auth/me | Phiên hợp lệ | 200 thông tin user an toàn |
| POST /api/admin/users | Admin hiện tại | 201 tạo Manager/nhân sự; 400 role/validation sai; 409 UserName trùng |

Request tạo tài khoản chứa UserName, Password, RoleId, FullName và Email/PhoneNumber/AvatarUrl tùy chọn. Tài khoản được tạo hoạt động ngay. UserName gồm chữ, số, `_`, `-`, `.`, tối đa 100 ký tự, không có whitespace. JWT chứa `sub` (UserId), `sid` (UserSessionId) và role; chữ ký HS256, issuer, audience và thời hạn được kiểm tra với clock skew bằng 0.

Mỗi request được bảo vệ đọc lại User, Role và UserSession từ SQL Server: tài khoản phải còn hoạt động, session đúng user và chưa hết hạn, role hiện tại được phép. Quyền Admin lấy từ DB hiện tại, không tin role cũ trong JWT. Riêng Waiter, đăng nhập mới kết thúc phiên cũ trong transaction giữ UPDLOCK trên dòng User; hai API instance đăng nhập đồng thời vẫn chỉ giữ một phiên hợp lệ. Các role khác giữ nhiều phiên.

Login/logout/tạo user thành công ghi AuditLog cùng transaction. Thất bại của user đã xác định ghi LoginFailed không chứa credentials; user chưa xác định chỉ ghi application event chung, không tạo user hoặc actor giả. DeviceType để null; UserAgent/IP được giới hạn theo cột, IP lấy từ kết nối trực tiếp. Không có register/refresh/quên mật khẩu/CRUD đầy đủ, QR, bàn ăn, ca Cashier, đặt món hoặc frontend.

## Test local trên SQL Server riêng

Không có test bắt buộc nào bị bỏ qua khi thiếu DB: test sẽ fail. `IDENTITY_TEST_CONNECTION` bắt buộc chỉ tới SQL Server test, tên database dạng `IdentityTests_*`. Fixture tạo một database mới có hậu tố GUID, chạy **chính DDL Database First**, thêm dữ liệu test trong DB đó và chỉ xóa database do fixture vừa tạo khi kết thúc. Tài khoản test SQL cần quyền tạo/xóa DB test. Không dùng credentials/database ứng dụng.

Khởi động một SQL Server test riêng nếu chưa có, bằng Docker Linux:

```powershell
# TEST_SQL_PASSWORD được cấp từ môi trường ngoài repo.
docker run --detach --name identity-test-sql --publish 14339:1433 --env ACCEPT_EULA=Y --env "MSSQL_SA_PASSWORD=$env:TEST_SQL_PASSWORD" mcr.microsoft.com/mssql/server:2022-latest
```

Chờ SQL Server sẵn sàng. Cấp `IDENTITY_TEST_CONNECTION` qua môi trường, ví dụ định dạng `Server=localhost,14339;Database=IdentityTests_local;User Id=sa;Password=<test-secret>;Encrypt=True;TrustServerCertificate=True`. Database tên này là dấu hiệu kiểm tra cấu hình; fixture sẽ dùng DB mới riêng của nó. Chỉ dùng TrustServerCertificate cho SQL Server test/development phù hợp.

```powershell
dotnet restore AIRestaurantPlatform.Backend.slnx
dotnet build AIRestaurantPlatform.Backend.slnx -c Release --no-restore
dotnet test IdentityService.Tests/IdentityService.Tests.csproj -c Release --no-build --logger "trx;LogFileName=identity.trx" --results-directory artifacts/TestResults
python -m unittest discover -s ci -p '*_tests.py' -v
python ci/test-report.py --verify
```

SQL Server integration tests kiểm tra sáu role, đăng nhập sai/unknown/inactive, Customer/role lạ, anonymous, logout, vô hiệu hóa user, phiên Waiter tuần tự và đồng thời qua hai host riêng, nhiều phiên của role khác, quyền tạo tài khoản, role bị cấm, username duy nhất không phân biệt hoa thường, JWT sai signature/issuer/audience/expiry/algorithm, session sai user/hết hạn, quyền role hiện tại, validation và response/audit an toàn. Các test Python xác minh Summary với thiếu test, skipped, test fail, thiếu theory case và restore fail.

## GitHub Actions

[`identity-ci.yml`](.github/workflows/identity-ci.yml) chạy khi push, pull_request hoặc workflow_dispatch. Workflow cài SDK .NET 9 bằng installer chính thức, lưu cả setup log để Summary có lỗi thực tế; tạo SQL Server test container và DB bằng DDL, restore, build Release rồi chạy project test thực tế cùng test báo cáo Python.

Shell dùng `set -euo pipefail`; không dùng continue-on-error. `ci/required-tests.txt` kiểm tra số case tối thiểu của từng test nghiệp vụ; thiếu, fail hoặc skipped đều không đạt. Summary chỉ PASS khi các bước bắt buộc đều thành công và đủ test. Lỗi trước test ghi “Tests chưa chạy”; lỗi test ghi tên, message và đoạn stack trace, kèm total/pass/fail/skipped. Không suy đoán nguyên nhân ngoài log.

Mở **Actions → Identity CI → lần chạy → Summary**. Tải **identity-ci-results** ở **Artifacts** để xem setup/database/restore/build/test logs và TRX. Summary/upload chạy với `always()`; credentials SQL test được sinh mới và masked, không in credentials vào log.

Remote GitHub: [phucgg/AIRestaurantPlatform.Backend](https://github.com/phucgg/AIRestaurantPlatform.Backend). Xem [Identity CI](https://github.com/phucgg/AIRestaurantPlatform.Backend/actions/workflows/identity-ci.yml) để kiểm tra lần chạy và commit tương ứng. Chỉ công nhận CI PASS khi lần chạy thật hoàn tất thành công; kết quả local được bàn giao riêng, không thay cho kết quả GitHub Actions.
