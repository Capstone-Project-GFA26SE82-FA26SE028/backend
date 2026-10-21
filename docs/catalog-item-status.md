# Trạng thái bán của món

Triển khai trên Catalog cho UC_MGR_03 và phần báo hết món của UC_KIT_07. Quyền được chốt ngày 20/10/2026: Manager quản lý trạng thái; Kitchen chỉ báo tạm hết món.

## API

`PATCH /api/items/{id}/status`, gửi `Authorization: Bearer <accessToken>`:

```json
{
  "availability": "Available",
  "version": "<version lấy từ GET /api/items/{id}>"
}
```

`version` là SQL Server rowversion gồm 8 byte, mã hóa Base64. Thành công trả HTTP 200 với `ResponseDTO.result` là ItemDTO, bao gồm trạng thái và version mới. Frontend phải cập nhật version sau mỗi lần sửa thông tin hoặc trạng thái; HTTP 409 do version cũ cần tải lại món trước khi thử lại.

| Giá trị | Ý nghĩa | Quyền và điều kiện |
| --- | --- | --- |
| `Available` | Đang bán | Manager; Category hoạt động, có đúng một công thức đang hiệu lực và đúng một giá chuẩn đang hiệu lực, số tiền không âm |
| `OutOfStock` | Tạm hết món | Manager hoặc Kitchen; trạng thái hiện tại phải là `Available` hoặc `OutOfStock` |
| `Inactive` | Ngừng kinh doanh | Manager; không yêu cầu giá chuẩn |

Giá chuẩn là bản ghi có Channel bằng `Pricing:AllChannelsCode`, không có DailyStart/DailyEnd và thỏa `EffectiveFrom <= thời điểm server < EffectiveTo` (hoặc không có EffectiveTo). Giá theo kênh riêng, giá theo giờ, giá tương lai và giá hết hạn không thay thế được giá chuẩn. Giá 0 được phép. Kiểm tra và cập nhật nằm trong cùng transaction Serializable.

Gửi lại trạng thái hiện tại với version đúng trả 200, không đổi version nếu dữ liệu không thay đổi. Yêu cầu `Available` vẫn kiểm tra lại Category, công thức và giá chuẩn. Món chưa có công thức, công thức chỉ có hiệu lực trong tương lai, hết hiệu lực hoặc có nhiều công thức đồng thời đều bị chặn (409). Không được dùng `OutOfStock` để đưa món đã ngừng kinh doanh trở lại thực đơn; Manager phải mở bán trước.

Kitchen được dùng `GET /api/items` và `GET /api/items/{id}` để xem trạng thái và lấy version. Kitchen không được tạo/sửa thông tin món, mở bán/ngừng kinh doanh, hoặc quản lý Category và Price. Token, phiên và tài khoản được kiểm tra qua Identity `GetMe`; role được đối chiếu đúng RoleId cấu hình.

## Cấu hình

- `CatalogManagement:MenuManagerRoleIds`: các RoleId được quản lý menu; giữ cấu hình hiện tại `manager`.
- `CatalogManagement:KitchenRoleIds`: các RoleId Kitchen; mặc định `kitchen`. Thay bằng RoleId thực tế nếu database dùng giá trị khác. Mảng rỗng không cấp quyền Kitchen.
- Docker có biến `CATALOG_KITCHEN_ROLE_ID` trong `.env`; không tự thay đổi `.env` đang dùng.
- `CatalogManagement:NonSellingItemAvailabilities` phải chứa `Inactive`, không chứa `Available` hay `OutOfStock`. Món tạm hết vẫn thuộc thực đơn đang kinh doanh nên tiếp tục chặn tắt Category.

Lỗi chính: 400 khi trạng thái/ID/version sai định dạng; 401 khi chưa xác thực hoặc phiên/tài khoản không hợp lệ; 403 khi thiếu quyền; 404 khi không có món; 409 khi version cũ hoặc không đủ điều kiện chuyển trạng thái; 503 khi cấu hình cần thiết hoặc dịch vụ phụ thuộc không khả dụng.

## Phạm vi tích hợp

Thao tác chỉ đổi Availability và rowversion của Item, không sửa thông tin món, giá, công thức hay đơn hàng cũ. Không thêm cột database. Chưa lưu lý do đổi trạng thái vì model hiện tại chưa có trường/lịch sử tương ứng.

API [menu](catalog-menu.md) và [validate lựa chọn đặt món](catalog-order-validation.md) kiểm tra lại trạng thái, giá và công thức hiện tại. Việc mở bán thành công không đảm bảo dữ liệu vẫn còn hợp lệ ở lần đặt sau. Luồng tạo/xác nhận Order và thông báo realtime vẫn chưa được triển khai.

## Kiểm thử

```powershell
dotnet test Tests/Catalog.Tests/Catalog.Tests.csproj -c Release
```

Test đi qua HTTP Gateway → gRPC → BLL → SQLite; kiểm tra quyền qua IdentityCatalogAccess với Identity client giả lập, quy tắc giá và Category, biên thời gian, chuyển trạng thái, version cũ và các trường không được thay đổi. SQLite mô phỏng rowversion; cần kiểm tra khóa, deadlock và rowversion trên SQL Server thật khi kiểm thử tích hợp môi trường triển khai.
