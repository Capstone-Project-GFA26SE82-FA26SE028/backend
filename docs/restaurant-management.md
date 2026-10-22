# Quản lý nhà hàng, khu vực và bàn

QR bàn UC_ADM_10 đã được bổ sung ở [qr-management.md](qr-management.md), gồm tạo/tạo lại, PNG và resolve để lấy đúng mã bàn.

Phạm vi đợt này: UC_ADM_11, UC_ADM_07, UC_ADM_08, UC_ADM_09. Chỉ dùng model/schema hiện có theo yêu cầu người dùng; không có migration hoặc script thay đổi database. Menu, kho, QR và luồng phục vụ chưa nằm trong đợt triển khai này.

## Khởi chạy và xác thực

Chạy đồng thời Identity.API, Restaurant.API và ApiGateway. Restaurant dùng connection string trong User Secrets đã cấu hình; database phải có schema Restaurant hiện tại.

- Gateway local: http://localhost:5000/swagger.
- Restaurant local: https://localhost:5002, HTTP/2.
- Restaurant gọi Identity qua cấu hình GrpcServices:Identity để xác minh token, phiên còn hiệu lực, tài khoản active và vai trò Admin.
- Danh sách mã vai trò Admin lấy từ UserManagement:AdminRoleIds của Identity. Không hardcode tên vai trò trong Restaurant hoặc chỉ tin role do client gửi.
- Đăng nhập qua Gateway bằng tài khoản Admin, dùng access token với nút Authorize trong Swagger.
- RPC AccessGrpc.RequireAdmin được bổ sung cho Identity; phải build/chạy bản Identity mới cùng Gateway và Restaurant.
- Nếu Identity không hoạt động, API quản lý không cấp quyền và trả lỗi dịch vụ; không bỏ qua kiểm tra phiên.

Docker: điền RESTAURANT_DB_CONNECTION trong .env, sau đó chạy:

```powershell
docker compose up -d --build identity restaurant gateway
```

Compose cho phép chọn trực tiếp restaurant dù service thuộc profile services. Gateway dùng địa chỉ http://restaurant:5002; Restaurant dùng http://identity:5001. Không cần copy JWT secret sang Restaurant.

## API qua Gateway

Tất cả endpoint yêu cầu Admin và trả envelope ResponseDTO. GetAll trả result gồm items, totalCount, page, pageSize. Query chung: search, page (mặc định 1), pageSize (mặc định 20, tối đa 100). Khu vực/bàn hỗ trợ restaurantId, isActive; bàn thêm areaId.

| Method | Route | Chức năng |
| --- | --- | --- |
| GET / POST | /api/restaurants | Danh sách / tạo hồ sơ nhà hàng |
| GET / PUT | /api/restaurants/{id} | Chi tiết / cập nhật hồ sơ và giờ hoạt động |
| GET / POST | /api/areas | Danh sách / tạo khu vực |
| GET / PUT / DELETE | /api/areas/{id} | Chi tiết / cập nhật / xóa khu vực chưa có bàn |
| PATCH | /api/areas/{id}/status | Bật/tắt khu vực |
| GET / POST | /api/tables | Danh sách / tạo bàn |
| GET / PUT | /api/tables/{id} | Chi tiết / cập nhật bàn |
| DELETE | /api/tables/{id}?version=... | Xóa bàn chưa có lịch sử, QR hoặc đặt bàn |
| PATCH | /api/tables/{id}/status | Bật/tắt bàn |
| PATCH | /api/tables/{id}/area | Gán/chuyển khu vực của bàn |

Lỗi: 400 dữ liệu không hợp lệ, 401 phiên/token không hợp lệ, 403 không phải Admin, 404 không tìm thấy, 409 xung đột nghiệp vụ hoặc version cũ, 503/504 dịch vụ phụ thuộc không sẵn sàng/quá hạn.

## Ví dụ tạo dữ liệu theo thứ tự

POST /api/restaurants:

```json
{
  "restaurantName": "Nhà hàng A",
  "address": "Địa chỉ nhà hàng",
  "hotline": "0901234567",
  "taxCode": null,
  "logoUrl": null,
  "timeZone": "Asia/Ho_Chi_Minh",
  "openingHours": [
    { "dayOfWeek": 1, "openAt": "08:00", "closeAt": "22:00" }
  ]
}
```

POST /api/areas (thay restaurantId bằng ID thật từ kết quả trên):

```json
{
  "restaurantId": "11111111-1111-1111-1111-111111111111",
  "areaCode": "TANG1",
  "areaName": "Tầng 1",
  "serviceType": "DineIn",
  "spaceType": "Indoor"
}
```

POST /api/tables (thay areaId bằng ID thật):

```json
{
  "areaId": "22222222-2222-2222-2222-222222222222",
  "tableCode": "B01",
  "capacity": 4,
  "positionX": 10,
  "positionY": 20
}
```

Kết quả bàn có version (Base64). PUT, PATCH status/area và DELETE phải gửi version mới nhất. DELETE truyền version được URL-encode qua query string. Khi nhận 409, tải lại bàn trước khi thao tác tiếp.

PATCH /api/tables/{id}/area:

```json
{
  "areaId": "33333333-3333-3333-3333-333333333333",
  "version": "VERSION_TU_KET_QUA_GET"
}
```

PATCH /api/tables/{id}/status:

```json
{
  "isActive": false,
  "version": "VERSION_TU_KET_QUA_GET"
}
```

Khu vực dùng body status chỉ cần isActive.

## Ràng buộc và giới hạn theo model hiện tại

- Không thêm email nhà hàng, mô tả khu vực, tên/ghi chú bàn vì model chưa có. Logo hiện là URL, không phải chức năng upload/kiểm tra nội dung ảnh.
- Bàn bắt buộc có AreaId nên phải chọn khu vực đang hoạt động ngay khi tạo. Không có trạng thái bàn chưa gán khu vực trong schema này.
- Mã khu vực duy nhất trong một nhà hàng; mã bàn duy nhất trong một nhà hàng, kể cả ở hai khu vực khác nhau. Kiểm tra không phân biệt hoa/thường và bỏ khoảng trắng hai đầu.
- Không chuyển khu vực hoặc bàn sang nhà hàng khác.
- Không xóa khu vực còn bất kỳ bàn nào. Khu vực có thể ngừng hoạt động nếu không còn phiên mở; bàn trong khu vực vẫn giữ trạng thái quản lý riêng. Luồng mở phiên/QR về sau phải kiểm tra cả trạng thái bàn và khu vực.
- Không xóa bàn có lịch sử phiên, QR hoặc đặt bàn; dùng ngừng sử dụng để bảo toàn dữ liệu.
- Không sửa/chuyển/ngừng sử dụng bàn còn liên kết với phiên chưa đóng. Kiểm tra cả phiên cũ chưa đóng sau khi chuyển bàn để tránh ảnh hưởng nghiệp vụ còn treo.
- Đơn hàng nằm ở Ordering, không truy cập chéo database. Hiện kiểm tra bảo vệ dựa vào phiên chưa đóng ở Restaurant. Khi triển khai đóng phiên phải bắt buộc xử lý hết order/thanh toán trước khi đặt ClosedAt; chưa có kiểm tra RPC trực tiếp sang Ordering trong nhóm chức năng này.
- Ghi dữ liệu trong transaction Serializable; Table dùng rowversion có sẵn. Cần kiểm thử thêm SQL Server thật cho khóa, deadlock và rowversion. Không tự tạo index hoặc thay đổi schema.
- Ngày hoạt động dùng 0 = Chủ nhật đến 6 = Thứ bảy. Phiên bản này nhận tối đa một khoảng giờ trong cùng ngày; openAt < closeAt; danh sách giờ trong PUT thay thế toàn bộ danh sách cũ. Ca qua đêm/nhiều khoảng giờ mỗi ngày cần yêu cầu bổ sung.
- ServiceType và SpaceType dùng chuỗi bắt buộc tối đa 50 ký tự theo model; chưa áp đặt danh mục enum chưa được chốt. Bàn mới có CleaningStatus = Clean.
- Chưa bổ sung Audit Log nghiệp vụ liên service; không coi log lỗi kỹ thuật là lịch sử thay đổi. Việc tích hợp audit bền vững cho các thao tác bàn/khu vực cần thực hiện cùng luồng Audit Log liên service.

## Kiểm tra

```powershell
dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj
dotnet test Tests/Foundation.Tests/Foundation.Tests.csproj
dotnet test Tests/Identity.Tests/Identity.Tests.csproj
```

Restaurant.Tests kiểm tra HTTP Gateway → gRPC Restaurant → BLL → SQLite bằng TestServer. Quyền Identity được giả lập trong test luồng; test riêng của IdentityManagementAccess kiểm tra chuyển token và không cấp quyền khi Identity từ chối/mất kết nối. SQLite mô phỏng version trong test, không thay thế kiểm thử rowversion và khóa của SQL Server.
