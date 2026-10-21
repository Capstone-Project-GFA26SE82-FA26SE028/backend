# Gán món vào trạm bếp

Manager dùng PATCH /api/items/{itemId}/kitchen-station với bearer token.

Body mẫu:
{
  "kitchenStationId": "GUID-tram-bep",
  "version": "rowversion-base64-cua-mon"
}

Lấy version mới nhất từ GET /api/items/{itemId}. API đọc món trả thêm
kitchenStationId. Truyền kitchenStationId = null để bỏ gán riêng.
Version thiếu/sai định dạng trả 400; version cũ trả 409; role khác Manager bị chặn.

Catalog xác minh trạm qua Ordering GetById bằng token đang đăng nhập.
Trạm không tồn tại trả 404, trạm inactive trả 409, Ordering không liên lạc được
trả 503. Không thay đổi món nếu xác minh thất bại. Gán trạm không làm món tự bật bán.
Không tạo foreign key xuyên database; không thay đổi schema.

Ordering kiểm tra lại trạm khi waiter tạo đơn hoặc xác nhận đơn khách:
1. Trạm gán trên món.
2. Ordering:DefaultKitchenStationId nếu món chưa gán.
3. Trạm active duy nhất nếu cả hai cấu hình trên đều chưa có.
Nếu trạm đã gán không tồn tại/inactive, không tự chuyển sang bếp khác.
Thiếu trạm hoặc nhiều trạm không xác định được nơi nhận trả 409 với thông báo rõ.
Các tác vụ bếp cũ giữ nguyên trạm đã nhận.

Ví dụ: cơm gán HOT, nước gán BAR. Một đơn gồm cả hai món tạo hai KitchenTask
đúng trạm. Option đi cùng món chính. Nếu một trạm không dùng được, toàn bộ bước
tạo/xác nhận đơn thất bại; không tạo tác vụ một phần. Đơn khách vẫn chờ xác nhận.
Phân giải trạm khi xác nhận dùng cấu hình món hiện tại, giá vẫn giữ snapshot đơn.

TestUI Manager: Danh sách món → Gán trạm bếp → chọn trạm active → Lưu.
Tạo trạm trước bằng API KitchenStation/API Console với tài khoản Manager.
Nếu có nhiều bếp, nên gán rõ từng món.
Build/chạy lại Gateway, Catalog, Ordering rồi Ctrl+F5 TestUI.
Catalog có GrpcServices:Ordering cho local và Docker.

Kiểm thử tự động: quyền Manager, version cũ, gán/bỏ gán, lỗi trạm không làm đổi món;
đơn nhiều trạm cho cả waiter trực tiếp và xác nhận khách, rollback khi trạm tắt.
Chưa kiểm thử UI trên trình duyệt thật hoặc ghi dữ liệu vào database thật.