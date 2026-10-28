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
- Database đang dùng: docker/sql/upgrades/BillingCheckout.sql và docker/sql/upgrades/OrderingCheckout.sql. Mỗi script có transaction, không xóa dữ liệu, kiểm tra shape bảng/index trước khi chấp nhận chạy lại; không tự sửa một bảng trùng tên nhưng khác cấu trúc.
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
