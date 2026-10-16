# Quản lý user

Luồng: Gateway UserController → UserGrpcService → UserService → UserRepository.

Mở Swagger Gateway, đăng nhập và dán accessToken vào Authorize (không thêm Bearer).

| Endpoint | Quyền |
| --- | --- |
| GET /api/users/me | Phiên đăng nhập hợp lệ |
| PUT /api/users/me | Phiên đăng nhập hợp lệ |
| GET /api/users?page=1&pageSize=20&search= | RoleId admin |
| GET /api/users/{id} | RoleId admin |
| PATCH /api/users/{id}/status | RoleId admin |

## Cập nhật hồ sơ

PUT /api/users/me:

```json
{
  "fullName": "Nguyen Van A",
  "phoneNumber": "0901234567",
  "avatarUrl": "https://example.com/avatar.png"
}
```

PUT thay thế các trường hồ sơ: fullName bắt buộc, phoneNumber/avatarUrl bị xóa nếu bỏ trống hoặc gửi null. Không thay đổi email, role, trạng thái hay mật khẩu. UserId lấy từ token, không từ body. Avatar phải là URL HTTP/HTTPS; API không tải nội dung URL này.

## Quản trị

Identity.API/appsettings.json cấu hình UserManagement:AdminRoleIds = ["admin"]. So khớp RoleId chính xác, phân biệt chữ hoa/thường. Nếu danh sách trống, tất cả thao tác Admin bị từ chối. Identity kiểm tra role hiện tại từ database, không chỉ dựa vào Swagger.

PATCH /api/users/{id}/status nhận { "isActive": false } để khóa và true để mở khóa. isActive bắt buộc. Không cho tự khóa. Khóa user và thu hồi phiên được lưu trong cùng transaction. Mở khóa không khôi phục phiên đã bị thu hồi; user phải đăng nhập lại.

Danh sách có page >= 1, pageSize từ 1 đến 100, tìm theo email/username/fullName. Response không chứa password hash hoặc session secrets.

RequireBearerTokenAttribute và BearerSecurityFilter chỉ cung cấp metadata cho Swagger. Xác thực thực tế ở Identity qua GrpcAuthentication và JWT validator (bao gồm kiểm tra phiên).

## Kiểm tra

```powershell
dotnet build Backend.sln -c Release
dotnet test Tests/Identity.Tests/Identity.Tests.csproj -c Release
```

Tests dùng SQLite in-memory, không ghi vào database thật. Sau khi cập nhật code cần khởi động lại Gateway và Identity. Kiểm thử với tài khoản thật: login → GET me → PUT me → GET me; user thường gọi danh sách phải nhận 403; admin khóa user thì token cũ bị từ chối.

## Admin tạo tài khoản và gán role

Không có đăng ký công khai. Dùng access token của Admin (RoleId admin) trong Authorize.

1. GET /api/roles: lấy roleId có thật trong database.
2. POST /api/users: tạo nhân viên; tài khoản mới ở trạng thái hoạt động.

```json
{
  "userName": "nhanvien01",
  "email": "nhanvien01@example.com",
  "password": "EmployeePass123!",
  "fullName": "Nguyen Van A",
  "phoneNumber": "0901234567",
  "roleId": "ROLE_ID_LAY_TU_API_ROLES"
}
```

Mật khẩu ít nhất 8 ký tự, tối đa 72 byte UTF-8; chỉ lưu BCrypt hash. Không trả mật khẩu/hash trong response. Email và username trùng (không phân biệt hoa thường) trả 409. Role không tồn tại trả 400. Tạo thành công trả 201 kèm Location.

PATCH /api/users/{id}/role với { "roleId": "ROLE_ID_MOI" } để đổi role. Đổi role thực sự thu hồi mọi phiên hiện có; gán lại role cũ không thu hồi phiên. Không thể hạ quyền Admin hoạt động cuối cùng (409). Chặn tự khóa vẫn áp dụng. Nếu tự đổi role khi có Admin khác, phiên của chính mình cũng bị thu hồi.

Các thao tác tạo và thay đổi quyền dùng transaction Serializable để bảo vệ kiểm tra trùng và Admin cuối cùng. Không có thay đổi schema database trong bản này. GET /api/roles chỉ đọc các role sẵn có, không tạo role mới.

## Tổ chức code

UserService sử dụng IUnitOfWork.UserRepo. Lưu hồ sơ qua UnitOfWork.SaveAsync; tạo tài khoản và thay đổi quyền qua ExecuteInTransactionAsync (Serializable). Repository chứa truy vấn/thao tác dữ liệu, UnitOfWork chịu trách nhiệm lưu và commit/rollback transaction. Gateway dùng DTO riêng ở DTOs/Identity và ResponseDTO ở DTOs; AuthController và UserController nằm trong Controllers/IdentityController.
