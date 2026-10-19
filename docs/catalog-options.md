# Nhóm tùy chọn, tùy chọn và gán vào món

API quản lý Catalog ngày 20/10/2026, theo luồng Gateway → gRPC → BLL → repository → CatalogDbContext. Dùng schema hiện có, không chạy migration hay thay đổi database.

## Quyền và phản hồi

Tất cả endpoint dưới đây yêu cầu `Authorization: Bearer <accessToken>`. Tài khoản phải đang hoạt động, token/phiên hợp lệ và RoleId thuộc `CatalogManagement:MenuManagerRoleIds` (cấu hình hiện tại: `manager`, `admin`). Kitchen không có quyền quản lý nhóm/tùy chọn/gán nhóm; quyền báo tạm hết Item vẫn theo API trạng thái món đã có.

Tạo thành công trả 201 và header Location. Đọc/sửa/xóa thành công trả 200. Dữ liệu nằm trong `ResponseDTO.result`; xóa trả result null. Các ID trong ví dụ chỉ minh họa, cần thay bằng ID thật API trả về.

## 1. OptionGroup — nhóm Size/Topping

| Method | Route | Chức năng |
| --- | --- | --- |
| POST | `/api/option-groups` | Tạo nhóm |
| GET | `/api/option-groups?page=1&pageSize=20&search=Size` | Tìm kiếm, phân trang |
| GET | `/api/option-groups/{id}` | Chi tiết |
| PUT | `/api/option-groups/{id}` | Sửa tên |
| DELETE | `/api/option-groups/{id}` | Xóa nhóm trống, chưa gán vào món |

Body POST/PUT:

```json
{
  "optionGroupName": "Size"
}
```

Ví dụ result:

```json
{
  "optionGroupId": "4dc57fdd-a931-43c8-9fba-336c6bc4e01d",
  "optionGroupName": "Size"
}
```

Tên được trim và chuẩn hóa Unicode, dài 1–255 ký tự, không chứa ký tự điều khiển. Tên nhóm không trùng nhóm khác; so sánh tên tại database tuân theo collation của database. Không xóa nhóm còn Option hoặc còn ItemOptionGroup.

## 2. Option — lựa chọn trong nhóm

| Method | Route | Chức năng |
| --- | --- | --- |
| POST | `/api/options` | Tạo lựa chọn |
| GET | `/api/options?page=1&pageSize=20&optionGroupId={groupId}&search=Lớn&availability=Available` | Lọc theo nhóm, tên, trạng thái; phân trang |
| GET | `/api/options/{id}` | Chi tiết |
| PUT | `/api/options/{id}` | Sửa tên và phụ thu |
| PATCH | `/api/options/{id}/status` | Đổi trạng thái |
| DELETE | `/api/options/{id}` | Xóa khi không có recipe và nhóm không đang gán vào món |

Body POST:

```json
{
  "optionGroupId": "4dc57fdd-a931-43c8-9fba-336c6bc4e01d",
  "optionName": "Lớn",
  "extraPrice": 15000
}
```

Ví dụ result:

```json
{
  "optionId": "c8880399-ff94-4b4f-9c22-048df1594394",
  "optionGroupId": "4dc57fdd-a931-43c8-9fba-336c6bc4e01d",
  "optionName": "Lớn",
  "extraPrice": 15000,
  "availability": "Inactive"
}
```

Tùy chọn mới mặc định Inactive. Body PATCH để cho phép chọn:

```json
{
  "availability": "Available"
}
```

Trạng thái hỗ trợ: `Available`, `OutOfStock`, `Inactive`. Việc đổi trạng thái Option do người quản lý menu thực hiện; chưa mở quyền cho Kitchen.

Body PUT:

```json
{
  "optionName": "Size lớn",
  "extraPrice": 20000
}
```

PUT không chuyển Option sang nhóm khác và không đổi trạng thái. Tên phải dài 1–255 ký tự sau trim, không chứa ký tự điều khiển và không trùng trong cùng nhóm; cùng tên ở hai nhóm khác nhau được phép. Phụ thu bắt buộc, không âm, tối đa 2 chữ số thập phân và nằm trong decimal(18,2). Giá 0 hợp lệ.

## 3. ItemOptionGroup — gán nhóm cho món

| Method | Route | Chức năng |
| --- | --- | --- |
| POST | `/api/items/{itemId}/option-groups` | Gán nhóm |
| GET | `/api/items/{itemId}/option-groups` | Danh sách nhóm của món, result là mảng |
| GET | `/api/items/{itemId}/option-groups/{groupId}` | Chi tiết một liên kết |
| PUT | `/api/items/{itemId}/option-groups/{groupId}` | Sửa giới hạn chọn |
| DELETE | `/api/items/{itemId}/option-groups/{groupId}` | Gỡ nhóm khỏi món |

Ví dụ bắt buộc chọn đúng một Size — body POST:

```json
{
  "optionGroupId": "4dc57fdd-a931-43c8-9fba-336c6bc4e01d",
  "minSelection": 1,
  "maxSelection": 1
}
```

Ví dụ nhóm Topping không bắt buộc, tối đa ba lựa chọn — body PUT:

```json
{
  "minSelection": 0,
  "maxSelection": 3
}
```

Ví dụ result:

```json
{
  "itemId": "7d9dd887-e49c-4d66-885f-c3b138cc2560",
  "optionGroupId": "4dc57fdd-a931-43c8-9fba-336c6bc4e01d",
  "optionGroupName": "Size",
  "minSelection": 1,
  "maxSelection": 1
}
```

Cả MinSelection và MaxSelection đều bắt buộc: `0 <= MinSelection <= MaxSelection`, MaxSelection ít nhất 1. Item và OptionGroup phải tồn tại. Một nhóm có thể dùng cho nhiều món, mỗi món có giới hạn riêng; không gán trùng cặp ItemId + OptionGroupId. Gỡ liên kết không xóa nhóm, Option hay liên kết của món khác.

Có thể gán nhóm trước khi thêm/bật các Option để chuẩn bị cấu hình. API chưa ép giới hạn chọn theo số Option hiện đang khả dụng; luồng menu/đặt hàng cần phát hiện nhóm bắt buộc nhưng không đủ lựa chọn khả dụng.

## Lỗi, đồng thời và phạm vi

- 400: ID, tên, phụ thu, trạng thái, phân trang hoặc giới hạn chọn không hợp lệ.
- 401/403: không xác thực hoặc không đủ quyền.
- 404: không có nhóm/tùy chọn/món/liên kết được yêu cầu.
- 409: tên trùng, gán trùng hoặc dữ liệu liên quan chặn xóa.
- 503: database/dịch vụ phụ thuộc không khả dụng; 504: quá thời gian gọi dịch vụ.

Danh sách nhóm và Option dùng Page mặc định 1, PageSize mặc định 20, tối đa 100. Chuỗi tìm kiếm tối đa 255 ký tự. Result có items, totalCount, page, pageSize.

Các thao tác ghi dùng transaction Serializable. Ba model hiện không có rowversion; API không yêu cầu version và không phát hiện form cũ theo cơ chế Item.Version. Các cập nhật tuần tự dùng dữ liệu từ yêu cầu sau.

Các kiểm tra trước khi xóa chỉ bao phủ quan hệ trong Catalog. Chưa có RPC kiểm tra giỏ hàng/đơn hàng ở Ordering; với tùy chọn đã được sử dụng thực tế, dùng Inactive để bảo toàn tham chiếu. Khi nối Ordering, phải xác thực lại lựa chọn thuộc đúng món, trạng thái khả dụng, giới hạn chọn và lưu snapshot tên/phụ thu vào đơn. Phần này chưa triển khai API thực đơn khách hàng, tính tổng giỏ hàng, realtime hay cập nhật đơn cũ.

## Kiểm thử

```powershell
dotnet test Tests/Catalog.Tests/Catalog.Tests.csproj -c Release
dotnet test Tests/Foundation.Tests/Foundation.Tests.csproj -c Release
```

Test mới đi qua HTTP Gateway, gRPC và SQLite với Identity client giả lập, bao phủ CRUD, phân trang/lọc, dữ liệu sai, phụ thu, tên trùng, quan hệ chặn xóa, khóa kép và quyền trên đủ 16 endpoint. Chưa kiểm thử khóa/constraint/collation trên SQL Server thật.
