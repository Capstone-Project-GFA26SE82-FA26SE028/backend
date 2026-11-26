# Nhật ký thay đổi database / SQL / ERD

Cập nhật nhật ký: 2026-10-28. Mốc kiểm kê ban đầu: 2026-10-26. Phạm vi kiểm kê: lịch sử Git của checkout `feature/Billing`, bảy script `docker/sql/schema`, toàn bộ `docker/sql/upgrades`, model/DbContext và metadata SQL Server Docker của bảy database. Đây là bằng chứng tại một môi trường, không xác nhận database của máy thành viên khác đã nâng cấp.

## Mốc đối chiếu và giới hạn bằng chứng

- `cac39b3`: model ban đầu của các service và `scripts/billing-schema.sql`. Script Billing cũ có lệnh DROP DATABASE; chỉ đọc lịch sử, tuyệt đối không chạy lại.
- `17ffdb9`: xóa script Billing cũ khỏi repository; việc xóa file không chứng minh database đã bị xóa.
- `10c23b0`: lần đầu nhập bảy schema Docker vào Git. Bảng đã có trong mốc này được gọi là baseline, không khẳng định bảng được sáng tạo trong commit đó.
- Bộ SQL nguồn nằm trong repo Git riêng `Capstone-Project-GFA26SE82-FA26SE028/Database-SQLServer`, không phải thư mục runtime của backend. Mốc rà soát ban đầu chỉ đọc các file; bản Restaurant ở mốc đó đã có RestaurantStaff và các trường đặt bàn nên không coi đó là bản Restaurant nguyên thủy. Ngày 2026-10-27 đã đồng bộ riêng hai RestaurantId của Billing và CogsSnapshot nullable vào repo SQL tại commit `626faff`; không có thay đổi schema mới ngoài các mục đã liệt kê. Các hash bên dưới ghi nhận file tại mốc rà soát ban đầu, trước lần đồng bộ này. Các chỉnh sửa chưa commit trước mốc lịch sử hiện có không được suy đoán.
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

## Dấu nhận diện bộ SQL nguồn tại mốc rà soát ban đầu

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


## Đợt quản lý chính sách VAT/phí dịch vụ — 2026-10-27

**Các thay đổi cần cập nhật trên SQL/ERD: Không có thay đổi schema.**

| Database / bảng | Thay đổi thực tế | PK/FK/UNIQUE/CHECK/index / ERD | File / bằng chứng | Trạng thái |
| --- | --- | --- | --- | --- |
| BillingDb.TaxFeePolicy | Không thêm, sửa hoặc xóa cột; giữ `TaxFeePolicyId uniqueidentifier NOT NULL`, `Version int NOT NULL`, `VatRate decimal(9,6) NOT NULL`, `ServiceRate decimal(9,6) NOT NULL`, `ServiceEnabled bit NOT NULL`, `EffectiveFrom datetime NOT NULL`; không thêm default | Giữ PK_TaxFeePolicy(TaxFeePolicyId). Không thêm FK, UNIQUE, CHECK hoặc index. Không có RestaurantId, không thêm dây ERD | `docker/sql/schema/BillingDb.sql` là schema đã có; entity/DbContext và toàn bộ file SQL không thay đổi trong đợt này. Commit đợt được tra bằng `git log --follow -- docs/database-change-log.md` | Chỉ mở rộng code/RPC và test; không có script nâng cấp hoặc migration mới |

Phạm vi code và quyền:

- Chỉ System Administrator có tài khoản/phiên hợp lệ được tạo, xem danh sách hoặc xem chi tiết qua `POST /api/tax-fee-policies`, `GET /api/tax-fee-policies`, `GET /api/tax-fee-policies/{id}`. Tái sử dụng xác thực/IsAdministrator từ RestaurantAccessGrpc và Identity. Manager/Cashier/Waiter/Kitchen/Inventory không được dùng các API cấu hình này, kể cả được phân công chi nhánh.
- Chính sách chung toàn hệ thống, đúng mô hình TaxFeePolicy hiện có. Không tự tạo cấu hình thuế theo chi nhánh hoặc thêm RestaurantId.
- Gateway → gRPC TaxFeePolicy → BLL TaxFeePolicyService → TaxFeePolicyRepository hiện có → BillingDbContext/UnitOfWork. Không thay đổi nghiệp vụ hoặc database của service khác.
- Request gồm vatRate, serviceRate và serviceEnabled bắt buộc; phải nhập rõ cả giá trị 0 hoặc false. Tỷ lệ là phân số 0..1, giá trị phải lưu chính xác trong 6 chữ số thập phân. Không có VAT/phí mặc định; API không xác nhận tính phù hợp pháp lý của một mức thuế cụ thể.
- Backend tự tạo ID và Version nguyên tăng dần trong transaction có `sp_getapplock` theo database; client không gửi ID/Version. Version là thứ tự tạo phiên bản, không phải rowversion hoặc thứ tự ưu tiên hiệu lực. Khóa ứng dụng không phải unique index; SQL thủ công ngoài API vẫn có thể tạo dữ liệu không nhất quán.
- Bỏ trống hoặc null effectiveFrom: lấy UTC hiện tại bên trong transaction, biểu diễn tại độ chính xác SQL datetime và không đẩy mốc hiệu lực sang tương lai do làm tròn. Chuỗi rỗng hoặc chuỗi "null" không được coi là null.
- Nhập effectiveFrom: ISO 8601 có Z/offset, trong miền datetime SQL và còn ở tương lai khi đã lấy được khóa. Không hồi tố. Thời điểm được chuẩn hóa về UTC/SQL datetime; trùng cùng mốc sau chuẩn hóa trả 409 để không làm Bill.GetEffectiveAsync gặp hai chính sách cùng ngày giờ.
- Không có Update/Delete. Đổi chính sách bằng một phiên bản mới. Kiểm tra lại quyền Admin sau khi lấy khóa trước khi ghi dữ liệu.
- ServiceEnabled=false giữ nguyên ServiceRate cấu hình để đọc lại; nghiệp vụ lập bill hiện có chỉ thu 0 phí khi tắt. Không xóa hoặc sửa giá trị chính sách trước đó.
- Giữ nguyên cách Billing chọn chính sách: bản có EffectiveFrom lớn nhất nhưng không vượt thời điểm tính. Chính sách tương lai không áp dụng sớm; Version cao hơn không làm một chính sách hiệu lực sớm hơn thắng bản có EffectiveFrom muộn hơn.
- Không cập nhật lại bất kỳ bill đã tạo nào khi thêm chính sách. Chưa triển khai thu tiền, đóng ca, PayOS hoặc nghiệp vụ tiếp theo.

Kiểm chứng đợt này:

- Trước khi sửa: CI đúng `65baffaef7b97d10e57cf841cd1ed8155cdf6724`, run `40513619424`, thành công. Log xác nhận build/test Billing: 126 pass, 11 conditional skip; toàn solution 700 pass, 48 conditional skip.
- Build Docker Billing/Gateway thành công. Toàn solution: 743 pass, 51 conditional skip, 0 fail; Billing 169 pass/14 conditional skip. Các test cũ vẫn pass.
- Ba test SQL Server riêng đã chạy thật và pass: phiên bản không trùng cho hai mốc khác nhau, 201/409 cho cùng mốc, xung đột độ chính xác datetime và hiệu lực tức thì.
- 69 kiểm tra qua Gateway thật pass với Billing/Restaurant/Identity và SQL Server; bài kiểm tra bill đọc thêm Ordering thật. Bao phủ Admin/non-Admin/anonymous/token sai, dữ liệu thiếu/sai, UTC/offset, quá khứ/trùng mốc, phân trang, không có Update/Delete, request đồng thời và snapshot bill. Dữ liệu đơn Confirmed được seed riêng cho kiểm thử đọc, không kiểm thử lại toàn luồng đặt/duyệt đơn của Ordering.
- Kiểm tra fixture: 6 chính sách có 6 Version khác nhau, 3 bill; 0 Payment/CashMovement/CashShift; TaxFeePolicy vẫn 6 cột đúng schema cũ. BillingDb thường vẫn 0 chính sách/0 bill; không seed VAT/phí vào dữ liệu thật.
- Đã dừng/xóa các container kiểm thử TaxPolicyCheck và xóa bốn database tạm của lượt này, giữ nguyên các database thường. Bằng chứng test cục bộ ở .ai/billing-tax-policies (Git ignore).
- Khởi động lại đúng Billing/Gateway với image mới; Swagger cổng 5000 có ba API chính sách. Không sửa/push repository Database-SQLServer vì đợt này không đổi SQL hoặc ERD.

## Đợt làm mới bill chưa thanh toán — 2026-10-28

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK, FK, UNIQUE, CHECK hoặc index. Không có script upgrade, migration hoặc scaffold mới; không sửa repository Database-SQLServer.

### Dữ liệu và ERD

- Tái sử dụng Bill và BillLine có sẵn trong BillingDb. Giữ nguyên BillId, BillNumber, RestaurantId, Status, Currency, VatRateSnapshot và ServiceRateSnapshot.
- Cập nhật Bill.SubTotal, ServiceAmount, VatAmount, TotalAmount từ snapshot Ordering. Bill.Version là rowversion có sẵn, SQL Server tự cập nhật khi header thay đổi; không thêm token hoặc kiểu Version mới.
- Ghép dòng theo BillLine.OrderItemRef, giữ BillLineId của dòng đã có. Cập nhật Quantity, ItemSnapshot, UnitPriceSnapshot, OriginalTableRef, ComboGroupRef, VatAllocated và ServiceAllocated; món mới có dòng mới và CogsSnapshot = NULL. Giá lấy từ snapshot Ordering, không lấy lại giá Catalog.
- Món đã hủy giữ dòng cũ với Quantity = 0, snapshot Status = Cancelled và phần thuế/phí phân bổ bằng 0. Khi hủy hết món, bill vẫn Unpaid, tổng bằng 0, không tự xác nhận đã thu tiền. Giữ các dòng này để không mất liên kết BillLine.TableSessionRef và ngăn tạo bill thứ hai cho phiên.
- BillLine.ItemSnapshot tiếp tục lưu SourceFingerprint trong JSON đã có, không thêm cột. Cập nhật fingerprint cho cả dòng số lượng 0 để đọc lại vẫn đánh giá đúng Current/Stale.
- Quan hệ không đổi: Bill 1–n BillLine là FK thật; Restaurant 1–n Bill và TableSession 1–n BillLine là tham chiếu logic xuyên service. Không có dây ERD mới hoặc FK xuyên database.

### API và quy tắc

- `POST /api/bills/{id}/refresh`, không có request body. Đăng nhập Cashier, có phân công hiện hành tại chi nhánh của bill. Admin/Manager chỉ giữ quyền đọc hiện có, không được refresh.
- Gateway → BillGrpc.Refresh → BillService.Refresh → BillRepository.GetForRefreshAsync → UnitOfWork/BillingDbContext. Không sửa nghiệp vụ hoặc database của Ordering/Restaurant/Identity.
- Chỉ xử lý Status = Unpaid, PaidAt = NULL và chưa có bất kỳ Payment nào. Bill có giảm giá, adjustment hoặc voucher chưa được hỗ trợ trong đợt này và bị chặn rõ ràng; không xóa dữ liệu này để tính lại.
- Giữ mức VAT/phí snapshot lúc lập bill, kể cả khi chính sách mới đã hiệu lực. Giữ quy ước giá chưa VAT/phí, VND, làm tròn VAT/phí đến 2 chữ số và tổng đến đơn vị đồng theo AwayFromZero.
- Ordering cung cấp số lượng đã được xác nhận sau khi trừ phần hủy. Nếu còn đơn PendingConfirmation, refresh trả 409. Không cộng tổng mới vào tổng cũ; tính lại từ toàn bộ snapshot đủ điều kiện và không ghi trùng OrderItemId.
- DAL đọc bill với UPDLOCK/HOLDLOCK trong transaction; BLL kiểm tra lại trạng thái, tài khoản và phân công dưới khóa. Hai refresh cùng bill được tuần tự hóa. Request lặp với cùng nguồn giữ đúng số dòng, ID và số tiền.
- Đọc nguồn hai lần trước khi áp thay đổi; nếu khác nhau thì trả 409 và không lưu. Đây chưa phải khóa phân tán với Ordering: nguồn vẫn có thể đổi sau lần đọc cuối; GetById tiếp tục phát hiện Stale. Đợt này chưa triển khai chốt món hoặc thu tiền. Khi làm thanh toán phải phối hợp cùng khóa bill và cơ chế chốt nguồn riêng.
- Không thể refresh bill cũ thiếu snapshot xác minh, nhiều phiên hoặc trùng OrderItemRef; trả 409, không tự suy ra phiên/chi nhánh. Thiếu giá vốn vẫn trả NULL và cảnh báo như trước.

### Kiểm chứng

- Build toàn Backend.sln đạt; các cảnh báo nullable hiện có nằm ngoài phạm vi sửa.
- Test Billing bằng SQLite/client giả lập: 186 pass; 16 test điều kiện SQL không được tính là pass khi chưa có kết nối.
- Toàn solution sau thay đổi: 760 pass, 53 conditional skip, 0 fail. Các test skip thuộc nhiều service, không được tính là kiểm chứng tích hợp thật.
- SQL Server thật: 5 test BillSqlServerTests pass, không skip; gồm tạo bill đồng thời, hai refresh không ghi trùng và refresh chờ khóa phải đọc lại Paid sau commit. Nguồn Ordering/quyền trong các test SQL này là client giả lập.
- Gateway thật: 42 kiểm tra đạt với Identity/Restaurant/Ordering/Billing và SQL Server thật. Bao phủ thêm món, hủy một phần/toàn bộ, thêm lại sau hủy, snapshot VAT/phí, request lặp/đồng thời, Paid, pending, vai trò, khác chi nhánh và thu hồi phân công. Đơn được seed trong database kiểm thử riêng; không tuyên bố đã kiểm thử lại toàn bộ luồng tạo/duyệt đơn của Ordering.
- Log và kết quả cục bộ ở `.ai/billing-refresh/` (Git ignore). Không seed hoặc sửa dữ liệu nghiệp vụ thường. Không commit/push đợt này; người dùng tự xem diff và quyết định.
- Đã dừng/xóa nhóm container billing-refresh-check và xóa bốn database BillRefreshCheck của lượt thử. Build lại và khởi động Billing/Gateway thường; Swagger cổng 5000 đã có POST /api/bills/{id}/refresh.

## Đợt checkout và đồng bộ bill chưa thanh toán — 2026-10-28

### Schema thực tế: đúng hai bảng mới

Không sửa/xóa cột hoặc ràng buộc của bảng cũ; không thêm bảng ngoài hai bảng dưới đây. Không có DEFAULT hoặc CHECK mới. Bảng Bill, BillLine, Order, OrderItem, TableSession, GuestAccess và Table đã tồn tại từ trước.

| Database/bảng mới | Cột | Kiểu dữ liệu | NULL | DEFAULT |
| --- | --- | --- | --- | --- |
| BillingDb.BillCheckout | CheckoutId | uniqueidentifier | NOT NULL | Không |
| BillingDb.BillCheckout | BillId | uniqueidentifier | NULL | Không |
| BillingDb.BillCheckout | TableSessionRef | uniqueidentifier | NOT NULL | Không |
| BillingDb.BillCheckout | SourceHash | char(64) | NULL | Không |
| BillingDb.BillCheckout | Status | varchar(20) | NOT NULL | Không |
| BillingDb.BillCheckout | CreatedAt | datetime2(7) | NOT NULL | Không |
| BillingDb.BillCheckout | ReleasedAt | datetime2(7) | NULL | Không |
| OrderingDb.CheckoutLock | CheckoutId | uniqueidentifier | NOT NULL | Không |
| OrderingDb.CheckoutLock | BillIdRef | uniqueidentifier | NULL | Không |
| OrderingDb.CheckoutLock | TableSessionRef | uniqueidentifier | NOT NULL | Không |
| OrderingDb.CheckoutLock | SourceHash | char(64) | NULL | Không |
| OrderingDb.CheckoutLock | Status | varchar(20) | NOT NULL | Không |
| OrderingDb.CheckoutLock | CreatedAt | datetime2(7) | NOT NULL | Không |
| OrderingDb.CheckoutLock | ReleasedAt | datetime2(7) | NULL | Không |

- PK_BillCheckout(CheckoutId), PK_CheckoutLock(CheckoutId).
- FK_BillCheckout_Bill: BillCheckout.BillId → BillingDb.Bill.BillId, FK thật nội bộ, không cascade delete. Bill 1–n BillCheckout; một checkout có thể chưa có bill, nên phía Bill của một checkout là 0..1.
- UX_BillCheckout_ActiveSession: UNIQUE trên TableSessionRef, WHERE ReleasedAt IS NULL.
- UX_CheckoutLock_ActiveSession: UNIQUE trên TableSessionRef, WHERE ReleasedAt IS NULL.
- Restaurant.TableSession 1–n BillCheckout và 1–n CheckoutLock là hai quan hệ logic qua TableSessionRef. Billing.Bill 1–n Ordering.CheckoutLock là quan hệ logic qua BillIdRef (nullable).
- CheckoutId đối chiếu cùng lượt giữa hai service là quan hệ logic tối đa 1–1; bản ghi Ordering có thể chưa được tạo khi Billing đang Preparing. Không tạo FK xuyên database, không thêm FK từ CheckoutLock đến Restaurant/Ordering.Order.
- ReleasedAt biểu thị đã cho phép gọi thêm trở lại; không biểu thị đã thanh toán hoặc đã kết thúc phiên. Giữ NULL khi đang chặn gọi thêm, bao gồm trạng thái Ready; không tự ghi trường này khi thu tiền thành công ở đợt sau.
- SourceHash chỉ được ghi khi đã chốt dữ liệu thật; NULL trong thời gian chờ xác nhận. BillId/BillIdRef được gắn sau khi tạo/đồng bộ được bill, không dùng ID giả.

### File SQL, Database First và trạng thái áp dụng

- Schema database mới: docker/sql/schema/BillingDb.sql và docker/sql/schema/OrderingDb.sql, chỉ bổ sung DDL hai bảng.
- Ở đợt checkout trước, database đang dùng được nâng cấp bằng `BillingCheckout.sql` và `OrderingCheckout.sql`. Hai script đã được gỡ khỏi backend mới vì chỉ chấp nhận cấu trúc cũ chưa có `RestaurantId`. Schema hiện tại nằm trong `docker/sql/schema`. Việc gỡ file không thay đổi bảng, khóa, index, ERD hoặc dữ liệu đang dùng.
- docker/sql/init.sh gọi hai upgrade cho database Docker đã khởi tạo và cho database mới. Không dùng schema tạo mới để reset database hiện có.
- Đã áp dụng lên BillingDb và OrderingDb của SQL Server Docker hiện tại; đã chạy lại upgrade hai lần và kiểm tra. Không áp dụng thay cho các database trên máy thành viên khác. Không chỉnh/push repository Database-SQLServer riêng trong đợt này.
- Đã scaffold từ SQL Server bằng dotnet-ef 8.0.24, đồng phiên bản EF Core của DAL; chỉ nhập model BillCheckout/CheckoutLock, mapping DbContext liên quan và navigation Bill.BillCheckouts. Cập nhật danh sách bảng trong hai efpt.config.json. Không tạo migration hoặc ghi connection string vào model.
- Đây là thay đổi chưa commit. File và commit bằng chứng hiện tại: DDL/upgrade trong working tree; chưa có mã commit cho đợt này.

### Luồng và ranh giới service

- Gateway cung cấp Request/GetCurrent/Synchronize/Cancel theo TableSessionId. Request nhận UUID ở Idempotency-Key; backend vẫn xác thực QR hoặc nhân viên, không dùng UUID làm bằng chứng quyền truy cập.
- Billing lưu Preparing trước RPC giữ phiên. Ordering dùng cùng application lock Ordering.Checkout.Session:{sessionId} trong transaction cho tạo đơn, xác nhận đơn và thay đổi hủy món có ảnh hưởng giá. Unique filtered index bảo vệ thêm mỗi phiên chỉ có một lượt chưa mở lại.
- Tạo đơn: lấy khóa trước kiểm tra/ghi; request có key đã thành công được trả lại kết quả cũ. Đơn commit trước checkout được đối chiếu; đơn mới sau khi giữ phiên bị chặn, kể cả thiết bị khác và nhân viên gọi API.
- Ordering WaitingConfirmation vẫn giữ chặn gọi thêm; Waiter xác nhận các đơn đã nhận theo nghiệp vụ cũ. Không tự xác nhận, không bỏ món pending, không chuyển Ready khi còn pending. Khi không còn pending, Begin lặp lại chốt Frozen và hash dữ liệu tính tiền.
- Cashier Synchronize tiếp tục đúng lượt, tạo hoặc refresh bill từ snapshot Ordering đã giữ, giữ VAT/phí snapshot của bill có sẵn, trừ phần hủy theo nguồn. Bind phải được Ordering xác nhận trước khi Billing trả Ready. Không dùng nguyên tổng bill tạm cũ.
- Helper hash trong Shared.Common.Billing chỉ gồm OrderItemId, ItemId, số lượng dương, giá cơ bản, option ID/số lượng/phụ thu; chuẩn hóa thang decimal và thứ tự. Không chứa trạng thái nấu/phục vụ. Bill.SourceFingerprint trong JSON dùng hash tài chính này; món pending làm SourceStatus Stale. Bill cũ với fingerprint dạng trước cần refresh/checkout sync để xác minh theo dạng mới; không đổi tiền của bill bằng việc đọc.
- Checkout Frozen/Ready chặn tạo món mới, xác nhận thêm tập món và hủy/khôi phục món làm thay đổi tiền. Nấu, Ready, Served và làm lại không thay đổi tập tính tiền vẫn được phép. Public Bill.Create/Refresh không được vượt qua checkout đang hoạt động; đường đồng bộ nội bộ kiểm tra đúng CheckoutId dưới khóa Billing theo phiên.
- Hủy: chỉ Cashier đúng chi nhánh, chưa có Payment hoặc bill finalized. Lưu ReleasePending, rồi Ordering xác minh lại bằng RPC Billing.AuthorizeRelease trước khi mở khóa. Billing chỉ trả Released/orderingReopened=true sau acknowledgement.
- Ordering lưu bản ghi Released kể cả khi Begin cùng ID chưa đến, để Begin đến trễ không làm sống lại checkout. Hủy lặp ID cũ không mở khóa một checkout mới. Billing đọc lại bản ghi sau mỗi transaction/RPC, không ghi đè trạng thái hủy từ entity cache cũ.
- Timeout/mất phản hồi giữ Preparing, Frozen hoặc ReleasePending để tiếp tục đúng lượt; không tự hết hạn, không unlock trong catch hoặc khi service restart. GetCurrent chỉ đọc; Request/Synchronize/Cancel lặp tiếp tục công việc.
- Quyền bill/quầy cũ giữ nguyên. Guest chỉ yêu cầu/xem checkout bằng phiên QR hợp lệ; Cashier đúng chi nhánh mới synchronize/cancel. Admin/Manager không tự được cấp quyền ghi. Restaurant/Identity/Catalog không bị sửa nghiệp vụ hoặc schema; các tích hợp đều qua RPC.
- Đợt này chưa có thu tiền, xử lý kết quả Payment/PayOS, đóng phiên hoặc dọn bàn. Bước thu tiền sau này phải dùng Ready/hash và khóa Billing theo phiên, chặn thu khi ReleasePending. Thành công chỉ cập nhật bill/payment, giữ chặn gọi thêm; không đóng TableSession, không revoke QR. Đóng phiên, QR và CleaningStatus là luồng riêng chưa triển khai ở đây.
- Nếu chưa có bill và nguồn không còn món hợp lệ, hoặc thiếu TaxFeePolicy, Synchronize trả lỗi và giữ checkout chưa Ready; không tạo bill/dòng/giá trị thuế giả. Cashier có thể hủy khi đủ điều kiện.

### Bằng chứng kiểm thử

- Bài test nghiệp vụ dùng SQLite/client giả lập được báo riêng với SQL Server và Gateway thật. Có test mất phản hồi Begin/Bind/Release, tiếp tục cùng lượt, không nhân đôi bill, giá/phí snapshot, thiếu cấu hình và quyền ghi.
- Gateway thật dùng database CheckoutCheck tạm, Identity/Restaurant/Catalog/Ordering/Billing thật. Guest join qua API có cookie thực; tạo cart, gửi đơn và Waiter xác nhận qua API thật. Dữ liệu nền menu/phiên/đơn cũ được seed riêng, không phải dữ liệu production.
- 82 kiểm tra Gateway chính đạt, gồm hai thiết bị, pending không Ready, đồng bộ bill cũ, chặn hủy món frozen, nấu/phục vụ tiếp tục, restart hai service giữ lượt, hủy có acknowledgement và phân quyền chi nhánh. Bốn lượt submit/checkout đồng thời: cả bốn lượt đơn thắng trước và trả 201; từng món đều được tính đúng sau xác nhận, subtotal tăng 50000 mỗi lượt. Nhánh chốt trước chặn đơn bằng 409 được kiểm tra bằng thiết bị thứ hai sau khi chốt được chấp nhận; không tuyên bố bốn race đã bao phủ cả hai thứ tự thắng.
- 8 kiểm tra timeout qua Gateway thật đạt: cố ý giữ application lock SQL cho Begin/Release vượt deadline; Preparing/ReleasePending vẫn được lưu, orderingReopened=false, retry cùng ID tiếp tục và chỉ mở lại sau acknowledgement. Đây là thử lỗi hạ tầng cho checkout, chưa phải thử thanh toán thất bại.
- 13 kiểm tra Gateway bổ sung đạt với trạng thái Paid được mô phỏng bằng SQL chỉ trong database fixture: checkout đang giữ không cho gọi thêm hoặc hủy mở lại; GuestAccess vẫn hợp lệ, TableSession vẫn Open/ClosedAt=NULL. Không có API thu tiền hoặc giao dịch Payment thật trong thử nghiệm này.
- Lượt test solution cuối: 779 pass, 54 conditional skip, 0 fail; riêng Billing 200 pass/17 skip, Ordering 45 pass/0 skip. Không tính skip là đã kiểm chứng. Hai lỗi Foundation về đăng ký generic repository cho model mới đã được sửa đúng theo quy ước DI; không bỏ hoặc sửa test Foundation để che lỗi.
- 6 BillSqlServerTests đạt trên SQL Server thật, không skip, gồm upgrade lặp giữ dữ liệu và unique filtered index chỉ cho một lượt chưa release. Những test SQL này dùng client nguồn/quyền giả lập; 103 kiểm tra Gateway nêu trên dùng service thật.
- Toàn bộ bằng chứng cục bộ nằm trong .ai/checkout (Git ignore). Dữ liệu fixture/secret/cookie không đưa vào commit.
- Đã dừng/xóa nhóm checkout-check và xóa đúng năm database fixture CheckoutCheck của lượt thử, không xóa database thường. Ordering/Billing/Gateway thường đã được build/restart; Swagger có các route checkout và kiểm tra anonymous qua Gateway → Billing → Ordering trả 401. Lượt kiểm tra 103 tình huống hợp lệ/lỗi ở trên chạy trên các database cô lập có dữ liệu seed, không trên dữ liệu nghiệp vụ thường.
- Chưa chạy GitHub Actions cho working tree chưa commit/push. Không coi CI của commit nền là CI của đợt này.

Request mẫu tạo chính sách ngay (tỷ lệ minh họa cho test, không phải khuyến nghị mức thuế):

```json
{
  "vatRate": 0.08,
  "serviceRate": 0.05,
  "serviceEnabled": true,
  "effectiveFrom": null
}
```

Để lên lịch, thay effectiveFrom bằng ISO có Z/offset còn ở tương lai. ID/Version do backend trả về, không đưa vào request. Mọi response và lỗi API mới đều bằng tiếng Anh.


## 2026-10-28 — Thu tiền mặt theo checkout đã chốt

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Payment, PaymentMethod, CashMovement, CashShift, Bill và BillCheckout đều đã tồn tại; không tạo bảng/model/DbContext mới, không cần scaffold. Không thay đổi cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK, FK, UNIQUE, CHECK, index hoặc dây nối/cardinality trên ERD. Không thêm FK xuyên database.

| Database/bảng | Thay đổi thực tế | File | Trạng thái |
| --- | --- | --- | --- |
| BillingDb.PaymentMethod | Chỉ thêm dữ liệu `MethodCode='Cash'`, `IsEnabled=1` nếu chưa có. Giữ nguyên bản ghi đã có kể cả bị tắt. Không DEFAULT, không thêm phương thức khác. | docker/sql/seed/BillingDb.sql; docker/sql/upgrades/BillingCashMethod.sql | Đã cập nhật file và áp dụng script vào BillingDb Docker hiện có; kiểm tra script lặp/không bật lại Cash bị tắt trên SQL Server fixture. |
| Các bảng Billing còn lại | Ghi dữ liệu nghiệp vụ qua API, không đổi schema. Không seed Payment/CashShift/CashMovement giả vào database thường. | PaymentService và repository tương ứng | Kiểm thử dữ liệu riêng, xem bằng chứng bên dưới. |

init.sh chạy data upgrade cho cả database mới và database đã tồn tại. Script upgrade dùng transaction và UPDLOCK/HOLDLOCK để chạy lặp an toàn. Bản ghi data seed không làm thay đổi hình ERD. Working tree chưa commit/push; không có commit mới để dẫn chứng, không sửa lịch sử Git.

### Quy tắc và dữ liệu được ghi

- POST /api/bills/{billId}/payments/cash: chỉ Cashier được phân công chi nhánh, trong ca Active của chính tài khoản. Xác thực account/session/role/assignment qua tích hợp Identity/Restaurant hiện có; không nhận danh tính từ body.
- Ready checkout, bill Unpaid, không Payment, nguồn Ordering Current/hash khớp và Bind xác nhận Frozen là điều kiện trước khi ghi. Giá/option/hủy và VAT/phí giữ snapshot đã tính; cash không tính lại chính sách hay dùng số tiền client tự khai.
- Payment: Paid, Amount=Bill.TotalAmount, CashReceived, ChangeAmount, CashShiftId, MethodCode, IdempotencyKey UUID chuẩn hóa N, PaidAt UTC, ConfirmedByRef và VerificationSource=Cashier. Không điền ProviderOrderCode/ProviderTransactionId giả.
- CashMovement: đúng một Sale/Payment, SignedAmount=Payment.Amount, PostedAt=PaidAt, cùng CashShiftId, khóa Sale:{PaymentId:N}. CashReceived/ChangeAmount chỉ lưu ở Payment, không ghi lần nữa vào sổ quỹ.
- Bill: Status=Paid/PaidAt. BillCheckout: Status=Paid, ReleasedAt vẫn NULL. Ordering.CheckoutLock giữ Frozen/chưa release, không đổi schema hoặc code Ordering. Không gọi mở khóa, đóng TableSession, thu hồi GuestAccess hoặc đổi CleaningStatus.
- Thứ tự khóa trong transaction: khóa idempotency payment → khóa Billing theo session → khóa register/shift → bill/method; soát lại quyền và trạng thái trước ghi. Cùng key/payload trả lại biên nhận cũ; key khác trên bill Paid bị chặn. Ca và Cash có thể đã đóng/tắt khi đọc lại biên nhận thành công; quyền hiện hành vẫn bắt buộc.
- Chưa hỗ trợ bill 0, thanh toán một phần/nhiều phương thức, PayOS, hoàn tiền, in biên nhận, đóng ca hoặc ghi thu/chi ngoài bán hàng. Không triển khai bước tiếp theo.

### Bằng chứng và giới hạn kiểm thử

Bằng chứng cục bộ trong .ai/billing-cash (Git ignore); không đưa cookie/token/secret hoặc dữ liệu test vào commit. Các con số được cập nhật sau khi chạy hoàn tất. Test SQL tạo database CodexBillingCash riêng và tự xóa; Gateway dùng năm database CashPaymentCheck riêng, dữ liệu nền seed chỉ để kiểm thử. Không coi dữ liệu seed là dữ liệu nghiệp vụ thực tế.

Kết quả cuối của đợt:

- Build solution đạt, 0 lỗi; build Docker Billing/Gateway/db-init đạt. Rà diff không có lỗi whitespace và không sửa nghiệp vụ/source/schema Identity, Restaurant, Catalog hoặc Ordering.
- Test solution: **800 pass, 59 conditional skip, 0 fail**. Riêng Billing: **221 pass, 22 SQL conditional skip** trong lượt thông thường. Chạy riêng BillSqlServerTests + CashPaymentSqlServerTests với SQL Server thật: **11 pass, 0 skip, 0 fail**. Không tính toàn bộ skip là đã đạt; các SQL test cũ khác không nằm trong lượt 11 này.
- SQL test mới: khác key chỉ một Payment/Sale; cùng key cùng receipt; Pay/Cancel không cùng thắng; lỗi ghi ledger rollback Payment/Sale/bill/checkout rồi retry thành công; data seed lặp không bật lại Cash bị tắt. Các bài SQL này dùng client nguồn/quyền giả lập, không gọi service ngoài thật.
- Gateway thật được kiểm tra theo các chặng trên cùng bộ năm database CashPaymentCheck riêng, với Identity/Restaurant/Catalog/Ordering/Billing thật. Guest join/cart/order và Waiter confirm qua API thật; dữ liệu menu/phiên/đơn nền là seed test. Bao phủ gọi thêm trước chốt → pending → Waiter xác nhận → Ready → Cashier thu tiền → đọc receipt. Các vòng sửa kỳ vọng của script thử không thay đổi source nghiệp vụ.
- Bill thật trong fixture: subtotal 154000.50, tổng 177871, khách đưa 200000, thừa 22129. Cùng key trả 200/cùng receipt; key khác hoặc payload khác 409. Hai request cùng key nhận 201+200; hai key khác nhau nhận 201+409. Kiểm tra Admin/Manager/Waiter/Kitchen không thu, thiếu phiên/guest không thu, thiếu tiền/số tiền không đủ/số lẻ dưới đơn vị đồng, thiếu hoặc tắt Cash, ca đóng, ca của người khác, phân công bị thu hồi, khác chi nhánh và Manager không đọc receipt ngoài chi nhánh.
- Đối soát SQL sau Gateway: **3 Payment, 3 Sale, tổng net 418113, tender 600000, change 181887**; tất cả khớp bill và cùng timestamp/ca, không provider ID giả. Ba checkout Billing đều Paid/chưa release; ba lock Ordering vẫn Frozen/chưa release. Phiên bàn vẫn Open/ClosedAt NULL, GuestAccess chưa bị thu hồi, CleaningStatus không bị đổi. QR me qua API vẫn 200; order mới vẫn 409. Kitchen Cooking/Ready và Waiter Serve sau Payment thật đều 200.
- Restart Billing/Ordering giữ Paid, retry cùng key vẫn trả receipt cũ và không mở lại gọi món. Replay vẫn thành công khi ca đã đóng và Cash đã tắt bằng SQL chỉ trong fixture. Không tuyên bố đã mô phỏng mất điện đúng thời điểm commit hoặc timeout thanh toán vật lý; lỗi dependency được kiểm tra bằng client giả, lỗi ghi DB được kiểm tra trên SQL thật.
- Đã xóa đúng nhóm container cash-payment-check và năm database fixture của lượt thử, giữ dữ liệu backend thường. Billing/Gateway thường đã build/restart; Swagger có hai route Payment mới, GET receipt không Bearer trả 401. Cash/IsEnabled=1 đã kiểm tra trên BillingDb thường. Không tạo Payment demo trên database thường.
- Chưa chạy GitHub Actions cho working tree chưa commit/push. Giữ HEAD 04f77fd trên feature/Billing; không tạo commit mới. Dừng ở thu tiền mặt, không tự triển khai phần kế tiếp.


## 2026-10-28 — Thu/chi tiền mặt ngoài bill trong ca

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK, FK, UNIQUE, CHECK, index hoặc quan hệ/cardinality. Không có SQL upgrade, migration, scaffold, data seed hoặc thay đổi repository Database-SQLServer của đợt này. CashMovement/CashShift/Payment đều là bảng có sẵn; các DTO/service mới không phải bảng mới.

- Đề xuất PostedByRef đã bị từ chối và **không triển khai**. Không lưu actor trong Reason/IdempotencyKey. Khách hàng đã đồng ý suy ra người thực hiện khoản ngoài bill từ chủ ca, với điều kiện chỉ Cashier ghi trong ca Active của chính mình và chưa hỗ trợ chuyển người sở hữu ca.
- CashMovement mới ghi CashMovementId, CashShiftId, Type=CashIn/CashOut, SignedAmount dương/âm do backend quyết định, Reason bắt buộc, PostedAt UTC, IdempotencyKey có namespace ManualCash và UUID của request. PaymentId giữ NULL. Namespace phân biệt request ngoài bill với Sale của Payment, không mã hóa người thực hiện trong key.
- Mỗi Sale đã có PaymentId; người xác nhận lấy Payment.ConfirmedByRef. Manual mới trả PerformedByRef=CashShift.CashierUserRef, PerformerSource=ShiftOwnerDerived. Sale có nguồn trả PaymentConfirmation. Legacy không xác minh được nguồn trả NULL/Unknown, không tự gán chủ ca thành người đã thao tác. Không có actor snapshot riêng từng dòng; sửa chủ ca trực tiếp trong SQL là giới hạn không được khắc phục bởi thiết kế giữ nguyên schema này.
- Amount dương, số nguyên VND trong giới hạn decimal(18,2), Type chỉ CashIn/CashOut, Reason trim 1-1000 ký tự, không control và lưu được vào varchar CP1252 hiện có. Lỗi ký tự không hỗ trợ trả 400, không đổi cột hoặc lưu chuỗi bị mất ký tự.
- Quỹ dự kiến = CashShift.OpeningCash + SUM(CashMovement.SignedAmount). Bao gồm Sale đúng một lần; không cộng thêm Payment hoặc CashReceived, không ghi lại tiền đầu ca. Không phải số tiền kiểm đếm thực tế. Chặn CashOut vượt quỹ; cho rút đúng bằng quỹ để về 0.
- Create khóa request key rồi register/shift, kiểm tra lại account/session/role/branch/owner/trạng thái ca, tính lại tổng và ghi trong cùng UoW transaction. Payment hiện có cũng khóa register/shift nên chia sẻ cơ chế chống chi vượt quỹ. Không sửa PaymentService hoặc thêm unique index.
- Retry cùng key/canonical payload trả dòng cũ; khác payload hoặc ca trả 409. Retry thành công sau khi ca đóng không ghi mới; request mới khi ca đóng bị chặn. Không có endpoint Update/Delete. Read ledger khóa register/shift để tổng, số dòng và trang dữ liệu cùng snapshot; pagination không giới hạn phần tổng quỹ.
- Quyền xem giữ Admin toàn hệ thống, Manager/Cashier theo chi nhánh phân công. Chỉ Cashier đúng chủ ca được ghi; không cấp quyền ghi cho Admin/Manager. Client không được gửi PaymentId/actor/SignedAmount hoặc tự tạo Sale để tránh lặp khoản đã được Payment ghi tự động. Hai khai báo ngoài bill với hai key mới vẫn là hai giao dịch; không tuyên bố tự phát hiện cùng một khoản tiền vật lý chỉ từ số tiền/lý do.
- Chưa triển khai đóng/bàn giao ca, điều chỉnh/xóa bút toán, refund, kiểm đếm thực tế, PayOS, in phiếu hoặc tổng kết ca. Không thay đổi nghiệp vụ Identity/Restaurant/Ordering/Catalog, schema service khác hoặc hợp đồng đọc của chúng.

### Kiểm thử và trạng thái bàn giao

Bằng chứng cục bộ nằm trong .ai/billing-movements (Git ignore); không commit fixture, token hoặc secret. Source bắt đầu sạch tại 05fa991 trên feature/Billing. Không commit/push hoặc sửa lịch sử Git.
- Build solution và Docker Billing/Gateway đạt, không có lỗi. Các cảnh báo nullable từ Identity.Common/Shared.Persistence là code có sẵn, không sửa ngoài phạm vi.
- Test solution: **816 pass, 63 conditional skip, 0 fail**; riêng Billing **237 pass, 26 SQL conditional skip** trong lượt thường. Chạy riêng CashMovementSqlServerTests và CashPaymentSqlServerTests: **9 pass, 0 skip, 0 fail** (4 test SQL mới và 5 test hồi quy thu tiền). Không tính các SQL test cũ còn skip là đã kiểm chứng.
- SQL Server test dùng database CodexBillingLedger/CodexBillingCash cô lập, source/access giả lập. Bao phủ hai CashOut khác key không âm quỹ; cùng key chỉ một dòng; Cash Payment đồng thời với CashOut dùng cùng khóa ca; lỗi ghi SQL rollback rồi retry đúng một lần. Không mô phỏng mất điện đúng mốc commit.
- **64 kiểm tra Gateway đạt**, dùng Identity/Restaurant/Catalog/Ordering/Billing thật trên năm database CashLedgerCheck riêng. Guest join và checkout qua API thực; menu/phiên/đơn nền là dữ liệu seed test. Các tình huống quyền, thiếu/sai input, request lặp, thanh toán và thu/chi chạy qua Gateway/gRPC đầy đủ; ca đóng và đổi chủ ca được mô phỏng bằng SQL chỉ trong fixture để kiểm tra chặn ghi, không phải API đóng/chuyển ca đã triển khai.
- Ví dụ kiểm chứng: OpeningCash=1000, CashIn=200, CashOut=300, Payment Sale=120121. Hai CashOut 80000 đồng thời trả 201+409; hai request cùng key cho CashOut 1 trả 201+200/cùng ID. Cuối lượt: **5 CashMovement = 1 CashIn (+200), 3 CashOut (-80301), 1 Sale (+120121)**; MovementTotal=40020, ExpectedCash=41020. Có đúng **1 Payment**, tender=200000, change=79879. Tổng không cộng lại Payment/tender/change hoặc quỹ đầu ca lần hai.
- Read trả đúng ShiftOwnerDerived cho khoản ngoài bill mới, PaymentConfirmation cho Sale. Legacy Unknown được kiểm tra trong bài test nghiệp vụ với client giả lập/SQLite; không tuyên bố đã xác minh được người thao tác của dữ liệu legacy thực tế.
- GET ledger phân trang vẫn trả tổng quỹ toàn ca; Manager không xem được chi nhánh khác; thu hồi phân công chặn cả đọc và ghi. Sau restart Billing, retry cùng key vẫn trả dòng cũ. Ca đã đóng không ghi được khoản mới, retry thành công không tăng số dòng, read vẫn được theo phạm vi.
- Đã dừng/xóa đúng nhóm cash-ledger-check và năm database fixture của lượt thử; giữ các database thường. Không tạo manual movement hoặc dữ liệu Payment demo trong BillingDb thường, không chạy upgrade hoặc đổi schema. Billing/Gateway thường đã build/restart; Swagger có ba thao tác mới và anonymous GET ledger trả 401.
- Rà diff chỉ có Billing, billing.proto, Gateway integration, Tests/Billing.Tests, README và nhật ký này. Chưa chạy GitHub Actions, chưa commit/push. HEAD vẫn 05fa991 trên feature/Billing, giữ nguyên các commit cũ. Dừng ở đợt thu/chi ngoài bill.


## 2026-10-28 — Đóng ca và đối soát tiền mặt

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK, FK, UNIQUE, CHECK, index hoặc cardinality/dây nối. Không có migration, upgrade, scaffold hoặc data seed mới; không sửa repository Database-SQLServer.

| Bảng có sẵn | Cột sử dụng / ràng buộc đã tồn tại | Cách dùng trong đợt này |
| --- | --- | --- |
| BillingDb.ShiftReport | ShiftReportId uniqueidentifier NOT NULL PK; CashShiftId uniqueidentifier NOT NULL FK nội bộ CashShift và UQ_ShiftReport_CashShiftId | Tạo đúng một báo cáo cho mỗi ca, quan hệ CashShift 1–0..1 ShiftReport đã có sẵn. Không tạo thêm bảng từ DTO/repository mới. |
| BillingDb.ShiftReport | ExpectedCash, ActualCash, CashHandleOver đều decimal(18,2) NOT NULL; DifferenceReason varchar(1000) NULL; ClosedAt datetime NOT NULL; không DEFAULT mới | ExpectedCash chốt từ sổ quỹ. ActualCash là toàn bộ tiền kiểm đếm trước khi lấy ra nộp. CashHandleOver nhập rõ 0..ActualCash; lý do bắt buộc khi lệch bất kỳ chiều nào. |
| BillingDb.CashShift | Status varchar(50) NOT NULL, ClosedAt datetime NULL đã có | Cập nhật Closed và cùng thời điểm UTC với báo cáo trong một transaction. Không sửa owner hoặc OpeningCash. |
| BillingDb.Payment/CashMovement/CashHandover/Refund/Bill | Chỉ đọc các cột/quan hệ sẵn có | Đối soát tiền và chặn luồng chưa hỗ trợ/dữ liệu sai, không tự đổi trạng thái giao dịch. Không thêm FK xuyên database. |

Không có cột Difference, RemainingCash hoặc ClosedBy mới: hai số tiền đầu được tính từ snapshot Report đã lưu; người đóng được suy ra từ chủ ca do chỉ chủ ca được Close, chưa hỗ trợ chuyển chủ ca. Không nhét actor/metadata vào Reason hoặc key. Không suy luận có model/repository/service mới là có bảng mới.

### Quy tắc và phạm vi

- Chỉ Cashier đúng chi nhánh đóng ca Active của chính mình; đọc giữ Admin toàn hệ thống và Manager/Cashier theo RestaurantStaff hiện hành qua tích hợp Restaurant/Identity cũ. Không sửa quyền hoặc nghiệp vụ các service khác.
- Request yêu cầu ActualCash và CashHandleOver nhập rõ, không mặc định 0. ActualCash là số tiền trước khi nộp; CashHandleOver không vượt ActualCash. Difference=ActualCash−ExpectedCash; thiếu/thừa đều cần DifferenceReason. RemainingCash=ActualCash−CashHandleOver. Không ghi CashOut bổ sung lúc đóng/nộp để tránh giảm sổ lần hai.
- Preview chứa tổng sổ, số Payment và đầy đủ blocker/ID cần kiểm tra. CanClose là khả năng đối soát/trạng thái, không cấp quyền đóng cho vai trò chỉ đọc. Close đọc lại mọi dữ liệu dưới khóa register/shift và kiểm tra quyền/owner/ca hiện hành trong UoW transaction.
- Ca chỉ được chốt với Payment Cash/Paid/VerificationSource Cashier, số tiền/tiền đưa/thừa khớp bill Paid và đúng một Sale cùng ca/số tiền/thời điểm. Block Payment chưa Paid, phương thức/luồng provider/refund chưa hỗ trợ, mọi CashHandover chưa có workflow xác minh, Sale lẻ/sai/trùng, nhiều Payment cho cùng bill (kể cả khác ca), key sổ trùng, bút toán không xác định hoặc sai dấu/lý do, số dư âm/không lưu được vào report. Không tự đánh dấu giao dịch thất bại/hết hạn/hoàn thành.
- Một report/shift dùng UQ đã có và khóa ca làm định danh tự nhiên cho retry. Cùng input chuẩn hóa trả report cũ (200); lần đầu 201; khác input 409. Ca đã đóng không có report hoặc Active lại có report bị xem là không nhất quán, không tự sửa hoặc tạo báo cáo thay thế.
- CashShift Closed/ClosedAt và ShiftReport ghi cùng transaction. Payment/thu chi ghi thành công trước chốt phải vào ExpectedCash; sau chốt bị chặn. Retry giao dịch đã thành công không tạo bút toán mới. Báo cáo đọc từ snapshot, không tính lại theo ca sau.
- Bill/checkout chưa gắn CashShift không bị suy ra thuộc ca hiện tại. Giữ nguyên bill Unpaid/checkout Ready/Frozen; không release checkout, đóng TableSession, vô hiệu QR, đổi CleaningStatus hoặc logout Identity. Không tự mở ca mới hay lấy RemainingCash làm OpeningCash ca sau.
- Scope hiện tại là đối soát tiền mặt. Schema không lưu snapshot doanh thu/phương thức/chi tiết ledger trên report; các tổng chi tiết Preview không phải snapshot doanh thu lịch sử. Chưa tích hợp bank/PayOS, xác nhận người nhận bàn giao, sửa/mở lại báo cáo, tự gửi Manager hoặc signout thiết bị. Manager xem qua API báo cáo theo chi nhánh. Không tuyên bố hoàn tất toàn bộ UC_CAS_09 của Excel.

### Kiểm thử và trạng thái bàn giao

Bắt đầu từ working tree sạch, HEAD 806a383 trên feature/Billing. Chưa commit/push hoặc sửa lịch sử. Bằng chứng cục bộ trong .ai/billing-close, Git ignore; fixture/token/secret không đưa vào commit.

Kết quả kiểm chứng cuối:

- Build solution đạt, 0 lỗi; build Docker Billing/Gateway đạt. Có các cảnh báo nullable có sẵn ở Identity.Common/Shared.Persistence trong lượt build sạch, không sửa ngoài phạm vi.
- Test solution cuối **840 pass, 68 conditional skip, 0 fail**; riêng Billing **261 pass, 31 SQL conditional skip** trong lượt thường. Chạy riêng ShiftReportSqlServerTests + CashMovementSqlServerTests + CashPaymentSqlServerTests trên SQL Server thật: **14 pass, 0 skip, 0 fail** (5 đóng ca mới + 9 hồi quy). Những SQL test cũ khác còn skip không được tính là đã đạt.
- Các test SQL dùng client nguồn/quyền giả lập và database CodexBillingClose/… tạm. Bao phủ Close cùng input chỉ một report (201+200), khác input một thắng/một 409, Close đấu với manual posting/Payment không bỏ sót giao dịch đã ghi, lỗi insert report rollback cả report và trạng thái/thời điểm ca rồi retry thành công. Chưa mô phỏng mất điện đúng mốc commit; không coi giả lập lỗi DB là kiểm thử toàn bộ sự cố phần cứng.
- **87 kiểm tra Gateway chính đạt**, với Identity/Restaurant/Catalog/Ordering/Billing thật, phiên guest join qua API và năm database ShiftCloseCheck cô lập. Menu/phiên/đơn nền là dữ liệu seed test. Trạng thái Pending/luồng BankTransfer/CashHandover/Sale sai được mô phỏng bằng SQL chỉ trong fixture rồi khôi phục để chứng minh preview nhận diện và Close chặn; không phải đã triển khai API thu chuyển khoản hoặc xử lý bàn giao.
- Kiểm tra bổ sung trên binary mới: 4 kiểm tra dữ liệu trùng qua Gateway đạt (không tính 5 lượt login lặp là tính năng mới). Tạo legacy Payment trùng bill ở ca khác và key bút toán trùng chỉ trong fixture; preview nêu DuplicateBillPayments/DuplicateMovementKey, Close trả 409, report cũ vẫn snapshot; khôi phục đúng những dòng mô phỏng rồi preview ca sau hợp lệ. Các request login diễn ra riêng, không bỏ qua xác thực.
- Báo cáo ca A thật: OpeningCash=1000; CashIn=200; CashOut=-100; Sale=120121; ExpectedCash=121221, ActualCash=121220 **trước khi nộp**, CashHandleOver=100000, Difference=-1, RemainingCash=21220. SQL xác minh đúng 3 CashMovement cho ca A, không ghi CashOut 100000, đúng 1 Payment, report và shift cùng ClosedAt. Ca B rỗng đóng với actual/handover=0 nhập rõ qua API thật, report 0; không dùng default giả.
- Cùng input đóng ca đồng thời trả 201+200/cùng report, khác input sau chốt 409. Các vai trò/phạm vi/chủ ca sai bị chặn; Manager chỉ liệt kê/đọc report chi nhánh phân công, Admin xem cả hai. Shortage/surplus thiếu lý do đều 400; thiếu ActualCash/CashHandleOver hoặc tiền nộp vượt tiền đếm đều 400; client không gửi được ExpectedCash/ClosedAt giả.
- Sau đóng ca: GetCurrent cũ 404, manual posting và Payment mới vào ca cũ 409; retry Payment đã thành công trả biên nhận cũ. Một bill Unpaid/checkout Ready khác chưa gắn ca giữ nguyên và Ordering vẫn Frozen/chưa release; TableSession/QR giữ nguyên. Billing restart giữ report ID/thời điểm cũ. Mở ca mới qua API cũ và ghi CashIn thật không đổi snapshot report cũ.
- Đã đối soát SQL, dừng/xóa đúng nhóm shift-close-check và năm database fixture của lượt thử. Không đóng ca hoặc tạo report demo trên database thường, không chạy DDL/upgrade/seed vào database thường. Billing/Gateway thường đã build/restart, Swagger có 5 thao tác mới; anonymous list/close đều 401.
- Rà diff chỉ gồm Billing, billing.proto, Gateway integration, Tests/Billing.Tests, README và nhật ký này. Không sửa schema/source nghiệp vụ của Restaurant, Identity, Catalog, Ordering hoặc shared infrastructure. Chưa chạy GitHub Actions cho working tree chưa commit/push. HEAD giữ nguyên 806a383, dừng tại đối soát/đóng ca tiền mặt.



## Đợt 6 — PayOS/chuyển khoản tự động (2026-10-28, chưa commit)

### Các thay đổi cần cập nhật trên SQL/ERD

| Database/bảng | Thay đổi | Kiểu đầy đủ | NULL/default | Khóa/index/quan hệ | Bằng chứng và trạng thái |
| --- | --- | --- | --- | --- | --- |
| BillingDb.dbo.Payment (bảng có sẵn) | Thêm ProviderPaymentLinkId | varchar(100) | NULL; không DEFAULT | Không thêm/sửa PK/FK/UNIQUE/CHECK/index | docker/sql/schema/BillingDb.sql; docker/sql/upgrades/BillingPayOs.sql. Đã áp dụng Docker BillingDb tại backend-sqlserver-1 và scaffold bằng dotnet-ef 8.0.24; chưa có commit mới. |
| BillingDb.dbo.Payment (bảng có sẵn) | Thêm ProviderCheckoutUrl | varchar(2048) | NULL; không DEFAULT | Không đổi quan hệ | Cùng hai file trên; dòng cũ giữ NULL. |
| BillingDb.dbo.Payment (bảng có sẵn) | Thêm ProviderQrCode | varchar(max) | NULL; không DEFAULT | Không đổi quan hệ | Cùng hai file trên; dòng cũ giữ NULL. |
| BillingDb.dbo.PaymentMethod (bảng có sẵn) | Seed BankTransfer nếu thiếu | MethodCode varchar(50), IsEnabled bit có sẵn | Insert BankTransfer/1 chỉ khi chưa có; không tự bật lại dòng bị tắt | Giữ PK MethodCode, quan hệ PaymentMethod 1–n Payment đã có | docker/sql/seed/BillingDb.sql; upgrade BillingPayOs.sql; đã áp dụng Docker BillingDb và kiểm thử lặp trên DB cô lập. Đây là thay đổi dữ liệu, không thêm schema. |

Không thêm bảng. PaymentWebhook, BillCheckout, CashMovement, CashShift, ShiftReport đã tồn tại trước đợt này. Dây ERD giữ nguyên: Bill 1–n Payment; CashShift 1–n Payment; PaymentMethod 1–n Payment; Payment 1–n PaymentWebhook (PaymentId nullable). Chỉ thêm ba thuộc tính vào entity Payment trên hình ERD. Không có FK xuyên database hoặc schema thay đổi ở Restaurant/Ordering/Identity/Catalog.

Database First: cập nhật creation/upgrade, áp dụng upgrade bảo toàn dữ liệu, scaffold chọn Payment vào thư mục tạm (không sinh connection string), nhập đúng ba property/mapping vào Payment.cs/BillingDbContext.cs; giữ navigation/relationship hiện có. Script tạo mới khác script nâng cấp; không xóa/tạo lại database nhóm. init.sh gọi upgrade với cả database mới/cũ. Chưa sửa repository SQL độc lập trong Downloads; file SQL trong backend là bản đã cập nhật đợt này.

### Nghiệp vụ và code/RPC không đổi schema

- Tạo link chỉ Cashier đúng chi nhánh/ca Active của chính mình; seed BankTransfer không cấp quyền. GET theo phạm vi đọc Billing; Reconcile/Cancel chỉ chủ ca. Không xác nhận chuyển khoản thủ công/refund/thu hộ.
- Persist Creating và ProviderOrderCode trước HTTP; retry giữ mã/key. IdempotencyKey nội bộ ràng buộc UUID request với CheckoutId gốc; không thêm cột, không cho dùng lại UUID sang checkout/bill/shift khác. Khóa work riêng theo payment để worker/webhook/reconcile không chạy cùng payment; transaction khóa phiên/register/shift/bill trước ghi trạng thái. Không giữ khóa register/bill trong lúc chờ HTTP.
- Provider create/query/cancel và webhook HMAC theo hợp đồng chính thức. Query bắt buộc có amountPaid/amountRemaining/transactions rõ ràng. Không coi default 0, timeout, local expiry hoặc trang return/cancel là xác nhận không nhận tiền.
- 15 phút từ appsettings. Local expiry kích hoạt đối chiếu/hủy tại provider. Chỉ trạng thái terminal với totals=0/remaining=full/list trống và không có bằng chứng đã nhận tiền mới được thử phương thức/lượt mới. Unknown/CancelRequested/Review giữ chặn. Checkout chỉ mở lại qua thao tác Cashier hiện có sau khi Ordering xác nhận; thanh toán không tự release.
- Paid cần một transfer đủ, ref/link/order/số tiền/Ready hash đúng, ca còn Active. Cập nhật Payment/Bill/checkout cùng transaction; không Sale, không double-paid, không đóng phiên/QR/dọn bàn. PaidAt là UTC backend xác minh; không bịa timezone của timestamp ngân hàng.
- PaymentWebhook lưu hash/dedupe/signature/audit vào bảng có sẵn; không lưu raw payload/secret. Webhook có bằng chứng nhận tiền được persist trước truy vấn; provider timeout không làm mất claim. Request lặp không apply lần hai; conflict tiền/ID/ref hoặc late/thiếu/thừa chuyển ReviewRequired. Review không tự gỡ.
- Chốt ca chấp nhận bank Paid đã xác minh và terminal-zero đúng điều kiện; các payment này không tạo Sale. Preview BankTransferTotal thêm trong DTO/proto, không thêm cột báo cáo hay tự nhận đã có snapshot doanh thu bất biến. Cash receipt/thu chi ngoài bill giữ luồng cũ; chỉ bỏ qua verified-zero khi quyết định có payment đang giữ/đã thu.
- appsettings chung không secret, Enabled=false. appsettings.Local.json ignored được nạp thực sự; environment/CLI ưu tiên sau Local. Compose nhận biến PAYOS_...; shared .env có tracked nên credential phải nằm ở .env.payos.local ignored hoặc shell. Đã thêm Local.example chỉ chứa giá trị trống.
- Link/QR có thể NULL nếu mất create response và provider query không trả lại; không tái dựng giả. Không có review override/refund trong phạm vi. Giao dịch không thể xác minh tiếp tục bị chặn và cần đối soát merchant.

### Kiểm chứng và giới hạn

Kết quả cuối trên source đợt này:

- Build/publish Docker Billing và Gateway đạt. Build db-init và kiểm tra bash -n đạt (không chạy toàn bộ initializer lên các database khác). Normal Gateway sau cập nhật trả Swagger 200, API bank không có phiên nhân viên trả 401. PayOS mặc định tắt/thiếu credential không ngăn khởi động nghiệp vụ cũ.
- Test solution: **886 pass, 73 conditional skip, 0 fail**, đủ 9 test assemblies. Các lượt skip không được tính đạt: 36 Billing SQL tests, 36 Restaurant SQL tests và 1 AI model test trong lượt thường. Build sạch có nullable warnings đã tồn tại ở Shared.Persistence/Identity.Common; không sửa ngoài scope.
- Chạy toàn bộ Billing.Tests bằng .NET 9 trên SQL Server Docker thật: **343 pass, 0 skip, 0 fail**. Trong đó 5 PayOsSqlServerTests mới kiểm chứng cùng key/different keys đồng thời, webhook lặp đấu reconcile, rollback khi ghi Paid lỗi rồi phục hồi bằng service/context mới, upgrade lặp giữ payment cũ và phương thức bị tắt. Nguồn quyền/đơn và provider trong nhóm SQL tests được giả lập; không gọi đây là toàn luồng service thật.
- Gateway chính **46 request kiểm tra đạt**, bổ sung **19 request kiểm tra đạt** cho restart/worker/đóng ca; số này gồm cả đăng nhập/thiết lập fixture, không phải 65 nghiệp vụ khác nhau. Gateway/Identity/Restaurant/Ordering/Billing thật trên DB PayOsCheck riêng, nguồn menu/đơn nền seed, provider **HTTP test double có HMAC**, không phải PayOS thật. Binary cuối đã được đối chiếu image ID và chạy với bộ test này.
- Bao phủ Cashier/chủ ca/chi nhánh/vai trò sai; same-key replay/different-key 409; Pending/Unknown chặn Cash; chữ ký webhook sai không được nhận; webhook/replay xác minh Paid; zero được provider xác nhận mới đổi Cash; tiền đến muộn sau Cash thành Review và chặn close; không double-paid, không duplicate Sale.
- Kiểm tra restart: persist Unknown trước HTTP, khởi động lại rồi replay giữ PaymentId/order code; worker startup tự đối chiếu và Paid không cần giả danh Cashier hoặc webhook; GET/reconcile sau ca đóng không đổi PaidAt. Bản cuối còn kiểm tra tạo -> cancel zero -> lượt mới -> worker Paid.
- Ca B chuyển khoản 120121 đóng với OpeningCash/ExpectedCash/ActualCash=1000, CashHandleOver=0; bank không tạo CashMovement. Phiên QR guest vẫn được phép đọc checkout Paid sau thu tiền/đóng ca; checkout không Released, không mở lại Ordering. Phiên/đơn/kitchen và CleaningStatus không được thay đổi bởi thanh toán.
- SQL normal BillingDb xác minh đúng varchar(100)/varchar(2048)/varchar(max), NULL, default_object_id=0, BankTransfer đã seed. Trong fixture có đúng một Sale cho Payment Cash; bank không ghi thêm tiền mặt. Không sửa database nghiệp vụ khác ngoài các fixture test được tạo riêng.
- git diff --check đạt, không có file xóa hoặc đổi nghiệp vụ ngoài Billing; có proto Billing, Gateway, Compose/bootstrap SQL và tài liệu được phép. Danh mục đủ 60 file thực tế nằm ở .ai/billing-payos/changed-files.md (local/ignored); log không đưa credential/token vào Git.

**Chưa nghiệm thu PayOS thật:** còn thiếu merchant credentials và public HTTPS webhook đã đăng ký, chưa thực hiện chuyển tiền thật. Các ca partial/excess/invalid-ID/signature/network dùng fake client/HTTP provider và test chữ ký mẫu chính thức. Mất điện đúng lúc remote commit, sự cố ngân hàng thật và public webhook ngoài internet chưa được kiểm chứng; không đánh đồng restart/context mới với mọi sự cố hạ tầng. ReviewRequired được phát hiện/khóa nhưng không có chức năng xử lý/refund trong đợt này. Hướng dẫn cấu hình riêng, API Swagger và giới hạn được ghi ở README. Tham chiếu chính thức: https://payos.vn/docs/api/ ; https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/ ; https://payos.vn/docs/sdks/back-end/net/ ; https://payos.vn/docs/moi-truong-test/ .

Đã dừng stack payos-check và xóa đúng 25 database PayOsCheck trong các manifest fixture của đợt thử, không xóa database nhóm. Bằng chứng/log cục bộ giữ trong .ai/billing-payos (ignored).

HEAD gốc 643a0de, nhánh feature/Billing; các thay đổi vẫn chưa commit/stage/push. Nhật ký này thay thế trạng thái chuẩn bị trước khi người dùng chốt ba cột và quy tắc retry/closing.

## 2026-10-28 — Billing: bảo vệ số dư quỹ và đối chiếu PayOS trước đóng ca

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/DEFAULT, PK, FK, UNIQUE, CHECK hoặc index. Không có script upgrade mới và không đổi quan hệ/dây nối ERD. Giữ nguyên decimal(18,2) của CashShift.OpeningCash, CashMovement.SignedAmount và các số tiền ShiftReport/Payment.

- Mở ca chỉ nhận số nguyên đồng VND: 250000 và 250000.00 hợp lệ; 250000.50 bị từ chối, không làm tròn.
- CashIn và Sale tiền mặt kiểm tra số dư sau giao dịch trong transaction đã khóa register/shift. Vượt 9999999999999999.99 thì từ chối trước khi ghi; Payment/Bill/Checkout không được chuyển Paid khi Sale bị từ chối. Giữ cơ chế request lặp và khóa chung với đóng ca/CashOut.
- Worker tiếp tục đối chiếu PayOS Paid của ca chưa đóng, hoặc PaidAt trong khoảng PayOs:PaidReconcileHours (mặc định 24 giờ). Webhook vẫn được kiểm tra ngoài khoảng polling này. Không ghi thêm Sale hoặc thanh toán lần hai khi có tiền đến thêm.
- Lần đóng ca đầu phải có kết quả provider hiện hành cho các PayOS của ca, rồi kiểm tra lại tập/trạng thái payment dưới khóa ca. Kết quả không rõ hoặc tập payment thay đổi thì chặn. Lặp lại báo cáo đã đóng không gọi provider lại. Preview không bảo đảm thao tác Close sẽ thành công.
- Dữ liệu cũ không tự sửa: truy vấn chỉ đọc BillingDb trên backend-sqlserver-1 không tìm thấy ca có OpeningCash lẻ hoặc ExpectedCash vượt giới hạn tại lần kiểm tra này; chưa khảo sát SQL Server bên ngoài Docker.
- Các thay đổi chỉ ở Billing, test Billing, README và nhật ký này. Không sửa nghiệp vụ, schema hoặc dữ liệu thường của service khác; dữ liệu mô phỏng chỉ được tạo trong database fixture riêng.

Kiểm chứng:
- Build Billing và các image phụ thuộc dùng cho fixture đạt. Test solution: 895 pass, 76 conditional skip, 0 fail; không tính skip là đạt. Full Billing trên SQL Server thật: 355 pass, 0 skip, 0 fail. Sau đó bổ sung test dữ liệu cũ vượt quỹ và chạy lại nhóm CashMovementSqlServerTests: 8 pass, gồm hai CashIn đồng thời, CashIn đấu với Sale, từ chối Sale không có Paid/bút toán dở dang và không tự sửa dữ liệu cũ.
- Gateway fixture dùng Identity/Restaurant/Ordering/Billing và SQL Server thật, provider PayOS HTTP double có chữ ký. Bộ PayOS hồi quy 46 checks đạt; lượt tiếp theo xác minh mở ca số nguyên/.00, từ chối .50, CashIn sát/vượt giới hạn. Script PowerShell đầu tiên đọc JSON số tiền lớn qua floating point nên assert sai; kiểm tra lại JSON bằng GetDecimal và SQL xác nhận số tiền chính xác. Lượt tiếp tục 30 checks đạt (gồm login/setup), bao phủ CashIn đồng thời, provider mất kết nối chặn đóng ca, retry đóng ca thành công, retry báo cáo đã đóng không gọi bank, Sale overflow giữ Unpaid/Ready và không có Sale, retry sau CashOut hợp lệ chỉ tạo một Payment/Sale, restart worker phát hiện tiền đến thêm ở ca đã đóng khi không có webhook.
- Kiểm tra SQL fixture xác nhận REVIEW-LIMIT và REVIEW-CONCURRENT chỉ một movement, số dư 9999999999999999.00; REVIEW-SALE đúng một Sale, không tạo Payment khi bị từ chối; REVIEW-BANK Payment ReviewRequired nhưng Bill/Checkout Paid và ca Closed, không có CashMovement ngân hàng.
- Chưa kiểm chứng ngân hàng/PayOS thật hoặc SQL Server ngoài Docker. Thời điểm ngân hàng ghi nhận tiền sau lần đối chiếu cuối không thể khóa bằng transaction SQL; webhook/worker tiếp tục theo dõi theo quy tắc trên, không sửa báo cáo tiền mặt đã đóng.
- Đã dừng nhóm payos-check và xóa đúng 5 database fixture có hậu tố 20261028234420; không thay đổi database thường. Không commit/push. Container backend thường không được restart trong đợt này; cần khởi động lại Billing để nạp binary mới khi sử dụng.

Danh sách file của đợt sửa:
- `Services/billing_service/Billing.BLL/Implements/CashShiftService.cs`: validation tiền đầu ca.
- `Services/billing_service/Billing.BLL/Implements/CashMovementService.cs`: kiểm tra số dư sau CashIn.
- `Services/billing_service/Billing.BLL/Implements/PaymentService.cs`: kiểm tra quỹ trước khi ghi Sale/Payment.
- `Services/billing_service/Billing.BLL/Implements/ShiftReportService.cs`: đối chiếu PayOS trước đóng ca và kiểm tra lại dưới khóa.
- `Services/billing_service/Billing.DAL/Interfaces/IPaymentRepository.cs` và `Services/billing_service/Billing.DAL/Implements/PaymentRepository.cs`: truy vấn Paid trong ca mở/khoảng theo dõi.
- `Services/billing_service/Billing.API/Workers/PayOsReconciliationWorker.cs`: sử dụng TimeProvider và khoảng theo dõi.
- `Services/billing_service/Billing.API/Clients/PayOsClient.cs`: kiểm tra cấu hình khoảng theo dõi.
- `Services/billing_service/Billing.Common/Settings/PayOsSettings.cs`: PaidReconcileHours.
- `Services/billing_service/Billing.API/appsettings.json`, `Services/billing_service/Billing.API/appsettings.Docker.json`, `Services/billing_service/Billing.API/appsettings.Local.example.json`: giá trị chung 24 giờ; không thêm secret.
- `Tests/Billing.Tests/CashShiftServiceTests.cs`: số nguyên/.00/.50.
- `Tests/Billing.Tests/ShiftReportServiceTests.cs`: cập nhật dependency và fixture tracking sau đối chiếu.
- `Tests/Billing.Tests/BillingBalanceSafetyTests.cs` (mới): boundary và rollback trạng thái.
- `Tests/Billing.Tests/PayOsClosingSafetyTests.cs` (mới): polling Paid, lỗi provider, đóng ca, tiền đến thêm.
- `Tests/Billing.Tests/CashMovementSqlServerTests.cs`: đồng thời, giới hạn chính xác SQL và bảo toàn dữ liệu cũ.
- `README.md` và `docs/database-change-log.md`: quy tắc vận hành, phạm vi SQL/ERD và kết quả kiểm thử.

### Đồng bộ repo SQL riêng trong Downloads — 2026-10-28

Đã đồng bộ `C:/Users/phucg/Downloads/Database-SQLServer-main/Database-SQLServer/BillingDb.sql`: thêm đúng ba cột PayOS nullable/không default và seed Cash/BankTransfer nếu thiếu, giữ bản ghi đã có kể cả bị tắt. Đây là đưa các thay đổi Billing đã được phép vào bản SQL riêng, không phát sinh schema mới. Chỉ file BillingDb.sql thay đổi trong repo SQL; OrderingDb.sql giữ nguyên. Đã kiểm thử bản sao script trên database SQL Server Docker cô lập CodexBillingSqlSync_* và kiểm tra đúng varchar(100)/varchar(2048)/varchar(max), NULL, default=0 cùng hai seed; đã xóa database thử. Không chạy script reset vào BillingDb đang dùng. File Downloads là script tạo mới có DROP/CREATE; nâng cấp database có dữ liệu dùng docker/sql/upgrades/BillingPayOs.sql trong backend. Repo SQL và backend đều chưa commit/push ở lần đồng bộ này.

### B2 — Chuyển yêu cầu thanh toán đến quầy mặc định

| Database và bảng | Thay đổi | Kiểu, NULL và DEFAULT | Khóa và ERD |
| --- | --- | --- | --- |
| BillingDb.dbo.BillCheckout | Thêm `CashRegisterId` | `uniqueidentifier NULL`, không DEFAULT | FK `FK_BillCheckout_CashRegister` đến `CashRegister(CashRegisterId)`. Dây mới CashRegister 1–n BillCheckout, mỗi checkout cũ có thể chưa có quầy. |
| BillingDb.dbo.BillCheckout | Thêm `SubmittedAt` | `datetime2(7) NULL`, không DEFAULT | Không đổi khóa hoặc dây ERD. |
| BillingDb.dbo.CashRegister | Thêm UNIQUE index `UX_CashRegister_Restaurant` | Dùng cột `RestaurantId uniqueidentifier NOT NULL` có sẵn | Restaurant 1–0..1 CashRegister, quan hệ logic, không FK xuyên database. |

Giữ PK, UNIQUE/CHECK và các index đang có. Không thêm bảng, không tạo FK xuyên database, không gán quầy vào bản ghi cũ. Script dừng toàn bộ nếu phát hiện một nhà hàng có nhiều quầy. Hàng chờ theo quầy được Cashier đứng ca mới tiếp quản khi đọc danh sách, không đổi khóa ca trên giao dịch cũ. Schema tạo mới ở `docker/sql/schema/BillingDb.sql`. Dùng `docker/sql/upgrades/BillingCheckoutSubmission.sql` để nâng cấp BillingDb hiện có. Đã thử nâng cấp hai lần và scaffold bằng EF Core 8.0.24 trên SQL Server LocalDB cô lập. Chưa áp dụng vào database đang dùng.

### B3 — Chốt hóa đơn và xác nhận đã đưa khách

| Bảng | Cột | Kiểu và mặc định | Quan hệ |
| --- | --- | --- | --- |
| BillingDb.dbo.BillCheckout | `PreparedByRef` | `uniqueidentifier NULL`, không DEFAULT | Tham chiếu logic đến Identity.User, 1–n. Không có FK xuyên database. |
| BillingDb.dbo.BillCheckout | `PreparedAt` | `datetime2(7) NULL`, không DEFAULT | Thời điểm Cashier chốt hóa đơn (UTC). |
| BillingDb.dbo.BillCheckout | `PresentedAt` | `datetime2(7) NULL`, không DEFAULT | Thời điểm Waiter phụ trách xác nhận đã đưa hóa đơn cho khách (UTC). |

Không thêm bảng, PK, FK, UNIQUE, CHECK hoặc index trong B3. Dữ liệu cũ giữ NULL. Với BillingDb đang dùng, áp dụng `BillingCheckoutSubmission.sql` trước, sau đó áp dụng `BillingCheckoutPreparation.sql`. Đã chạy thử trên SQL Server LocalDB cô lập và xác nhận chạy lặp giữ nguyên bản ghi cũ. Chưa áp dụng vào database đang dùng.

### B4 — Thu hộ tiền mặt và xác nhận bàn giao

B4 dùng `Payment`, `CashHandover`, `CashMovement` và các khóa đã có trong `docker/sql/schema/BillingDb.sql`. `CashHandover.PaymentId` là PK và FK nội bộ đến `Payment.PaymentId`, quan hệ Payment 1–0..1 CashHandover. `WaiterUserRef` và `CashierUserRef` là tham chiếu logic đến Identity, không có FK xuyên database.

Khoản thu hộ chờ xác nhận được giữ trong ca nhận tiền và chặn đóng ca. Khi Cashier xác nhận đủ toàn bộ số tiền đã ghi, Billing ghi một bút toán `Sale` bằng giá trị hóa đơn và chuyển giao dịch sang `Paid` trong cùng transaction.

#### Đồng bộ CashHandover sau review B4

Repo SQL riêng bổ sung theo schema backend cột `BillingDb.dbo.CashHandover.RestaurantId uniqueidentifier NULL`, không DEFAULT, và index nonclustered không UNIQUE `IX_CashHandover_RestaurantIsolation(RestaurantId)`. Đây là quan hệ logic Restaurant 1–n CashHandover, mỗi bản ghi có thể chưa xác định được nhà hàng. PK và FK nội bộ đến Payment giữ nguyên. EF Core đã dùng cột này làm thuộc tính ẩn để cô lập dữ liệu.

Với database hiện có, dùng `BillingCashHandoverRestaurantIsolation.sql` trong thư mục upgrade của một trong hai repo. Script chỉ điền `RestaurantId` còn NULL khi hóa đơn và quầy nhận cùng thuộc một nhà hàng hợp lệ. Đã đối chiếu DDL, thử nâng cấp và chạy lặp trên SQL Server LocalDB cô lập. Chưa áp dụng vào database đang dùng.

### B5 — QR PayOS sau khi đưa hóa đơn

Không có thay đổi schema trong backend. Repo SQL riêng đồng bộ ba cột đã có trong `BillingDb.dbo.Payment`: `ProviderPaymentLinkId varchar(100) NULL`, `ProviderCheckoutUrl varchar(2048) NULL` và `ProviderQrCode varchar(max) NULL`. Ba cột không có DEFAULT. PK, FK, UNIQUE, CHECK, index và quan hệ ERD giữ nguyên. Seed `PaymentMethod.BankTransfer` nếu thiếu, giữ bản ghi đã bị tắt. Bản tạo mới và script upgrade chạy lặp đã được kiểm tra trên SQL Server LocalDB cô lập.

Chỉ Cashier đứng ca tại quầy nhận tạo QR mới khi checkout ở `Presented`. Waiter phụ trách xem QR và kết quả theo phiên bàn. Các giao dịch PayOS đã mở trên checkout `Ready` cũ vẫn có thể đối soát. Webhook kích hoạt xác minh với PayOS và Billing chỉ ghi `Paid` khi kết quả khớp. Tự đóng phiên thuộc đợt riêng.

### B6 — Tự đóng phiên sau khi thanh toán

**Không có thay đổi schema.** Dùng các trường đã có `TableSession.Status/ClosedAt`, `SessionTable.ReleasedAt`, `GuestAccess.RevokedAt` và `ServiceRequest.Status/CompletedAt` để đóng phiên sau khi xác minh giao dịch. Giữ nguyên khóa, index và quan hệ ERD. Không có script SQL nâng cấp cho B6.

### 2026-11-21 — Kiểm kê Billing/AI và nâng cấp Docker có dữ liệu

#### Phạm vi và bằng chứng

Kiểm kê working tree của `feature/AI-restaurant-rework`, HEAD `df9aaa3`; mốc SQL Docker đầu tiên có trong lịch sử là `072863b`. Dùng `git show <commit> -- docker/sql/schema/<database>.sql` để xem bằng chứng thay đổi. Đối chiếu thêm model EF đang chạy, script upgrade và SQL tạo mới. Không coi mọi thay đổi do các thành viên nhập vào dev là sản phẩm của riêng đợt Billing/AI rework. Không đối chiếu model mới rồi tự kết luận bảng mới.

BillingDb ở mốc `072863b` đã có **22 bảng**: Bill, CashRegister, PaymentProviderConfig, PaymentMethod, Voucher, TaxFeePolicy, Promotion, DiscountLimit, VoucherRedemption, ElectronicInvoice, CashShift, ReceiptPrint, PromotionTarget, BillLine, BillAdjustment, ShiftReport, Payment, Refund, CashMovement, CashHandover, PaymentWebhook, RefundLine. Do đó thu hộ, sổ quỹ, ca, báo cáo, in bill và webhook không mặc nhiên là các bảng được tạo thêm khi triển khai nghiệp vụ.

SQL Billing hiện có 25 bảng: ngoài 22 bảng trên là BillCheckout, OnlinePayments và NotificationOutbox. OnlinePayments và NotificationOutbox được đưa vào qua các thay đổi dùng chung của repository, tách riêng dưới đây. Việc một bảng từng có trong một bản SQL cũ nhưng không còn trong bản tạo mới không chứng minh database thật đã DROP bảng.

#### Những thay đổi schema trực tiếp của các đợt Billing

Các cột trong bảng dưới không có DEFAULT, trừ khi ghi rõ. Các file schema là bản tạo mới; file trong `docker/sql/upgrades/` là nâng cấp bảo toàn dữ liệu, không thay thế bằng cách chạy lại CREATE/DROP database.

| Database/bảng | Thay đổi so với SQL gốc | Khóa, quan hệ và ERD | File và commit bằng chứng |
| --- | --- | --- | --- |
| BillingDb.CashRegister | Thêm `RestaurantId uniqueidentifier NOT NULL` | Tham chiếu logic Restaurant 1–0..1 CashRegister theo quy tắc một quầy hiện tại; không FK xuyên DB. `UX_CashRegister_Restaurant` là UNIQUE(RestaurantId), được thêm ở B2; PK đã có | `BillingDb.sql`, `BillingCashRegisterRestaurant.sql`: `e1a41ac`; unique index: `8bc315a`, `BillingCheckoutSubmission.sql` |
| BillingDb.Bill | Thêm `RestaurantId uniqueidentifier NOT NULL` | Restaurant 1–n Bill là logic, PK_Bill giữ nguyên | `BillingDb.sql`, `BillingBillRestaurant.sql`: `6814ad9` |
| BillingDb.BillLine | `CogsSnapshot decimal(18,2) NOT NULL` → `decimal(18,2) NULL` | Không thêm bảng, không đổi PK/FK/dây ERD. NULL là chưa có giá vốn | Cùng script và commit `6814ad9` |
| BillingDb.BillCheckout | Bảng mới ban đầu: `CheckoutId uniqueidentifier NOT NULL`, `BillId uniqueidentifier NULL`, `TableSessionRef uniqueidentifier NOT NULL`, `SourceHash char(64) NULL`, `Status varchar(20) NOT NULL`, `CreatedAt datetime2(7) NOT NULL`, `ReleasedAt datetime2(7) NULL` | PK(CheckoutId); FK_BillCheckout_Bill → Bill(BillId): Bill 1–n checkout, checkout có 0..1 bill; UNIQUE `UX_BillCheckout_ActiveSession` trên TableSessionRef WHERE ReleasedAt IS NULL. TableSession 1–n checkout là logic | `BillingDb.sql`: `8dfe450`; đường upgrade hiện tại: `BillingCheckoutTables.sql` |
| OrderingDb.CheckoutLock | Bảng mới ban đầu: `CheckoutId uniqueidentifier NOT NULL`, `BillIdRef uniqueidentifier NULL`, `TableSessionRef uniqueidentifier NOT NULL`, `SourceHash char(64) NULL`, `Status varchar(20) NOT NULL`, `CreatedAt datetime2(7) NOT NULL`, `ReleasedAt datetime2(7) NULL` | PK(CheckoutId); UNIQUE `UX_CheckoutLock_ActiveSession` trên TableSessionRef WHERE ReleasedAt IS NULL. Bill 1–n CheckoutLock, TableSession 1–n CheckoutLock và đối chiếu CheckoutId giữa hai service là logic; không FK xuyên DB | `OrderingDb.sql`: `8dfe450`; đường upgrade hiện tại: `OrderingCheckoutLock.sql` |
| BillingDb.Payment | Thêm `ProviderPaymentLinkId varchar(100) NULL`, `ProviderCheckoutUrl varchar(2048) NULL`, `ProviderQrCode varchar(max) NULL` | Không thêm PK/FK/index hoặc đổi dây ERD. Các cột ProviderOrderCode/ProviderTransactionId đã có từ SQL gốc | `BillingDb.sql`, `BillingPayOs.sql`: `ccd65f3` |
| BillingDb.BillCheckout (B2) | Thêm `CashRegisterId uniqueidentifier NULL`, `SubmittedAt datetime2(7) NULL` | FK_BillCheckout_CashRegister → CashRegister(CashRegisterId): quầy 1–n checkout, checkout cũ có thể chưa có quầy | `BillingDb.sql`, `BillingCheckoutSubmission.sql`: `8bc315a` |
| BillingDb.BillCheckout (B3) | Thêm `PreparedByRef uniqueidentifier NULL`, `PreparedAt datetime2(7) NULL`, `PresentedAt datetime2(7) NULL` | PreparedByRef là tham chiếu logic Identity.User 1–n checkout; không thêm FK/index | `BillingDb.sql`, `BillingCheckoutPreparation.sql`: `e67abb4` |
| BillingDb.CashHandover (B4) | Cột `RestaurantId uniqueidentifier NULL` vốn đã có trong schema đa nhà hàng, không phải bảng mới/cột mới của B4 | Upgrade B4 bảo đảm cột và `IX_CashHandover_RestaurantIsolation` NONCLUSTERED(RestaurantId) tồn tại; chỉ backfill khi Bill và CashRegister xác nhận cùng nhà hàng. Restaurant 1–n CashHandover là logic | `BillingCashHandoverRestaurantIsolation.sql`: `85be05b`; xem thêm snapshot `2264c8a` |
| BillingDb.PaymentMethod | Seed `Cash, IsEnabled=1` và `BankTransfer, IsEnabled=1` chỉ khi thiếu; giữ phương thức đã tắt | **DML, không đổi schema/ERD** | `BillingCashMethod.sql`: `10d8a29`; `BillingPayOs.sql`: `ccd65f3` |

Các phần đóng ca/đối soát, thu chi, thu tiền mặt, phục hồi timeout, cập nhật trạng thái thanh toán và tự đóng phiên không tự tạo thêm cột ngoài các dòng đã nêu. Không có cột `CashMovement.PostedByRef` trong schema hiện tại; người thao tác thu chi được suy từ ca theo nghiệp vụ đã chốt. Các trường của ShiftReport, CashShift, CashMovement, CashHandover và ReceiptPrint còn lại đã có trong baseline.

#### Thay đổi dùng chung đã có trong dev, cần cho Billing/AI hoạt động

| Database/bảng | Cột/kiểu/NULL/DEFAULT | Khóa và quan hệ | Bằng chứng |
| --- | --- | --- | --- |
| BillingDb: BillAdjustment, BillLine, CashHandover, CashMovement, CashShift, DiscountLimit, ElectronicInvoice, Payment, PaymentWebhook, Promotion, PromotionTarget, ReceiptPrint, Refund, RefundLine, ShiftReport, TaxFeePolicy, Voucher, VoucherRedemption | Mỗi bảng thêm `RestaurantId uniqueidentifier NULL`, không DEFAULT; BillCheckout và Ordering.CheckoutLock cũng có cột cùng định nghĩa. Đây là phân vùng dữ liệu đa nhà hàng | Các `IX_<table>_RestaurantIsolation` theo RestaurantId; Restaurant 1–n mỗi bảng là logic. Không FK xuyên DB | Snapshot schema `2264c8a`, cơ chế scope/model trong `a40106c`; không gán tất cả vào đợt rework |
| BillingDb.PaymentProviderConfig | Thêm `Enabled bit NOT NULL`, `ReturnUrl varchar(2048) NOT NULL`, `CancelUrl varchar(2048) NOT NULL`; `RestaurantId uniqueidentifier NULL` → NOT NULL; `SecretReference varchar(500) NOT NULL` → `varchar(max) NOT NULL`; không DEFAULT | UNIQUE `IX_PaymentProviderConfig_RestaurantId_Provider` trên (RestaurantId,Provider); Restaurant 1–n cấu hình provider là logic. PK_PaymentProviderConfig giữ nguyên | `ff89e44`; schema `BillingDb.sql`; đường upgrade mới `BillingProviderConfiguration.sql` |
| BillingDb.OnlinePayments | Bảng mới của luồng mang đi: `OrderId uniqueidentifier NOT NULL` (PK), `RestaurantId uniqueidentifier NOT NULL`, `CustomerUserId uniqueidentifier NOT NULL`, `ProviderOrderCode bigint NOT NULL`, `Amount decimal(18,2) NOT NULL`, `Status nvarchar(30) NOT NULL`, `LinkId nvarchar(100) NULL`, `CheckoutUrl nvarchar(2048) NULL`, `ExpiresAt datetime2(7) NOT NULL`, `PaidAt datetime2(7) NULL`, `TransactionReference nvarchar(255) NULL`, `RefundReason nvarchar(1000) NULL`, `RefundReference nvarchar(255) NULL`, `RefundEvidence nvarchar(2048) NULL`, `RefundVerifiedBy uniqueidentifier NULL`, `RefundedAt datetime2(7) NULL`; không DEFAULT | UNIQUE ProviderOrderCode; UNIQUE TransactionReference/RefundReference khi không NULL; IX_OnlinePayments_RestaurantIsolation. Order–OnlinePayment là logic 1–0..1 qua OrderId; không FK xuyên DB | `4ae0ce1`, snapshot `2264c8a`; không tạo lại hay sửa nghiệp vụ OnlinePayment trong đợt này |
| BillingDb.NotificationOutbox và OrderingDb.NotificationOutbox | Mỗi DB có bảng riêng: `NotificationOutboxId uniqueidentifier NOT NULL`, `RestaurantId uniqueidentifier NOT NULL`, `EventKey nvarchar(200) NOT NULL`, `Payload nvarchar(max) NOT NULL`, `CreatedAt datetime2(7) NOT NULL`, `NextAttemptAt datetime2(7) NOT NULL`, `ProcessedAt datetime2(7) NULL`, `Attempts int NOT NULL`, `LastError nvarchar(100) NULL`; không DEFAULT | PK(NotificationOutboxId); UNIQUE IX_NotificationOutbox_EventKey(EventKey); IX_NotificationOutbox_ProcessedAt_NextAttemptAt(ProcessedAt,NextAttemptAt); Restaurant 1–n outbox là logic | Schema `51d11a7`, notification code `b714411`; đường upgrade mới `NotificationOutbox.sql` chạy riêng mỗi DB |
| OrderingDb.Order | Thêm `RefundBankName nvarchar(100) NULL`, `RefundAccountNumber nvarchar(34) NULL`, `RefundAccountHolder nvarchar(150) NULL`; không DEFAULT | Không thêm PK/FK/index/dây ERD. Truy vấn toàn entity Order từ Billing có thể cần các cột này dù không xử lý hoàn tiền | `cc78682`; đường upgrade mới `OrderingRefundAccount.sql` |
| RestaurantDb.Qr | `ImageUrl varchar(2048) NULL`, không DEFAULT, đã có trong model nhưng thiếu SQL tạo mới | Không đổi PK/FK/index/dây ERD | Script/model Cloudinary `def8bfe`, initializer cũ `ea2c454`; đợt này đồng bộ CREATE schema và chạy lại `RestaurantCloudinary.sql` |

`PayoutRequest` từng có trong snapshot `2264c8a` rồi bị bỏ khỏi SQL tạo mới ở `ff89e44`. Đợt này **không DROP** bảng hay xóa dữ liệu payout trên database cũ. Các cột mang đi/giỏ khách hàng trên Ordering do `4ae0ce1` và schema đa nhà hàng đưa vào là dependency hiện hữu, không phải DDL của AI rework; không sửa nghiệp vụ đó.

#### AI và SQL

**AI Service không có AI database, DbContext, migration hay bảng SQL riêng.** Chat và gợi ý giỏ gọi hợp đồng Restaurant/Catalog/Ordering; không đọc trực tiếp database service khác. Ngữ cảnh hội thoại dùng token được bảo vệ và khóa Data Protection ở volume `ai-chat-keys`, không phải bảng SQL. Gemini API key là cấu hình môi trường, không phải cột database. Không có DDL Ollama/Qwen/local model.

`scripts/ai/acceptance-menu.sql` (`df9aaa3`) chỉ là **DML cho dữ liệu thử**, có INSERT/UPDATE/DELETE đối với các ID tổng hợp đã định; không CREATE/ALTER bảng, không được `db-init` tự chạy và không được coi là migration hay seed sản xuất. Những bảng RecipeVersion, Item, Price, Cart… AI sử dụng đã thuộc các service hiện hữu, không phải bảng mới của AI.

#### Sửa trong đợt này và điều kiện nâng cấp

- Không thay đổi model hoặc nghiệp vụ thanh toán/AI, không bổ sung trường nghiệp vụ mới. SQL tạo mới duy nhất được đồng bộ là `RestaurantDb.Qr.ImageUrl` cho đúng model và upgrade đã có.
- Dockerfile đóng gói thư mục upgrades; `init.sh` gọi các script Billing được liệt kê tường minh theo thứ tự, rồi nâng cấp CheckoutLock/refund account/outbox ở Ordering và Qr.ImageUrl ở Restaurant. Không tự chạy mọi script trong thư mục, không tự chạy dữ liệu AI nghiệm thu, không sửa Identity/Inventory/Catalog.
- Các script mới chỉ đưa database cũ đến cấu trúc đã có trong schema hiện tại: BillingCheckoutTables, BillingProviderConfiguration, OrderingCheckoutLock, OrderingRefundAccount, NotificationOutbox. Không thêm bảng/column mới ngoài hợp đồng schema hiện hữu.
- Sau upgrade, initializer đọc **0 dòng** từ từng bảng/cột được khai báo trong schema Billing, Ordering, Catalog, Restaurant, Inventory và Identity. Thiếu bảng/cột bất kỳ ở dependency sẽ làm `db-init` thất bại rõ ràng thay vì báo thành công rồi lỗi tại API. Đây là kiểm tra bảng/cột, không phải bằng chứng dữ liệu hoặc mọi quy tắc nghiệp vụ đều hợp lệ.
- Hỗ trợ database mới hoặc baseline Docker `DockerBootstrapVersion=2`. Database version khác hoặc database có bảng nhưng không có marker bị từ chối để kiểm kê riêng, không tự đánh dấu là đã nâng cấp. Không thể tự chuyển mọi schema tùy biến/lịch sử không xác định thành schema mới mà không có ánh xạ dữ liệu.
- Quầy/bill cũ thiếu restaurant mapping, nhiều quầy một nhà hàng, cấu hình PayOS cũ thiếu Enabled/URL/nhà hàng, cấu hình trùng provider hoặc index tùy biến cần xử lý rõ trước; không điền giá trị giả, không xóa dữ liệu, không tự bật phương thức bị tắt. Lỗi bước sau không đồng nghĩa hoàn tác các script bước trước đã thành công; từng script có transaction và chạy lại được.
- Chỉ áp dụng vào SQL Server Docker và LocalDB **thử nghiệm cô lập** trong lượt này. Không chạy upgrade trên BillingDb/OrderingDb/RestaurantDb đang dùng; repo SQL riêng có thay đổi sẵn được giữ nguyên, không ghi đè hoặc commit.

Kiểm thử: `pwsh -File scripts/Test-BillingDockerBootstrap.ps1` build image từ working tree, kiểm tra khởi tạo mới, tái hiện schema cũ thiếu cột, nâng cấp lặp và giữ checkout cũ, đối chiếu toàn bộ metadata cột Billing/Ordering với schema mới, tạo checkout tables khi thiếu, giữ phương thức đã tắt, chặn thiếu cấu hình PayOS/nhiều quầy và chặn thiếu cột dependency AI. Tài nguyên test riêng được dọn. Các kết quả build/test và phần Gateway được báo ở bàn giao; không dùng kết quả LocalDB để khẳng định Docker của thành viên khác đã được nâng cấp.

**Dependency Identity cần chốt riêng:** `e3f36b3` đã đưa `User.RestaurantId uniqueidentifier NULL` và `User.ProvisioningPending bit NOT NULL DEFAULT(0)` vào model; bản schema cũ `2264c8a` chưa có hai cột này. `AuthService.Login` đọc User qua repository nên thiếu cột có thể làm lỗi đăng nhập, không chỉ Billing. Các bảng Notification/NotificationRecipient/NotificationSetting/Announcement cũng đã được thêm vào Identity qua chức năng thông báo. Trong lượt này chỉ kiểm tra và chặn initializer nếu Identity thiếu bảng/cột; chưa thêm DDL hoặc sửa code Identity. Đã gửi câu hỏi phạm vi để người dùng chốt có cho phép bổ sung upgrade Identity theo schema hiện có hay không. Không tự gán RestaurantId cho user cũ.

Đã chạy thêm Compose thật từ `compose.yaml` bằng project test riêng: `up -d --build sqlserver db-init` → db-init Exited(0); sau khi cố tình bỏ 5 cột checkout trong BillingDb test, chạy lại db-init → bổ sung đủ cột, Exited(0). Database/volume/project test đã dọn; không chạy lệnh xóa volume của project backend đang dùng.

### 2026-11-23 — Dữ liệu màn Bàn & hóa đơn thu ngân

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK, FK, UNIQUE, CHECK hoặc index; không cần scaffold hoặc SQL upgrade; không đổi dây ERD. Không sửa repository SQL riêng hoặc database đang dùng.

Billing đọc mã/tổng bill hiện có, Payment.CashShiftId/Status/PaidAt để xác định tab đã thanh toán trong ca, và đọc metadata bàn/khu vực qua RPC mới `BillingSessionGrpc.GetDisplays` ở Restaurant. DTO/gRPC bổ sung `tables[]`, các trường bàn/khu vực đơn khi xác định duy nhất, billCode (từ BillNumber), tổng, số khách và thời điểm. Bộ lọc/tổng tính trước phân trang. Đây là thay đổi code/hợp đồng đọc, không phải cột SQL mới. Luồng tiền mặt, PayOS, thu hộ, đóng phiên và quyền quản trị giữ nguyên theo BM04 F-WF02-06.

Đã kiểm chứng: Billing 381/381 test (bao gồm 2 test SQL mới về ca Paid và chống nhân đôi); 6/6 test luồng PaymentSubmission qua Gateway TestServer/SQL Server LocalDB, có bổ sung bộ lọc và thẻ; kiểm tra lại riêng hai luồng tiền mặt/PayOS sau khi thêm assertion Paid. PayOS trong các test dùng HTTP fixture, không gọi cổng thanh toán thật. Fixture Billing seed sơ đồ trực tiếp vào database thử vì quyền API quản lý sơ đồ trên dev đã thay đổi; không đổi quyền production để phục vụ test.

Compose thật dùng project/container/volume riêng và dữ liệu seed thử: đăng nhập Cashier, mở ca qua API, đọc incoming/tìm mã bill/tab Paid/chi tiết bill đều HTTP 200. Dataset có 2 thẻ chờ, 1 bill chưa lập, tổng bill đã lập 45.000 VND; tìm BOARD-UNPAID trả bàn T02/45.000 VND; tab Paid trả bàn T03 đã đóng phiên/90.000 VND theo ca. Đây là kiểm chứng API đọc bằng dữ liệu SQL thử, không phải kiểm chứng toàn luồng thu tiền thật trong Docker. Tài nguyên thử đã dọn, không reset database backend đang dùng.

### 2026-11-23 — Upgrade Identity StaffTransfer và quyền QR theo nhà hàng

Phạm vi: sửa lỗi đổi vai trò/trạng thái tài khoản khi database Docker cũ thiếu `dbo.StaffTransfer`; cho `admin` nhà hàng quản lý QR trong phân công đang hoạt động, giữ nguyên quyền `platform_admin`. Không thay đổi nghiệp vụ chuyển nhân viên hoặc quyền quản trị dùng chung.

**SQL/ERD:** `StaffTransfer` đã tồn tại trong file tạo mới `docker/sql/schema/IdentityDb.sql` (từ chức năng chuyển nhân viên), không phải bảng nghiệp vụ mới do đợt QR tạo ra. Đợt này thêm `docker/sql/upgrades/IdentityStaffTransfer.sql` và gọi tại `docker/sql/init.sh` để bổ sung đối tượng còn thiếu trên database đang dùng. Không sửa baseline, model/DbContext hoặc dữ liệu User; không cần scaffold vì mô hình đã có đúng bảng này.

| Database/bảng | Cột | Kiểu SQL đầy đủ | NULL | DEFAULT |
| --- | --- | --- | --- | --- |
| IdentityDb.dbo.StaffTransfer | StaffTransferId | uniqueidentifier | Không | Không |
| IdentityDb.dbo.StaffTransfer | UserId | uniqueidentifier | Không | Không |
| IdentityDb.dbo.StaffTransfer | SourceRestaurantId | uniqueidentifier | Không | Không |
| IdentityDb.dbo.StaffTransfer | TargetRestaurantId | uniqueidentifier | Không | Không |
| IdentityDb.dbo.StaffTransfer | RequestedBy | uniqueidentifier | Không | Không |
| IdentityDb.dbo.StaffTransfer | WasActive | bit | Không | Không |
| IdentityDb.dbo.StaffTransfer | CompletedAt | datetime2(7) | Có | Không |
| IdentityDb.dbo.StaffTransfer | Status | nvarchar(20) | Không | Không |
| IdentityDb.dbo.StaffTransfer | Message | nvarchar(500) | Có | Không |
| IdentityDb.dbo.StaffTransfer | CreatedAt | datetime2(7) | Không | Không |

Khóa/index: PK `PK_StaffTransfer(StaffTransferId)`; index không UNIQUE `IX_StaffTransfer_UserId_Status(UserId, Status)`. Không thêm FK, UNIQUE, CHECK hay DEFAULT; không xóa/sửa cột. Script kiểm tra cấu trúc hiện có, giữ nguyên dữ liệu và dừng/rollback nếu không tương thích; dùng transaction và application lock, chạy lặp an toàn.

Dây ERD: User 1–n StaffTransfer qua UserId (nhân viên được chuyển); User 1–n StaffTransfer qua RequestedBy (người yêu cầu). Đây là tham chiếu logic, schema hiện tại không khai báo FK. Restaurant 1–n StaffTransfer qua từng vai trò SourceRestaurantId/TargetRestaurantId là tham chiếu logic sang RestaurantDb; không tạo FK xuyên database. Nếu ERD đã có bảng theo schema gốc thì không cần thêm bảng/dây lần nữa; nếu chưa có, bổ sung theo mô tả này. Phần QR không có thay đổi schema.

Trạng thái áp dụng: đã chạy script hai lần trên `IdentityDb` trong container Docker hiện có `backend-sqlserver-1`; trước upgrade thiếu bảng, sau upgrade có đủ PK/index, User giữ 7 bản ghi trước/sau và StaffTransfer có 0 bản ghi (không seed chuyển nhân viên). Không xóa volume, không chạy lại schema/seed cho database này. Không thay đổi repo Database-SQLServer riêng vì đợt này chỉ thêm đường upgrade Docker cho định nghĩa đã có. Chưa commit/push.

Kiểm chứng riêng: test SQL Server LocalDB tái hiện lỗi 208 khi bảng thiếu rồi đổi vai trò thành công sau upgrade; chạy lặp giữ User/lịch sử chuyển nhân viên, khôi phục index thiếu, từ chối schema không tương thích. Gateway TestServer dùng SQLite và Identity/Cloudinary giả lập kiểm tra tạo/tạo lại/tải ảnh đúng nhà hàng, lọc trước phân trang, chặn khác nhà hàng, phân công đã thu hồi và các vai trò không được cấp quyền; giữ regression Platform Admin và resolve QR công khai. Một test mới chạy với QrService cũ đã thất bại, chạy lại bản sửa đạt.

Docker Compose project thử `identity-qr-check` build từ working tree: SQL Server, db-init, Identity, Restaurant, Gateway thật. Dùng seed thử riêng và tài khoản đăng nhập thật; thiếu bảng gây PATCH role HTTP 502, chạy lại initializer giữ 7 User và PATCH role HTTP 200. Admin nhà hàng GET /api/qr HTTP 200; truy cập QR của nhà hàng khác qua list/image/create/regenerate đều HTTP 403; Manager GET list HTTP 403. Upload/download ảnh Cloudinary thành công chỉ được kiểm tra bằng test image store trong TestServer, chưa dùng credential Cloudinary thật. Không thay đổi vai trò tài khoản trên database Docker chính để kiểm thử.

Kết quả tổng hợp cuối lượt: build `Backend.sln -c Release --no-restore` đạt; test toàn solution `-c Release --no-build --no-restore` với `IDENTITY_SQL_TEST_CONNECTION` trỏ SQL Server LocalDB đạt 1.167, lỗi 0; 100 test SQL khác bị bỏ qua do chưa bật điều kiện riêng (không tính đạt). Identity 76/76 gồm 3 test SQL upgrade mới; Restaurant 141 đạt, 51 bỏ qua, gồm 8 test QR mới. Các test Gateway QR dùng nhận diện tài khoản/Cloudinary giả lập; kiểm chứng Docker bổ sung dùng Identity và Restaurant thật như mô tả trên. Chưa kiểm chứng upload/tải ảnh thành công với Cloudinary thật hoặc chạy lại GitHub Actions trên Ubuntu.

Kiểm tra Docker sau rebuild Restaurant cuối cùng: lượt gọi ngay sau recreate chưa đạt assertion; kiểm tra lại khi service đã nhận request trả GET /api/qr?pageSize=1 HTTP 200, totalCount=1, chỉ bàn T01 của nhà hàng được phân công. Thu hồi phân công trong database thử làm cùng tài khoản/token nhận HTTP 403; đã khôi phục phân công thử sau test. Project thử đã dừng; không xóa container SQL chính hoặc bất kỳ volume database nào.

### 2026-11-23 — Metadata nhà hàng và khu vực khi quét QR

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng, cột, kiểu dữ liệu, NULL/NOT NULL, DEFAULT, PK/FK, UNIQUE, CHECK hoặc index; không đổi quan hệ/dây ERD, không cần SQL upgrade hoặc scaffold.

`POST /api/table-sessions/scan` bổ sung `restaurantName`, `areaName`, `logoUrl` từ quan hệ bàn đã xác thực → Area → Restaurant, đọc trong transaction mở/tái sử dụng phiên. `LogoUrl` NULL/rỗng/khoảng trắng được trả thành JSON null. Giữ nguyên trường cũ và quét ẩn danh; không dùng tên/ID do client gửi để chọn nhà hàng, không cấp GuestAccess/cookie chỉ vì quét QR. `TableSessionScanReply` bổ sung field 9/10/11, không đổi field cũ. Endpoint Waiter mở bàn dùng chung response cũng có metadata, giữ nguyên kiểm tra quyền.

Kiểm chứng metadata: 5 test mới qua Gateway TestServer với SQLite và image store/nhận diện tài khoản giả lập kiểm tra QR đúng nhà hàng (kể cả client gửi ID/tên khác), tên nhà hàng/khu vực, logo NULL/rỗng/khoảng trắng/có URL, tạo và tái sử dụng phiên, metadata cập nhật từ DB, không tạo GuestAccess/cookie khi chỉ scan. Build toàn solution Release đạt. Test riêng scan/QR: 30 đạt, 4 SQL test bỏ qua.

Docker: đã rebuild Restaurant, Identity và Gateway từ working tree, dùng project/database thử `identity-qr-check`; request scan không có Authorization trả HTTP 200, tên/area đúng DB và logoUrl JSON null; cập nhật URL logo trong DB thử rồi scan lại trả URL mới, cùng phiên, isNewSession=false; token sai trả 404. Không thay đổi dữ liệu hoặc schema trên volume/database Docker chính trong đợt metadata. Lượt khởi động Docker đầu có lỗi db-init kết nối IdentityDb trong lúc SQL khởi động; chạy lại sau khi DB sẵn sàng thì initializer Exited(0) và API chạy được. SQL gốc dùng varchar/collation SQL_Latin1_General_CP1_CI_AS cho RestaurantName/AreaName; fixture Docker dùng tên ASCII sau khi assertion tên tiếng Việt không khớp, không tự đổi kiểu cột trong đợt này. Dữ liệu tiếng Việt được kiểm tra trên SQLite không thay thế việc kiểm tra lưu Unicode ở SQL Server.

Lượt test solution đầu chạy song song với Docker build có hai lỗi: AI TotalBudgetIncludesDataReadAndModelTime đo 1,8766 giây vượt ngưỡng 1,8 giây; test Identity IncompatibleExistingSchemaIsRejectedWithoutRewritingData timeout ở khởi tạo kết nối LocalDB. Chưa sửa code/test ngoài phạm vi metadata; giữ kết quả lượt này để đối chiếu và chạy lại solution tuần tự sau khi dừng Docker thử.

Kết quả chạy lại tuần tự sau khi dừng Docker thử: dotnet test Backend.sln -c Release --no-build --no-restore -m:1 đạt 1.172, lỗi 0, bỏ qua 100; hai test lỗi ở lượt song song đã đạt mà không sửa code hoặc assertion. Identity 76/76, Restaurant 146 đạt/51 bỏ qua, AI 163/163. Chưa chạy GitHub Actions Ubuntu. Đã rà diff; chưa commit/push.


## 2026-11-24 — Bảo toàn tiếng Việt trên toàn backend

Nhánh `fix/vietnamese-unicode`, baseline `9a2bbe0`; thay đổi chưa commit. Các bảng/cột dưới đây đã tồn tại. Chỉ sửa kiểu chuỗi, giữ độ dài ký tự, NULL/NOT NULL, collation và DEFAULT hiện có; không thêm DEFAULT. Không thêm/xóa bảng/cột, PK, FK hoặc quan hệ; cardinality và dây ERD không đổi. Chỉ đổi nhãn kiểu dữ liệu cho 56 cột dưới đây.

SQL tạo mới: `docker/sql/schema/{Database}.sql` cho 7 database. SQL nâng cấp bảo toàn dữ liệu: `docker/sql/upgrades/UnicodeText.sql`, tự gọi trong `docker/sql/init.sh` sau các upgrade nghiệp vụ. `BillingPayOs.sql`, `BillingProviderConfiguration.sql`, `RestaurantCloudinary.sql` được cập nhật kiểu tạo mới/kiểm tra tương thích để chạy lại được sau nâng cấp. Index/UQ/CHECK/DEFAULT phụ thuộc được dựng lại cùng tên/định nghĩa/tùy chọn; không thêm ràng buộc nghiệp vụ. Script từ chối dependency không hỗ trợ thay vì xóa âm thầm.

Đã áp dụng và kiểm tra trên SQL Server Docker **tách biệt** (database mới, fixture schema varchar cũ, chạy lặp, đối chiếu số dòng/index/giá trị cũ). Chưa áp dụng database dev đang chạy của nhóm. Đã scaffold từ database thử bằng EF 8.0.24; chỉ cập nhật mapping ảnh hưởng, giữ các tùy chỉnh tenant. Repo SQL riêng được đồng bộ 54 định nghĩa có sẵn và file upgrade Unicode; hai cột PaymentProviderConfig.ReturnUrl/CancelUrl chưa có ở baseline SQL riêng nên không tự thêm. Bản SQL riêng cần hoàn tất các upgrade nghiệp vụ hiện tại trước khi áp dụng UnicodeText.sql; không coi nó đã đồng bộ toàn bộ Billing. Không ghi đè thay đổi Billing/README đã tồn tại.

| Database | Bảng.cột có sẵn | Thay đổi kiểu | Nullability giữ nguyên |
| --- | --- | --- | --- |
| IdentityDb | Role.RoleName | varchar(50) → nvarchar(50) | NOT NULL |
| IdentityDb | Role.Description | varchar(max) → nvarchar(max) | NULL |
| IdentityDb | User.UserName | varchar(100) → nvarchar(100) | NOT NULL |
| IdentityDb | User.Email | varchar(255) → nvarchar(255) | NOT NULL |
| IdentityDb | User.FullName | varchar(255) → nvarchar(255) | NOT NULL |
| IdentityDb | User.AvatarUrl | varchar(max) → nvarchar(max) | NULL |
| IdentityDb | UserSession.UserAgent | varchar(max) → nvarchar(max) | NULL |
| IdentityDb | UserSession.DeviceType | varchar(50) → nvarchar(50) | NULL |
| IdentityDb | AuditLog.OldValue | varchar(max) → nvarchar(max) | NULL |
| IdentityDb | AuditLog.NewValue | varchar(max) → nvarchar(max) | NULL |
| RestaurantDb | Restaurant.RestaurantName | varchar(255) → nvarchar(255) | NOT NULL |
| RestaurantDb | Restaurant.Address | varchar(500) → nvarchar(500) | NOT NULL |
| RestaurantDb | Restaurant.LogoUrl | varchar(max) → nvarchar(max) | NULL |
| RestaurantDb | Reservation.ContactName | varchar(255) → nvarchar(255) | NOT NULL |
| RestaurantDb | Area.AreaCode | varchar(50) → nvarchar(50) | NOT NULL |
| RestaurantDb | Area.AreaName | varchar(100) → nvarchar(100) | NOT NULL |
| RestaurantDb | Table.TableCode | varchar(50) → nvarchar(50) | NOT NULL |
| RestaurantDb | SessionTable.MoveReason | varchar(500) → nvarchar(500) | NULL |
| RestaurantDb | Qr.ImageUrl | varchar(2048) → nvarchar(2048) | NULL |
| CatalogDb | Category.CategoryName | varchar(255) → nvarchar(255) | NOT NULL |
| CatalogDb | Item.ItemName | varchar(255) → nvarchar(255) | NOT NULL |
| CatalogDb | Item.Description | varchar(max) → nvarchar(max) | NULL |
| CatalogDb | Item.ImageUrl | varchar(max) → nvarchar(max) | NULL |
| CatalogDb | Option.OptionName | varchar(255) → nvarchar(255) | NOT NULL |
| CatalogDb | OptionGroup.OptionGroupName | varchar(255) → nvarchar(255) | NOT NULL |
| CatalogDb | RecipeMedia.Url | varchar(max) → nvarchar(max) | NOT NULL |
| CatalogDb | RecipeVersion.Instructions | varchar(max) → nvarchar(max) | NULL |
| InventoryDb | GoodsReceipt.Note | varchar(max) → nvarchar(max) | NULL |
| InventoryDb | Ingredient.IngredientName | varchar(255) → nvarchar(255) | NOT NULL |
| InventoryDb | StockMovement.Reason | varchar(max) → nvarchar(max) | NULL |
| InventoryDb | StocktakeItem.DiscrepancyReason | varchar(max) → nvarchar(max) | NULL |
| OrderingDb | CartItem.Note | varchar(max) → nvarchar(max) | NULL |
| OrderingDb | KitchenStation.KitchenStationName | varchar(255) → nvarchar(255) | NOT NULL |
| OrderingDb | OrderItem.NameSnapshot | varchar(255) → nvarchar(255) | NOT NULL |
| OrderingDb | OrderItem.Note | varchar(max) → nvarchar(max) | NULL |
| OrderingDb | OrderItemOption.NameSnapshot | varchar(255) → nvarchar(255) | NOT NULL |
| CustomerDb | Customer.CustomerName | varchar(255) → nvarchar(255) | NOT NULL |
| CustomerDb | Feedback.Comment | varchar(max) → nvarchar(max) | NULL |
| CustomerDb | FeedbackItem.Comment | varchar(max) → nvarchar(max) | NULL |
| CustomerDb | LoyaltyTier.LoyaltyTierName | varchar(100) → nvarchar(100) | NOT NULL |
| BillingDb | BillAdjustment.RuleSnapshot | varchar(max) → nvarchar(max) | NULL |
| BillingDb | BillAdjustment.Reason | varchar(1000) → nvarchar(1000) | NULL |
| BillingDb | BillLine.ItemSnapshot | varchar(max) → nvarchar(max) | NOT NULL |
| BillingDb | CashMovement.Reason | varchar(1000) → nvarchar(1000) | NULL |
| BillingDb | ElectronicInvoice.CompanyName | varchar(255) → nvarchar(255) | NOT NULL |
| BillingDb | ElectronicInvoice.Address | varchar(500) → nvarchar(500) | NOT NULL |
| BillingDb | ElectronicInvoice.Email | varchar(320) → nvarchar(320) | NOT NULL |
| BillingDb | Payment.ProviderCheckoutUrl | varchar(2048) → nvarchar(2048) | NULL |
| BillingDb | Payment.ProviderQrCode | varchar(max) → nvarchar(max) | NULL |
| BillingDb | Payment.EvidenceUrl | varchar(2048) → nvarchar(2048) | NULL |
| BillingDb | PaymentProviderConfig.ReturnUrl | varchar(2048) → nvarchar(2048) | NOT NULL |
| BillingDb | PaymentProviderConfig.CancelUrl | varchar(2048) → nvarchar(2048) | NOT NULL |
| BillingDb | PaymentProviderConfig.WebhookUrl | varchar(2048) → nvarchar(2048) | NOT NULL |
| BillingDb | Promotion.PromotionName | varchar(255) → nvarchar(255) | NOT NULL |
| BillingDb | Refund.Reason | varchar(1000) → nvarchar(1000) | NOT NULL |
| BillingDb | ShiftReport.DifferenceReason | varchar(1000) → nvarchar(1000) | NULL |

Giới hạn: chuỗi đã thành `?` trước nâng cấp không thể tự khôi phục; cần nguồn dữ liệu đúng để nhập lại. Mã kỹ thuật/status/hash/secret ciphertext giữ varchar; BaseUnit liên kết mã đơn vị ASCII nên không thay đổi. SQL thủ công nhập Unicode phải dùng tham số Unicode hoặc N'...'. NVARCHAR tăng dung lượng chuỗi; nâng cấp cần maintenance window cho database lớn vì ALTER COLUMN/index có khóa schema.

Kiểm tra dependency của 56 cột trên baseline Docker: không cột nào có DEFAULT, PK/FK hoặc index trực tiếp. Test bổ sung một UNIQUE filtered nonclustered index có INCLUDE/FILLFACTOR và CHECK/DEFAULT trên IngredientName để xác minh nhánh dựng lại dependency (không thêm các object thử vào schema thật).

Bằng chứng nghiệm thu cục bộ:
- `dotnet build Backend.sln -c Release`: đạt, 0 warning/0 error ở lần build solution. Docker build các service có cảnh báo nullable sẵn có trong Shared.Persistence.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1`: 1.388 đạt, 0 lỗi, 103 skipped do điều kiện môi trường của các bài tích hợp khác; skipped không tính là đạt. Có cấp connection SQL riêng cho 56 bài parameter Unicode.
- `pwsh -File scripts/Test-UnicodeDockerBootstrap.ps1 -VerifyEf`: đạt kiểm tra SQL mới/cũ, dependency, chạy lặp và 112 test Unicode (0 skipped).
- `pwsh -File scripts/Test-BillingDockerBootstrap.ps1`: ALL DOCKER BOOTSTRAP CHECKS PASSED.
- Gateway Docker build từ source mới, database thử tách biệt: POST/GET categories; POST/GET items (tên/mô tả); PUT/GET users/me; POST/GET ingredients đều giữ nguyên chuỗi tiếng Việt. Không gọi Cloudinary thật trong các ca này.
- Lần test tổng đầu chạy song song build Docker có 3 lỗi timing/regex tại AI. Chạy AI riêng đạt 163/163; chạy lại toàn solution tuần tự đạt các số liệu ở trên. Không sửa code AI.
- Chưa chạy GitHub Actions trên remote, chưa nâng cấp database dev của nhóm, chưa kiểm thử giao diện FE; không tuyên bố mọi luồng nghiệp vụ đã được kiểm thử end-to-end.


## 2026-11-25 — Quyền xem và kỳ báo cáo nhà hàng

**Không có thay đổi schema.** Chỉ bổ sung contract gRPC, DTO, lớp kiểm tra ngữ cảnh/kỳ và API Gateway; không thêm/sửa/xóa bảng/cột/PK/FK/UNIQUE/CHECK/index, không đổi quan hệ/cardinality hoặc dây ERD. DTO mới không phải entity. Không sửa repo SQL riêng. Fixture Docker tách biệt dùng schema/seed hiện có để kiểm thử; không áp SQL hoặc đổi dữ liệu database dev của nhóm. Chi tiết file, test, giới hạn và review tại `docs/restaurant-performance-reports.md`, mục R1. Thay đổi đang để chưa commit/push trên `feature/restaurant-performance-reports`.


## 2026-11-25 — Hoàn thiện định dạng thời gian sau review

**Không có thay đổi schema.** Chỉ chuẩn hóa hậu tố UTC z thường trong validation kỳ báo cáo, bổ sung hướng dẫn encode URL trong Swagger và test. Không đổi bảng/cột/khóa/ràng buộc/index/quan hệ, không sửa SQL Docker hoặc repo SQL riêng, không áp thay đổi database nhóm. Kiểm chứng Docker dùng project và dữ liệu demo riêng. Bản sửa dựa trên commit 1a64b53, giữ nguyên lịch sử và chưa commit/push. Chi tiết trong `docs/restaurant-performance-reports.md`, mục hoàn thiện sau review R1.


## 2026-11-25 — Báo cáo doanh thu và tiền thu theo kỳ

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng/cột/default/PK/FK/UNIQUE/CHECK/index hoặc quan hệ/cardinality/dây ERD. Chỉ thêm repository đọc, DTO, tổng hợp, RPC/API và test. Không sửa SQL Docker hoặc repo Database-SQLServer riêng; không áp thay đổi trên database dev của nhóm. Fixture SQL tổng hợp chỉ dùng trong project Docker thử tách biệt, không được tính là nguồn hoàn/thu PayOS thật. Chi tiết và giới hạn dữ liệu tại `docs/restaurant-performance-reports.md`, mục R2. Giữ HEAD 8e8d872, thay đổi chưa commit/push.


## 2026-11-25 — Báo cáo theo phương thức và ca thu ngân (R3 đang làm)

**Không có thay đổi schema.** Bổ sung filter/partition trên dữ liệu đã có, không đổi bảng/cột/NULL/default/khóa/ràng buộc/index/quan hệ/cardinality hoặc ERD; không sửa SQL Docker/upgrade/repo SQL riêng. Chưa bổ sung nguồn kênh từ Ordering hoặc cột Channel; phần này đang chờ người dùng chốt bill nhiều kênh, xem mục R3 trong `docs/restaurant-performance-reports.md`. Không thay dữ liệu database nhóm, không commit/push.


## 2026-11-25 — Hoàn thiện nguồn kênh báo cáo R3

**Không có thay đổi schema.** Chỉ bổ sung RPC/DTO/client và truy vấn chỉ đọc Order.Channel có kiểm tra RestaurantRef, không thêm Channel vào Bill hoặc thay bảng/cột/khóa/index/ràng buộc/default/NULL/cardinality/dây ERD. Không sửa SQL Docker/upgrade hoặc repo SQL riêng, không áp thay đổi database nhóm. Fixture nguồn kênh/thanh toán trong project Docker riêng được ghi rõ ở tài liệu báo cáo; không phải migration hay dữ liệu vận hành. Giữ nguyên luồng lập/thu/hoàn và quyền API quản trị cũ, chưa commit/push.


## 2026-11-25 — Sửa trạng thái đầy đủ của phân tổ kênh báo cáo

**Không có thay đổi schema.** Chỉ sửa completeness của chiều kênh và bổ sung regression/test pipeline thật trong TestServer. Không đổi bảng/cột/default/NULL/khóa/index/ràng buộc/cardinality/ERD, SQL Docker/upgrade hay repo SQL riêng. Fixture SQLite/SQL thử không phải model/schema production. Không đổi DB của nhóm hoặc luồng thu/hoàn; giữ nguyên HEAD8e8d872, chưa commit/push. Xem mục review kênh R3 trong tài liệu báo cáo.


## 2026-11-25 — R4 bảng món bán và snapshot giá vốn

**Không có thay đổi schema.** BillLine.CogsSnapshot đã có sẵn, giữ decimal(18,2) NULL, không DEFAULT; người dùng chốt đây là giá vốn tổng dòng. Chỉ bổ sung reader/DTO/RPC/bảng món, không thêm/sửa/xóa bảng/cột/khóa/ràng buộc/index/quan hệ/cardinality hoặc ERD, không sửa model production/SQL Docker/upgrade/repo SQL riêng. Không tự điền COGS, không chia cost khi lượng thay đổi; nguồn writer kho vẫn thiếu. restaurant_id cấp gốc mới là field proto, không phải cột SQL. Fixture SQLite/SQL tách biệt phục vụ test không phải migration hoặc dữ liệu vận hành. Không đổi database nhóm, chưa commit/push; chi tiết và bằng chứng R4 tại docs/restaurant-performance-reports.md.

## 2026-11-25 — Hoàn thiện review R4

**Không có thay đổi schema.** Chỉ tách completeness theo ItemId, bổ sung currency vào DTO/proto và cảnh báo lọc kênh; không thêm/sửa/xóa bảng/cột/NULL/default/khóa/index/quan hệ/cardinality/ERD. Không sửa SQL tạo mới/upgrade/model hoặc database nhóm. Dữ liệu tổng hợp Docker riêng chỉ phục vụ kiểm thử, không phải migration. Chi tiết kết quả tại docs/restaurant-performance-reports.md. Chưa commit/push.


## 2026-11-25 — R5 giảm giá và tỷ suất đóng góp từng món

**Không có thay đổi schema.** Chỉ thêm field DTO/proto cho discounts và contributionMarginPercent, tổng hợp chỉ đọc và kiểm thử. Không thêm/sửa/xóa bảng/cột/NULL/default/PK/FK/UNIQUE/CHECK/index/quan hệ/cardinality/dây ERD; không sửa model, DbContext, SQL tạo mới/upgrade/Docker hoặc repo SQL riêng. BillAdjustment, DiscountAllocated, CogsSnapshot đều là bảng/cột đã có; đọc hay tạo fixture không có nghĩa tạo bảng/cột mới. Dữ liệu fixture chỉ được ghi vào SQLite và SQL Server của project Docker thử riêng, không thay database nhóm. Chưa commit/push. Chi tiết và giới hạn nguồn xem R5 trong docs/restaurant-performance-reports.md.


## 2026-11-25 — Hoàn thiện review R5

**Không có thay đổi schema.** Chỉ sửa phạm vi cảnh báo theo kết quả lọc món, bổ sung test backend/Gateway cho bảng trống và tỷ suất âm/gộp cùng món. Không thêm/sửa/xóa bảng/cột/kiểu/NULL/default/khóa/index/quan hệ/cardinality/ERD, SQL tạo mới/upgrade/model/DbContext hoặc repo SQL riêng. SQL fixture chỉ dùng trong project Docker thử riêng đã dọn, không thay database nhóm. Chưa commit/push; chi tiết tại mục review R5 của tài liệu báo cáo.


## 2026-11-26 — Sửa lỗi nạp bộ dữ liệu mẫu một tháng trên Docker

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng/cột/kiểu/NULL/default/PK/FK/UNIQUE/CHECK/index, không đổi quan hệ/cardinality/dây ERD, không scaffold model/DbContext và không sửa repository SQL riêng. Đây là chỉnh dữ liệu và thứ tự INSERT trong seed phát triển, không phải migration cho database đã sử dụng.

| File | Thay đổi thực tế |
| --- | --- |
| docker/sql/seed/RestaurantDb.sql | Bảng Qr có sẵn: đổi4 QrId bị lặp của các bàn có TableId kết thúc017/018/019/020 sang QrId có hậu tố007/008/009/00a. Giữ6 QrId đầu, toàn bộ TableId, TokenHash, CreatedAt, RevokedAt và ImageUrl. Kết quả10 bản ghi/10 PK riêng, không đổi kiểu uniqueidentifier hoặc FK. |
| docker/sql/seed/InventoryDb.sql | Chuyển nguyên khối Stocktake và StocktakeItem lên trước StockMovement để đáp ứng FK_StockMovement_StocktakeItem. Không sửa bất kỳ giá trị hàng nào; so sánh toàn bộ khối dữ liệu trước/sau cho kết quả giống nhau. |
| docker/sql/seed/BillingDb.sql | PaymentMethod chỉ INSERT mã chưa tồn tại, dùng NOT EXISTS với UPDLOCK/HOLDLOCK trong transaction của runner; giữ các mã Cash/BankTransfer đã được upgrade tạo và không bật lại bản ghi đã tắt. Giữ4 mã đã có trong bộ seed, không tự thêm phương thức mới. Chuyển nguyên khối CashMovement/CashHandover xuống sau Payment, đáp ứng FK nội bộ; giữ toàn bộ dữ liệu các bảng còn lại. |
| scratch/generate_seeds.js | Sinh QrId theo chỉ số duy nhất trong TABLES thay vì phần số của TableCode có thể lặp giữa các khu vực; đồng bộ thứ tự bảng Inventory/Billing và quy tắc PaymentMethod để chạy generator lại không tái tạo lỗi. Không chạy generator ghi đè7 file trong repository; kiểm tra bằng bản sao ở thư mục tạm. |
| scripts/Test-UnicodeDockerBootstrap.ps1 | Cập nhật điểm kiểm UTF-8 theo IngredientCode=RICE và tên bắt đầu bằng Gạo, phù hợp dataset mới có tên Gạo ST25. Giữ kiểm tra fresh Unicode và legacy ANSI loss; không đổi dữ liệu nguyên liệu để qua test. |

Bằng chứng trước sửa từ SQL Server cô lập:

- Seed Restaurant: init exit1, PK_Qr trùng; Identity8 role/15 user đã seed nhưng Restaurant0 nhà hàng/0 phân công/0 QR do rollback.
- Sau sửa QR mới đến bước Inventory: FK_StockMovement_StocktakeItem vì chèn con trước cha.
- Sau sửa thứ tự Inventory mới đến Billing: PK_PaymentMethod trùng Cash do upgrade đã tạo trước seed; rà FK nội bộ còn thấy CashMovement/CashHandover chèn trước Payment.

Kiểm chứng sau sửa:

- node --check scratch/generate_seeds.js đạt. Chạy generator thực tế trong thư mục tạm,10 QR riêng và mapping ID/bàn/token khớp seed đã sửa; đầu ra Inventory/Billing giữ thứ tự cha trước con và PaymentMethod bỏ qua mã đã có.
- Build image db-init từ working tree, chạy đúng init.sh có auto seed trên SQL Server/volume mới riêng: exit0, đủ marker seed ở7 database; Identity8 role/15 user; Restaurant2 nhà hàng/9 phân công/10 QR.
- Chạy lại initializer trên cùng database thử: exit0, số dòng của mọi bảng ở cả7 database giữ nguyên.
- Trong database thử, tắt Cash rồi chạy lại khối seed PaymentMethod: vẫn tắt, không phát sinh mã trùng; tổng4 phương thức.
- Build/start Identity, Restaurant và Gateway từ source trên project thử: Admin admin@sen-viet.test đăng nhập được; GET /api/roles HTTP200 trả cashier/inventory/kitchen/manager/waiter. Đã logout phiên thử. Chưa kiểm thử giao diện hoặc luồng nghiệp vụ/thanh toán của toàn bộ dữ liệu mẫu một tháng.
- Chạy đúng bài bootstrap CI: ./scripts/Test-UnicodeDockerBootstrap.ps1 -VerifyEf đạt fresh/repeat, tái hiện VARCHAR loss, nâng cấp legacy giữ nguyên dữ liệu/số dòng/index, từ chối an toàn custom statistic và khôi phục sau xử lý tường minh; 112 test EF/SQL Server đạt,0 lỗi,0 bỏ qua. Bài có build Foundation.Tests và các DAL; còn cảnh báo CS0108/CS0114 có sẵn ở Promotion/Voucher, không sửa ngoài phạm vi.
- Không cập nhật database/volume nhóm; container, volume, tag image và credential của project thử được dọn. File seed không tự sửa lại database đã có DockerSeedVersion; không xóa marker hoặc volume để ép nạp lại dữ liệu.

Các chỉnh sửa CI về DI hai model chuyển bàn và test ngân sách AI đang có sẵn trước lượt này được giữ nguyên, không gộp thành thay đổi dữ liệu mẫu. Chưa stage/commit/push.

Tên commit đề xuất: **Sửa khóa trùng và thứ tự nạp dữ liệu mẫu Docker**.
Mô tả: **Tạo mã QR riêng, tránh chèn trùng phương thức thanh toán đã có, sắp thứ tự seed theo FK và đồng bộ generator; cập nhật kiểm tra Unicode theo bộ mẫu mới. Không thay đổi schema hoặc dữ liệu nhóm.**


## 2026-11-26 — Rà lại Unicode trong backend và dữ liệu Docker

Không có thay đổi schema. Không thêm/sửa bảng, cột, kiểu dữ liệu, NULL/DEFAULT, PK/FK/index hoặc quan hệ ERD trong đợt này. Các cột `BillingDb.CashMovement.Reason` và `BillingDb.ShiftReport.DifferenceReason` đã là `nvarchar(1000) NULL` trong SQL tạo mới và manifest nâng cấp `docker/sql/upgrades/UnicodeText.sql`; không tạo migration mới.

Thay đổi code:
- `CashMovementService.ValidateReason` và `ShiftReportService.NormalizeReason`: bỏ kiểm tra khả năng biểu diễn CP1252 còn sót từ schema cũ. Giữ nguyên trim, giới hạn 1000 ký tự, từ chối ký tự điều khiển và các kiểm tra quyền/tiền/transaction.
- Bổ sung test tiếng Việt/Unicode ở service, test đọc lại từ context SQL Server mới, và kiểm tra JSON qua Gateway test host (gRPC giả lập). Không coi Gateway giả lập là Docker toàn luồng.
- `scripts/Test-UnicodeDockerBootstrap.ps1`: đọc SQL/script tường minh UTF-8, dùng tiền tố Unicode không phụ thuộc encoding file, đặt/khôi phục OutputEncoding; khai báo PowerShell 7 theo môi trường CI hiện có. Windows PowerShell 5.1 bị từ chối trước khi tạo tài nguyên, vì lần chạy thực tế trên 5.1 gặp lỗi xử lý native stderr/cleanup; không tuyên bố hỗ trợ 5.1.
- README: sửa hướng dẫn cũ còn nói chỉ cho phép CP1252 thành NVARCHAR và yêu cầu nâng cấp database cũ trước khi chạy service.

Khảo sát chỉ đọc trên SQL Server Docker nhóm `backend-sqlserver-1`:
- Quét 7 database; tìm 20 ô văn bản chứa `?` trên 9 cột. Đây là danh sách ứng viên, không mặc định mọi dấu hỏi đều là dữ liệu hỏng.
- IdentityDb: Role.RoleName (6), User.FullName (6).
- RestaurantDb: Restaurant.Address (1), Area.AreaName (1).
- InventoryDb: Ingredient.IngredientName (1).
- CatalogDb: Category.CategoryName (1), Item.ItemName (1), OptionGroup.OptionGroupName (1).
- OrderingDb: KitchenStation.KitchenStationName (2).
- CustomerDb/BillingDb: không có dấu hỏi tại các cột tên/mô tả/lý do/địa chỉ/ghi chú đã quét; không khẳng định toàn bộ dữ liệu hai database không có vấn đề.
- Xác minh Role.RoleName và User.FullName trên container hiện tại vẫn là varchar; hex lưu của vai trò kitchen có byte 3F (`B?p`), chứng minh đã mất ký tự trước khi trả API. Các file schema/nâng cấp mới không tự sửa dữ liệu từng bị mất dấu. Chưa nâng cấp hoặc ghi đè database nhóm trong lượt này; không xóa volume, không tự suy đoán tên/người/địa chỉ để phục hồi.

Quét source và cấu hình: 1.395 file văn bản tracked/untracked không bị ignore đều đọc được UTF-8, không phát hiện U+FFFD hoặc mẫu mojibake đã kiểm tra. Quét bổ sung toàn thư mục, kể cả ignored, 1.353 file văn bản dưới 3 MB ngoài .git/bin/obj/node_modules/TestResults/.vs/packages, không phát hiện mẫu tương tự. Hai tập có chồng lặp, không cộng thành tổng file duy nhất. Không in giá trị secret. Không có literal Unicode SQL thiếu tiền tố N trong các file SQL đã quét. Kết quả này không chứng minh mọi chuỗi từng bị biến thành dấu hỏi đều có thể nhận diện tự động.

Kiểm chứng:
- Trước sửa: 4 ca mới UnicodeReasonIsPreserved/UnicodeDifferenceReasonIsPreserved đều lỗi đúng tại kiểm tra CP1252.
- Sau sửa bước đầu: 460 test Billing đạt, 0 lỗi, 42 test SQL bỏ qua vì chưa cấp connection; không tính skipped là đạt.
- `pwsh -NoProfile -File scripts/Test-UnicodeDockerBootstrap.ps1 -VerifyEf`: đạt fresh/repeat, fixture mất dấu VARCHAR, nâng cấp bảo toàn dữ liệu/index/ràng buộc, từ chối dependency không hỗ trợ; 112/112 test EF/SQL Server đạt, 0 skipped. Tài nguyên thử riêng được dọn, không dùng volume nhóm.

- Lượt cuối cấp BILLING_SQL_TEST_CONNECTION tới container SQL Server riêng: 503/503 test Billing đạt, 0 lỗi, 0 skipped; có VietnameseLedgerAndClosingReasonsRoundTripSqlServer đọc lại bằng DbContext mới và Gateway test host với gRPC giả lập. Kết quả: TestResults/Encoding/encoding.trx (artifact không commit).
- `dotnet build Backend.sln -c Release --no-restore -m:1`: đạt, 0 warning/0 error trong lượt cuối; `git diff --check`: đạt. Chưa build/chạy image Billing mới qua Gateway Docker của nhóm; kiểm chứng HTTP dùng Gateway test host, không gọi là toàn luồng Docker.
- Toàn bộ container/network/image SQL thử đã dọn. Nhánh data/seed-data-docker, giữ nguyên HEAD 4253d53; thay đổi để review, chưa commit/push.


## 2026-11-26 — Sửa xung đột fixture báo cáo với seed Docker mới

Không có thay đổi schema: không đổi bảng/cột, kiểu, NULL/DEFAULT, PK/FK/UNIQUE/CHECK/index hoặc dây ERD. Giữ nguyên UX_CashRegister_Restaurant; không sửa database nhóm hay seed demo một tháng.

- Tái hiện thực tế: bootstrap hiện tại nạp seed demo, sau đó fixture BillingDb.sql chèn quầy gây Msg 2601 tại dòng 5, trùng quầy nhà hàng 20000000-0000-0000-0000-000000000001. Bài test cũ trên snapshot origin/dev trước seed một tháng vẫn đạt 64 HTTP checks.
- scripts/Test-RestaurantReports.ps1: dùng thư mục fixture seed read-only riêng cho initializer của project Docker thử; bỏ INSERT fixture lần hai, giữ lượt seed tường minh chạy lặp theo marker. Lỗi SQL thêm mã Msg/exit code, chi tiết giữ trong log riêng.
- Tests/Fixtures/RestaurantReports/IdentityDb.sql (mới): tài khoản/role tổng hợp phục vụ test, độc lập seed demo. RestaurantDb.sql: thêm nhà hàng A và sáu phân công cần cho kiểm thử quyền. Chỉ INSERT dữ liệu thử vào database cô lập, không phải migration/production seed.
- Không thay fixture BillingDb.sql/OrderingDb.sql/expected.json, không thay backend endpoint/công thức/quyền.
- Lệnh CI tương đương đã chạy local: pwsh -NoProfile -File scripts/Test-RestaurantReports.ps1 -OutputDirectory TestResults/RestaurantReports. Kết quả 19/19 ca, 64 HTTP checks đạt; 11 Covered/8 Partial; cleanupSucceeded=true. Build lại đủ image từ working tree rồi gọi API qua Gateway Docker thật; không gọi PayOS/Gemini thật. Kết quả TestResults/RestaurantReports/results.json là artifact không commit. Chưa chạy lại GitHub Actions Ubuntu sau sửa.
- Đã fast-forward nhánh local data/seed-data-docker từ 4253d53 đến commit remote đã có 5876c43 để sửa đúng phiên bản CI; giữ nguyên toàn bộ thay đổi Unicode chưa commit. Không tạo commit mới, không push, không sửa lịch sử.

## 2026-11-26 — Sửa ánh xạ null của khuyến mãi qua Protobuf

Không có thay đổi schema hoặc SQL/ERD: không sửa bảng/cột, kiểu dữ liệu, NULL/DEFAULT, PK/FK/index hay quan hệ. Không đổi contract Protobuf, seed demo, dữ liệu nhóm, quyền trạm bếp hoặc nghiệp vụ đăng nhập.

- `Services/billing_service/Billing.API/GrpcServices/PromotionGrpcService.cs`, Map: chỉ gán ItemRef/CategoryRef khi Guid? có giá trị; nếu null giữ trường optional vắng mặt. Dùng chung cho phản hồi list/detail/create/update/status.
- `Services/api_gateway/ApiGateway/Controllers/Billing/PromotionController.cs`, Create: áp dụng cùng quy tắc cho request target; tránh ArgumentNullException trước RPC khi tạo theo món/danh mục. Không dùng chuỗi rỗng hoặc Guid.Empty thay thế ID thiếu.
- `Tests/Billing.Tests/PromotionNullMappingTests.cs`: 8 ca kiểm tra bốn tổ hợp nullable, presence flag Protobuf, JSON null và giá trị ID qua HTTP Gateway cùng gRPC thật trong TestServer; business service giả lập, không coi là test SQL/quyền thực tế. Trước sửa 6 lỗi/2 đạt; sau sửa 8/8 đạt. Luồng kiểm thử HTTP gồm create/list/detail/update/status.
- `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --no-restore` với BILLING_SQL_TEST_CONNECTION đến SQL Server Docker riêng: 511/511 đạt, 0 lỗi/0 skipped. TRX tại TestResults/Encoding/encoding.trx (artifact bỏ qua Git). Không dùng database nhóm.
- `dotnet build Backend.sln -c Release --no-restore -m:1`: đạt, 0 lỗi; 6 cảnh báo CS8618 có sẵn tại Identity.Common/DTOs/UserDTO.cs, không sửa ngoài phạm vi. git diff --check đạt.
- Kiểm thử Docker cô lập từ working tree và seed demo một tháng: trước sửa GET /api/promotions trả 502 và log ArgumentNullException tại Map; sau sửa Admin/Manager trả 200. POST ba loại Order/Item/Category trả 201, GET detail trả 200 và giữ đúng null/ID. Đăng nhập tài khoản seed hợp lệ trả 200; email seed cũ không tồn tại và mật khẩu sai trả 401. Đây là dữ liệu dev tổng hợp, không chứng minh chính xác credentials mà người dùng đã nhập trên máy khác.
- Lượt công cụ Docker đầu gửi request trước khi Gateway sẵn sàng nên lỗi ResponseEnded; đã thêm chờ đăng nhập thành công vào công cụ tạm rồi chạy lại đạt. Không sửa production để né lỗi công cụ kiểm thử.
- Đã dọn container/volume/image của project thử; không cập nhật image/container backend-* đang dùng của nhóm. Không kiểm thử frontend, không commit/push. Vấn đề Admin nhà hàng bị 403 ở GET /api/kitchen-stations được giữ ngoài đợt sửa này.


## 2026-11-26 — Bổ sung cấu hình đặt bàn cho Docker

Không có thay đổi schema hoặc SQL/ERD: không thêm/sửa bảng, cột, kiểu dữ liệu, NULL/DEFAULT, khóa/index hoặc quan hệ. Không sửa ReservationAccess, quy trình duyệt/hết hạn, quyền quản trị, Identity hoặc seed. Giữ nguyên các thay đổi Khuyến mãi chưa commit.

- Nguyên nhân đã xác minh: .dockerignore loại appsettings.json, chỉ giữ appsettings.Docker.json. Reservations trước đây chỉ nằm trong file local nên image Docker thiếu toàn bộ section, StaffRoleIds rỗng/các thời lượng bằng 0/ExpirationEnabled=false. Tái hiện bằng image build lại từ source cũ trên project Docker riêng: waiter1 và cashier1 đăng nhập thành công nhưng GET /api/reservations?status=Pending&page=1&pageSize=100 đều trả 403.
- Services/restaurant_service/Restaurant.API/appsettings.Docker.json: bổ sung đúng giá trị Reservations có sẵn trong appsettings.json: StaffRoleIds=[waiter,cashier], DefaultDurationMinutes=120, DurationMinutesByRestaurant={}, MinimumAdvanceMinutes=30, MaximumAdvanceDays=30, HoldBeforeMinutes=30, HoldAfterMinutes=30, ExpirationEnabled=true, ExpirationIntervalSeconds=30, ExpirationBatchSize=100. Không đưa cấu hình local/secret vào image hoặc thay .dockerignore.
- Tests/Restaurant.Tests/ReservationDockerConfigurationTests.cs (mới): chỉ nạp file Docker, không kế thừa appsettings.json, kiểm tra quyền và toàn bộ thời lượng/hết hạn. Trước sửa 2/2 lỗi. Sau sửa nằm trong bộ Restaurant.Tests: 207 đạt, 0 lỗi, 52 skipped do thiếu điều kiện môi trường của các bài tích hợp khác (không tính skipped là đạt). Không cố chạy bộ ReservationSqlServerTests dùng IntegratedSecurity bằng connection SA Docker.
- Kiểm thử qua Gateway Docker thật với image build từ working tree, SQL Server và seed dev riêng: waiter1/cashier1 đều 200; khi không có Pending thì items=[]/totalCount=0. Thêm một Pending của nhà hàng A và một Pending của nhà hàng B vào database thử, xác minh từng tài khoản chỉ nhận đúng bản ghi A (totalCount=1), lọc restaurantId B=403, thời gian không có dữ liệu=200/rỗng. Thu hồi phân công waiter trong fixture trả 403; phục hồi trước bước tiếp theo. Nhà hàng B và khu vực thử chỉ phục vụ kiểm chứng, không sửa seed thật hoặc database nhóm.
- Đọc lại cấu hình trong container đã build xác nhận toàn bộ tham số. Worker thật xử lý fixture quá hạn duyệt thành Expired, quá hạn đến thành NoShow, giữ Pending tương lai. Đây là SQL/Docker thật, không phải worker giả lập.
- dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --no-restore: 207 passed/0 failed/52 skipped. dotnet build Backend.sln -c Release --no-restore -m:1: đạt, 0 warning/0 error ở lượt cuối; git diff --check đạt.
- Tài nguyên thử đã dọn. Không thay thế container backend-restaurant-1 đang dùng/image thử nghiệm của nhóm; khi áp dụng phải build/recreate service restaurant bằng source mới với bộ env/Compose đang sử dụng. Không xóa volume, không thay mật khẩu/quyền dữ liệu người dùng và chưa commit/push. Chưa chạy mobile hoặc GitHub Actions sau sửa.


## 2026-11-26 — Khách đặt bàn không cần chọn khu vực

Nhánh backend: fix/reservation-waiter-table-selection, tạo từ origin/dev (6f343df). Không commit/push. Chỉ Restaurant và hợp đồng/Gateway/test cần thiết; không sửa frontend, Identity, Ordering, Billing hoặc nghiệp vụ thanh toán.

| Database/bảng | Thay đổi thực tế | NULL/DEFAULT | Khóa/index/ERD | File và trạng thái |
| --- | --- | --- | --- | --- |
| RestaurantDb.dbo.Reservation (bảng có sẵn) | RequestedAreaId: uniqueidentifier NOT NULL -> uniqueidentifier NULL | Không thêm DEFAULT; không sửa giá trị cũ | Giữ FK_Reservation_RequestedArea tới Area.AreaId, IX_Reservation_RequestedAreaId và PK/khóa khác. Mỗi Reservation có 0..1 Area; mỗi Area có 0..n Reservation. Không thêm dây/FK xuyên DB. | docker/sql/schema/RestaurantDb.sql; docker/sql/upgrades/RestaurantReservationOptionalArea.sql; docker/sql/init.sh. Đã kiểm tra LocalDB/Docker riêng, chưa áp dụng DB nhóm. |

Repo SQL riêng D:/LEARN/Doan-new/Database-SQLServer: cập nhật đúng cột RequestedAreaId ở RestaurantDb.sql và thêm upgrades/RestaurantReservationOptionalArea.sql đồng nội dung. Repo này đã có nhiều thay đổi chưa commit từ trước; giữ nguyên chúng, không gom/untrack/commit hoặc đổi nhánh main của repo SQL.

Database First: sửa creation/upgrade trước; dựng schema cũ trên LocalDB cô lập, áp dụng upgrade hai lần; xác minh dữ liệu RequestedAreaId cũ còn nguyên, FK trusted và index còn nguyên, chèn được Pending mới với null. Scaffold bằng EF Core 8.0.24 vào thư mục tạm, chỉ nhập Guid? của Reservation và relationship optional vào DbContext, giữ các tùy chỉnh mô hình hiện có. Fresh Docker và initializer chạy lặp cũng đạt. Không thêm bảng/cột khác hoặc sửa seed demo.

DTO Create/response RequestedAreaId nhận null; Protobuf đổi hai trường cùng số hiệu 2 (create) và 16 (response) sang optional, không đổi route/field number. Đầu vào cũ được giữ để tương thích nhưng không dùng khi tạo; Pending mới luôn chưa có khu vực/bàn. Tại confirm/update, sau khi khóa và kiểm tra bàn hợp lệ, lấy RequestedAreaId từ AreaId của bàn nhân viên chọn trong cùng transaction. Giữ các bàn trong cùng khu vực, cùng nhà hàng, kiểm tra sức chứa/lịch và quyền cũ. Không cho guest tự xếp bàn hoặc sửa status/endAt.

Kiểm chứng đã chạy:
- Bộ Restaurant.Tests: 209 passed/0 failed/52 skipped; gồm hai test mới Gateway -> gRPC -> Restaurant BLL -> SQLite, Identity giả lập, kiểm tra không có khu vực vẫn tạo được và waiter chọn bàn/check-in, bàn khác nhà hàng hoặc nhóm bàn khác khu vực bị chặn, không lưu dở dang và chống duyệt lặp.
- Đã thử riêng 6 test SQL cũ bằng LocalDB: bị chặn ở bước fixture tạo layout, vì fixture cấu hình admin là quản trị toàn hệ thống trong khi LayoutManagementAccess hiện chỉ cho admin/manager nhà hàng. Chưa tới thao tác reservation; không coi các ca này đã đạt hoặc coi đó là lỗi mới của chức năng. Không nới quyền production để làm test xanh.
- Docker Linux containers riêng, image từ working tree + SQL Server thật + seed dev: POST ẩn danh không requestedAreaId trả 201 Pending/null ở cả A và B (B không có khu vực; giờ mở cửa của B được bổ sung chỉ trong fixture). Waiter thấy đúng Pending của A, không thấy B; cashier đọc được Pending; duyệt B hoặc Admin nhà hàng duyệt đều 403. Hai request duyệt A cùng lúc trả 200 và 409; DB chỉ có một lần xếp bàn, khu vực suy ra đúng từ bàn. Initializer chạy lần hai đạt.
- Có hai lần công cụ tạm bị chặn: Docker Engine dừng (đã khởi động Docker Desktop lại, không reset volume) và lỗi cú pháp TimeZoneInfo trong harness (đã sửa harness, không sửa production để né lỗi). Lượt Docker cuối đạt và đã dọn container/volume/image riêng.
- Chưa áp dụng upgrade/recreate bộ container backend-* đang dùng; chưa kiểm thử frontend/mobile hoặc GitHub Actions sau sửa. Hướng dẫn request, deploy và review ở docs/restaurant-management.md.


Kết quả cuối toàn solution: `dotnet build Backend.sln -c Release --no-restore -m:1` đạt 0 warning/0 error; `dotnet test Backend.sln -c Release --no-build --no-restore --logger trx --results-directory TestResults/ReservationAreaSelection` đạt 1.524 test/0 lỗi, 107 skipped trên 9 project (không tính skipped là đạt). TRX là artifact bị ignore. Không ghi đè các kết quả thử riêng 6 ca SQL cũ bị lỗi fixture đã nêu ở trên.

Danh sách file backend của đợt này:
- Contracts/Protos/restaurant.proto: optional presence và giữ số hiệu field.
- Services/api_gateway/ApiGateway/Controllers/RestaurantManagement/ReservationController.cs: không gửi khu vực khi tạo; đọc response null an toàn.
- Services/api_gateway/ApiGateway/DTOs/Restaurant/CreateReservationDto.cs; ReservationDto.cs: nullable DTO, giữ input cũ chỉ để tương thích.
- Services/restaurant_service/Restaurant.API/GrpcServices/ReservationGrpcService.cs: bỏ yêu cầu area trên create và ánh xạ optional response.
- Services/restaurant_service/Restaurant.BLL/Implements/ReservationService.cs: tiếp nhận Pending chưa có khu vực, xếp khu vực từ bàn waiter chọn khi confirm/update.
- Services/restaurant_service/Restaurant.Common/DTOs/CreateReservationDto.cs; ReservationDto.cs: đồng bộ nullable.
- Services/restaurant_service/Restaurant.DAL/Models/Reservation.cs; RestaurantDbContext.cs: phần model/quan hệ optional từ scaffold.
- Tests/Restaurant.Tests/ReservationFlowTests.cs: cập nhật kỳ vọng không để input khu vực cũ gán Pending.
- Tests/Restaurant.Tests/ReservationAreaSelectionFlowTests.cs: hai ca Gateway mới cho luồng không khu vực và waiter xếp bàn/check-in.
- docker/sql/schema/RestaurantDb.sql; docker/sql/upgrades/RestaurantReservationOptionalArea.sql; docker/sql/init.sh: creation/upgrade bảo toàn dữ liệu và Docker tự nâng cấp.
- docs/database-change-log.md; docs/restaurant-management.md: SQL/ERD, request frontend, triển khai và hướng dẫn review Gemini.

Tên commit đề xuất: **Cho phép khách đặt bàn không cần chọn khu vực**. Mô tả: **Lưu Pending chưa xếp bàn, để waiter chọn bàn/khu vực khi duyệt; đồng bộ DTO/Protobuf, SQL nâng cấp và kiểm thử Gateway/Docker.** Chưa thực hiện commit.


## 2026-11-26 — Chỉ nhận đặt bàn trong ngày hiện tại

Đây là quyết định mới thay giới hạn đặt trước 30 ngày đã ghi ở các mục lịch sử. Riêng lần điều chỉnh này không có thay đổi schema/SQL/ERD; giữ các thay đổi nullable RequestedAreaId của đợt trước, không thay dữ liệu cũ hoặc repo SQL riêng thêm lần nữa.

- ReservationRules.Window: nhận TimeZone từ nhà hàng do backend truy vấn, so sánh ngày địa phương của StartAt đã chuẩn hóa UTC với ngày hiện tại của chính nhà hàng; không dùng ngày máy chủ/client và không dùng cửa sổ 24 giờ. Giữ tối thiểu 30 phút và kiểm tra giờ mở cửa/thời lượng. GetTimeZone được dùng chung cho kiểm tra ngày và giờ mở cửa; cấu hình timezone sai vẫn lỗi 503 có kiểm soát.
- ReservationService.Create/Update/GetAvailability lấy timezone từ nhà hàng và dùng quy tắc chung. Đổi lịch được kiểm lại sau khi khóa bàn như trước; lỗi không sửa lịch đã lưu. Không tự hủy/xóa/đổi lịch các bản ghi tương lai cũ; đọc/duyệt các yêu cầu cũ không bị thêm quy tắc mới ngoài phạm vi tạo/đổi lịch/preview.
- Bỏ MaximumAdvanceDays ở ReservationSettings và cả appsettings.json/appsettings.Docker.json, không sửa .dockerignore hoặc đưa secret vào cấu hình Docker.
- ReservationSameDayFlowTests: 6 ca HTTP Gateway/gRPC/TestServer, Identity giả lập/SQLite/clock cố định; kiểm tra ngày Việt Nam, Los Angeles, Honolulu, ngày UTC khác nhưng ngày nhà hàng giống, ngày UTC giống nhưng ngày nhà hàng khác và offset client khác. Trước sửa 3 lỗi/3 đạt; sau sửa toàn bộ đạt. Cập nhật test biên đặt trước 30 ngày thành từ chối, giữ ca 29 phút từ chối/30 phút chấp nhận; thêm kiểm tra PUT ngày mai thất bại và lịch cũ còn nguyên trong ReservationAreaSelectionFlowTests. ReservationDockerConfigurationTests cập nhật theo cấu hình không còn MaximumAdvanceDays.
- Bộ Restaurant.Tests: 215 passed/0 failed/52 skipped (skipped không phải đã kiểm chứng). Build solution đạt 0 warning/0 error ở lượt cuối. Lệnh và kết quả toàn solution bổ sung bên dưới sau khi hoàn tất.
- Docker riêng build từ working tree + SQL Server thật: tạo hôm nay 201 Pending/null, ngày hôm qua/ngày mai/sau 30 ngày đều 400; GET availability ngày mai 400; PUT đổi lịch ngày mai 400, GET lại giữ đúng StartAt đã lưu; waiter/cashier và đồng thời 200+409 vẫn đạt. Fixture dùng múi giờ UTC và giờ mở cửa hợp lệ để có khung giờ thử theo đồng hồ thật, thêm bàn thử không có lịch seed; không sửa dữ liệu nhóm. Kiểm chứng timezone/qua nửa đêm dùng clock cố định trong TestServer nêu trên. Tài nguyên thử đã dọn.
- Chưa recreate bộ container chính hoặc kiểm thử frontend/mobile/GitHub Actions. Không commit/push; giữ nguyên nhánh fix/reservation-waiter-table-selection và các thay đổi chưa commit của yêu cầu không chọn khu vực.


Kết quả cuối: dotnet test Backend.sln -c Release --no-build --no-restore --logger trx --results-directory TestResults/ReservationSameDay đạt 1.530 passed/0 failed/107 skipped trên 9 project; không tính skipped là đạt. git diff --check đạt. Các file bổ sung của phần giới hạn ngày: ReservationRules.cs, ReservationSettings.cs, hai appsettings của Restaurant, ReservationSameDayFlowTests.cs, cập nhật ReservationDockerConfigurationTests/ReservationFlowTests/ReservationAreaSelectionFlowTests và hai tài liệu hiện có. Không sửa thêm service khác hoặc SQL.
