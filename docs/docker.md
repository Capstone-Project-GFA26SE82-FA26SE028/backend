# Chạy backend bằng Docker (local development)

## Quy ước cổng

| Dịch vụ | Local | Docker |
| --- | --- | --- |
| Gateway HTTPS | https://localhost:5000 | Không cấu hình HTTPS |
| Gateway HTTP | http://localhost:15000 | http://localhost:5000 (container: 5000) |
| Identity gRPC | https://localhost:5001 | http://identity:5001 (chỉ nội bộ) |
| Restaurant | https://localhost:5002 | http://restaurant:5002 (chỉ nội bộ) |
| Catalog | https://localhost:5003 | http://catalog:5003 (chỉ nội bộ) |
| Ordering | https://localhost:5004 | http://ordering:5004 (chỉ nội bộ) |
| Customer | https://localhost:5005 | http://customer:5005 (chỉ nội bộ) |
| Inventory | https://localhost:5006 | http://inventory:5006 (chỉ nội bộ) |
| Billing | https://localhost:5007 | http://billing:5007 (chỉ nội bộ) |

Chạy local với profile `https`, mở https://localhost:5000/swagger. Chạy Docker, mở http://localhost:5000/swagger. Local HTTPS và Docker dùng chung cổng 5000 nên chạy luân phiên. HTTP phụ khi chạy local dùng dãy 15000 trở lên (Gateway 15000), không chiếm dãy cổng chính.

## Sáu service mới

Restaurant, Catalog, Ordering, Customer, Inventory và Billing đã được cấu hình DbContext, repository theo entity và UnitOfWork scoped. Chúng có endpoint `/health/live` (tiến trình hoạt động) và `/health/ready` (kết nối database được: 200, không được: 503). Readiness chỉ kiểm tra kết nối, chưa xác nhận schema đầy đủ. Các cổng này chỉ nhận HTTP/2; dùng client hỗ trợ HTTP/2 khi kiểm tra health.

Các service mới chưa có RPC nghiệp vụ hoặc controller tương ứng ở Gateway. Thêm `.proto`, gRPC service, BLL và controller theo từng chức năng; xem [hướng dẫn chuẩn bị code](development.md).

Chạy local với User Secrets, ví dụ Catalog:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=CatalogDb;Integrated Security=True;TrustServerCertificate=True" --project Services/catalog_service/Catalog.API
dotnet run --project Services/catalog_service/Catalog.API --launch-profile https
```

Thay database và kiểu xác thực cho phù hợp máy của bạn. Các service còn lại cấu hình tương tự theo project API của chúng. `appsettings.json` để trống connection string; ứng dụng báo lỗi ngay nếu chưa cấu hình. User Secrets chỉ được nạp tự động trong Development. Docker đọc `ConnectionStrings__DefaultConnection` từ Compose.

Compose mặc định vẫn chạy Gateway + Identity. Để chạy thêm sáu service, bổ sung các biến `RESTAURANT_DB_CONNECTION`, `CATALOG_DB_CONNECTION`, `ORDERING_DB_CONNECTION`, `CUSTOMER_DB_CONNECTION`, `INVENTORY_DB_CONNECTION`, `BILLING_DB_CONNECTION` vào `.env` theo `.env.example`, sau đó:

```powershell
docker compose --profile services config --quiet
docker compose --profile services up -d --build
docker compose --profile services ps
docker compose --profile services logs -f catalog
```

Có thể chạy riêng một service mới bằng `docker compose up -d --build catalog`. Các connection string mới mặc định rỗng để không ảnh hưởng chế độ chỉ chạy Identity; service được chọn sẽ từ chối khởi động nếu biến của nó chưa có giá trị. `config --quiet` chỉ kiểm tra cấu trúc Compose, không xác nhận kết nối SQL. Không chép đè `.env` đang dùng; bổ sung các biến còn thiếu. Dùng `docker compose --profile services down` để dừng cả nhóm.

## Chuẩn bị

- Bật Docker Desktop, dùng Linux containers.
- SQL Server hiện có phải truy cập được bằng TCP. Compose này không tạo database/schema hay tài khoản Admin, và không chạy SQL Server mới.
- Database cần có schema và dữ liệu Role/Admin đang dùng. Linux container không dùng được Windows Integrated Security của máy host; dùng SQL login có quyền phù hợp.

## Cấu hình

Tại thư mục Backend:

```powershell
Copy-Item .env.example .env
```

Sửa .env:

- IDENTITY_DB_CONNECTION: connection string SQL Server thật. SQL trên máy host Docker Desktop dùng host.docker.internal và cổng TCP thực tế (ví dụ 1433), không dùng localhost hay LocalDB. SQL Express thường cần bật TCP/IP và cấu hình cổng cố định. Database ở máy khác dùng DNS/IP của máy đó.
- JWT_SECRET_KEY: khóa ngẫu nhiên ít nhất 32 byte UTF-8. Để tạo khóa trên PowerShell:

```powershell
$jwtBytes = New-Object byte[] 48
$jwtRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtRng.GetBytes($jwtBytes)
[Convert]::ToBase64String($jwtBytes)
$jwtRng.Dispose()
```

Dán kết quả vào JWT_SECRET_KEY trong .env. Không commit .env. Dùng dấu nháy đơn quanh connection string để bảo toàn ký tự $ trong mật khẩu. File appsettings thường và secrets được loại khỏi Docker build context; cấu hình không nhạy cảm lấy từ appsettings.Docker.json.

## Chạy

```powershell
docker compose config --quiet
docker compose up -d --build
docker compose ps
docker compose logs -f gateway identity
```

- Gateway Swagger: http://localhost:5000/swagger
- Ping Gateway → Identity gRPC: http://localhost:5000/api/identity/ping

Gateway gọi http://identity:5001 qua mạng Compose (HTTP/2). Cổng 5001 của Identity không publish ra host. Identity chỉ phục vụ gRPC, không có REST/Swagger hoặc cổng publish ra host. Không dùng launchSettings.json hoặc dev certificate trong container. Cấu hình HTTP/2 tách cổng là cần thiết khi không có TLS.

Đăng nhập lại qua Gateway Docker để lấy token, vì issuer/audience/key có thể khác phiên Visual Studio. Swagger chạy bằng môi trường Docker; các cổng publish chỉ bind 127.0.0.1. Đây là cấu hình local development, cần TLS ở ingress và cấu hình Swagger phù hợp khi triển khai public.

## Dừng / cập nhật

```powershell
docker compose down
docker compose up -d --build
```

Compose không xóa database SQL Server hiện có. depends_on chỉ điều khiển thứ tự khởi động; nếu Ping trả 503 ngay lúc startup, kiểm tra logs và thử lại sau khi Identity báo listening.

## Lỗi thường gặp

- dockerDesktopLinuxEngine / daemon unavailable: mở Docker Desktop và chờ engine sẵn sàng.
- Biến JWT_SECRET_KEY chưa đặt: sửa .env; Compose cố ý dừng nếu thiếu khóa.
- Login lỗi SQL: kiểm tra connection string, SQL authentication, TCP port và firewall. Ping thành công không xác nhận database kết nối được.
- Cổng 5000 bị chiếm: đổi GATEWAY_HTTP_PORT trong .env.

Tham khảo: https://learn.microsoft.com/en-us/aspnet/core/grpc/troubleshoot và https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/
