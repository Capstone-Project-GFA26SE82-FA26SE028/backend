# Nhật ký thay đổi database / SQL / ERD

Cập nhật: 2026-10-26. Phạm vi kiểm kê: lịch sử Git của checkout `feature/Billing`, bảy script `docker/sql/schema`, toàn bộ `docker/sql/upgrades`, model/DbContext và metadata SQL Server Docker của bảy database. Đây là bằng chứng tại một môi trường, không xác nhận database của máy thành viên khác đã nâng cấp.

## Mốc đối chiếu và giới hạn bằng chứng

- `cac39b3`: model ban đầu của các service và `scripts/billing-schema.sql`. Script Billing cũ có lệnh DROP DATABASE; chỉ đọc lịch sử, tuyệt đối không chạy lại.
- `17ffdb9`: xóa script Billing cũ khỏi repository; việc xóa file không chứng minh database đã bị xóa.
- `10c23b0`: lần đầu nhập bảy schema Docker vào Git. Bảng đã có trong mốc này được gọi là baseline, không khẳng định bảng được sáng tạo trong commit đó.
- Bộ SQL ngoài repository được đọc tại `C:/Users/phucg/Downloads/Database-SQLServer-main/Database-SQLServer/`. Không sửa hoặc chạy các script này. Bản Restaurant hiện tại ở thư mục này đã chứa RestaurantStaff và các trường đặt bàn, vì vậy không còn là bản Restaurant nguyên thủy. Lịch sử chỉnh sửa ngoài Git không xác định đầy đủ.
- `rowversion` và `timestamp` là hai tên cho cùng kiểu SQL Server, không phải một thay đổi kiểu dữ liệu cần vẽ lại ERD.
- Không suy từ model/RPC mới ra bảng mới. Chỉ các mục có DDL dưới đây được coi là thay đổi schema đã có bằng chứng. Các nâng cấp cũ chỉ được xác nhận hiện diện qua metadata; không có lịch sử máy để khẳng định ai đã chạy vào lúc nào.
- Dữ liệu demo/seed, cấu hình appsettings, application lock và transaction lock không phải bảng/cột/index mới.

## Bảng có sẵn ở baseline

| Database | Các bảng có sẵn trong `10c23b0` |
| --- | --- |
| BillingDb | `Bill`, `CashRegister`, `PaymentProviderConfig`, `PaymentMethod`, `Voucher`, `TaxFeePolicy`, `Promotion`, `DiscountLimit`, `VoucherRedemption`, `ElectronicInvoice`, `CashShift`, `ReceiptPrint`, `PromotionTarget`, `BillLine`, `BillAdjustment`, `ShiftReport`, `Payment`, `Refund`, `CashMovement`, `CashHandover`, `PaymentWebhook`, `RefundLine` |
| CatalogDb | `Category`, `OptionGroup`, `Item`, `Option`, `ItemOptionGroup`, `Price`, `RecipeVersion`, `RecipeMedia`, `RecipeIngredient` |
| CustomerDb | `LoyaltyTier`, `LoyaltyPolicy`, `FeedbackAccess`, `Customer`, `Feedback`, `PointReservation`, `LoyaltyEntry`, `FeedbackItem` |
| IdentityDb | `Role`, `User`, `UserSession`, `AuditLog` |
| InventoryDb | `GoodsReceipt`, `Stocktake`, `Ingredient`, `StocktakeItem`, `GoodsReceiptItem`, `StockMovement` |
| OrderingDb | `Order`, `Cart`, `KitchenStation`, `KitchenAssignment`, `CartItem`, `OrderItem`, `CartItemOption`, `ServiceRecord`, `OrderItemOption`, `KitchenTask`, `CancellationRequest`, `Event` |
| RestaurantDb | `Restaurant`, `Reservation`, `TableSession`, `Area`, `OpeningHour`, `GuestAccess`, `Table`, `ServiceRequest`, `ReservationTable`, `SessionTable`, `Qr` |

`Bill`, `BillLine`, `CashShift`, `CashMovement`, `TaxFeePolicy` và các bảng thanh toán là bảng có sẵn, không được tạo mới bởi các đợt quầy/mở ca/lập bill. `OriginalTableRef` đã có tại Ordering.OrderItem (nullable) và Billing.BillLine (NOT NULL) từ baseline; đợt lập bill chỉ thêm hợp đồng đọc nó. Nếu bản ghi Ordering thiếu bàn gốc, backend báo lỗi, không gán bàn giả.

## Các thay đổi lịch sử có bằng chứng

Tất cả các cột thêm bên dưới **không có DEFAULT**, trừ khi ghi khác. Quan hệ `1–n` cho phép phía n có 0 bản ghi. Nullable FK cho phép bản ghi con chưa có cha.

| Database / bảng | Thay đổi và khai báo chính xác | Khóa/ràng buộc/index | Dây ERD / cardinality | File / commit bằng chứng | Trạng thái kiểm tra |
| --- | --- | --- | --- | --- | --- |
| RestaurantDb.Reservation | Thêm `RestaurantId uniqueidentifier NOT NULL`, không default | `FK_Reservation_Restaurant` → Restaurant.RestaurantId | Restaurant 1–n Reservation, FK thật trong cùng DB | `docker/sql/schema/RestaurantDb.sql`, `6f37a10`; model trước đó `766c519` | Có trong SQL Git và metadata Docker; không xác định thời điểm thực thi cũ |
| RestaurantDb.Reservation | Thêm `RequestedAreaId uniqueidentifier NOT NULL`, không default | `FK_Reservation_RequestedArea` → Area.AreaId | Area 1–n Reservation, FK thật | Cùng file/commit trên | Có trong SQL Git và metadata Docker |
| RestaurantDb.Reservation | Thêm `ConfirmedAt datetime NULL`, `CancelledAt datetime NULL`, `CancelledByRef uniqueidentifier NULL`, không default | Không thêm PK/FK/UNIQUE/CHECK cho ba cột | Hai mốc thời gian không có dây; CancelledByRef là tham chiếu logic Identity.User, User 1–n Reservation | Cùng file/commit trên; model `766c519` | Có trong SQL Git và metadata Docker |
| RestaurantDb.Reservation | Thêm index `IX_Reservation_RestaurantId_Status_StartAt (RestaurantId, Status, StartAt)`, `IX_Reservation_RequestedAreaId (RequestedAreaId)` | Hai index thường, không unique | Không đổi cardinality | Cùng file/commit trên | Có trong SQL Git và metadata Docker |
| RestaurantDb.RestaurantStaff | Bảng mới: `RestaurantId uniqueidentifier NOT NULL`, `UserIdRef uniqueidentifier NOT NULL`, `IsActive bit NOT NULL`; không default | `PK_RestaurantStaff (RestaurantId, UserIdRef)`; `FK_RestaurantStaff_Restaurant` → Restaurant.RestaurantId | Restaurant 1–n RestaurantStaff; Identity.User 1–n RestaurantStaff là quan hệ logic. Tổng thể người dùng–chi nhánh n–n qua bảng này | `docker/sql/schema/RestaurantDb.sql`, `6f37a10`; model `766c519` | Có trong SQL Git và metadata Docker. Không có upgrade Restaurant riêng ở repo; không tự thêm trong đợt Billing |
| InventoryDb.Unit | Bảng mới: `UnitId uniqueidentifier NOT NULL`, `Code varchar(50) NOT NULL`, `Name nvarchar(100) NOT NULL`; không default | PK_Unit(UnitId); UQ_Unit_Code UNIQUE(Code) | Unit 1–n IngredientUnit; Unit 1–n GoodsReceiptItem qua InputUnitId nullable | `docker/sql/upgrades/InventoryUnits.sql`, `8638f78` | Có DDL upgrade và metadata Docker; không nằm trong CREATE schema baseline |
| InventoryDb.IngredientUnit | Bảng mới: `IngredientId uniqueidentifier NOT NULL`, `UnitId uniqueidentifier NOT NULL`, `ConversionFactor decimal(18,6) NOT NULL`; không default | PK_IngredientUnit(IngredientId,UnitId); FK_IngredientUnit_Ingredient; FK_IngredientUnit_Unit; CK_IngredientUnit_Factor CHECK(ConversionFactor > 0) | Ingredient 1–n IngredientUnit; Unit 1–n IngredientUnit. Ingredient–Unit n–n qua bảng này | Cùng upgrade/commit trên | Có DDL upgrade và metadata Docker |
| InventoryDb.GoodsReceiptItem | Thêm `InputUnitId uniqueidentifier NULL`, `InputQuantity decimal(18,6) NULL`, `ConversionFactorSnapshot decimal(18,6) NULL`; không default | FK_GoodsReceiptItem_Unit(InputUnitId) → Unit.UnitId | Unit 1–n GoodsReceiptItem; bản ghi cũ có thể NULL | Cùng upgrade/commit trên | Có DDL upgrade và metadata Docker |
| InventoryDb.StockIssue | Bảng mới: `StockIssueId uniqueidentifier NOT NULL`, `CreatedByRef uniqueidentifier NOT NULL`, `CreatedAt datetime2(7) NOT NULL`, `Reason nvarchar(1000) NOT NULL`, `RequestHash varchar(64) NOT NULL`; không default | PK_StockIssue(StockIssueId); không UNIQUE RequestHash | Identity.User 1–n StockIssue là logic, không FK xuyên DB | `docker/sql/upgrades/InventoryFifo.sql`, `8638f78` | Có DDL upgrade và metadata Docker |
| InventoryDb.StockMovement | Thêm `StockIssueId uniqueidentifier NULL`, không default | FK_StockMovement_StockIssue → StockIssue.StockIssueId; thêm IX_StockMovement_StockIssueId(StockIssueId), IX_StockMovement_GoodsReceiptItemId(GoodsReceiptItemId) | StockIssue 1–n StockMovement. GoodsReceiptItem–StockMovement là FK đã có, chỉ thêm index | Cùng upgrade/commit trên | Có DDL upgrade và metadata Docker |
| CatalogDb.Item | Thêm `PreparationTimeMinutes int NULL`, không default | Không thêm PK/FK/UNIQUE/CHECK/index | Không đổi dây ERD | `docker/sql/schema/CatalogDb.sql`, `docker/sql/upgrades/CatalogCookingTime.sql`, `a1eb05b` | Có trong file và metadata Docker |
| OrderingDb.KitchenTask | Thêm `EstimatedMinutes int NULL`, không default | Không thêm PK/FK/UNIQUE/CHECK/index | Không đổi dây ERD | `docker/sql/schema/OrderingDb.sql`, `docker/sql/upgrades/OrderingCookingTime.sql`, `5cd4f9d` | Có trong file và metadata Docker |
| BillingDb.CashRegister | Thêm `RestaurantId uniqueidentifier NOT NULL`, không default | Không thêm FK/UNIQUE/index. Upgrade dừng khi quầy cũ chưa có ánh xạ thật | Restaurant.Restaurant 1–n Billing.CashRegister là quan hệ logic | `docker/sql/schema/BillingDb.sql`, `docker/sql/upgrades/BillingCashRegisterRestaurant.sql`, `fc60a64` | Đã áp dụng/kiểm tra thực tế đợt quầy; metadata Docker xác nhận cột required |

Đợt mở ca `735b423`: **Không có thay đổi schema**. CashShift và FK_CashShift_CashRegister đã tồn tại. Chống ca trùng dùng khóa hàng CashRegister trong transaction, không có unique index mới.

## Những ràng buộc không phải mới

- Restaurant: `UX_TableSession_ReservationId` unique có lọc `ReservationId IS NOT NULL` đã có ở SQL baseline. Reservation 1–0..1 TableSession; khách vãng lai có ReservationId NULL. Không thêm index này khi triển khai đặt bàn.
- Billing: `UQ_ShiftReport_CashShiftId` đã có; CashShift 1–0..1 ShiftReport. FK_BillLine_Bill, Bill 1–n BillLine, đã có.
- Identity: `UQ_UserSession_RefreshTokenHash` đã có. Customer: `UQ_Feedback_AccessId` đã có.
- Không tìm thấy UNIQUE index CategoryCode/CategoryName hoặc collation Vietnamese_100_CI_AS trong schema/metadata hiện tại. Việc kiểm tra trùng ở BLL không chứng minh DB có unique index. Các chỉnh sửa chưa commit trước đây không thể khôi phục đầy đủ chỉ từ lịch sử Git, nên không khẳng định lịch sử đã từng áp dụng/xóa tại mọi máy.
- IdentityDb và CustomerDb: không thấy thay đổi cấu trúc sau baseline `10c23b0` trong checkout này. Không khẳng định không có thay đổi ngoài Git tại môi trường khác.

## Đợt lập bill hiện tại — thay đổi SQL/ERD được phép

| Database / bảng | Thay đổi | Cột / kiểu / NULL / default | Khóa/index | Quan hệ cần cập nhật ERD | File / commit | Trạng thái |
| --- | --- | --- | --- | --- | --- | --- |
| BillingDb.Bill | Thêm cột tham chiếu chi nhánh | `RestaurantId uniqueidentifier NOT NULL`, không default | Giữ nguyên PK_Bill; không thêm FK/UNIQUE/index/CHECK | Vẽ quan hệ **logic** Restaurant.Restaurant 1–n Billing.Bill; mỗi bill đúng một chi nhánh | `docker/sql/schema/BillingDb.sql`, `docker/sql/upgrades/BillingBillRestaurant.sql`; commit của đợt chứa nhật ký này (`git log --follow` để tra) | Đã cập nhật SQL, áp dụng và chạy lại upgrade thành công trên BillingDb Docker ngày 2026-10-26; metadata xác nhận kiểu/NULL/default; test SQL Server thật pass |
| BillingDb.BillLine | Đổi nullability | `CogsSnapshot decimal(18,2) NOT NULL` → `decimal(18,2) NULL`, không default | Không đổi PK/FK/index | Không thêm hoặc đổi dây nối | Cùng file/commit trên | Đã cập nhật SQL, áp dụng và chạy lại upgrade thành công trên BillingDb Docker ngày 2026-10-26; metadata xác nhận kiểu/NULL/default; test SQL Server thật pass |

Không thêm bảng. Không thêm OriginalTableRef. Không thêm cột fingerprint: dấu đối chiếu dữ liệu nguồn lưu trong JSON ItemSnapshot đã có (varchar(max), JsonSerializer escape Unicode); đây là quy ước dữ liệu, không phải schema mới. Không thêm cột CreatedAt, OrderId, TableSessionId hoặc cột thời điểm lập vào Bill.

Upgrade Bill khóa và kiểm tra schema trước khi sửa, chạy lại được. Bill cũ thiếu ánh xạ chi nhánh thì dừng và rollback; không chọn một chi nhánh mặc định hoặc suy từ một bàn bất kỳ. Không tự đổi giá trị COGS cũ 0 thành NULL vì không biết 0 đó có thật hay không. Giá vốn NULL không phải 0, giá bán hoặc lợi nhuận đã tính.

## Các thay đổi chỉ nằm trong code/hợp đồng

- `fc60a64`: RestaurantAccessGrpc.GetCurrentScope xác thực phiên và phân công; cho phép Admin phân công thêm Manager. Không tạo thêm bảng ngoài CashRegister.RestaurantId đã liệt kê; RestaurantStaff đã có.
- `735b423`: CashShift Open/GetCurrent, khóa/transaction và quyền chi nhánh; không DDL.
- Đợt bill: BillingSessionGrpc.GetById ở Restaurant và BillingOrderGrpc.GetBySession ở Ordering là hợp đồng **chỉ đọc**, không mở quyền của API Ordering cũ. Thêm BillGrpc và Gateway POST/GET bill. Không đổi database Restaurant/Ordering hoặc luồng gọi món, bếp, đặt bàn, QR.
- AI UC1/UC2, Catalog Category/Item/Price/Option, phân quyền và các CRUD dùng bảng baseline không đồng nghĩa tạo schema mới. Lịch sử SQL kiểm kê không cho thấy DDL khác ngoài các hàng đã nêu. Model mới ở thời điểm import không đủ bằng chứng về ngày tạo bảng thực.

## Giới hạn nghiệp vụ bill

- Giá snapshot từ Ordering được lấy từ Catalog. Source trước đợt này không có cờ giá gồm/chưa gồm thuế. Người dùng xác nhận **giá chưa gồm VAT/phí** cho đợt này; cấu hình `Billing:OrderPriceBasis=Exclusive`, `Currency=VND`, không hồi tố suy diễn giá cũ ở các máy khác.
- Chỉ tính cùng mức thuế, chưa áp dụng ưu đãi. Lấy TaxFeePolicy có hiệu lực ở UTC; tỷ lệ là số thập phân 0..1. Thiếu/không hợp lệ/mơ hồ thì không tạo bill, không seed chính sách thật.
- Phí = SubTotal × ServiceRate khi bật, VAT = (SubTotal + phí) × VatRate. Hai khoản làm tròn 2 chữ số AwayFromZero; TotalAmount làm tròn đến 1 đồng AwayFromZero. Response RoundingAdjustment thể hiện chênh lệch làm tròn, không đổi kiểu decimal hoặc thêm cột. Phần payment sau này phải dùng TotalAmount đã chốt, không tự tính lại.
- Một bill/phiên trong bản đầu. Chỉ đơn đã xác nhận; trừ CancelledQuantity, bỏ dòng Cancelled. Có đơn chờ Waiter xác nhận thì báo 409 để tránh bỏ sót. Cảnh báo món chưa Served. Không làm tách/gộp, cập nhật bill hoặc thanh toán.
- Fingerprint đối chiếu đơn/line/option/giá/số lượng/trạng thái. Detail báo Current hoặc Stale; danh sách NotChecked. Không tự sửa snapshot khi đơn hoặc chính sách thay đổi. Hai lần đọc lúc tạo giúp phát hiện thay đổi nhưng không phải khóa phân tán với Ordering.
- Chưa có cơ chế chốt tập món với Ordering trước khi thu tiền. Không được coi thanh toán hoàn chỉnh, kể cả khi SourceStatus Current. Chưa làm COGS UC_MGR_06: nguồn còn thiếu gồm BOM, định lượng và đơn giá nguyên liệu bình quân.

## Quy trình duy trì

Mỗi đợt thay đổi schema cập nhật bảng này cùng code và SQL upgrade trong một commit. Nếu không có DDL phải ghi **Không có thay đổi schema**. Không dùng commit hash tự tham chiếu (hash của commit thay đổi khi thêm chính hash đó vào file); commit chứa mục được tra bằng `git log --follow -- docs/database-change-log.md` và mã commit báo ở bàn giao.

## Kiểm kê metadata SQL Server trước nâng cấp Bill

| Database | Số bảng thực tế | Phạm vi xác nhận |
| --- | ---: | --- |
| BillingDb | 22 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| CatalogDb | 9 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| CustomerDb | 8 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| IdentityDb | 4 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| InventoryDb | 9 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| OrderingDb | 12 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |
| RestaurantDb | 12 | Đọc sys.tables, sys.columns, defaults, indexes, foreign_keys và checks ngày 2026-10-26; không sửa DB |

## Dấu nhận diện bộ SQL ngoài Git đã đối chiếu

| File | SHA-256 tại thời điểm đọc |
| --- | --- |
| BillingDb.sql | `6455bc12ee940d541864fc14b89ee620ad05bf82cc391832c27266fb8f199b48` |
| CatalogDb.sql | `e74e848f4f2a49ef3d9edcb0c6071ab6021e413180aba425354a7cd75623ca68` |
| CustomerDb.sql | `1a17611f7468d438a654033ff87234974a4ee76126e40e9f6ee83f0d0d56aaae` |
| IdentityDb.sql | `532cb24d0daf1132e32151b3b125303489231554f6cde5df353a9f7f91f3c667` |
| InventoryDb.sql | `cebff89bd401233573998fa01d110de7c3adc133a25bef367138b0c65ab38fdd` |
| OrderingDb.sql | `62ada82ac02c4376526e64143d1b8720e725a48c9a0dab9aff35e712452fa109` |
| RestaurantDb.sql | `34ad893ea334f892eabffa3bba3b6b2ac504128960e2e357a1342d35c17717f8` |

## Quan hệ bill cần vẽ đúng

- Bill.RestaurantId → Restaurant.RestaurantId: quan hệ logic Restaurant 1–n Bill, không FK xuyên DB.
- BillLine.BillId → Bill.BillId: FK thật 1–n, đã có từ trước.
- BillLine.TableSessionRef → Restaurant.TableSession.TableSessionId và BillLine.OriginalTableRef → Restaurant.Table.TableId: các tham chiếu logic đã có, mỗi phiên/bàn có nhiều dòng bill; không thêm dây FK thật trong đợt này.
- Ordering.OrderItem.OriginalTableRef → Restaurant.Table.TableId: tham chiếu logic đã có và nullable. Chỉ bổ sung RPC đọc trường này.
- Quy tắc một bill/phiên trong bản đầu được BLL + application lock kiểm soát thông qua BillLine.TableSessionRef. Không có cột TableSessionId ở Bill hoặc unique index 1–1 giữa hai bảng; không vẽ một FK 1–1 không tồn tại.
- CogsSnapshot cho phép NULL không làm thay đổi bất kỳ quan hệ nào.

## Bằng chứng kiểm thử đợt bill

- CI của commit nền `735b423`: run `40337244134`, thành công, Billing 100 pass/8 conditional skip; không phải CI của commit mới chưa push.
- Build/test toàn solution: 700 pass, 48 conditional skip, 0 fail. Billing 126 pass/11 conditional skip; Ordering 40 pass.
- Ba test SQL Server riêng đã chạy thật: chống tạo bill đồng thời, upgrade lặp lại, rollback khi bill cũ thiếu ánh xạ. Không bỏ qua ba test này trong lần chạy SQL thật.
- Gateway thật: 64 kiểm tra pass, gồm quyền, bill chi nhánh khác, món hủy, dữ liệu thiếu, request lặp/đồng thời, snapshot chính sách, Stale, Ordering dừng và phiên nguồn đã đóng. Test dùng database BillCheck tách biệt, dữ liệu đơn Confirmed được seed trực tiếp để kiểm tra tích hợp đọc; không tuyên bố đã kiểm thử lại toàn luồng gửi/duyệt đơn Ordering từ UI.
- Kiểm tra dữ liệu fixture: 3 bill / 6 dòng, cả 6 CogsSnapshot NULL; 0 Payment, CashMovement, ShiftReport. BillingDb thường vẫn 0 bill và 0 TaxFeePolicy; không thêm dữ liệu demo/chính sách thuế vào database thường.
- Đã khởi động lại đúng Restaurant/Ordering/Billing/Gateway với image mới; Swagger cổng 5000 có POST/GET /api/bills và GET /api/bills/{id}. Database của thành viên khác vẫn cần chạy upgrade riêng qua db-init hoặc sqlcmd.
- Các container fixture được dừng và bốn database BillCheck được xóa sau kiểm thử; giữ kết quả kiểm thử cục bộ trong .ai (được Git ignore).
