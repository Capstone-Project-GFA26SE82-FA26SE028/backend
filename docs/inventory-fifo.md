# Tồn theo lô và xuất FIFO

## Phạm vi và quyền

Manager và Inventory xem tồn theo lô, chi tiết phiếu xuất. Chỉ Inventory tạo phiếu xuất thủ công, theo cấu hình `InventoryAccess:IssuerRoleIds` (mặc định `["inventory"]`). Kitchen chưa được gọi API xuất kho; bước tích hợp Cooking sẽ dùng một hợp đồng riêng có xác minh sự kiện và công thức.

| Method | Route | Chức năng |
| --- | --- | --- |
| GET | `/api/ingredients/{ingredientId}/lots?page=1&pageSize=20` | Lô theo thứ tự FIFO, tối đa 100 dòng/trang |
| POST | `/api/stock-issues` | Tạo phiếu và xuất kho ngay, trả 200 |
| GET | `/api/stock-issues/{id}` | Xem phiếu và phân bổ từng lô |

Các request cần bearer token, qua Gateway `http://localhost:5000`; response dùng envelope hiện có. Lịch sử `/api/ingredients/{id}/stock-movements` có thêm `stockIssueId` để liên kết phiếu xuất.

## Quy tắc lô và hạn dùng

- Mỗi GoodsReceiptItem của phiếu Confirmed là một lô. Phiếu Draft không được xuất.
- Thời điểm nhập dùng `ConfirmAt`, fallback `CreatedAt` cho dữ liệu cũ. FIFO sắp xếp theo thời điểm này rồi ID lô để xử lý trường hợp trùng giờ. Không chọn lô theo hạn dùng sớm nhất.
- Tồn lô = ReceivedQuantity theo đơn vị cơ sở + tổng QuantityChange của các movement gắn với lô, ngoại trừ movement `Receipt` đã được tính vào ReceivedQuantity. Movement `Issue` ghi số âm.
- Ngày nghiệp vụ dùng UTC+7 (Việt Nam). Lô dùng được đến hết ngày ExpiryDate; ngày hôm sau là hết hạn.
- Trạng thái `Expired`: ngày hết hạn trước hôm nay; `ExpiringSoon`: từ hôm nay đến 7 ngày tới, tính cả hai đầu; `Valid`: xa hơn 7 ngày; `Unknown`: thiếu ngày hết hạn.
- Không xuất lô Expired hoặc Unknown. Không tự xóa/trừ tồn của lô hết hạn. `canIssue` cho biết lô active, còn lượng và chưa hết hạn; số lượng xuất cụ thể vẫn phải được kiểm tra khi ghi giao dịch.
- Tồn seed/tồn cũ không có lô nguồn vẫn giữ trong StockQuantity, nhưng không dùng để xuất FIFO. Cần quy trình đối chiếu nguồn gốc/lô riêng, không tự tạo phiếu giả hoặc nhập bù làm tăng tồn lần nữa.

## Ví dụ xuất 3 kg gạo

Giả sử gạo có đơn vị cơ sở `g`, hai phiếu nhập đã Confirmed:

| Lô | Thứ tự nhập | Còn |
| --- | --- | --- |
| A | trước | 2000 g |
| B | sau | 5000 g |

Inventory gửi:

```http
POST /api/stock-issues
Authorization: Bearer <inventory-token>
Content-Type: application/json
```

```json
{
  "stockIssueId": "80000000-0000-0000-0000-000000000001",
  "reason": "Xuất gạo cho sơ chế",
  "items": [
    {
      "ingredientId": "<ID nguyên liệu gạo>",
      "quantity": 3000
    }
  ]
}
```

Số lượng luôn theo đơn vị cơ sở; API xuất chưa nhận đơn vị mua. Kết quả `allocations` có hai movement: A = -2000, B = -1000. Tồn nguyên liệu giảm 3000, A còn 0, B còn 4000. Các ID trong ví dụ cần thay bằng dữ liệu thật; mỗi lần xuất mới dùng GUID mới.

Nếu A hết hạn, hệ thống chỉ lấy B. Nếu tổng các lô hợp lệ không đủ, trả 409 và không xuất gì, kể cả các nguyên liệu khác trong cùng request.

## Idempotency và transaction

Một phiếu có 1–100 nguyên liệu, không trùng IngredientId; quantity dương, tối đa decimal(18,6), không làm tròn. Reason bắt buộc, tối đa 1000 ký tự.

Client tạo StockIssueId và giữ nguyên khi retry sau timeout. Backend lưu hash của lý do và danh sách nguyên liệu/số lượng đã chuẩn hóa, không phụ thuộc thứ tự dòng hoặc số 0 thừa sau dấu thập phân.

- Cùng ID/body/tài khoản trả phiếu cũ, không trừ thêm tồn, kể cả lô sau đó đã hết hạn hoặc cạn.
- Cùng ID nhưng body hoặc tài khoản khác trả 409.
- Phiếu, phân bổ lô, movement và cập nhật tổng tồn ghi trong một transaction Serializable; StockVersion của Ingredient kiểm tra cập nhật đồng thời. Khóa đọc/range được giữ đến khi commit. Deadlock/xung đột trả 409; retry cùng ID.
- Không có API sửa/xóa phiếu đã ghi. Hoàn trả/xuất hủy/kiểm kê sẽ cần nghiệp vụ và movement riêng.

400: dữ liệu sai; 401/403: xác thực/quyền; 404: nguyên liệu/phiếu không tồn tại; 409: thiếu tồn tổng hoặc tồn lô hợp lệ, nguyên liệu inactive, tồn lô âm hoặc ID bị dùng khác; 503/504: phụ thuộc không sẵn sàng/timeout.

## Nâng cấp và chạy

`docker/sql/upgrades/InventoryFifo.sql` thêm bảng StockIssue, cột nullable StockIssueId trên StockMovement và index tra cứu. Không đổi tồn hay movement cũ. Docker bootstrap chạy sau InventoryUnits.sql, cho cả database mới và database đã khởi tạo.

```powershell
docker compose build db-init
docker compose run --rm db-init
# Chỉ tiếp tục nếu db-init thành công.
docker compose up -d --build --no-deps inventory gateway
```

SQL Server local: chạy InventoryUnits.sql nếu chưa nâng cấp, sau đó InventoryFifo.sql trên InventoryDb, rồi restart Inventory/Gateway. Script có transaction/khóa nâng cấp và chạy lại được; không cần xóa volume.

## Kiểm thử và giới hạn

```powershell
dotnet test Tests/Inventory.Tests/Inventory.Tests.csproj
```

Test SQLite bao phủ FIFO nhiều lô, lô hết hạn/thiếu hạn/draft, cảnh báo, thời điểm UTC+7, retry, thay body/tài khoản, rollback đa nguyên liệu, tồn cũ không có lô, inactive, quyền và dữ liệu không hợp lệ. Rowversion SQL Server được giả lập trong SQLite; chưa kiểm chứng tranh chấp đa kết nối trên SQL Server thật hoặc chạy script nâng cấp khi Docker đang tắt.

Truy vấn hiện đọc các lô và movement của một nguyên liệu rồi tính số dư; pagination áp dụng sau khi tính. Với lịch sử lớn cần tối ưu aggregation/paging phía SQL hoặc bảng số dư lô. Chưa có thông báo realtime, màn hình TestUI kho riêng hoặc tự trừ kho khi bếp bấm Cooking.
