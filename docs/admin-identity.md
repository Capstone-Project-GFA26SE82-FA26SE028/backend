# Quản lý Admin: UC_ADM_01–06

Đợt này hoàn thiện luồng Identity qua Gateway, giữ nguyên schema. Phải rebuild Identity và Gateway vì contract protobuf được bổ sung RPC/field mới; các field và RPC cũ vẫn được giữ.

## Đăng nhập và phiên

- Endpoint đăng nhập hiện tại vẫn dùng trường JSON email; trường này nhận email hoặc username, không phân biệt hoa/thường và bỏ khoảng trắng hai đầu.
- Nếu dữ liệu cũ có username của một tài khoản trùng email tài khoản khác, đăng nhập bằng chuỗi đó bị từ chối thay vì chọn nhầm tài khoản. Tạo/cập nhật tài khoản mới chặn cả dạng trùng chéo này.
- Login/Logout/Refresh và kiểm tra phiên JWT tiếp tục dùng cơ chế hiện có. Tài khoản khóa không đăng nhập được; khóa tài khoản hoặc đổi vai trò thu hồi các phiên. Đổi sang cùng vai trò không thu hồi phiên.
- Không cho tự khóa tài khoản. Không cho thu hồi quyền của Admin đang hoạt động cuối cùng. Các thay đổi quyền dùng transaction Serializable và audit cùng transaction.

## Tài khoản

- GET /api/users: page, pageSize, search (họ tên/email/username), roleId, isActive. Các điều kiện kết hợp trước phân trang.
- GET /api/users/{id}: xem chi tiết, không trả mật khẩu hoặc token.
- POST /api/users: thông tin tạo như trước, bổ sung avatarUrl tùy chọn (URL HTTP/HTTPS).
- PUT /api/users/{id}: Admin cập nhật hồ sơ nhân viên, gồm userName, email, fullName, phoneNumber, avatarUrl. Trường tùy chọn null/xóa khỏi request sẽ xóa giá trị cũ vì đây là PUT thay thế hồ sơ.
- PATCH /api/users/{id}/role và /status: vẫn là các thao tác riêng; PUT không thay đổi mật khẩu, quyền hay trạng thái.
- Ghi UPDATE_USER vào Audit Log với dữ liệu trước/sau không chứa password/hash/token. Nếu ghi audit thất bại, cập nhật rollback.

Ví dụ PUT:

```json
{
  "userName": "waiter01",
  "email": "waiter01@example.com",
  "fullName": "Nguyễn Văn A",
  "phoneNumber": "0901234567",
  "avatarUrl": null
}
```

## Tra cứu phiên

GET /api/sessions nhận các query:

| Query | Ý nghĩa |
| --- | --- |
| page, pageSize | Phân trang, pageSize tối đa 100 |
| userId | Một tài khoản cụ thể |
| search | Username hoặc email |
| roleId | Vai trò hiện tại của tài khoản, không phải snapshot lúc đăng nhập |
| deviceType | Loại thiết bị đã được lưu, ví dụ Mobile |
| status | active, expired, revoked (chữ thường) |
| fromUtc, toUtc | Khoảng thời gian đăng nhập, bao gồm hai đầu; truyền ISO 8601 có Z/offset |

active = chưa thu hồi, chưa hết hạn, tài khoản active; expired = hết hạn và chưa thu hồi; revoked = đã thu hồi. Mặc định không lọc trạng thái. Bản ghi trả thêm userName, email, roleId, không trả refresh token/hash. GET /api/sessions/me vẫn chỉ xem phiên của chính người gọi.

## Vai trò cố định

- GET /api/roles hỗ trợ search theo mã/tên (tối đa 50 ký tự); GET /api/roles/{id} xem chi tiết.
- Vai trò lấy từ dữ liệu Role đã có trong database. Đợt này không tự seed, xóa hoặc đổi danh mục vai trò hiện có.
- POST, PUT, DELETE role bị ẩn khỏi Swagger. Route và protobuf RPC cũ vẫn tồn tại để tương thích, nhưng trả 409 cho Admin vì danh mục cố định; người không có quyền nhận 403.
- Gán vai trò qua PATCH /api/users/{id}/role, chỉ nhận role tồn tại. Vai trò Admin được xác định bằng UserManagement:AdminRoleIds.
- Tài liệu này thay thế mô tả CRUD vai trò trước đó trong role-management.md.

## Kiểm tra

Test Identity dùng SQLite kiểm tra vai trò cố định, tìm kiếm/bộ lọc, sửa hồ sơ và audit rollback, username login, bảo vệ Admin cuối cùng và thu hồi phiên. Chưa kiểm chứng transaction/concurrency trên SQL Server thật hoặc triển khai Docker. Không đánh đồng đợt này với việc hoàn tất các UC Admin 10–15.
