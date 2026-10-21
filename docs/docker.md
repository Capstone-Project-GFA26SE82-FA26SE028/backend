# Chạy backend bằng Docker

## Khởi động cho nhóm FE

Cần Docker Desktop đang chạy Linux containers. Không cần cài SQL Server hay .NET trên máy FE.

```powershell
docker compose config --quiet
docker compose up -d --build
docker compose ps -a
```

Mở http://localhost:5000/swagger. FE gọi API tại `http://localhost:5000` (HTTP). Gateway local cũng dùng cổng 5000, nên dừng Gateway local trước khi chạy Docker.

Compose gồm SQL Server, một container `db-init` chạy một lần và 8 container ứng dụng. SQL Server phải healthy, sau đó `db-init` tạo đủ 7 database và seed thành công thì các service mới chạy. `db-init` có trạng thái `Exited (0)` là bình thường.

## Dữ liệu có sẵn

- 6 tài khoản: `admin@gmail.com`, `manager@gmail.com`, `waiter@gmail.com`, `cashier@gmail.com`, `kitchen@gmail.com`, `inventory@gmail.com`. Mật khẩu chung: **123**.
- Nhà hàng Demo Restaurant, khu MAIN, 3 bàn T01–T03.
- Hai trạm bếp HOT và BAR.
- Steamed Rice: 20.000, vào HOT; Iced Tea: 10.000, vào BAR.
- Món cơm có option Extra rice: thêm 5.000. Hai món và option có công thức, nguyên liệu demo.
- CustomerDb và BillingDb có đầy đủ bảng nhưng chưa có giao dịch demo.

Đăng nhập lại để lấy token của bộ Docker. Dùng Admin tạo QR cho bàn qua API; dùng Waiter mở phiên bàn hoặc khách quét QR và tham gia phiên, sau đó thử order. QR không được seed sẵn vì token và ảnh được tạo bởi backend.

ID cố định của T01: `20000000-0000-0000-0000-000000000011`; T02/T03 kết thúc bằng `012`/`013`. Món cơm kết thúc bằng `051`, trà `052`, option `072`, trạm HOT `021`, BAR `022` (cùng tiền tố GUID). UnitRef demo kết thúc bằng `100`, đại diện gram với hệ số 1; hiện dự án chưa có bảng/API quản lý đơn vị.

## Cấu hình

`.env` chứa cấu hình development để chạy Docker. Cần có file này trong thư mục Backend trước khi chạy Compose. `SQL_SA_PASSWORD` dùng chung cho SQL Server và connection string các service. `JWT_SECRET_KEY` là khóa ký token. Các giá trị mẫu chỉ dành cho môi trường phát triển.

Docker kết nối `sqlserver:1433` trong mạng nội bộ, không dùng database SQL trên máy host và không publish cổng SQL. Các connection string `*_DB_CONNECTION` của cấu hình Docker cũ không còn được sử dụng. Cấu hình chạy .NET local vẫn độc lập trong appsettings/User Secrets.

- `GATEWAY_HTTP_PORT=5000`: cổng Gateway trên máy.
- `QR_MENU_BASE_URL`: địa chỉ trang FE mà khách mở sau khi quét QR. Mặc định `http://localhost:3300/customer.html`; đổi theo FE của nhóm. localhost trên điện thoại là chính điện thoại.
- TestUI không nằm trong Compose. TestUI gọi Gateway tại `http://localhost:5000`, khớp với `GATEWAY_HTTP_PORT=5000`.

Gateway chỉ publish trên loopback. Các service gRPC chỉ dùng HTTP/2 trong mạng Docker: Identity 5001, Restaurant 5002, Catalog 5003, Ordering 5004, Customer 5005, Inventory 5006, Billing 5007. Không cần dev HTTPS certificate trong container.

## Lưu dữ liệu và cập nhật

Schema tại `docker/sql/schema` được xuất từ 7 database hiện có, giữ bảng, khóa và index; không chứa dữ liệu thật. Seed tại `docker/sql/seed` là dữ liệu demo riêng. Mỗi database khởi tạo trong transaction và được đánh dấu bằng extended property `DockerBootstrapVersion`.

Khởi động lại sẽ bỏ qua database đã khởi tạo, không reset tài khoản, menu hoặc đơn hàng đã test. Database có bảng nhưng chưa có dấu khởi tạo sẽ bị từ chối ghi đè. Thay đổi schema/seed trong repo không tự áp dụng vào volume đã khởi tạo; cần script nâng cấp riêng hoặc tạo môi trường Docker mới.

```powershell
docker compose down
docker compose up -d --build
```

`down` giữ dữ liệu trong volume `sql-data` và ảnh QR trong `qr-images`. **Không dùng `down -v` nếu muốn giữ dữ liệu**: tùy chọn này xóa cả database Docker và ảnh QR. Đổi mật khẩu trong `.env` không tự đổi mật khẩu SQL đã lưu trong volume.

## Kiểm tra và xử lý lỗi

```powershell
docker compose logs db-init
docker compose logs --tail 100 sqlserver
docker compose logs --tail 100 gateway identity restaurant catalog ordering
```

- SQL unhealthy: kiểm tra log SQL, tài nguyên Docker và mật khẩu khi tạo volume lần đầu.
- db-init lỗi: xem lỗi SQL đầu tiên; service sẽ không chạy với schema chưa hoàn tất. Sau khi sửa, chạy lại `docker compose up -d --build`.
- Cổng 5000 bị chiếm: dừng tiến trình đang dùng hoặc đổi `GATEWAY_HTTP_PORT`.
- Swagger lên nhưng API chưa đáp ứng ngay: chờ service hoàn tất startup và kiểm tra logs.
- Các service Restaurant/Catalog/Ordering/Customer/Inventory/Billing có `/health/ready` trên cổng HTTP/2 nội bộ. Readiness chỉ kiểm tra kết nối DB.

Customer, Inventory và Billing hiện có nền tảng persistence/health; Docker chạy được các service không có nghĩa tất cả API nghiệp vụ đã được triển khai.
