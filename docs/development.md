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
dotnet test Services/identity_service/Identity.Tests/Identity.Tests.csproj
```

Foundation.Tests kiểm tra DI và mapping của sáu service mà không kết nối SQL Server; test lưu/xóa và commit/rollback dùng SQLite in-memory. Cần kiểm tra thêm trên SQL Server thật cho constraint, rowversion và transaction của nghiệp vụ cụ thể.

Xem [Docker và cấu hình local](docker.md) để đặt connection string và chạy service.
