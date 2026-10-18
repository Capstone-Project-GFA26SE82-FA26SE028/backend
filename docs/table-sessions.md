# Quét QR tự mở phiên bàn

Theo yêu cầu đã cập nhật: khách quét QR hợp lệ thì tự mở phiên nếu bàn chưa có phiên; không cần nhân viên mở trước và không chờ nhập SĐT/3 số. Frontend menu phải gọi API scan khi mở QR. Việc mở URL trong trình duyệt chưa tự gọi backend nếu frontend chưa tích hợp.

## API

`POST /api/table-sessions/scan` qua Gateway, không yêu cầu Bearer token:

```json
{ "token": "TOKEN_TRONG_FRAGMENT_QR" }
```

Thành công trả 200 và envelope ResponseDTO. Result mẫu:

```json
{
  "tableSessionId": "55555555-5555-5555-5555-555555555555",
  "tableId": "33333333-3333-3333-3333-333333333333",
  "tableCode": "B01",
  "areaId": "22222222-2222-2222-2222-222222222222",
  "restaurantId": "11111111-1111-1111-1111-111111111111",
  "status": "Open",
  "openedAt": "2026-10-18T02:00:00Z",
  "isNewSession": true
}
```

- QR sai/thu hồi, bàn hoặc khu vực ngừng hoạt động: 404, không tạo phiên.
- Chưa có phiên mở gắn với bàn: tạo TableSession và SessionTable trong cùng transaction.
- Đã có phiên chưa đóng và liên kết chưa ReleasedAt: trả lại phiên đó, isNewSession=false. Không thay mã tham gia, người nhận điểm hoặc thời điểm mở.
- Bỏ qua liên kết đã ReleasedAt và phiên đã ClosedAt. Dữ liệu có nhiều phiên chưa đóng cùng gắn với bàn: 409 để nhân viên kiểm tra, không tự chọn một phiên.
- PartySize khởi tạo 1 cho lượt quét đầu, chưa phải số khách thực tế đã xác nhận; quét lặp không tăng số khách. OpenedByRef/ChangedByRef để null vì không do nhân viên thực hiện.
- Chưa đặt mã tham gia, chưa tạo GuestAccess/cookie, chưa liên kết thành viên. SessionId trả về không phải bằng chứng có quyền gọi món.
- Giữ nguyên POST /api/qr/resolve để chỉ xác minh QR, không tạo phiên. Luồng quét QR trên frontend phải chuyển sang scan để có hành vi tự mở phiên.

## Cấu trúc và đồng thời

Gateway TableSessionController → TableSessionGrpcService → ITableSessionService/TableSessionService → ITableSessionRepository/TableSessionRepository. DTO Gateway và Common riêng; repository kế thừa generic. Dùng generic repository của SessionTable để thêm liên kết, không lặp Add/Delete.

Trên SQL Server, transaction Serializable lấy khóa UPDLOCK/HOLDLOCK trên bản ghi bàn trước khi tìm/tạo phiên. Các lượt scan cùng bàn chờ nhau, sau commit sẽ đọc phiên đã tạo. QR được xác minh lại bên trong transaction để không dùng trạng thái cũ. Deadlock/concurrency trả 409 theo cơ chế hiện có; client có thể quét lại. Các luồng mở/chuyển bàn bổ sung sau này phải tuân thủ cùng cơ chế khóa và kiểm tra phiên.

Không đổi schema/model. Tests dùng SQLite xác minh API, quét lặp, rollback, dữ liệu sai/thu hồi/ngừng hoạt động, liên kết đã rời bàn và nhiều phiên xung đột. Chưa kiểm thử tranh chấp khóa thực tế trên SQL Server; SQLite không mô phỏng UPDLOCK.

Chưa có API đóng phiên trong đợt này. Phiên mở từ scan tồn tại cho tới khi được đóng bởi luồng phục vụ/thanh toán triển khai sau; chưa tự hết hạn khi khách quét rồi rời đi. API quản lý bàn hiện có sẽ chặn sửa/chuyển/ngừng bàn có phiên chưa đóng.

## Tham gia phiên và cookie khách

Hai API mới qua Gateway (không cần JWT):

- `POST /api/table-sessions/{sessionId}/guest-access/join`
- `GET /api/table-sessions/{sessionId}/guest-access/me`

Sau `scan`, lấy `tableSessionId` để gọi `join`. Khách đầu gửi:

```json
{ "phoneOrCode": "0912345789" }
```

Hoặc chỉ gửi `{"phoneOrCode":"789"}`. SĐT hiện nhận 10 chữ số bắt đầu bằng 0; dùng 3 số cuối làm mã. Khi đã đặt mã, thiết bị tiếp theo chỉ nhập đúng 3 chữ số. Mã lưu dạng PBKDF2 có salt, chỉ thiết lập một lần trong transaction có khóa phiên SQL Server. Không lưu SĐT hoặc liên kết tích điểm trong bước này: Customer service chưa có API tích hợp. Không thay đổi LoyaltyCustomerRef.

Kết quả `data` của join/me:

```json
{
  "tableSessionId": "55555555-5555-5555-5555-555555555555",
  "expiresAt": "2026-10-18T14:00:00Z"
}
```

Join thành công trả thêm header Set-Cookie. Token không xuất hiện trong JSON, database chỉ lưu hash SHA256. Cookie HttpOnly, SameSite=Lax, Path=/api, Secure khi dùng HTTPS; local HTTP vẫn dùng được. Cookie riêng theo session để các bàn không ghi đè nhau. Frontend gọi fetch với `credentials: "include"` (Axios: `withCredentials: true`), gọi `me` khi quay lại trang; 401 thì hiện ô nhập mã. Cookie Lax phù hợp local localhost hoặc frontend/Gateway cùng site; triển khai khác site cần cấu hình cookie và bảo vệ CSRF phù hợp trước khi dùng.

Sai mã trả 403, cho nhập lại, không giới hạn lượt thử hay khóa theo yêu cầu. Dữ liệu sai định dạng trả 400. Phiên không tồn tại trả 404; join phiên đã đóng/không Open trả 409. `me` trả 401 nếu thiếu/sai cookie, token bị thu hồi/hết hạn, khác phiên hoặc phiên đóng. Kiểm tra trạng thái phiên khiến cookie mất quyền ngay cả khi bản ghi GuestAccess chưa có RevokedAt. Không gia hạn tự động khi gọi me.

### Đổi thời hạn

Trong `Services/restaurant_service/Restaurant.API/appsettings.json`:

```json
"GuestAccess": {
  "LifetimeHours": 12
}
```

Có thể đổi thành 2, 24, 0.5... (lớn hơn 0, tối đa 8760 giờ), hoặc dùng biến môi trường `GuestAccess__LifetimeHours`. Khởi động lại Restaurant.API sau khi đổi. Thời hạn mới áp dụng cho lần join tiếp theo; cookie đã cấp giữ thời hạn cũ. Gateway lấy đúng thời điểm hết hạn từ Restaurant, không cần cấu hình trùng ở Gateway. Phiên đóng vẫn mất quyền ngay dù chưa tới thời hạn.

Luồng gọi món chưa triển khai; khi bổ sung phải gọi GuestAccessGrpc.GetMe để xác minh token và phiên, không tin riêng tableSessionId gửi từ client. Chưa có API đóng phiên hoặc liên kết tích điểm. Test tích hợp dùng SQLite kiểm tra join, mã sai thử lại, cookie, cấu hình thời hạn, thu hồi/hết hạn/đóng phiên; khóa đồng thời SQL Server chưa kiểm thử với database thật.