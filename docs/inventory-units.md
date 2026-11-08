# Đơn vị tính, quy đổi và xác minh công thức

## Nâng cấp database trước khi chạy code mới

Script `docker/sql/upgrades/InventoryUnits.sql` bổ sung bảng `Unit`, `IngredientUnit` và ba cột nullable trên GoodsReceiptItem: `InputUnitId`, `InputQuantity`, `ConversionFactorSnapshot`. Script chạy lại được, dùng transaction và khóa nâng cấp. Không thay StockQuantity, ReceivedQuantity hay công thức cũ.

Docker bootstrap gọi script cho cả InventoryDb mới và database đã có dấu bootstrap. Với môi trường Docker đang dùng:

```powershell
docker compose build db-init
docker compose run --rm db-init
docker compose up -d --build --no-deps inventory catalog gateway
```

Chỉ chạy bước khởi động service sau khi db-init thành công. Không xóa volume. SQL Server local: chạy nội dung script trên đúng InventoryDb, rồi restart Inventory, Catalog và Gateway. Không dùng EnsureCreated để nâng cấp database hiện có.

Script tạo `g` với ID `20000000-0000-0000-0000-000000000100` để khớp UnitRef demo Catalog, và `kg` với ID `20000000-0000-0000-0000-000000000101`. Các đơn vị cơ sở cũ còn lại được đưa vào danh mục nhưng không tự đoán hệ số quy đổi. Nếu mã g đã mang ID khác, script dừng để đối chiếu chủ động.

## API và phân quyền

| Method | Route | Quyền |
| --- | --- | --- |
| POST | `/api/units` | Manager |
| GET | `/api/units` | Manager, Inventory |
| PUT | `/api/ingredients/{ingredientId}/units/{unitId}` | Manager |
| GET | `/api/ingredients/{ingredientId}/units` | Manager, Inventory |

Tạo đơn vị dùng `{"code":"bag","name":"Bao"}`. Code được trim, lowercase, chỉ gồm chữ/số ASCII và dấu gạch dưới; tối đa 50 ký tự. Tên tối đa 100 ký tự. Mã trùng trả 409. Code/ID không sửa hoặc xóa để không đổi ý nghĩa dữ liệu cũ.

Khi tạo nguyên liệu, `baseUnit` phải là code đã có trong danh mục. Đơn vị cơ sở vẫn bất biến. Một đơn vị nhập phải có quy đổi riêng cho nguyên liệu; ví dụ “bao” gạo 25 kg và “bao” bột 10 kg không dùng chung hệ số. Hệ số của đơn vị cơ sở luôn bằng 1. Manager chịu trách nhiệm cấu hình đúng ý nghĩa đơn vị; hệ thống không suy đoán chuyển đổi khối lượng/thể tích hoặc kích cỡ bao.

## Ví dụ: nhập 10 kg gạo, công thức dùng 0,15 kg

1. Manager tạo nguyên liệu `{"ingredientName":"Gạo","baseUnit":"g","lowStockThreshold":1000}`. Lưu ID trả về thành `ingredientId`.
2. Manager gọi `PUT /api/ingredients/{ingredientId}/units/20000000-0000-0000-0000-000000000101`:

```json
{
  "conversionFactor": 1000
}
```

Nghĩa là **1 kg = 1000 đơn vị cơ sở g** cho nguyên liệu này. PUT tạo mới hoặc cập nhật cấu hình. Không tự đổi các phiếu/công thức đã lưu.

3. Inventory tạo phiếu nhập:

```json
{
  "goodsReceiptId": "70000000-0000-0000-0000-000000000002",
  "note": "Nhập 10 kg gạo",
  "items": [
    {
      "ingredientId": "<ID gạo thực tế>",
      "unitId": "20000000-0000-0000-0000-000000000101",
      "receivedQuantity": 10,
      "expiryDate": "2027-10-31"
    }
  ]
}
```

POST `/api/goods-receipts` lưu `inputQuantity=10`, `conversionFactorSnapshot=1000`, `receivedQuantity=10000` (đơn vị cơ sở). Trường receivedQuantity trong **request có unitId** là lượng nhập; trong **response** luôn là lượng quy về cơ sở. Không gửi unitId vẫn dùng cách nhập cũ theo đơn vị cơ sở.

POST `/api/goods-receipts/{id}/confirm` mới cộng 10000 g vào tồn. Đổi hệ số sau khi tạo nháp không đổi lượng nhập của phiếu đó. Retry cùng receipt ID/body sau khi đổi hệ số vẫn trả phiếu cũ; đổi unitId hoặc lượng nhập với cùng ID trả 409.

4. Manager tạo công thức món qua endpoint hiện có `POST /api/items/{itemId}/recipes`:

```json
{
  "instructions": "Định lượng gạo cho một phần",
  "ingredients": [
    {
      "ingredientRef": "<ID gạo thực tế>",
      "unitRef": "20000000-0000-0000-0000-000000000101",
      "quantity": 0.15
    }
  ]
}
```

Catalog chuyển token Manager tới Inventory để xác minh nguyên liệu active, đơn vị tồn tại và cấu hình quy đổi. Snapshot lưu `quantity=0.15`, `conversionFactorSnapshot=1000`, `baseQuantitySnapshot=150`. Áp dụng tương tự cho công thức option. Trường conversionFactor cũ vẫn được nhận để tương thích, nhưng hệ số từ Inventory mới được dùng. Công thức lịch sử không bị tính lại; sửa công thức tạo version mới.

## Kiểm soát lỗi và giới hạn

- Số lượng/hệ số phải dương và biểu diễn chính xác được trong decimal(18,6). Tràn số hoặc cần làm tròn bị từ chối; không âm thầm làm mất lượng nguyên liệu.
- Nguyên liệu hoặc đơn vị không tồn tại: 404. Nguyên liệu inactive hoặc đơn vị chưa cấu hình cho nguyên liệu: 409. Sai quyền: 401/403.
- Catalog không lưu công thức khi Inventory không đáp ứng: 503/504; không tự lấy hệ số 1 khi thiếu dữ liệu.
- Kiểm tra giữa Catalog và Inventory không phải transaction phân tán. Snapshot phản ánh hệ số đọc được khi tạo; không bảo đảm cấu hình không đổi ngay sau lần đọc.
- Dữ liệu Catalog cũ có UnitRef tùy ý cần đối chiếu khi tạo version mới. Không tự sửa các tham chiếu hoặc snapshot cũ.
- Chưa trừ kho/FIFO/kiểm kê. Giữ nguyên thời điểm dự kiến trừ kho khi bếp chuyển Cooking.
