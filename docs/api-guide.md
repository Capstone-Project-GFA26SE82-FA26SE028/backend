# Hướng dẫn toàn bộ HTTP API hiện tại

Đối chiếu mã nguồn Gateway và service ngày 16/10/2026. Các JSON dưới đây là dữ liệu minh họa, không phải dữ liệu có sẵn trong database. Thay ID, roleId, token và version bằng giá trị thật nhận từ API. Tài liệu mô tả API đang có, không phải toàn bộ chức năng dự kiến trong yêu cầu nghiệp vụ.

## 1. Cách gọi và đọc phản hồi

Frontend/Postman gọi Gateway; Gateway gọi gRPC đến Identity hoặc Restaurant. Cần chạy các service tương ứng và cấu hình database.

- Local profile HTTPS: `https://localhost:5000`; Swagger: `https://localhost:5000/swagger`.
- Local profile HTTP: `http://localhost:15000`.
- Docker: `http://localhost:5000` theo port mapping hiện tại.
- Body JSON: header `Content-Type: application/json`.
- API có xác thực: header `Authorization: Bearer <accessToken>`.
- Trong Swagger, Authorize bằng access token nhận sau đăng nhập.
- `Admin`: tài khoản active có roleId thuộc cấu hình `UserManagement:AdminRoleIds`, token và phiên còn hiệu lực.
- `Đăng nhập`: tài khoản bất kỳ có token và phiên hợp lệ; không nhất thiết Admin.
- `Công khai`: không yêu cầu Bearer token.

Phản hồi nghiệp vụ thông thường:

```json
{
  "statusCode": 200,
  "message": "Thành công.",
  "isSuccess": true,
  "result": {}
}
```

Trong các ví dụ phản hồi tiếp theo, phần JSON chỉ là **result**, trừ khi ghi rõ khác. Message có thể khác theo thao tác. Tạo dữ liệu thường trả 201; đọc/sửa/xóa thành công thường trả 200. Xóa/đăng xuất/đổi mật khẩu có thể trả result null.

Danh sách phân trang dùng cấu trúc:

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 20
}
```

Thay `items` bằng các object mẫu của nhóm tương ứng. `page` mặc định 1, `pageSize` mặc định 20; giới hạn hợp lệ 1–100. Riêng danh sách role trả mảng trực tiếp, không phân trang.

Lỗi nghiệp vụ thường dùng cùng envelope, `isSuccess: false`, `result: null`. Lỗi validation tự động của ASP.NET (thiếu trường Required, sai kiểu JSON/query) có thể dùng ProblemDetails với `errors`, không cùng envelope. Ping và health cũng không dùng envelope trên.

| HTTP | Ý nghĩa |
| --- | --- |
| 400 | Dữ liệu thiếu/sai hoặc phân trang không hợp lệ |
| 401 | Chưa đăng nhập, token/phiên hết hạn hoặc bị thu hồi |
| 403 | Đã xác thực nhưng thiếu quyền |
| 404 | Không tìm thấy dữ liệu hoặc QR không dùng được |
| 409 | Trùng dữ liệu, version cũ hoặc thao tác bị quy tắc nghiệp vụ chặn |
| 502 | Lỗi xử lý phản hồi/dịch vụ phía sau |
| 503 | Dịch vụ phụ thuộc không khả dụng |
| 504 | Dịch vụ phụ thuộc phản hồi quá hạn |

## 2. Đăng nhập và xác thực — /api/auth

| Method và route | Quyền | Công dụng và đầu vào |
| --- | --- | --- |
| POST /api/auth/login | Công khai | Đăng nhập bằng email hoặc username, mẫu A |
| POST /api/auth/refresh | Công khai, cần refresh token hợp lệ | Lấy cặp token mới, mẫu B |
| POST /api/auth/logout | Đăng nhập | Thu hồi phiên hiện tại, không body |
| POST /api/auth/change-password | Đăng nhập | Đổi mật khẩu bản thân, mẫu C |

**A — Login:** tên trường vẫn là `email` nhưng giá trị có thể là email hoặc username.

```json
{
  "email": "admin@example.com",
  "password": "DemoPass123!"
}
```

Ví dụ đăng nhập bằng username: `{"email":"admin","password":"DemoPass123!"}`. Đây không phải tài khoản mặc định được tạo sẵn.

Result login/refresh:

```json
{
  "user": {
    "userId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "roleId": "Admin",
    "userName": "admin",
    "email": "admin@example.com",
    "phoneNumber": "0901234567",
    "fullName": "Nguyễn Văn An",
    "avatarUrl": null,
    "isActive": true,
    "createdAt": "2026-10-16T01:00:00Z",
    "updatedAt": null
  },
  "accessToken": "<ACCESS_TOKEN>",
  "refreshToken": "<REFRESH_TOKEN>",
  "sessionId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "expiresAt": "2026-10-16T02:15:00Z",
  "sessionExpiresAt": "2026-10-17T02:00:00Z"
}
```

Thời điểm hết hạn trong mẫu chỉ minh họa; dùng giá trị thực API trả về. Hiện đăng nhập mới sẽ thu hồi các phiên cũ của cùng tài khoản.

**B — Refresh:**

```json
{ "refreshToken": "<REFRESH_TOKEN_VUA_NHAN>" }
```

Refresh token được xoay vòng: sau thành công phải lưu token mới; không dùng lại refresh token cũ. Access token dùng để gọi API, refresh token dùng riêng cho endpoint refresh.

**C — Đổi mật khẩu:**

```json
{
  "currentPassword": "DemoPass123!",
  "newPassword": "NewDemoPass456!",
  "confirmPassword": "NewDemoPass456!"
}
```

Mật khẩu mới tối thiểu 8 ký tự, tối đa 72 byte UTF-8, khác mật khẩu cũ, khớp xác nhận. Thành công phải đăng nhập lại.

## 3. Tài khoản — /api/users

| Method và route | Quyền | Công dụng/dữ liệu mẫu |
| --- | --- | --- |
| GET /api/users/me | Đăng nhập | Hồ sơ bản thân; không body |
| PUT /api/users/me | Đăng nhập | Sửa hồ sơ bản thân, mẫu A |
| GET /api/users?page=1&pageSize=20&search=an&roleId=Waiter&isActive=true | Admin | Tìm/lọc nhân viên; mọi bộ lọc đều tùy chọn |
| GET /api/users/{id} | Admin | Chi tiết tài khoản theo userId |
| POST /api/users | Admin | Tạo tài khoản, mẫu B |
| PUT /api/users/{id} | Admin | Sửa thông tin nhân viên, mẫu C |
| PATCH /api/users/{id}/status | Admin | Khóa/mở tài khoản, mẫu D |
| PATCH /api/users/{id}/role | Admin | Đổi vai trò, mẫu E |

**A — Cập nhật bản thân:**

```json
{
  "fullName": "Nguyễn Văn An",
  "phoneNumber": "0901234567",
  "avatarUrl": "https://example.com/avatars/an.png"
}
```

Endpoint này không đổi username, email, mật khẩu hoặc vai trò.

**B — Tạo nhân viên:**

```json
{
  "userName": "waiter01",
  "email": "waiter01@example.com",
  "password": "DemoPass123!",
  "fullName": "Trần Văn Bình",
  "phoneNumber": "0901111222",
  "roleId": "Waiter",
  "avatarUrl": null
}
```

Lấy roleId thực tế từ `GET /api/roles`; không mặc định database của bạn có mã `Waiter`. Username/email phải phù hợp ràng buộc duy nhất. Mật khẩu tuân theo giới hạn 8 ký tự/72 byte UTF-8.

**C — Admin sửa nhân viên:**

```json
{
  "userName": "waiter01",
  "email": "binh@example.com",
  "fullName": "Trần Văn Bình",
  "phoneNumber": "0902222333",
  "avatarUrl": null
}
```

Không dùng body này để đổi role hoặc mật khẩu; các thao tác đó có endpoint riêng.

**D — Khóa:** `{"isActive":false}`. Mở lại: `{"isActive":true}`.

**E — Đổi vai trò:** `{"roleId":"Waiter"}`. Chỉ cần roleId; roleName/description không cần gửi.

Result cho lấy chi tiết/tạo/sửa là object `user` trong mẫu đăng nhập (không có token). Danh sách dùng phân trang với `items` là các user. Không có API DELETE tài khoản hiện tại; dùng status để ngừng hoạt động. Thay đổi quyền/trạng thái có cơ chế thu hồi phiên; không được loại bỏ Admin hoạt động cuối cùng.

## 4. Vai trò — /api/roles

| Method và route | Quyền | Kết quả |
| --- | --- | --- |
| GET /api/roles?search=wait | Admin | Mảng vai trò; search tùy chọn |
| GET /api/roles/{id} | Admin | Một vai trò; id là chuỗi roleId, không phải GUID |
| POST /api/roles | Admin | Route cũ, hiện bị chặn 409 |
| PUT /api/roles/{id} | Admin | Route cũ, hiện bị chặn 409 |
| DELETE /api/roles/{id} | Admin | Route cũ, hiện bị chặn 409 |

Mẫu result GET danh sách:

```json
[
  {
    "roleId": "Waiter",
    "roleName": "Nhân viên phục vụ",
    "description": "Phục vụ tại bàn"
  }
]
```

GET chi tiết trả một object như phần tử trên. Hệ thống đang dùng danh mục role cố định. Ba route ghi vẫn tồn tại để giữ tương thích nhưng ẩn khỏi Swagger; không dùng chúng để tạo/sửa/xóa role. Nếu kiểm tra route cũ: POST body `{"roleId":"Demo","roleName":"Demo","description":null}`; PUT body `{"roleName":"Demo","description":null}`; DELETE không body. Với đầu vào hợp lệ và Admin, trả 409; người không phải Admin bị từ chối quyền.

## 5. Phiên đăng nhập — /api/sessions

Đây là phiên đăng nhập nhân viên, khác phiên khách ngồi bàn/gọi món.

| Method và route | Quyền | Công dụng |
| --- | --- | --- |
| GET /api/sessions | Admin | Danh sách phiên toàn hệ thống, có phân trang/lọc |
| GET /api/sessions/me?page=1&pageSize=20 | Đăng nhập | Các phiên của bản thân |
| DELETE /api/sessions/me/{id} | Đăng nhập | Thu hồi phiên của mình theo userSessionId; không body |
| DELETE /api/sessions/me | Đăng nhập | Thu hồi mọi phiên của mình, gồm phiên hiện tại; không body |

Mẫu GET cho Admin (viết trên một dòng khi gọi):

```text
/api/sessions?page=1&pageSize=20&search=waiter&roleId=Waiter&status=active&deviceType=Desktop&fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-31T23:59:59Z
```

Có thể thêm `userId=<GUID>`. Các bộ lọc đều tùy chọn. Search tìm username/email; thời gian lọc theo LoggedInAt. Status gồm `active`, `expired`, `revoked`. DeviceType lọc theo giá trị lưu trong dữ liệu. API `/me` chỉ nhận phân trang, không cung cấp toàn bộ bộ lọc của Admin.

Mẫu một phần tử items:

```json
{
  "userName": "waiter01",
  "email": "waiter01@example.com",
  "roleId": "Waiter",
  "userSessionId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "userId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "ipAddress": "127.0.0.1",
  "userAgent": "Mozilla/5.0",
  "deviceType": "Desktop",
  "loggedInAt": "2026-10-16T02:00:00Z",
  "expiresAt": "2026-10-17T02:00:00Z",
  "revokedAt": null,
  "isCurrent": true
}
```

`isCurrent` cho biết đây có phải phiên đang gọi API. DELETE là thu hồi phiên, không phải xóa lịch sử khỏi database. Chưa có endpoint Admin thu hồi tùy ý phiên của người khác trong controller này.

## 6. Lịch sử thao tác — /api/audit-logs

`GET /api/audit-logs?page=1&pageSize=20&userId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` — Admin. Bỏ userId để xem danh sách không lọc tài khoản. Không body. Trả phân trang.

Mẫu một phần tử items (giá trị action chỉ minh họa):

```json
{
  "auditLogId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "userId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "action": "UPDATE",
  "entityName": "User",
  "entityId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "oldValue": "{\"FullName\":\"Tên cũ\"}",
  "newValue": "{\"FullName\":\"Tên mới\"}",
  "ipAddress": "127.0.0.1",
  "createdAt": "2026-10-16T03:00:00Z"
}
```

oldValue/newValue hiện là chuỗi, có thể chứa JSON hoặc null; không mặc định là object JSON. API này đọc audit của Identity; chưa phải audit tập trung cho mọi service. Không có API tạo/sửa/xóa audit cho client.

## 7. Nhà hàng — /api/restaurants

Tất cả yêu cầu Admin.

| Method và route | Công dụng |
| --- | --- |
| GET /api/restaurants?page=1&pageSize=20&search=Sen | Danh sách; search theo tên nhà hàng |
| GET /api/restaurants/{id} | Chi tiết, gồm giờ hoạt động |
| POST /api/restaurants | Tạo hồ sơ nhà hàng |
| PUT /api/restaurants/{id} | Cập nhật hồ sơ và thay toàn bộ giờ hoạt động |

POST và PUT dùng cùng dạng body:

```json
{
  "restaurantName": "Nhà hàng Sen",
  "address": "123 Nguyễn Văn Linh, Đà Nẵng",
  "hotline": "0901234567",
  "taxCode": "0123456789",
  "logoUrl": "https://example.com/logo.png",
  "timeZone": "Asia/Ho_Chi_Minh",
  "openingHours": [
    { "dayOfWeek": 1, "openAt": "08:00", "closeAt": "22:00" },
    { "dayOfWeek": 2, "openAt": "08:00", "closeAt": "22:00" }
  ]
}
```

Result tạo/sửa/chi tiết là body trên cộng `restaurantId`, ví dụ `11111111-1111-1111-1111-111111111111`. Danh sách chứa các object đó trong items.

Ngày 0 = Chủ nhật, 1 = Thứ hai, …, 6 = Thứ bảy. Mẫu chỉ khai báo thứ hai và thứ ba; muốn đủ tuần phải gửi đủ các ngày tương ứng. Tối đa một khoảng giờ/ngày, không trùng ngày, giờ mở trước giờ đóng trong cùng ngày. PUT thay toàn bộ danh sách, không chỉ thêm các ngày gửi lên. Logo là URL HTTP/HTTPS, chưa có upload ảnh. Hiện chưa có email nhà hàng, DELETE hoặc status nhà hàng. Danh sách chỉ áp dụng search và phân trang; không dùng các bộ lọc area/isActive của DTO dùng chung.

## 8. Khu vực — /api/areas

Tất cả yêu cầu Admin.

| Method và route | Công dụng |
| --- | --- |
| GET /api/areas?restaurantId={restaurantId}&isActive=true&search=TANG&page=1&pageSize=20 | Danh sách, bộ lọc tùy chọn |
| GET /api/areas/{id} | Chi tiết khu vực |
| POST /api/areas | Tạo khu vực |
| PUT /api/areas/{id} | Cập nhật khu vực |
| PATCH /api/areas/{id}/status | Bật/tắt khu vực |
| DELETE /api/areas/{id} | Xóa khu vực chưa có bàn, không body |

Body POST/PUT:

```json
{
  "restaurantId": "11111111-1111-1111-1111-111111111111",
  "areaCode": "TANG1",
  "areaName": "Tầng 1",
  "serviceType": "DineIn",
  "spaceType": "Indoor"
}
```

Result chi tiết/tạo/sửa/status là body trên cộng `areaId` và `isActive`, ví dụ:

```json
{
  "restaurantId": "11111111-1111-1111-1111-111111111111",
  "areaCode": "TANG1",
  "areaName": "Tầng 1",
  "serviceType": "DineIn",
  "spaceType": "Indoor",
  "areaId": "22222222-2222-2222-2222-222222222222",
  "isActive": true
}
```

Body PATCH status: `{"isActive":false}`; bật lại dùng true. Khu vực không cần version.

Mã khu vực duy nhất trong nhà hàng. ServiceType/SpaceType hiện là chuỗi bắt buộc tối đa 50 ký tự, chưa cố định enum; DineIn/Indoor là mẫu. Không chuyển khu vực sang nhà hàng khác, không xóa khi còn bàn, không ngừng hoạt động nếu còn phiên mở. Model hiện chưa có mô tả khu vực.

## 9. Bàn — /api/tables

Tất cả yêu cầu Admin.

| Method và route | Công dụng |
| --- | --- |
| GET /api/tables?restaurantId={restaurantId}&areaId={areaId}&isActive=true&search=B01&page=1&pageSize=20 | Danh sách, bộ lọc tùy chọn |
| GET /api/tables/{id} | Chi tiết và version mới nhất |
| POST /api/tables | Tạo bàn trong khu vực đang hoạt động |
| PUT /api/tables/{id} | Cập nhật thông tin bàn, cần version |
| PATCH /api/tables/{id}/status | Bật/tắt bàn, cần version |
| PATCH /api/tables/{id}/area | Chuyển khu vực, cần version |
| DELETE /api/tables/{id}?version={versionDaUrlEncode} | Xóa bàn đủ điều kiện, không body |

POST:

```json
{
  "areaId": "22222222-2222-2222-2222-222222222222",
  "tableCode": "B01",
  "capacity": 4,
  "positionX": 10,
  "positionY": 20
}
```

`tableCode` là mã/số bàn dùng để hiển thị; capacity là số chỗ; positionX/Y là tọa độ bố trí, có thể null. Result mẫu:

```json
{
  "areaId": "22222222-2222-2222-2222-222222222222",
  "tableCode": "B01",
  "capacity": 4,
  "positionX": 10,
  "positionY": 20,
  "version": "AAAAAAAAB9E=",
  "tableId": "33333333-3333-3333-3333-333333333333",
  "isActive": true,
  "cleaningStatus": "Clean"
}
```

PUT gửi đầy đủ thông tin cần lưu và version lấy từ API:

```json
{
  "areaId": "22222222-2222-2222-2222-222222222222",
  "tableCode": "B01",
  "capacity": 6,
  "positionX": 15,
  "positionY": 25,
  "version": "AAAAAAAAB9E="
}
```

PATCH status:

```json
{ "isActive": false, "version": "AAAAAAAAB9E=" }
```

PATCH area:

```json
{
  "areaId": "44444444-4444-4444-4444-444444444444",
  "version": "AAAAAAAAB9E="
}
```

DELETE ví dụ: `/api/tables/33333333-3333-3333-3333-333333333333?version=AAAAAAAAB9E%3D`.

Version là dấu kiểm soát cập nhật đồng thời do database trả, không phải số tự đặt. Sau mỗi lần sửa hãy dùng version mới trong response. Ví dụ: hai Admin cùng lấy một version; người A sửa trước, người B dùng version cũ sẽ nhận 409 và cần GET lại. Các version trong mẫu không dùng thay cho version thực.

Mã bàn duy nhất trong nhà hàng, không chuyển bàn sang nhà hàng khác. Không sửa/chuyển/ngừng bàn có phiên chưa đóng. Không xóa bàn có phiên, QR hoặc đặt bàn; dùng status để ngừng sử dụng. IsActive là trạng thái quản lý, không có nghĩa bàn đang trống/có khách. CleaningStatus được trả ra nhưng chưa có endpoint cập nhật riêng ở nhóm này.

## 10. QR bàn — /api/qr

| Method và route | Quyền | Công dụng |
| --- | --- | --- |
| GET /api/qr?restaurantId={restaurantId}&areaId={areaId}&isActive=true&search=B01&page=1&pageSize=20 | Admin | Danh sách metadata QR và imageUrl; không trả token/bytes ảnh |
| GET /api/qr/{qrId}/image | Admin | Lấy lại file PNG để xem/in; không body |
| POST /api/qr/tables/{tableId} | Admin | Tạo QR lần đầu; không body |
| POST /api/qr/tables/{tableId}/regenerate | Admin | Tạo lại QR, thu hồi mã cũ |
| POST /api/qr/resolve | Công khai | Xác định đúng bàn từ token quét được |

Result tạo/tạo lại:

```json
{
  "qr": {
    "qrId": "55555555-5555-5555-5555-555555555555",
    "imageUrl": "/api/qr/55555555-5555-5555-5555-555555555555/image",
    "tableId": "33333333-3333-3333-3333-333333333333",
    "tableCode": "B01",
    "areaId": "22222222-2222-2222-2222-222222222222",
    "createdAt": "2026-10-16T03:00:00Z",
    "revokedAt": null,
    "isUsable": true
  },
  "menuUrl": "http://localhost:5173/menu#qr=<TOKEN>&table=B01",
  "token": "<TOKEN_HEX_64_KY_TU>",
  "png": "<ANH_PNG_DANG_BASE64>"
}
```

Token và PNG trong mẫu đã thay bằng placeholder; phải dùng giá trị thật trong response. Frontend hiển thị ảnh bằng `data:image/png;base64,` cộng với trường png. URL frontend lấy từ cấu hình Qr:MenuBaseUrl. Dùng điện thoại quét cần địa chỉ frontend truy cập được từ điện thoại, không dùng localhost của máy phát triển.

Body regenerate:

```json
{ "expectedQrId": "55555555-5555-5555-5555-555555555555" }
```

expectedQrId là ID mã đang hiệu lực bạn muốn thay thế; nếu người khác đã tạo lại trước đó thì trả 409, tránh ghi đè ngoài ý muốn. Gọi Create khi đã có QR chưa thu hồi cũng trả 409.

Body resolve:

```json
{ "token": "<TOKEN_LAY_TU_URL_HOAC_KET_QUA_TAO_QR>" }
```

Result resolve:

```json
{
  "tableId": "33333333-3333-3333-3333-333333333333",
  "tableCode": "B01",
  "areaId": "22222222-2222-2222-2222-222222222222",
  "restaurantId": "11111111-1111-1111-1111-111111111111"
}
```

Luồng frontend: đọc `qr` trong URL fragment → POST resolve → hiển thị tableCode từ kết quả. Giá trị `table=B01` trên URL chỉ là gợi ý; sửa URL thành B02 không chuyển quyền sang bàn B02. Backend xác định bàn qua token.

Danh sách trả phân trang với items là các object `qr` như trên. Riêng bộ lọc `isActive=true` nghĩa là QR chưa bị thu hồi; `isUsable` còn xét bàn và khu vực đang hoạt động. Mã cũ sau regenerate hoặc bàn/khu vực ngừng hoạt động sẽ resolve 404. Database chỉ giữ hash; PNG lưu riêng theo qrId và lấy lại qua imageUrl, không cần đổi token khi in lại.

GET image trả trực tiếp bytes `image/png`, không bọc ResponseDTO khi thành công; lỗi dùng envelope thông thường. Cần Bearer Admin, phản hồi no-store. Ảnh QR đã thu hồi vẫn xem được để đối chiếu lịch sử nhưng không quét sử dụng được. Nếu chưa có/mất file ảnh thì imageUrl null và GET image trả 404; QR tạo trước tính năng lưu ảnh cần regenerate một lần để có ảnh lưu.

Lưu ảnh mặc định tại Restaurant.API/App_Data/qr-images, tùy chỉnh bằng Qr:ImageDirectory. Docker dùng volume qr-images. Ảnh không đưa lên Git. Hướng dẫn tải ảnh bằng fetch có Authorization, sao lưu và xử lý lỗi lưu ảnh xem [qr-management.md](qr-management.md).

Chưa có GET metadata QR theo ID, DELETE QR, tạo phiên khách hoặc quyền gọi món từ QR. Resolve hiện chỉ xác định bàn.

## 11. Kiểm tra kết nối và health

- `GET /api/identity/ping`: công khai tại Gateway, không body; kiểm tra Gateway gọi được Identity. Thành công trả object `{ "message": "..." }` với lời chào từ service; không dùng ResponseDTO. Identity không khả dụng/quá hạn trả 503/504.
- `GET /health/live`: endpoint trực tiếp tại Restaurant, Catalog, Inventory, Ordering, Billing, Customer; thành công 200, không body.
- `GET /health/ready`: tại sáu service trên; kiểm tra kết nối database, 200 nếu kết nối được, 503 nếu không kết nối được theo nhánh CanConnect. Đây chưa phải kiểm tra đầy đủ schema/nghiệp vụ.
- Health chưa được route qua Gateway. Gọi đúng host/port/protocol của service khi vận hành; frontend nghiệp vụ vẫn gọi Gateway.

Catalog, Inventory, Ordering, Billing và Customer hiện chưa có HTTP API nghiệp vụ qua Gateway. Có model/persistence không đồng nghĩa đã có API menu, kho, gọi món, hóa đơn, thanh toán hay khách hàng.

## 12. Thứ tự thử bằng Swagger/Postman

1. Chạy Gateway + Identity + Restaurant, cấu hình database và có tài khoản Admin hợp lệ.
2. Login, lấy accessToken và Authorize.
3. GET users/me để kiểm tra token; GET roles để biết roleId thực.
4. Thử tạo/sửa/lọc nhân viên; GET sessions và audit-logs.
5. POST restaurants, lưu restaurantId.
6. POST areas bằng restaurantId thật, lưu areaId.
7. POST tables bằng areaId thật, lưu tableId và version.
8. POST qr/tables/{tableId}, lưu QR/token/PNG.
9. Gọi resolve không Bearer, kiểm tra tableCode đúng B01.
10. Regenerate bằng expectedQrId hiện tại; kiểm tra token cũ trả 404, token mới resolve được.
11. Khi thử cập nhật bàn, luôn lấy version mới nhất; khi thử khóa tài khoản/đổi mật khẩu/logout, dự kiến token hiện tại có thể hết hiệu lực.

Các API GET không có request body. Các placeholder `{id}` trên route là biến phải thay trước khi gọi, không gửi nguyên dấu ngoặc. Không copy các GUID minh họa rồi kỳ vọng có dữ liệu tương ứng trong database.
