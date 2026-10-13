# Chạy backend bằng Docker (local development)

## Quy ước cổng

| Dịch vụ | Local | Docker |
| --- | --- | --- |
| Gateway HTTPS | https://localhost:5000 | Không cấu hình HTTPS |
| Gateway HTTP | http://localhost:15000 | http://localhost:5000 (container: 5000) |
| Identity gRPC | https://localhost:5001 | http://identity:5001 (chỉ nội bộ) |

Chạy local với profile `https`, mở https://localhost:5000/swagger. Chạy Docker, mở http://localhost:5000/swagger. Local HTTPS và Docker dùng chung cổng 5000 nên chạy luân phiên. Dãy cổng chính dành cho service: Gateway 5000, Identity 5001, Restaurant 5002 (dự kiến), các service tiếp theo 5003 tăng dần. HTTP phụ khi chạy local dùng dãy 15000 trở lên (Gateway 15000), không chiếm dãy cổng chính.

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
