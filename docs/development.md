# Chuẩn bị viết nghiệp vụ

## Nền tảng đã có

Sáu service mới đăng ký persistence tại `<Service>.DAL/Extensions/PersistenceExtensions.cs` và gọi từ `Program.cs`. Mỗi scope dùng chung một DbContext cho repository và UnitOfWork. Model sinh bởi EF Core Power Tools được giữ nguyên.

- Inject `IGenericRepository<TEntity>` cho thao tác đơn giản; mọi entity có DbSet đều đã được đăng ký. Bảng nối many-to-many ngầm của EF được thao tác qua navigation property.
- Khi cần truy vấn nghiệp vụ, tạo interface và repository riêng cho model trong DAL, kế thừa `GenericRepository<TEntity, ServiceDbContext>` khi phù hợp rồi đăng ký scoped. Không gộp thao tác của model khác vào repository này.
- Inject `Shared.Persistence.Interfaces.IUnitOfWork` vào BLL. Repository thêm/sửa/xóa chỉ thay đổi trạng thái tracking; gọi `SaveAsync` để ghi database.
- `ExecuteInTransactionAsync` mở transaction, chạy callback, lưu và commit. Exception làm transaction bị rollback khi dispose. Không lồng transaction; sau lỗi nên kết thúc scope, không tái sử dụng các entity đang tracking để tiếp tục ghi. Transaction chỉ bao phủ database của service đó.
- `FindAllAsync` trả `Task<List<T>>`, cần `await`. `GetById(string)` dành cho khóa chuỗi; `GetByIdAsync(Guid)` dành cho khóa Guid. Với khóa ghép, dùng điều kiện chứa đầy đủ các thành phần khóa trong repository riêng.
- Identity vẫn dùng UnitOfWork riêng để giữ cơ chế audit hiện có.

## Thêm chức năng đầu tiên

Nhóm quản lý Restaurant đã có luồng Gateway → gRPC → BLL → DAL và test tích hợp; xem [restaurant-management.md](restaurant-management.md) để chạy và tham khảo cách tổ chức theo từng model.

Ví dụ Category của Catalog:

1. Tạo DTO request/response trong Catalog.Common, quy định trường bắt buộc và phân trang.
2. Tạo `ICategoryRepository` / `CategoryRepository` trong Catalog.DAL cho truy vấn đặc thù.
3. Tạo `ICategoryService` / `CategoryService` trong Catalog.BLL; kiểm tra dữ liệu, quyền và gọi UnitOfWork để lưu.
4. Tạo contract `.proto` trong `Contracts/Protos`, tham chiếu server trong Catalog.API và client trong Gateway.
5. Tạo `CategoryGrpcService`, đăng ký bằng `MapGrpcService`, cấu hình xác thực và ánh xạ lỗi.
6. Thêm địa chỉ `GrpcServices:Catalog` theo môi trường, đăng ký gRPC client rồi tạo CategoryController trong Gateway. Gateway cần truyền token cho các RPC được bảo vệ.
7. Kiểm tra tạo, đọc, sửa, xóa, dữ liệu không hợp lệ, quyền và lỗi database qua Gateway.

CRUD ở controller/BLL dùng `Create`, `GetAll`, `GetById`, `Update`, `Delete`; method repository bất đồng bộ có hậu tố `Async`. Các thao tác đặc thù giữ tên mô tả nghiệp vụ. Viết C# theo Allman và dùng braces tường minh.

## Database First

Sửa SQL/schema trước, cập nhật database rồi scaffold lại bằng cấu hình EF Core Power Tools của từng DAL. Không tự chạy migration hoặc EnsureCreated lên database hiện có. Đặt logic tùy chỉnh trong file partial riêng hoặc repository/BLL để tránh bị scaffold ghi đè. Sau khi thêm model mới, bổ sung đăng ký repository trong PersistenceExtensions.

## Kiểm tra

```powershell
dotnet build Backend.sln
dotnet test Tests/Foundation.Tests/Foundation.Tests.csproj
dotnet test Tests/Identity.Tests/Identity.Tests.csproj
```

Foundation.Tests kiểm tra DI và mapping của sáu service mà không kết nối SQL Server; test lưu/xóa và commit/rollback dùng SQLite in-memory. Cần kiểm tra thêm trên SQL Server thật cho constraint, rowversion và transaction của nghiệp vụ cụ thể.

Xem [Docker và cấu hình local](docker.md) để đặt connection string và chạy service.


## Kiểm tra lưu tiếng Việt

Các trường văn bản được liệt kê trong `docker/sql/upgrades/UnicodeText.sql` dùng NVARCHAR ở SQL và mapping Unicode ở EF. Không bỏ `.IsUnicode(false)` ở trường mã kỹ thuật một cách hàng loạt. Danh sách đầy đủ kiểu dữ liệu/ERD nằm trong `database-change-log.md` (mục 2026-11-24).

Chạy regression trên Docker SQL riêng, không dùng volume của nhóm:

```powershell
pwsh -File scripts/Test-UnicodeDockerBootstrap.ps1 -VerifyEf
```

Lệnh kiểm tra tạo database mới, nâng cấp fixture varchar, chạy lại initializer/seed, giữ nguyên dữ liệu/index và 112 assertion EF/SQL. Nếu không có `UNICODE_TEST_CONNECTION`, các bài SQL parameter được ghi skipped; metadata vẫn chạy. CI có job riêng cấp SQL thật cho các bài này.

Với môi trường dev hiện có: sao lưu database, dùng đúng file env đang chạy, chạy `docker compose build db-init identity restaurant catalog inventory ordering customer billing` rồi `docker compose up -d --build`. db-init nâng cấp database cũ trước khi service chạy. Không xóa volume. Khi dùng dotnet ngoài Docker, chạy upgrade trên từng database rồi rebuild service; chỉ rebuild source không thay kiểu cột trong SQL. Giá trị đã thành dấu `?` cần nhập lại từ nguồn đúng sau nâng cấp, không tự sửa bằng cách đổi encoding.

Các file thuộc đợt Unicode (không bao gồm thay đổi có sẵn ở repo SQL riêng):

- `.github/workflows/backend-ci.yml`
- `Services/billing_service/Billing.DAL/Models/BillingDbContext.cs`
- `Services/catalog_service/Catalog.DAL/Models/CatalogDbContext.cs`
- `Services/customer_service/Customer.DAL/Models/CustomerDbContext.cs`
- `Services/identity_service/Identity.DAL/Models/IdentityDbContext.cs`
- `Services/inventory_service/Inventory.DAL/Models/InventoryDbContext.cs`
- `Services/ordering_service/Ordering.DAL/Models/OrderingDbContext.cs`
- `Services/restaurant_service/Restaurant.DAL/Models/RestaurantDbContext.cs`
- `Tests/Foundation.Tests/Foundation.Tests.csproj`
- `Tests/Foundation.Tests/UnicodePersistenceTests.cs`
- `docker/sql/init.sh`
- `docker/sql/schema/BillingDb.sql`
- `docker/sql/schema/CatalogDb.sql`
- `docker/sql/schema/CustomerDb.sql`
- `docker/sql/schema/IdentityDb.sql`
- `docker/sql/schema/InventoryDb.sql`
- `docker/sql/schema/OrderingDb.sql`
- `docker/sql/schema/RestaurantDb.sql`
- `docker/sql/seed.sh`
- `docker/sql/upgrades/BillingPayOs.sql`
- `docker/sql/upgrades/BillingProviderConfiguration.sql`
- `docker/sql/upgrades/RestaurantCloudinary.sql`
- `docker/sql/upgrades/UnicodeText.sql`
- `docs/database-change-log.md`
- `docs/development.md`
- `scripts/Test-UnicodeDockerBootstrap.ps1`
