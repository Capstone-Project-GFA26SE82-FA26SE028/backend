# Giỏ hàng và gửi đơn Ordering

Dùng nguyên schema Ordering hiện có, không cần migration hoặc SQL nâng cấp.

## Luồng sử dụng

Khách quét QR, tham gia phiên bàn và nhận cookie theo [API guide](api-guide.md), đọc menu rồi tạo giỏ. Cập nhật giỏ phải gửi version mới nhất. Khi gửi đơn thành công, giỏ được làm trống trong cùng transaction; đơn chờ waiter xác nhận (`PendingConfirmation`).

Waiter xác nhận thì đơn thành `Confirmed`, món thành `Pending`, có KitchenTask và Event ban đầu. Waiter có thể tạo đơn trực tiếp từ danh sách món và đưa vào bếp ngay.

Gateway gọi Ordering bằng gRPC. Ordering xác minh phiên/quyền tại Restaurant và lấy giá/công thức tại Catalog. Client không được gửi giá, tên snapshot hoặc recipe ID. Món chưa có công thức đang hiệu lực không được bán.

## API

Đường dẫn gốc: `/api/table-sessions/{sessionId}`. Response dùng envelope `ResponseDTO` hiện có: `statusCode`, `message`, `isSuccess`, `result`.

| Method | Đường dẫn sau gốc | Hành vi |
|---|---|---|
| POST | `/carts` | Khách: tạo hoặc trả giỏ còn hạn của chính khách |
| GET | `/carts/{cartId}` | Khách: lấy giỏ và báo giá hiện tại |
| PUT | `/carts/{cartId}` | Khách: thay toàn bộ lựa chọn, kiểm tra version |
| DELETE | `/carts/{cartId}?version=...` | Khách: xóa giỏ theo version |
| POST | `/orders` | Khách gửi giỏ; waiter tạo trực tiếp; bắt buộc `Idempotency-Key` |
| GET | `/orders/{orderId}` | Khách xem đơn mình; staff xem đơn trong phiên |
| GET | `/orders?page=1&pageSize=20` | Cùng phạm vi quyền; pageSize tối đa100 |
| POST | `/orders/{orderId}/confirm` | Staff xác nhận đơn khách bằng version |

Khách dùng cookie HttpOnly `guest_access_{sessionId:N}` do Gateway cấp, fetch cần `credentials: "include"`. Staff dùng `Authorization: Bearer <access-token>`. Nếu có Authorization, Gateway chọn staff và không fallback sang cookie khách. Restaurant xác minh tài khoản còn hoạt động; `Ordering:StaffRoleIds` mặc định `waiter`. Catalog vẫn áp dụng quyền đọc menu đã cấu hình.

Tất cả API trên cần phiên còn mở và có bàn/area hoạt động. Khi phiên có nhiều bàn, POST order phải chỉ rõ `tableId` thuộc phiên. Cart chỉ dành cho khách.

## Ví dụ khách

UUID bên dưới minh họa, cần thay bằng ID có thật từ menu và phiên bàn.

Tạo giỏ: `POST /api/table-sessions/{sessionId}/carts`, không body. Lưu `cartId` và `version` trong response.

Cập nhật: `PUT /api/table-sessions/{sessionId}/carts/{cartId}`:

```json
{
  "version": "<version mới nhất của giỏ>",
  "items": [
    {
      "itemId": "11111111-1111-1111-1111-111111111111",
      "quantity": 2,
      "note": "Ít hành",
      "options": [
        {
          "optionId": "22222222-2222-2222-2222-222222222222",
          "quantity": 2
        }
      ]
    }
  ]
}
```

Giả sử món50.000đ, option thêm thịt5.000đ: quantity option tính trên **mỗi suất**, tổng dòng là `(50.000 + 2 × 5.000) × 2 = 120.000đ`. PUT trả giỏ, version mới và quote. Gửi `items: []` để làm trống giỏ; bỏ dòng/option khỏi danh sách PUT để xóa lựa chọn.

Gửi đơn: `POST /api/table-sessions/{sessionId}/orders`, header `Idempotency-Key: guest-submit-001`:

```json
{
  "cartId": "33333333-3333-3333-3333-333333333333",
  "cartVersion": "<version mới nhất của giỏ>"
}
```

Không gửi items cho đơn khách. Lần đầu trả201 với orderId, orderNumber, source `Guest`, status `PendingConfirmation`, version, snapshot và totalAmount. Chưa tạo KitchenTask. Giá được lấy lại lúc gửi đơn; quote của giỏ chưa giữ giá cố định.

Nếu timeout hoặc mất response, gửi lại **đúng body và đúng key**: trả200, cùng orderId, không tạo đơn mới và không cần giỏ cũ còn nội dung. Key cũ với body khác trả409. Đơn mới dùng key mới; không đổi key chỉ vì timeout.

## Waiter

Tạo trực tiếp `POST /api/table-sessions/{sessionId}/orders`, bearer token và `Idempotency-Key: waiter-submit-001`:

```json
{
  "items": [
    {
      "itemId": "11111111-1111-1111-1111-111111111111",
      "quantity": 2,
      "note": "Ít hành",
      "options": []
    }
  ]
}
```

Đơn có source `Staff`, status `Confirmed`, người tạo/xác nhận lấy từ Identity. KitchenTask và Event lưu nguyên tử với đơn.

Xác nhận đơn khách `POST /api/table-sessions/{sessionId}/orders/{orderId}/confirm`:

```json
{
  "version": "<version Base64 từ GET order>"
}
```

Xác nhận kiểm tra lại món/option còn bán; **giữ snapshot giá/tên/recipe khi khách gửi**. Version cũ trả409. Gọi lại trên đơn đã Confirmed với version hợp lệ về định dạng trả đơn hiện tại, không tạo thêm task.

Trạm bếp ưu tiên `Item.KitchenStationRef` từ Catalog, rồi `Ordering:DefaultKitchenStationId`; nếu chưa cấu hình và chỉ có một trạm active thì dùng trạm đó. Không xác định được trạm hoặc trạm inactive:503, không lưu đơn/task dở dang. Không tự tạo trạm trong database.

## Đồng thời, lỗi và giới hạn

- Giỏ dùng hash nội dung làm version, không thêm cột. Khi409 do version cũ, GET lại giỏ để đối chiếu trước khi gửi lại.
- Order dùng rowversion có sẵn. SQL Server dùng Serializable và UPDLOCK/HOLDLOCK để bảo vệ kiểm tra/tạo đơn, sửa giỏ, xác nhận; không thêm unique index. Thiếu index có thể làm phạm vi khóa lớn khi tải cao.
- 400: body/số lượng/version sai; 401: thiếu hoặc hết hạn xác thực; 403: sai quyền; 404: không tìm thấy hoặc không sở hữu; 409: dữ liệu thay đổi/phiên đóng/món không hợp lệ; 503/504: dependency hoặc trạm bếp chưa sẵn sàng/timeout.
- Tối đa100 dòng, quantity1–1000, tối đa100 option/dòng, note tối đa1000 ký tự. Catalog kiểm tra option thuộc món và min/max nhóm.
- Đọc giỏ vẫn trả quote lỗi khi món ngừng bán để giao diện hiển thị. Cập nhật/gửi giỏ không hợp lệ bị từ chối.
- Chưa giữ/trừ tồn kho. Restaurant và Ordering không có transaction phân tán; phiên có thể đóng ngay sau lần kiểm tra cuối.
- Schema chỉ lưu recipe version của món. Option lưu tên/giá/số lượng, không có recipe version; cần xử lý giới hạn này khi triển khai trừ kho option.
- Chưa gồm hủy món, điều khiển trạng thái KDS, realtime, thanh toán và hóa đơn.

## Cấu hình và kiểm thử

Gateway: `GrpcServices:Ordering` (local `https://localhost:5004`), `OrderingClient:TimeoutSeconds` mặc định30. Ordering: `GrpcServices:Restaurant`, `GrpcServices:Catalog`, `Ordering:DependencyTimeoutSeconds` mặc định5, tùy chọn `Ordering:DefaultKitchenStationId` là ID trạm active đã tồn tại.

`Tests/Ordering.Tests` kiểm thử business service với repository/transaction thật trên SQLite trong RAM, giả lập Restaurant/Catalog và rowversion. Bao gồm quyền sở hữu, giỏ cũ, thay thế lựa chọn, snapshot, gửi lặp, xác nhận lặp, rollback khi món không hợp lệ và chọn trạm bếp. Chưa kiểm chứng khóa đồng thời SQL Server hay toàn luồng HTTP/gRPC với các service chạy thật. Không chạy migration hoặc ghi DB thật trong các test này.

Chi tiết phân quyền và giới hạn phiên bản công thức: [Phân quyền backend](backend-permissions.md).
