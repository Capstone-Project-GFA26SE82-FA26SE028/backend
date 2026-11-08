# UC_ADM_10 — QR bàn

Đã triển khai Gateway → QrGrpcService → QrService → QrRepository trên model Qr hiện có. Schema bổ sung Qr.ImageUrl để lưu URL Cloudinary. QR chứa đường dẫn menu, token ngẫu nhiên và mã bàn.

## Cấu hình địa chỉ mở menu

Restaurant.API đọc Qr:MenuBaseUrl; mặc định Development/Docker là http://localhost:5173/menu. Docker có thể đặt QR_MENU_BASE_URL trong .env. Khi quét bằng điện thoại, phải đổi localhost thành địa chỉ frontend mà điện thoại truy cập được (IP LAN hoặc domain). URL cấu hình không chứa query, fragment hoặc user/password.

Ví dụ nội dung QR:

```text
http://localhost:5173/menu#qr=TOKEN_NGAU_NHIEN&table=B01
```

table là mã/số bàn dùng làm gợi ý hiển thị; không phải bằng chứng truy cập. Frontend phải lấy bàn chính xác từ API resolve, không tin giá trị table bị sửa trên URL. Nếu mã bàn đổi, resolve trả mã hiện tại.

## API

| Method | Route | Quyền / hành vi |
| --- | --- | --- |
| GET | /api/qr | Admin: danh sách lịch sử QR, có imageUrl nếu có ảnh lưu; không trả token/hash/bytes ảnh |
| GET | /api/qr/{qrId}/image | Admin: trả file image/png để xem/in lại, Cache-Control: no-store |
| POST | /api/qr/tables/{tableId} | Admin: tạo lần đầu, trả 201; nếu đã có QR chưa thu hồi thì 409 |
| POST | /api/qr/tables/{tableId}/regenerate | Admin: tạo lại và thu hồi mã cũ trong cùng transaction |
| POST | /api/qr/resolve | Public: xác minh token, trả tableId, tableCode, areaId, restaurantId |

GET hỗ trợ page, pageSize, search (mã bàn), areaId, restaurantId, isActive. isActive=true nghĩa là QR chưa bị thu hồi; isUsable trong kết quả còn kiểm tra bàn/khu vực có hoạt động không.

Body tạo lại:

```json
{
  "expectedQrId": "ID_QR_HIEN_TAI"
}
```

ExpectedQrId ngăn request tạo lại dựa trên dữ liệu cũ vô hiệu hóa nhầm QR mới. Nó phải thuộc bàn đang thao tác và chưa bị thu hồi. Trường hợp có nhiều mã chưa thu hồi từ dữ liệu cũ, thao tác tạo lại thu hồi tất cả mã đó.

Tạo/tạo lại trả envelope ResponseDTO với result:

- qr: metadata gồm qrId, tableId, tableCode, areaId, createdAt, revokedAt, isUsable, imageUrl.
- menuUrl: đường dẫn hoàn chỉnh để mã hóa QR.
- token: chỉ trả lúc tạo/tạo lại.
- png: ảnh PNG dạng Base64 trong JSON; frontend hiển thị bằng data:image/png;base64,... hoặc tải thành file PNG để in.

Ảnh được tạo bằng [QRCoder](https://github.com/Shane32/QRCoder), dùng PngByteQRCode để chạy được trong Linux container mà không phụ thuộc System.Drawing.

Database chỉ lưu SHA-256 của token; token ngẫu nhiên 32 byte được biểu diễn bằng 64 ký tự hex. Ảnh PNG được lưu riêng theo qrId trước khi commit database. Nếu mất response sau commit, GET danh sách và tải ảnh theo imageUrl; không cần tạo lại QR. Không trả lại token/menuUrl từ danh sách.

## Lưu ảnh và in lại

Ảnh QR được upload lên Cloudinary trước khi commit database; URL HTTPS được lưu trong Qr.ImageUrl. Cấu hình Cloudinary, script nâng cấp database và cách xử lý QR cũ xem [cloudinary.md](cloudinary.md).

Frontend gán trực tiếp qr.imageUrl vào src của img, không ghép thêm địa chỉ Gateway và không gửi bearer token tới Cloudinary. URL ảnh là public. Route GET /api/qr/{qrId}/image vẫn yêu cầu Admin và tải ảnh từ Cloudinary để trả PNG với Cache-Control: no-store.

QR cũ chưa có URL Cloudinary trả imageUrl null và GET image trả 404. Token cũ vẫn resolve được; regenerate để tạo ảnh mới và thay QR đã in vì token sẽ đổi. Nếu xóa ảnh trực tiếp trên Cloudinary, URL trong database vẫn giữ nguyên và tải ảnh trả 404.

Upload thất bại rollback thao tác tạo/tạo lại, giữ nguyên QR cũ. Cloudinary và SQL không có transaction chung: commit thất bại sau upload có thể để lại ảnh thừa, cần đối chiếu database khi dọn. QR bị thu hồi vẫn có thể xem ảnh nhưng token không còn sử dụng được.

## Frontend khi khách quét QR

1. Đọc qr từ URL fragment (phần sau #).
2. Gọi POST /api/qr/resolve với body:

```json
{
  "token": "TOKEN_LAY_TU_QR"
}
```

3. Nếu thành công, hiển thị tableCode do backend trả về, ví dụ “Bàn B01”. Có thể dùng history.replaceState để bỏ token khỏi thanh địa chỉ sau khi đọc; tránh lưu vào analytics/log.
4. Nếu 404, báo QR không còn hợp lệ; không dùng thông tin bàn từ URL để tiếp tục.

API resolve chỉ xác minh QR và nhận diện bàn. Đã có API POST /api/table-sessions/scan để tự mở hoặc lấy lại phiên ngay khi quét; frontend dùng scan cho luồng này, xem [table-sessions.md](table-sessions.md). Chưa triển khai giao diện /menu, tham gia phiên, cookie khách hoặc gọi món UC_CUS_02. Resolve không tự tạo phiên/GuestAccess và không cấp quyền đặt món. Luồng tham gia/gọi món sau này phải kiểm tra lại QR, phiên và quyền truy cập.

## Quy tắc và kiểm tra

- Chỉ Admin có phiên hợp lệ qua Identity được tạo, tạo lại, xem danh sách.
- Bàn phải tồn tại, đang hoạt động, đã thuộc khu vực đang hoạt động.
- QR cũ không resolve được sau khi tạo lại. QR còn hiệu lực cũng không resolve được khi bàn/khu vực bị tắt.
- Khi bật lại bàn/khu vực, QR chưa thu hồi dùng lại được; QR đã thu hồi không dùng lại.
- Ghi QR mới và thu hồi mã cũ cùng transaction Serializable. Chưa có kiểm chứng cạnh tranh thật trên SQL Server; test rollback dùng SQLite.
- Response đặt Cache-Control: no-store. Không ghi token plaintext xuống model/schema hoặc trả TokenHash qua API.
- Audit Log liên service chưa được bổ sung trong đợt này.

Test chạy qua HTTP Gateway và gRPC bằng TestServer: phân quyền, ảnh PNG, thông tin bàn, hash, mã trùng, tạo lại, request cũ, bàn/khu vực tắt, token sai và rollback khi ghi thất bại. Test PNG kiểm tra header, chưa có kiểm thử giải mã độc lập bằng camera/decoder.

```powershell
dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj
```
