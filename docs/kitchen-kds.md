# Hệ Thống Điều Phối Bếp (KDS - Kitchen Display System)

Tài liệu hướng dẫn nghiệp vụ và đặc tả kỹ thuật cho phân hệ Bếp/Bar, đáp ứng các yêu cầu từ **UC_KIT_01 đến UC_KIT_13** và **UC_WAI_05**.

---

## 1. Phân quyền và Vai trò (Authorization)

- **Kitchen Staff (`kitchen`)**, **Manager (`manager`)**, **Admin (`admin`)**:
  - Truy cập danh sách nhiệm vụ bếp (`KitchenTask`) theo trạm hoặc toàn bộ.
  - Cập nhật trạng thái chế biến (`Pending` -> `Cooking` -> `Ready`).
  - Điều chỉnh mức ưu tiên của ticket bếp (`Priority`).
  - Xem dashboard thống kê tổng quan (Pending, Cooking, Ready, thời gian TB).
  - Báo hết món nhanh (`PATCH /api/items/{id}/status` sang `OutOfStock`).
  - Tra cứu công thức định lượng món (`GET /api/items/{id}/recipes`).
- **Waiter (`waiter`)**:
  - Xem trạng thái món ăn trong đơn hàng của bàn.
  - Xác nhận đã lên món (`POST /api/table-sessions/{sessionId}/orders/{id}/items/{orderItemId}/serve` -> `Served`).

---

## 2. Danh mục API KDS Bếp

Tất cả các API yêu cầu Bearer Token qua ApiGateway (`http://localhost:5000`):

| Method | Endpoint | Mô tả nghiệp vụ | UC liên quan |
|---|---|---|---|
| `GET` | `/api/kitchen-tasks` | Lấy danh sách nhiệm vụ bếp (hỗ trợ phân trang, lọc theo `kitchenStationId`, `status`, `orderId`, `tableSessionId`) | UC_KIT_03, UC_KIT_04 |
| `GET` | `/api/kitchen-tasks/summary` | Thống kê số lượng theo trạng thái (Pending, Cooking, Ready, AvgCookingMinutes) | UC_KIT_02 |
| `GET` | `/api/kitchen-tasks/{id}` | Chi tiết nhiệm vụ bếp kèm topping/options và ghi chú | UC_KIT_04 |
| `PATCH` | `/api/kitchen-tasks/{id}/status` | Chuyển trạng thái (`Cooking`, `Ready`, `Cancelled`) | UC_KIT_06, UC_KIT_11 |
| `PATCH` | `/api/kitchen-tasks/{id}/priority` | Tăng/giảm mức độ ưu tiên xử lý ticket | UC_KIT_05 |
| `POST` | `/api/table-sessions/{sessionId}/orders/{id}/items/{orderItemId}/serve` | Waiter xác nhận mang món ra bàn (`Ready` -> `Served`) | UC_WAI_05 |
| `PATCH` | `/api/items/{id}/status` | Báo tạm hết món (`OutOfStock`) khóa đặt món trên QR | UC_KIT_07 |
| `GET` | `/api/items/{id}/recipes` | Tra cứu công thức & định lượng BOM chuẩn | UC_KIT_09 |

---

## 3. Vòng đời món ăn (Item & Task Lifecycle)

```
[Khách / Waiter gọi món]
          │
          ▼
    PendingConfirmation (nếu khách gửi qua QR)
          │  (Waiter bấm "Xác nhận chuyển bếp")
          ▼
       Pending (Hàng đợi bếp - KitchenTask được tạo)
          │
          │  (Bếp bấm "Nấu món" - CookingAt được ghi nhận)
          ▼
       Cooking (Đang chế biến trên KDS)
          │
          │  (Bếp bấm "Xong món" - ReadyAt được ghi nhận, bắn chuông báo)
          ▼
        Ready (Đã xong - Chờ nhân viên phục vụ bưng ra)
          │
          │  (Waiter bấm "Lên món" trên POS)
          ▼
       Served (Hoàn tất phục vụ)
```

- Khi tất cả các món trong một Order chuyển sang `Ready`, `Order.Status` tự động chuyển sang `Ready`.
- Khi tất cả các món được phục vụ xong (`Served`), `Order.Status` chuyển sang `Served`.
- Mọi lần chuyển trạng thái đều được lưu vào bảng `Event` ghi nhận `FromStatus`, `ToStatus`, `OccurredAt`, và `ActorUserRef`.

---

## 4. Hướng dẫn trải nghiệm trên TestUI (`kitchen.html`)

1. Mở trình duyệt tại `http://localhost:3300/kitchen.html` (hoặc mở từ `index.html`).
2. Nếu chưa đăng nhập, bấm nút **"Đăng nhập Kitchen ngay"** (tài khoản mẫu: `kitchen@gmail.com` / `123`).
3. Chọn trạm bếp làm việc (ví dụ: `Tất cả trạm`, `Bếp nóng (HOT)`, `Quầy bar (BAR)`).
4. Quan sát các cột Kanban:
   - **Cột 1 (Chờ chế biến):** Danh sách món đang đợi. Thẻ món cảnh báo đổi màu theo thời gian chờ (xanh < 10p, vàng 10-20p, đỏ nhấp nháy > 20p). Bấm **"Nấu món"** để bắt đầu.
   - **Cột 2 (Đang chế biến):** Đồng hồ đếm thời gian thực đã nấu. Bấm **"Xong món (Ready)"** khi làm xong.
   - **Cột 3 (Đã xong):** Danh sách các món đã sẵn sàng để Waiter mang ra bàn.
5. Mở song song `waiter.html`, chọn bàn tương ứng để thấy nút **"Lên món"** màu xanh xuất hiện ngay khi Bếp bấm hoàn thành.

