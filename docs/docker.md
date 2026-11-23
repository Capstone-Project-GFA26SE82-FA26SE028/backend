# Chạy backend bằng Docker

## Khởi động cho nhóm FE

Cần Docker Desktop đang chạy Linux containers. Không cần cài SQL Server hay .NET trên máy FE.

```powershell
docker compose config --quiet
docker compose up -d --build
docker compose ps -a
```

Mở http://localhost:5000/swagger. FE gọi API tại `http://localhost:5000` (HTTP). Gateway local cũng dùng cổng 5000, nên dừng Gateway local trước khi chạy Docker.

Compose cơ sở gồm SQL Server, `db-init` và 8 container ứng dụng. Sau khi SQL Server báo healthy, `db-init` tạo schema cho 7 database. `db-init` có trạng thái `Exited (0)` là bình thường. Dùng lệnh seed bên dưới cho môi trường demo.

## Chạy và kiểm tra Billing

Dùng Compose cơ sở để chạy backend và Billing.

```powershell
docker compose -f compose.yaml config --quiet
docker compose -f compose.yaml up -d --build
docker compose -f compose.yaml ps -a
```

Trên database thử nghiệm mới, chạy seed demo sau khi `db-init` hoàn tất.

```powershell
docker compose -f compose.yaml run --rm --no-deps --entrypoint /bin/bash db-init /init/seed.sh
```

Trong Swagger của Gateway, dùng Manager demo tạo chính sách thuế thử nghiệm và Cashier mở ca. Khách yêu cầu thanh toán, Waiter chuyển yêu cầu cho Cashier chốt hóa đơn, rồi đưa hóa đơn và ghi nhận thu hộ. Cashier xác nhận bàn giao rồi kiểm tra `GET /api/cash-shifts/{cashShiftId}/reconciliation` và `POST /api/cash-shifts/{cashShiftId}/close`. Khoản đang chờ bàn giao chặn đóng ca. Sau xác nhận, số tiền hóa đơn được ghi một lần vào sổ quỹ.

Docker của Billing cần `CashRegisterAccess:WaiterRoleIds` trong cấu hình Docker. Compose giữ khóa mã hóa cấu hình PayOS trong volume `payment-keys`.

## Dữ liệu có sẵn

- 6 tài khoản: `admin@gmail.com`, `manager@gmail.com`, `waiter@gmail.com`, `cashier@gmail.com`, `kitchen@gmail.com`, `inventory@gmail.com`. Mật khẩu chung: **123**.
- Nhà hàng Demo Restaurant, khu MAIN, 3 bàn T01–T03.
- Hai trạm bếp HOT và BAR.
- Steamed Rice: 20.000, vào HOT; Iced Tea: 10.000, vào BAR.
- Món cơm có option Extra rice: thêm 5.000. Hai món và option có công thức, nguyên liệu demo.
- CustomerDb và BillingDb có đầy đủ bảng nhưng chưa có giao dịch demo.

Đăng nhập lại để lấy token của bộ Docker. Dùng Admin tạo QR cho bàn qua API; dùng Waiter mở phiên bàn hoặc khách quét QR và tham gia phiên, sau đó thử order. QR không được seed sẵn vì token và ảnh được tạo bởi backend.

`POST /api/table-sessions/scan` nhận `{"token":"<QR token>"}` và không yêu cầu đăng nhập. Ngoài các trường phiên/bàn hiện có, `result` trả thêm `restaurantName`, `areaName` và `logoUrl` lấy từ nhà hàng/khu vực của bàn đã được QR xác thực. Khi nhà hàng chưa có logo, `logoUrl` là JSON `null`, không phải chuỗi rỗng. Không cần gọi API danh sách nhà hàng để lấy thông tin này. Quét QR chưa cấp quyền đặt món; khách vẫn phải tham gia phiên theo luồng hiện có. Cần rebuild cả Restaurant và Gateway để nạp contract bổ sung; không cần SQL upgrade cho thay đổi này.

ID cố định của T01: `20000000-0000-0000-0000-000000000011`; T02/T03 kết thúc bằng `012`/`013`. Món cơm kết thúc bằng `051`, trà `052`, option `072`, trạm HOT `021`, BAR `022` (cùng tiền tố GUID). UnitRef demo kết thúc bằng `100`, đại diện gram với hệ số 1; hiện dự án chưa có bảng/API quản lý đơn vị.

## Cấu hình

Secrets belong in `.env`, in the repository root, ignored by Git. Copy `.env.example` there and fill in credentials. Compose uses SQL_SA_PASSWORD and JWT_SECRET_KEY.

Docker kết nối `sqlserver:1433` trong mạng nội bộ, không dùng database SQL trên máy host và không publish cổng SQL. Các connection string `*_DB_CONNECTION` của cấu hình Docker cũ không còn được sử dụng. Cấu hình chạy .NET local vẫn độc lập trong appsettings/User Secrets.

- `GATEWAY_HTTP_PORT=5000`: cổng Gateway trên máy.
- `QR_MENU_BASE_URL`: địa chỉ trang FE mà khách mở sau khi quét QR. Mặc định `http://localhost:5173/menu`; đổi theo FE của nhóm. localhost trên điện thoại là chính điện thoại.
- TestUI không nằm trong Compose. TestUI gọi Gateway tại `http://localhost:5000`, khớp với `GATEWAY_HTTP_PORT=5000`.

Gateway chỉ publish trên loopback. Các service gRPC chỉ dùng HTTP/2 trong mạng Docker: Identity 5001, Restaurant 5002, Catalog 5003, Ordering 5004, Customer 5005, Inventory 5006, Billing 5007. Không cần dev HTTPS certificate trong container.

## Lưu dữ liệu và cập nhật

Schema tại `docker/sql/schema` được xuất từ 7 database hiện có, giữ bảng, khóa và index; không chứa dữ liệu thật. Seed tại `docker/sql/seed` là dữ liệu demo riêng. Mỗi database khởi tạo trong transaction và được đánh dấu bằng extended property `DockerBootstrapVersion`.

Khởi động lại sẽ bỏ qua database đã khởi tạo, không reset tài khoản, menu hoặc đơn hàng đã test. Database có bảng nhưng chưa có dấu khởi tạo sẽ bị từ chối ghi đè. Thay đổi schema/seed trong repo không tự áp dụng vào volume đã khởi tạo; cần script nâng cấp riêng hoặc tạo môi trường Docker mới.

Các script nâng cấp được liệt kê trong `docker/sql/init.sh` vẫn chạy trên database đã khởi tạo. Trong đó, `IdentityStaffTransfer.sql` bổ sung bảng `dbo.StaffTransfer` và index tra cứu khi thiếu, không thay đổi tài khoản hoặc lịch sử chuyển nhà hàng đã có. Script chạy lặp an toàn; cấu trúc hiện có không tương thích sẽ bị báo lỗi thay vì tự sửa dữ liệu. Bảng này cần cho cả kiểm tra đổi vai trò/trạng thái tài khoản, không chỉ thao tác chuyển nhà hàng.

Sau khi lấy bản sửa schema Identity và quyền QR, với `.env` đã cấu hình:

```powershell
docker compose up -d --build db-init identity restaurant gateway
docker compose logs db-init
```

Kiểm tra `db-init` kết thúc với mã 0 trước khi thử đổi vai trò. Không xóa volume để xử lý lỗi thiếu bảng. Admin nhà hàng được quản lý QR trong phân công đang hoạt động; `GET /api/qr` tự chọn khi chỉ có một nhà hàng, hoặc cần `restaurantId` khi có nhiều phân công. Quyền quản trị nền tảng và API resolve QR công khai giữ nguyên.

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

Inventory có API nguyên liệu, nhập kho và lịch sử tồn. Xem [hướng dẫn Inventory](inventory.md).
