# Kiểm tra lựa chọn đặt món và lấy snapshot từ Catalog

## Endpoint và quyền

```http
POST /api/menu/validate-order-items
Content-Type: application/json
Authorization: Bearer <waiter-or-manager-access-token>
```

Khách dùng `?tableSessionId=<id>` và cookie GuestAccess của phiên bàn; frontend gửi `credentials: "include"`. Quyền giống API menu. Khi có `tableSessionId`, dùng quyền khách, không fallback sang token nhân viên. Token khách không được gửi trong JSON.

Gateway gọi `OrderValidationGrpc.ValidateOrderItems`. Ordering sau này có thể gọi trực tiếp RPC này, chuyển tiếp Bearer token của nhân viên hoặc `MenuAccessRequest` chứa token GuestAccess và ID phiên bàn. RPC tự xác minh quyền; không cần gọi các API quản trị bằng tài khoản manager.

Đây là kiểm tra chỉ đọc: chưa tạo Cart/Order, chưa giữ giá, chưa giữ tồn kho và chưa trừ nguyên liệu.

## Request mẫu

Thay các GUID minh họa bằng dữ liệu thực tế:

```json
{
  "channel": "DineIn",
  "items": [
    {
      "itemId": "22222222-2222-2222-2222-222222222222",
      "quantity": 2,
      "options": [
        {
          "optionId": "44444444-4444-4444-4444-444444444444",
          "quantity": 1
        }
      ]
    }
  ]
}
```

- HTTP mặc định `channel = DineIn`; client gRPC truyền channel cụ thể.
- Từ 1–100 dòng, số lượng món 1–1000.
- Mỗi dòng tối đa 100 option, số lượng mỗi option 1–1000.
- `option.quantity` là số phần option **trên mỗi phần món**.
- Min/max của từng nhóm tính theo **tổng số lượng option** trong nhóm trên một phần món.
- Không được lặp `optionId` trong một dòng; tăng `quantity` nếu muốn thêm nhiều phần.
- Cho phép nhiều dòng cùng `itemId` để khách có các lựa chọn khác nhau. Response giữ thứ tự và `lineIndex` bắt đầu từ 0.
- Món không có option thì gửi `options: []` hoặc bỏ trường này.
- Không nhận tên, giá, thời điểm tính giá hay recipe version từ client. HTTP từ chối các trường JSON ngoài DTO. Ghi chú món thuộc dữ liệu Cart/Order, không nằm trong request kiểm tra Catalog này.

## Điều kiện

1. Category active, món tồn tại và `Available`.
2. Có đúng một giá chuẩn đang hiệu lực và một giá được chọn theo ưu tiên kênh/khung giờ; giá không âm và trong giới hạn decimal(18,2).
3. Món có đúng một recipe đang hiệu lực: `EffectiveFrom <= evaluatedAt < EffectiveTo` (hoặc không có EffectiveTo).
4. Option thuộc nhóm đã gắn với món, đang `Available`, có phụ thu hợp lệ.
5. Tổng số lượng option của từng nhóm nằm trong min/max; kiểm tra cả nhóm bắt buộc bị bỏ qua.
6. Option có thể chưa có recipe. Nếu có đúng một recipe hiện hành thì trả ID; nhiều recipe cùng hiệu lực là lỗi.
7. Tổng tiền đơn và mỗi dòng không vượt decimal(18,2).

Backend lấy giờ server một lần cho cả batch, chuẩn hóa theo SQL datetime, đọc món/giá/option/công thức theo batch trong transaction Serializable. Các truy vấn dùng cùng DbContext; không gọi gRPC riêng cho từng món. Giá sử dụng cùng quy tắc với API menu.

## Response thành công

Ví dụ Phở bò 50.000đ + Thêm bò 15.000đ, mỗi phần chọn thêm bò một lần, số lượng món là 2:

```json
{
  "statusCode": 200,
  "message": "Order selections validated successfully.",
  "isSuccess": true,
  "result": {
    "channel": "DineIn",
    "evaluatedAt": "2026-10-20T08:00:00+00:00",
    "totalAmount": 130000,
    "items": [
      {
        "lineIndex": 0,
        "itemId": "22222222-2222-2222-2222-222222222222",
        "nameSnapshot": "Phở bò",
        "priceId": "55555555-5555-5555-5555-555555555555",
        "recipeVersionId": "66666666-6666-6666-6666-666666666666",
        "quantity": 2,
        "unitPriceSnapshot": 50000,
        "optionsTotalPerUnit": 15000,
        "unitTotal": 65000,
        "lineTotal": 130000,
        "options": [
          {
            "optionId": "44444444-4444-4444-4444-444444444444",
            "nameSnapshot": "Thêm bò",
            "extraPriceSnapshot": 15000,
            "quantity": 1,
            "recipeVersionId": "77777777-7777-7777-7777-777777777777"
          }
        ]
      }
    ],
    "errors": []
  }
}
```

Công thức tính:

```text
optionsTotalPerUnit = SUM(extraPriceSnapshot × option.quantity)
unitTotal = unitPriceSnapshot + optionsTotalPerUnit
lineTotal = unitTotal × item.quantity
totalAmount = SUM(lineTotal)
```

Đây là tổng tiền món/option, chưa tính thuế, phí hoặc giảm giá. HTTP dùng số decimal; protobuf dùng chuỗi cho tiền.

## Response lỗi

Dữ liệu đầu vào sai trả 400. Món/giá/công thức/option không còn hợp lệ trả 409, kèm lỗi đầu tiên của từng dòng lỗi:

```json
{
  "statusCode": 409,
  "message": "Some selections are no longer orderable.",
  "isSuccess": false,
  "result": {
    "channel": "DineIn",
    "evaluatedAt": "2026-10-20T08:00:00+00:00",
    "totalAmount": null,
    "items": [],
    "errors": [
      {
        "lineIndex": 0,
        "itemId": "22222222-2222-2222-2222-222222222222",
        "optionId": null,
        "optionGroupId": null,
        "code": "RecipeUnavailable",
        "message": "The item must have exactly one active recipe."
      }
    ]
  }
}
```

Không trả snapshot hoặc tổng tiền của một phần đơn nếu bất kỳ dòng nào lỗi.

| Code | Ý nghĩa |
| --- | --- |
| `ItemUnavailable` | Món không tồn tại, ngừng bán hoặc category inactive |
| `ItemOutOfStock` | Món không ở trạng thái Available |
| `PriceUnavailable` | Thiếu/xung đột/giá không hợp lệ |
| `RecipeUnavailable` | Món không có đúng một recipe hiện hành |
| `OptionNotAllowed` | Option không thuộc nhóm đã gắn với món |
| `OptionUnavailable` | Option không Available hoặc phụ thu không hợp lệ |
| `OptionRecipeConflict` | Nhiều recipe option cùng hiệu lực |
| `SelectionLimit` | Không đạt min/max của nhóm, hoặc cấu hình giới hạn sai |
| `AmountOutOfRange` | Tổng tiền dòng vượt giới hạn |

401/403 khi không đủ quyền; 503 khi cấu hình giá không hợp lệ hoặc dịch vụ xác thực không khả dụng; 504 khi gRPC quá hạn.

## Dùng trong Ordering

- Gọi lại RPC khi gửi đơn, không tin response validate do frontend gửi lại.
- Lưu snapshot trả về vào OrderItem/OrderItemOption. `UnitPriceSnapshot` là giá món gốc; tránh cộng phụ thu hai lần.
- `recipeVersionId` của món khớp `OrderItem.RecipeVersionRef`. Model `OrderItemOption` hiện chưa có trường recipe version, cần bổ sung khi triển khai Ordering/BOM.
- `evaluatedAt` ghi nhận thời điểm đánh giá, không phải cam kết giữ giá. Transaction kết thúc trước khi Ordering lưu đơn; chưa có reservation/quote token xuyên service.
- Vẫn cần xác thực lại phiên bàn, idempotency và quy trình chờ waiter xác nhận khi triển khai Create Order.

## Test

`OrderValidationFlowTests`, `RecipeReadinessFlowTests` và các test Catalog hiện có chạy qua Gateway → gRPC → BLL → SQLite. Test bao phủ snapshot, giá hiện tại, quyền khách/waiter, nhóm option và số lượng, lỗi theo dòng, điều kiện công thức trước mở bán và biên thời gian versioning. Identity/GuestAccess là giả lập; khóa Serializable/deadlock cần kiểm thử trên SQL Server thật.
