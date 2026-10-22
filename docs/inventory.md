# Inventory: nguyên liệu, nhập kho và tồn kho

Đợt đầu triển khai UC_MGR_04 (danh mục theo đơn vị cơ sở), UC_INV_02 và phần đọc tồn/lịch sử của UC_MGR_07. Giữ nguyên schema hiện tại, không migration hoặc sửa dữ liệu kho đang có.

## Phân quyền

Mọi endpoint cần bearer token. Inventory gọi Identity GetMe để kiểm tra tài khoản, trạng thái hoạt động và phiên đăng nhập; không tin role hoặc user ID do client gửi.

| Thao tác | Role mặc định |
| --- | --- |
| Tạo/sửa/bật tắt nguyên liệu | `manager` |
| Tạo/xác nhận phiếu nhập | `inventory` |
| Xem nguyên liệu, tồn, phiếu nhập, lịch sử | `manager`, `inventory` |

Role ID phân biệt hoa thường. Có thể cấu hình `InventoryAccess:ManagerRoleIds`, `ReceiverRoleIds`, `ReaderRoleIds` trong Inventory.API; danh sách rỗng từ chối quyền. Admin không tự động có quyền nghiệp vụ kho.

## API

Gateway: `http://localhost:5000`. Response dùng envelope `isSuccess`, `statusCode`, `message`, `result` hiện có.

| Method | Route | Chức năng |
| --- | --- | --- |
| POST | `/api/ingredients` | Tạo nguyên liệu, tồn ban đầu bằng 0; trả 201 |
| GET | `/api/ingredients` | Danh sách và tồn hiện tại |
| GET | `/api/ingredients/{id}` | Chi tiết nguyên liệu |
| PUT | `/api/ingredients/{id}` | Sửa tên/ngưỡng cảnh báo |
| PATCH | `/api/ingredients/{id}/status` | Bật/tắt nguyên liệu |
| POST | `/api/goods-receipts` | Tạo phiếu nháp hoặc trả phiếu đã tạo với cùng ID/body; trả 200 |
| GET | `/api/goods-receipts` | Danh sách phiếu nhập |
| GET | `/api/goods-receipts/{id}` | Phiếu nhập và các lô/hạn dùng |
| POST | `/api/goods-receipts/{id}/confirm` | Xác nhận nhập kho; không có body |
| GET | `/api/ingredients/{id}/stock-movements` | Lịch sử biến động nguyên liệu |

Các danh sách hỗ trợ `page=1&pageSize=20`, pageSize tối đa 100, trả `items`, `totalCount`, `page`, `pageSize`. Nguyên liệu có thêm `search`, `isActive`, `lowStockOnly`. Lịch sử sắp xếp mới nhất trước.

### 1. Manager tạo nguyên liệu

```json
{
  "ingredientName": "Gạo",
  "baseUnit": "g",
  "lowStockThreshold": 1000
}
```

Mã `ING-<guid>` do hệ thống tạo. Tên tối đa 255 ký tự, đơn vị tối đa 50 ký tự; tên/đơn vị không rỗng hoặc chứa ký tự điều khiển. Đơn vị được trim và chuyển chữ thường. `stockQuantity` không được sửa trực tiếp qua API.

PUT dùng cùng body. Đơn vị cơ sở không được đổi sau khi tạo vì Catalog có thể đã tham chiếu nguyên liệu này; tạo nguyên liệu mới nếu cần đơn vị khác. PATCH status dùng `{"isActive": false}`. Không xóa vật lý để giữ lịch sử.

### 2. Inventory tạo phiếu nhập nháp

Client tạo GUID mới cho mỗi phiếu, giữ nguyên ID và body khi retry sau timeout. Số lượng luôn theo đơn vị cơ sở của nguyên liệu.

```json
{
  "goodsReceiptId": "70000000-0000-0000-0000-000000000001",
  "note": "Nhập gạo",
  "items": [
    {
      "ingredientId": "<ingredientId từ bước 1>",
      "receivedQuantity": 10000,
      "expiryDate": "2027-10-31"
    }
  ]
}
```

Tối đa 100 dòng, ghi chú tối đa 1000 ký tự. Mỗi dòng bắt buộc có hạn dùng `yyyy-MM-dd` và số lượng dương, tối đa 6 chữ số thập phân, tối đa `999999999999.999999`. Không trùng cặp nguyên liệu/hạn dùng trong một phiếu; các hạn dùng khác nhau được lưu thành các lô riêng. Nguyên liệu phải tồn tại và đang active.

Phiếu nháp chưa tăng tồn. Gửi lại cùng ID/body/tài khoản trả phiếu hiện tại (kể cả đã xác nhận); ID cũ với body khác hoặc tài khoản khác trả 409. Không sửa/xóa phiếu qua API trong đợt này.

### 3. Xác nhận và kiểm tra tồn

Gọi `POST /api/goods-receipts/{id}/confirm`. Backend kiểm tra lại nguyên liệu, tăng tồn, tạo một StockMovement loại `Receipt` cho mỗi dòng và đổi phiếu sang `Confirmed` trong cùng transaction Serializable. Người thao tác lấy từ Identity; timestamp dùng UTC.

Xác nhận lại phiếu Confirmed trả kết quả hiện tại, không cộng tồn hoặc ghi movement lần nữa. Lỗi một dòng rollback toàn phiếu. Khi xảy ra tranh chấp/deadlock trả 409, retry với cùng ID. Không tạo phiếu mới chỉ vì mất response.

`isLowStock` được tính bằng `stockQuantity <= lowStockThreshold`; sau nhập kho, cảnh báo tự hết khi tồn vượt ngưỡng. `stockVersion` trả rowversion hiện tại, chưa là điều kiện If-Match cho sửa thông tin nguyên liệu.

400: dữ liệu không hợp lệ; 401: chưa xác thực/phiên không hợp lệ; 403: sai quyền; 404: không tồn tại; 409: xung đột hoặc nguyên liệu inactive; 503/504: service phụ thuộc không sẵn sàng/timeout.

## Chạy và kiểm thử

Đã cấu hình Gateway → Inventory và Inventory → Identity cho local/Docker. Rebuild và khởi động lại Gateway, Inventory để nạp API mới:

```powershell
docker compose build db-init
docker compose run --rm db-init
docker compose up -d --build --no-deps inventory catalog gateway
dotnet test Tests/Inventory.Tests/Inventory.Tests.csproj
```

Dùng Swagger hoặc TestUI API Console để gọi endpoint mới sau khi tải lại hợp đồng API. Chưa có màn hình Inventory riêng trong TestUI.

Tests dùng SQLite in-memory và giả lập Identity/rowversion: quyền, danh mục, đơn vị bất biến, validation, tạo phiếu lặp, xác nhận lặp, rollback khi một dòng lỗi, tràn số tồn và lịch sử. Chưa kiểm chứng khóa đồng thời trên SQL Server hoặc toàn luồng HTTP/gRPC đang chạy thật. Không ghi dữ liệu nghiệp vụ vào database thật trong kiểm thử này.

## Phần tiếp theo

- Schema hiện chưa có bảng đơn vị/quy đổi và đơn giá nhập: chưa nhập theo đơn vị mua, chưa tính giá vốn bình quân.
- Catalog chưa xác minh IngredientRef/UnitRef với Inventory. Cần bổ sung hợp đồng này trước tích hợp trừ kho.
- Tồn hiển thị là tồn ghi sổ, chưa phân biệt tồn giữ chỗ/tồn có thể chế biến. Hạn dùng được lưu, chưa có chính sách chặn nhập lô đã hết hạn hoặc cảnh báo hạn dùng.
- Chưa xuất FIFO, kiểm kê/điều chỉnh, giữ chỗ hoặc trừ kho tự động. Theo UC_KIT_12/UC_INV_03, bước sau trừ kho khi bếp chuyển sang Cooking; phải chốt chính sách lô hết hạn/âm kho trước khi triển khai.
- Tồn seed hoặc dữ liệu cũ không được tự tạo bổ sung lịch sử nhập; lịch sử chỉ phản ánh các movement đã ghi nhận.
