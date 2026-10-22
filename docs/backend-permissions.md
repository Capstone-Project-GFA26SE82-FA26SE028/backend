# Phân quyền backend

Quyền được kiểm tra trong service/gRPC, không dựa vào việc ẩn nút trên giao diện. Cấu hình mặc định:

| Vai trò | Quyền đã triển khai |
|---|---|
| admin | Tài khoản, role, phiên đăng nhập, audit, nhà hàng, khu vực, bàn, QR |
| manager | Quản lý Catalog: danh mục, món, giá, option, công thức, trạng thái bán; đọc menu |
| waiter | Đọc menu, đọc bàn/phiên phục vụ, tạo đơn trực tiếp, đọc đơn trong phiên và xác nhận đơn khách |
| kitchen | Công thức hiện hành; báo món OutOfStock |
| khách qua GuestAccess | Đọc menu, giỏ và đơn của chính khách trong phiên còn mở |

Admin không tự kế thừa quyền Manager hoặc Waiter. Manager không tự có quyền xác nhận đơn. Các ID role phân biệt hoa/thường; `staff` không thay thế `waiter`, `chef` không thay thế `kitchen`. Nếu triển khai có override các RoleIds trong biến môi trường/config, phải giữ đúng bảng quyền này.

## Công thức

- Kitchen đọc `/api/items/{id}/recipes/active`, `/api/options/{id}/recipes/active` không có `at`.
- Kitchen đọc `/api/recipes/{versionId}` chỉ khi phiên bản đang hiệu lực theo giờ server; phiên bản cũ/tương lai trả403.
- Truy vấn thời điểm `at`, lịch sử, tạo công thức dành cho Manager. Quy tắc áp dụng cả công thức món và option.
- Manager bật bán/ngừng kinh doanh/báo hết; Kitchen chỉ báo OutOfStock, không được tự mở bán lại.

## Waiter tìm bàn và đơn chờ

```http
GET /api/tables/service?restaurantId={restaurantId}&areaId={areaId}&page=1&pageSize=20
Authorization: Bearer <waiter-token>
```

Response `result` mẫu:

```json
{
  "items": [
    {
      "tableId": "11111111-1111-1111-1111-111111111111",
      "tableCode": "T1",
      "areaId": "22222222-2222-2222-2222-222222222222",
      "areaName": "Tầng 1",
      "restaurantId": "33333333-3333-3333-3333-333333333333",
      "cleaningStatus": "Clean",
      "openSessionIds": ["44444444-4444-4444-4444-444444444444"]
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

Chỉ trả bàn và khu vực active; danh sách phiên loại các liên kết bàn đã ReleasedAt và phiên đã đóng. `openSessionIds: []` nghĩa là chưa có phiên đang mở gắn với bàn. `cleaningStatus` là giá trị có sẵn từ DB; không giả lập trạng thái chờ trả món.

Dùng sessionId để gọi `GET /api/table-sessions/{sessionId}/orders`, chọn đơn PendingConfirmation, rồi `POST /api/table-sessions/{sessionId}/orders/{orderId}/confirm` với version hiện tại. API quản trị `/api/tables` vẫn chỉ Admin, Waiter không được sửa/xóa bàn.

Đây là quyền theo chức năng. Schema hiện chưa cung cấp phân công Waiter theo chi nhánh/khu vực cho luồng này; restaurantId/areaId là bộ lọc, không phải ràng buộc quyền chi nhánh. Chưa có realtime hoặc trạng thái Served/KDS mới trong thay đổi này. Không thay schema DB.
