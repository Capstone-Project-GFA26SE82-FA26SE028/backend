# API đọc menu cho khách và waiter

Gateway cung cấp hai endpoint, gọi `MenuGrpc` của Catalog. Menu chỉ đọc dữ liệu; không tạo giỏ hàng, giữ giá hoặc tạo order.

## Endpoint

| Method | Route | Kết quả |
| --- | --- | --- |
| GET | `/api/menu` | Danh sách món phân trang, kèm nhóm món, giá hiện tại và option |
| GET | `/api/menu/{id}` | Chi tiết một món đang hiển thị trên menu |

Query chung:

- `channel`: mặc định `DineIn`, phải thuộc `Pricing:Channels`. Không nhận thời điểm giá do client chọn.
- `tableSessionId`: khách truyền ID phiên bàn đã tham gia; waiter bỏ tham số này.

Query danh sách:

- `categoryId`: lọc nhóm món.
- `search`: tìm chuỗi con trong tên/mã món, tối đa 255 ký tự; chuẩn hóa Unicode và chuyển hoa để tìm không phân biệt hoa/thường theo database.
- `page`: mặc định 1, tối thiểu 1.
- `pageSize`: mặc định 20, từ 1 đến 100.

Tìm kiếm hiện là tìm chuỗi con; chưa có sửa lỗi chính tả, tìm không dấu hoặc xếp hạng gần đúng. Model hiện chưa có nhãn chay/cay/best-seller nên API chưa hỗ trợ các bộ lọc đó.

## Quyền truy cập

**Khách:** gọi `POST /api/table-sessions/{sessionId}/guest-access/join` trước. Gateway đã cấp cookie HttpOnly `guest_access_{sessionId:N}`. Sau đó:

```javascript
fetch(`/api/menu?tableSessionId=${sessionId}`, {
  credentials: "include"
});
```

Gateway lấy token từ cookie tương ứng và chuyển đến Catalog. Catalog gọi `GuestAccessGrpc.GetMe` tại Restaurant để kiểm tra token, đúng phiên bàn, hết hạn/thu hồi và trạng thái phiên. Không lấy token từ query. Nếu truyền `tableSessionId`, API dùng quyền khách, không fallback sang Bearer token.

**Waiter:** gửi `Authorization: Bearer <accessToken>`, không truyền `tableSessionId`. Catalog xác thực tài khoản/phiên qua Identity và kiểm tra role thuộc `CatalogManagement:MenuReaderRoleIds` hoặc `MenuManagerRoleIds`. Cấu hình mặc định cho phép waiter, manager; không mở quyền cho mọi tài khoản đăng nhập.

Các quyền quản trị món/giá/option giữ nguyên. Menu gRPC cũng thực hiện kiểm tra quyền, không chỉ Gateway.

## Quy tắc hiển thị

- Chỉ hiện món thuộc category active và có trạng thái `Available` hoặc `OutOfStock`.
- Món `Inactive`, category inactive và ID không tồn tại trả 404 khi xem chi tiết.
- Món tạm hết vẫn hiện tên, giá, option, nhưng `canOrder = false`, `unavailableReason = "OutOfStock"`.
- Option `Inactive` bị ẩn; option `OutOfStock` vẫn hiện với `canSelect = false`.
- Chỉ trả nhóm option đã gắn vào món, kèm `minSelection`, `maxSelection`.
- Giá dùng giờ server và `Pricing:BusinessTimeZone`, cùng thứ tự ưu tiên với `GetAppliedPrice`: giá theo giờ đúng kênh → theo giờ mọi kênh → giá thường đúng kênh → giá thường mọi kênh.
- Cần đúng một giá chuẩn hiện hành và đúng một giá ưu tiên cao nhất, không âm. Thiếu/xung đột giá: `price = null`, `priceId = null`, không cho đặt, lý do `PriceUnavailable`.
- Nếu nhóm bắt buộc không có option nào chọn được, lý do là `RequiredOptionsUnavailable`.
- Món phải có đúng một công thức đang hiệu lực mới được mở bán. Nếu dữ liệu cũ đang `Available` nhưng thiếu hoặc chồng công thức, menu vẫn chặn đặt: `canOrder = false`, lý do `RecipeUnavailable`.
- `canOrder` là khả năng đặt sơ bộ khi đọc menu. Kiểm tra số lượng, min/max và lấy snapshot giá qua [POST /api/menu/validate-order-items](catalog-order-validation.md). Ordering cần gọi lại khi gửi đơn.
- Không trả BOM, hướng dẫn bếp, token khách hoặc dữ liệu quản trị recipe.
- Phản hồi có `Cache-Control: no-store`.

## Ví dụ phản hồi

`GET /api/menu/{id}` trả envelope `ResponseDTO`, trong đó `result`:

```json
{
  "itemId": "11111111-1111-1111-1111-111111111111",
  "categoryId": "22222222-2222-2222-2222-222222222222",
  "categoryName": "Món chính",
  "itemCode": "MON001",
  "itemName": "Phở bò",
  "description": "Phở bò nóng",
  "imageUrl": null,
  "availability": "Available",
  "price": 50000,
  "priceId": "33333333-3333-3333-3333-333333333333",
  "channel": "DineIn",
  "evaluatedAt": "2026-10-20T08:00:00+00:00",
  "canOrder": true,
  "unavailableReason": null,
  "optionGroups": []
}
```

Danh sách trả `result: { "items": [...], "totalCount": 10, "page": 1, "pageSize": 20 }`. Giá và phụ thu trong HTTP JSON là số decimal; trong protobuf truyền chuỗi để giữ độ chính xác.

HTTP: 400 query sai; 401 thiếu/sai/hết quyền phiên; 403 không đúng role; 404 món không hiển thị; 503 thiếu cấu hình giá hoặc service phụ thuộc không khả dụng; 504 timeout.

## Cấu hình và kiểm thử

Catalog cần:

- `CatalogManagement:MenuReaderRoleIds`: role ID thực tế của waiter (mặc định `["waiter"]`).
- Docker Compose có thể ghi đè role waiter bằng `CATALOG_MENU_READER_ROLE_ID`.
- `GrpcServices:Restaurant`: `https://localhost:5002` khi local, `http://restaurant:5002` khi Docker.
- Cấu hình Identity và Pricing hiện có.

Không thay schema database. Khi triển khai, cập nhật Gateway và Catalog cùng contract protobuf mới.

```powershell
dotnet test Tests/Catalog.Tests/Catalog.Tests.csproj --no-restore
```

`MenuFlowTests` kiểm tra HTTP → gRPC → BLL → repository bằng SQLite; Identity và GuestAccess là test doubles. Cần chạy smoke test với Identity, Restaurant và SQL Server thật trước triển khai.

Chi tiết phân quyền và giới hạn phiên bản công thức: [Phân quyền backend](backend-permissions.md).
