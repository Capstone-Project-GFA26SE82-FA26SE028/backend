# Báo cáo hiệu quả hoạt động nhà hàng

Trạng thái cập nhật 2026-11-25: R1 đã được người dùng commit tại 1a64b53. Phần hoàn thiện sau review ở cuối tài liệu hiện chưa commit/push. Các trạng thái bàn giao R0/R1 bên dưới được giữ theo thời điểm kiểm chứng.

## R0: kết quả khảo sát nguồn, chưa triển khai báo cáo

Ngày: 2026-11-25. Repository: backend mới `Doan-new/backend`; baseline `1552753`, nhánh khảo sát `fix/vietnamese-unicode`. Working tree sạch khi bắt đầu. Đợt R0 chỉ tạo tài liệu này, không thay source, API, contract, SQL, cấu hình hoặc dữ liệu. Chưa tạo/chuyển nhánh phát triển WF-05. Không commit/push.

Chuẩn: BM04 `SEP490_R2_BM04_Review2_Commitment.pdf`, mục 6.5, trang 16–21; F-WF05-01–06, TC-WF05-01–18 và DS-05a–h. SHA-256 bản PDF đã đọc: `96f63813fd47d57bb10bf1abc2acf1e917625d216050a84c465db9aebbe82bb7`. BM04 là chuẩn nghiệm thu cao nhất theo quyết định người dùng; tài liệu yêu cầu cũ chỉ dùng tìm nguồn, không thay thế BM04.

WF-05: Manager yêu cầu → xác minh quyền/kỳ → tổng hợp dữ liệu đã xác nhận → kiểm tra đầy đủ giá vốn → trả kết quả/cảnh báo. Luồng chỉ đọc, không chốt ca, đối soát PayOS, xác nhận hoàn tiền hoặc thay đổi trạng thái khi xem báo cáo.

Mức bằng chứng: các kết luận nguồn bên dưới được truy vết trên source/schema/test tại baseline; không coi database đang chạy đã khớp schema. Test đã chạy nằm ở mục kiểm chứng. Chưa có API WF-05 để gọi/nghiệm thu.

## Quy tắc đã chốt

| ID | Quy tắc | Nguồn |
| --- | --- | --- |
| AC-01 | Chỉ Manager có phân công đang hoạt động tại đúng nhà hàng được đọc báo cáo; không kế thừa quyền đọc rộng của các API Billing khác. | F-WF05-06 |
| AC-02 | Lấy múi giờ từ Restaurant, không tin múi giờ/phạm vi nhà hàng do client tự xác nhận. Kỳ `[start, end)`, start < end; nhóm giờ/ngày/tuần/tháng. | F-WF05-01 |
| AC-03 | Tuần bắt đầu thứ Hai 00:00 theo múi giờ nhà hàng. | Người dùng chốt 2026-11-25 |
| AC-04 | Doanh thu món là tiền món sau giảm giá, không gồm VAT và phí dịch vụ; VAT/phí trình bày riêng. Thực thu gồm toàn bộ tiền thanh toán đã xác nhận. | Người dùng chốt 2026-11-25 |
| AC-05 | Thanh toán theo PaidAt, hoàn tiền theo lúc xác minh; hai sự kiện có thể thuộc hai kỳ khác nhau. Không dùng ngày tạo order/bill hoặc ngày nhận webhook thay thế. | F-WF05-02, DS-05f |
| AC-06 | Thực thu ròng = thu đã xác nhận − hoàn tiền đã xác minh trong kỳ. Thu hộ chưa được Cashier xác nhận không được tính. | F-WF05-02–03 |
| AC-07 | Giá bán/option và giá vốn phải có nguồn lịch sử. Loại đơn bị từ chối và số lượng hủy khi báo cáo món; xếp hạng theo số lượng bán hợp lệ. | F-WF05-04 |
| AC-08 | Không tính trùng giảm giá/món tặng. Thiếu giá vốn không chuyển thành 0; phần lợi nhuận bị ảnh hưởng trả null/N/A và cảnh báo. | F-WF05-04–05 |
| AC-09 | Kỳ rỗng: tổng 0, danh sách rỗng; gross margin N/A khi doanh thu ròng bằng 0. | DS-05c, F-WF05-05 |
| AC-10 | Reporting chỉ đọc; không tự xác minh giao dịch, hoàn tiền, gọi model AI hoặc dùng báo cáo để sửa dữ liệu nguồn. | Main entity state path WF-05 |

## Bảng nguồn theo chỉ tiêu

“Đủ nguồn cơ bản” không có nghĩa API đã hoàn thành hoặc mọi dữ liệu cũ đã hợp lệ.

| Chỉ tiêu | Nguồn chuẩn/khóa nối | Thời điểm và quy tắc | Đánh giá R0 |
| --- | --- | --- | --- |
| Quyền Manager | Identity xác thực → RestaurantAccessGrpc.GetCurrentScope → RestaurantStaff.IsActive; Billing nhận UserId/RoleId/RestaurantIds. | Xác minh mỗi request, chọn RestaurantDataScope sau kiểm tra quyền. | Có cơ chế dùng lại; WF-05 chưa có guard chỉ-Manager. E01–E03 |
| Múi giờ | Restaurant.TimeZone. | Quy đổi biên kỳ sang UTC, nhóm theo giờ địa phương; không mặc định timezone máy/container hoặc hard-code +07. | Có cột và validation; GetCurrentScope chưa trả timezone; GetById quản trị yêu cầu admin. Cần hợp đồng đọc phù hợp, không mở quyền API quản trị. E01, E04 |
| Thu tiền mặt tại bàn | Payment nối Bill, CashHandover và CashMovement. | PaidAt; chỉ khoản đã Cashier xác nhận, khớp bill, đúng một Sale. Dùng Payment.Amount, không cộng CashReceived hoặc cộng Sale lần nữa. | Đủ nguồn cơ bản; thu hộ Pending phải loại. E05 |
| Thu PayOS tại bàn | Payment + Bill + dữ liệu đối soát đã lưu. | Payment.PaidAt; trạng thái Paid với chứng cứ đối soát hợp lệ; webhook ReceivedAt chưa phải thu thành công. | Đủ nguồn cơ bản. ReviewRequired/Unknown không tự coi đã xác minh dù có trường thời gian. E06 |
| Thu mang về | OnlinePayments theo OrderId, RestaurantId, PaidAt, TransactionReference. | PaidAt được giữ khi trạng thái đi từ Paid sang RefundPending/Refunded; không chỉ lọc Status==Paid hiện tại. | Đủ nguồn lịch sử thu; phải kiểm tra đủ chứng cứ, không cộng lại Ordering.PaymentStatus như giao dịch thứ hai. E07 |
| Hoàn tiền mang về | OnlinePayments.RefundedAt, RefundReference, RefundEvidence, RefundVerifiedBy, Amount. | Chỉ trạng thái Refunded có chứng cứ xác minh; hiện luồng hoàn toàn bộ Amount, không có trường số tiền hoàn một phần. Dedupe theo sự kiện hoàn của OrderId. | Có writer và test; Cashier xác minh chứng từ ngân hàng, không phải PayOS tự refund. E07 |
| Hoàn tiền tại bàn | Refund.PaymentId, Amount, Status, CompletedAt; RefundLine nối BillLine. | Cần trạng thái và chứng cứ xác minh thực tế, không suy từ CompletedAt đơn lẻ. | Chỉ thấy schema/model, không tìm thấy writer tạo/cập nhật Refund trong service hiện tại. Chưa có nguồn vận hành đủ kiểm chứng. E08 |
| Doanh thu món tại bàn | Bill.SubTotal, DiscountAmount; BillLine.Quantity × UnitPriceSnapshot − DiscountAllocated. | Theo thanh toán đã xác nhận, giữ snapshot; không cộng VAT/phí vào doanh thu món. | Có cấu trúc; DiscountAmount hiện khởi tạo 0, chưa có writer ưu đãi. E09 |
| Doanh thu món mang về | OrderItem/OrderItemOption snapshot qua RPC chỉ đọc cần bổ sung; liên kết OnlinePayments.OrderId. | Giá món gốc cộng option, số lượng hợp lệ; không dùng Catalog hiện tại. | Có dữ liệu trong Ordering; chưa có hợp đồng báo cáo theo tập OrderId/kỳ. Chưa có snapshot VAT/phí/discount riêng cho luồng mang về. E10 |
| VAT, phí, làm tròn | Bill.VatAmount, ServiceAmount, TotalAmount và snapshot tỷ lệ. | Trình bày riêng. Chênh làm tròn = TotalAmount − (SubTotal − DiscountAmount + VatAmount + ServiceAmount), không tự sửa các số đã lưu. | Có tại bàn, BillDTO đã tính RoundingAdjustment; không tự coi mang về có VAT/phí bằng 0 do thiếu trường. E09–E10 |
| Số đơn đã thanh toán | OrderId lịch sử từ BillLine.ItemSnapshot cho tại bàn; OnlinePayments.OrderId cho mang về. | Dedupe theo (kênh, OrderId), không đếm Payment retry/RefundLine hoặc coi một Bill luôn là một order. | Nguồn ID có; quy tắc “đơn hợp lệ” khi order đã trả rồi bị từ chối cần đối chiếu với chỉ tiêu tiền, không gộp lẫn. E09–E10 |
| Phương thức thanh toán | Payment.MethodCode; OnlinePayment đi qua IPayOsClient. | Cộng từng giao dịch thu đã xác minh đúng một lần; hoàn tiền riêng. Không đếm PaymentWebhook hoặc CashMovement như khoản thu mới. | Có nguồn; OnlinePayment không có MethodCode, adapter phải thể hiện đúng luồng PayOS hiện có. E05–E07 |
| Kênh bán | Ordering.Order.Channel; tại bàn nối qua snapshot OrderId, mang về qua OnlinePayments.OrderId. | Không nhân đôi tiền khi join một order nhiều dòng món. | Có nguồn trong Ordering; Bill không có Channel, không tự thêm cột ở R0. E10 |
| Ca thu ngân | Payment.CashShiftId → CashShift → CashRegister. | Nhóm theo ca gắn giao dịch, không lấy ca đang mở hiện tại gán cho giao dịch cũ. | Có ở tại bàn. OnlinePayment không có CashShiftId: trả nhóm không gắn ca khi xem toàn nhà hàng, không tự gán ca/ẩn khỏi tổng. E05, E07 |
| Số lượng món/ranking | BillLine.OrderItemRef và ItemSnapshot.ItemId; Ordering: Quantity − CancelledQuantity, trạng thái hợp lệ. | BillLine.Quantity đã là lượng sau hủy khi lập bill; không trừ CancelledQuantity lần hai. Giá BillLine.UnitPriceSnapshot đã gồm option; không cộng option lần hai. | Có nguồn tại bàn; lịch sử hủy sau thanh toán/hoàn cần đọc đúng nguồn, không sửa bill Paid khi làm report. E09–E10 |
| Giá vốn lịch sử | BillLine.CogsSnapshot; RecipeVersionRef và StockMovement chỉ hỗ trợ truy vết nguyên liệu. | Chỉ lấy giá vốn có nghĩa rõ ràng và đã được ghi; loại Quantity=0 khỏi số dòng cần giá vốn. | CogsSnapshot luôn null khi tạo dòng mới; giữ nguyên nếu đã có khi refresh. Chưa chốt ý nghĩa đơn vị/tổng dòng. Inventory không có đơn giá nhập/đơn giá xuất hoặc amount-cost. E09, E11 |
| Giảm giá, món tặng | BillAdjustment, DiscountAllocated, Promotion/Voucher, RuleSnapshot. | Dùng số đã áp dụng/snapshot, không áp lại rule khuyến mãi hiện tại; không cộng giảm giá tổng bill với phân bổ dòng lần hai. | Có bảng nhưng không tìm thấy writer áp dụng BillAdjustment/Promotion/Voucher/discount trong service. Món giá 0 chưa đủ bằng chứng là món tặng, không tự phân loại. E12 |
| Doanh thu ròng/lợi nhuận | Tổng doanh thu món sau giảm giá − phần tiền món được hoàn đã xác minh; lợi nhuận gộp = doanh thu ròng − giá vốn. | Không trừ VAT/phí đã hoàn vào doanh thu món. Không tự hoàn nhập giá vốn chỉ vì có hoàn tiền. | Bị chặn phần đầy đủ bởi nguồn giá vốn và phân bổ hoàn/ưu đãi; phần thiếu trả incomplete/N/A, không 0. |

## Công thức và cách không tính trùng

1. Tập thu tại bàn: chọn giao dịch hợp lệ trước; aggregate ở mức PaymentId, rồi mới ghép dữ liệu món. Không SUM Payment.Amount trực tiếp trên join BillLine/RefundLine/PaymentWebhook.
2. Tập thu mang về: giữ dấu vết PaidAt và TransactionReference khi Status thuộc Paid/RefundPending/Refunded có quan hệ trạng thái hợp lệ. Không lấy mọi dòng có PaidAt mà bỏ kiểm tra chứng cứ; không dùng tổng đơn ở Ordering làm nguồn thu độc lập.
3. `confirmedCollections(P) = Σ Payment.Amount hợp lệ với PaidAt ∈ P + Σ OnlinePayment.Amount hợp lệ với PaidAt ∈ P`.
4. `verifiedRefunds(P) = Σ sự kiện hoàn được xác minh trong P`. Hiện đủ nguồn vận hành cho hoàn toàn bộ mang về; hoàn tại bàn chưa đủ writer/chứng cứ. Không trả “không có hoàn tiền” chỉ vì chưa tích hợp nguồn.
5. `netCollections(P) = confirmedCollections(P) − verifiedRefunds(P)`. Kỳ chỉ có hoàn tiền có thể âm; không ép về 0. Không xóa tiền thu kỳ trước khi trạng thái chuyển Refunded ở kỳ sau.
6. Doanh thu món trước hoàn tại bàn: `Σ(Quantity × UnitPriceSnapshot − DiscountAllocated)`; kiểm tra khớp `SubTotal − DiscountAmount`. Mismatch phải được thể hiện rõ, không lấy bên nhỏ hơn hoặc sửa dữ liệu.
7. `netRevenue(P) = validDishRevenueAfterDiscount(P) − verifiedRefundMerchandisePart(P)`. Không dùng tổng refund gồm VAT/phí thay cho MerchandisePart. Luồng mang về chưa tách cấu phần thuế/phí; chưa thể tự coi tất cả Amount là doanh thu sau khi thấy thiếu trường.
8. `grossProfit(P) = netRevenue(P) − historicalIngredientCost(P)` chỉ khi các thành phần đủ nguồn và quy tắc. `grossMargin = grossProfit / netRevenue × 100`; netRevenue=0 ⇒ N/A. Contribution profit theo món phải giữ cùng cơ sở doanh thu/giá vốn đã chốt.
9. Chi phí đã tiêu hao không tự biến mất khi thu tiền hoàn. Không tự cộng tồn hoặc âm giá vốn do có refund. Nếu có hoàn nhập chi phí hợp lệ sau này, phải dựa vào sự kiện/chứng cứ riêng.
10. Lọc dish không được tự chia tiền toàn bill theo tỷ lệ tùy ý. Phải chốt ngữ nghĩa bộ lọc ở mục điểm còn thiếu trước API phụ thuộc.

## Điểm còn thiếu/đề xuất chưa được chốt

| ID | Điểm thiếu | Ảnh hưởng và bước xử lý |
| --- | --- | --- |
| GAP-01 | Chưa có giá nhập/giá xuất lịch sử hoặc writer CogsSnapshot; chưa rõ CogsSnapshot là giá đơn vị hay tổng dòng. | Chưa tính được lợi nhuận đầy đủ. Đề xuất giai đoạn báo cáo trả incomplete đúng BM04; nguồn giá vốn cần một phần triển khai riêng, trình thiết kế/schema cụ thể trước khi sửa Inventory/Catalog. Không chọn FIFO valuation thay bình quân hoặc ngược lại trong R0. |
| GAP-02 | Refund tại bàn chưa có writer/định nghĩa trạng thái xác minh; RefundLine.Amount chưa chỉ rõ bao gồm VAT/phí hay không. | Không dùng model này làm “hoàn tiền đã xác minh” ngay. Cần chốt nguồn xác minh và phân bổ tiền món/VAT/phí nếu triển khai; chưa đề xuất thêm cột khi chưa đối chiếu nghiệp vụ. |
| GAP-03 | Ưu đãi/món tặng có bảng nhưng chưa có luồng ghi; dữ liệu mang về chưa có cấu phần VAT/phí/discount lịch sử. | Có thể kiểm thử công thức bằng fixture ghi rõ là giả lập, nhưng không coi demo đó chứng minh luồng áp ưu đãi/thuế mang về thật đã có. Không viết code tạo ưu đãi trong WF-05 một cách âm thầm. |
| DEC-01 | Ngữ nghĩa lọc món và lọc ca kết hợp các tổng toàn nhà hàng. | Đề xuất lọc món chỉ áp dụng phần món/ranking/lợi nhuận món; tổng thu/hoàn vẫn theo phạm vi kỳ/phương thức/ca. Nếu muốn tổng thu chỉ của bill chứa món thì phải đặt tên rõ vì đó không phải doanh thu riêng món. Cần người dùng chốt trước R1 contract bộ lọc. |
| DEC-02 | Order đã Paid rồi bị từ chối/hủy: phân biệt dòng tiền, doanh thu hợp lệ, số đơn và số lượng món. | BM04 yêu cầu dòng tiền thu/hoàn theo hai thời điểm và loại đơn bị từ chối khỏi món. Không lọc mất tiền đã thu, cũng không loại doanh thu vì hủy rồi trừ hoàn thêm lần hai. Cần xác định cách trình bày doanh thu/đơn của tình huống này trước R2/R4; có test biên kỳ riêng. |
| DEC-03 | Chính sách thay TimeZone nhà hàng và giờ địa phương DST không tồn tại/trùng. | Đề xuất dùng timezone hiện được lưu khi request; thời gian địa phương mơ hồ/không tồn tại phải từ chối hoặc yêu cầu offset rõ. Không có lịch sử timezone cấu hình trong source; không tuyên bố giữ timezone lịch sử nếu không có nguồn. Chốt contract biên thời gian ở R1. |

Hai quyết định doanh thu loại VAT/phí và tuần bắt đầu thứ Hai đã được chốt; không hỏi lại. Không có quyết định thêm bảng/cột/index trong R0.

## Phạm vi tích hợp dự kiến cho R1 trở đi

- Billing: tách lớp/report repository/BLL/gRPC theo form hiện có; không nhét báo cáo WF-05 vào ShiftReportService.Close vì Close ghi dữ liệu đóng ca. Không thêm service/công nghệ mới chỉ để tổng hợp.
- Gateway: controller/DTO riêng cho report, chỉ sau khi chốt contract; không có endpoint mới trong R0.
- Restaurant: dùng lại xác thực và phân công; hợp đồng đọc tối thiểu cho timezone/ngữ cảnh report. Giữ nguyên RequireAdmin của API quản trị hiện có. Không cấp Platform Admin/Cashier quyền report theo guard đọc Billing cũ.
- Ordering: hợp đồng đọc theo tập OrderId/ItemRef thuộc đúng nhà hàng, trả snapshot/channel/cancellation hợp lệ. Không đọc OrderingDb từ Billing, không đổi quy trình nhận/hủy/phục vụ đơn.
- Inventory/Catalog: chưa sửa. Chỉ bổ sung hợp đồng đọc giá vốn khi đã có nguồn lịch sử đúng và đã trình phạm vi. Không gọi recipe/giá nguyên liệu hiện tại để bịa lại giá vốn quá khứ.
- Schema: ưu tiên tận dụng nguồn sẵn có, chưa phê duyệt/tạo bảng reporting/materialized snapshot/index mới. Mọi nhu cầu SQL phát sinh phải trình riêng, upgrade bảo toàn dữ liệu và đồng bộ Docker/ERD.
- Mọi adapter đọc nguồn bị lỗi/thiếu phải được phản ánh trong completeness; không tự trả số 0 như đã đối soát đủ. Quy tắc response cụ thể chốt ở đợt contract.

## Dấu vết source để Gemini/người review mở lại

| Nguồn | Tệp:dòng | Điểm đã kiểm tra |
| --- | --- | --- |
| E01 | `Services/restaurant_service/Restaurant.BLL/Implements/CurrentRestaurantScopeService.cs:11` | GetCurrentScope: xác thực/phân công; Platform Admin có nhánh riêng, chưa phải guard Manager cho report. |
| E01 | `Services/restaurant_service/Restaurant.DAL/Implements/RestaurantStaffRepository.cs:38` | lọc đúng UserIdRef và IsActive. |
| E02 | `Services/billing_service/Billing.API/Clients/RestaurantBillingAccess.cs:16` | chuyển bearer đến Restaurant, nhận phạm vi backend. |
| E03 | `Shared/Shared.Persistence/RestaurantIsolation/RestaurantDbContext.cs:36` | query filter tenant; options-only constructor dành cho tooling/test không có scope. |
| E04 | `Services/restaurant_service/Restaurant.BLL/Implements/RestaurantService.cs:50` | GetById gọi RequireAdmin; Save validate TimeZone. |
| E04 | `Contracts/Protos/restaurant.proto:67` | reply chưa có timezone. |
| E05 | `Services/billing_service/Billing.BLL/Implements/CashHandoverService.cs:23` | tạo pending thu hộ; chưa Paid. |
| E05 | `Services/billing_service/Billing.BLL/Implements/PaymentService.cs:200` | Cashier xác nhận, cùng transaction ghi PaidAt/handover/Sale; Map kiểm tra biên lai. |
| E06 | `Services/billing_service/Billing.BLL/Implements/PaymentWebhookService.cs:24` | xác minh webhook/dedupe, sau đó đối soát. |
| E06 | `Services/billing_service/Billing.BLL/Implements/PayOsPaymentService.Synchronization.cs:243` | đối soát đạt mới ghi PaidAt; không lấy ReceivedAt. |
| E06 | `Services/billing_service/Billing.BLL/Implements/PayOsPaymentRules.cs:51` | đủ chứng cứ Paid tại bàn; không tái dùng y nguyên cho refund tương lai vì guard hiện loại Refunds. |
| E07 | `Services/billing_service/Billing.BLL/Implements/OnlinePaymentService.cs:80` | Cashier xác minh; RefundedAt/RefundVerifiedBy; RefundPending giữ PaidAt. |
| E07 | `Services/billing_service/Billing.BLL/Implements/OnlinePaymentService.cs:197` | match Amount/transaction và PaidAt ??= clock. |
| E07 | `Services/billing_service/Billing.DAL/Models/BillingDbContext.OnlinePayment.cs:17` | OrderId PK, provider/transaction/refund reference unique, không CashShiftId. |
| E08 | `Services/billing_service/Billing.DAL/Models/Refund.cs:8` | Amount/Status/CompletedAt chưa phải chứng cứ writer xác minh. |
| E08 | `Services/billing_service/Billing.DAL/Models/RefundLine.cs:8` | liên kết BillLine và quantity/amount, chưa có breakdown thuế/phí. |
| E09 | `Services/billing_service/Billing.BLL/Implements/BillService.cs:88` | writer tạo bill: discount0, snapshot giá, CogsSnapshot null. |
| E09 | `Services/billing_service/Billing.BLL/Implements/BillService.cs:334` | UnitPrice gồm options; CalculateTotals và allocation VAT/phí; Map tính rounding adjustment. |
| E09 | `Services/billing_service/Billing.BLL/Implements/BillService.cs:302` | refresh giữ cost cũ, dòng mới null. |
| E10 | `Services/ordering_service/Ordering.BLL/Implements/BillingOrderService.cs:21` | quantity trừ hủy, chỉ đơn Confirmed/Ready/Served, giữ giá/options snapshot. |
| E10 | `Services/ordering_service/Ordering.API/Clients/TakeawayPaymentClient.cs:30` | mang về tính amount bằng quantity × giá gốc cộng options; không có tax breakdown. |
| E10 | `Services/ordering_service/Ordering.BLL/Implements/OrderService.Takeaway.cs:338` | đơn từ chối/hủy chờ hoàn; nguồn Ordering là trạng thái sao chép, không phải giao dịch thu mới. |
| E11 | `Services/inventory_service/Inventory.BLL/Services/GoodsReceiptService.cs:50` | ghi input/base quantities và conversion snapshot, không ghi đơn giá. |
| E11 | `Services/inventory_service/Inventory.BLL/Services/StockIssueService.cs:271` | ghi số lượng FIFO và OrderItemRef, không ghi historical monetary cost. |
| E12 | `Services/billing_service/Billing.BLL/Implements/ShiftReportService.cs:23` | đóng/đối soát ca có mutation; không phải WF-05 chỉ đọc. |
| E12 | `Services/billing_service/Billing.DAL/Models/BillAdjustment.cs:8` | schema ưu đãi có trước, không chứng minh chức năng áp dụng đã có. |

Cách rà writer thiếu: tìm `new Refund`, `Refunds.Add`, `Set<Refund>`, `new BillAdjustment`, `DiscountAmount =`, `DiscountAllocated =`, `CogsSnapshot =` trong `Services` và đối chiếu model/SQL/partial. Kết luận “chưa tìm thấy trong source tại baseline”, không khẳng định database thật không có bản ghi do hệ thống khác/SQL thủ công tạo.

## Bộ nghiệm thu BM04 dự kiến, chưa triển khai/chạy WF-05

| Đợt | ID BM04 | Ca tối thiểu cần kiểm chứng |
| --- | --- | --- |
| R1 | TC-WF05-01–03, 16–18 | Kỳ/nhóm/lọc hợp lệ; kỳ sai; kỳ rỗng; Manager đúng nhà hàng; không đăng nhập/sai vai trò; khác nhà hàng. Kiểm tra start/end, múi giờ và thứ Hai. |
| R2 | TC-WF05-04–06 | Thu đã xác nhận; Pending/Failed/thu hộ chưa bàn giao bị loại; hoàn đã xác minh và retry không đếm trùng. Thêm trường hợp RefundPending/Refunded vẫn giữ sự kiện thu gốc. |
| R3 | TC-WF05-07–09 | Kênh tại bàn/mang về; ca/không gắn ca; phương thức cộng khớp tổng thu và hoàn tách riêng. |
| R4 | TC-WF05-10–12 | Số lượng/ranking đúng; giá/options lịch sử và phần hủy; lợi nhuận món đủ/thiếu giá vốn, dòng quantity=0 không làm thiếu chi phí giả. |
| R5 | TC-WF05-13–15 | Giảm giá/món tặng không trùng; net revenue/gross profit đủ nguồn; N/A khi thiếu cost hoặc mẫu số 0. Không dùng giả lập để tuyên bố nguồn giá vốn thật hoàn chỉnh. |
| R6 | E2E-WF05-01 | Một luồng Gateway trên Docker/staging với dataset hai nhà hàng, expected totals tính độc lập. |

Tổng cam kết: 18 feature tests + 1 E2E; mỗi ID phải có assertion cụ thể và tự động hóa CI, không dùng số test của Billing cũ thay thế. DS-05a là ca chính; DS-05b–h lần lượt là kỳ sai, kỳ rỗng, giao dịch chưa xác minh, quyền, biên kỳ, ưu đãi/thiếu cost, giá lịch sử/hủy/từ chối. Hiện cả 19 test report và DS-05a–h đều **chưa chạy**.

Dataset R6 cần có: thu tiền mặt trước/sau Cashier xác nhận, PayOS tại bàn, mang về Paid→RefundPending→Refunded qua hai kỳ; request lặp; giao dịch ở start/end; đơn nhiều dòng/options; đơn từ chối/hủy một phần; bill giá đã đổi; ca và không gắn ca; ưu đãi/món tặng có snapshot; giá vốn đầy đủ và thiếu; dữ liệu nhà hàng B để kiểm tra rò rỉ. Các nguồn chưa có luồng ghi phải ghi rõ fixture thay vì giả vờ được tạo từ API nghiệp vụ.

## Kiểm chứng thực tế của R0

| Lệnh | Kết quả thực tế | Phạm vi |
| --- | --- | --- |
| `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter "FullyQualifiedName~BillServiceTests\|FullyQualifiedName~BillRefreshServiceTests\|FullyQualifiedName~CashPaymentServiceTests\|FullyQualifiedName~PayOsPaymentServiceTests\|FullyQualifiedName~OnlinePaymentTests"` | 77 passed, 0 failed, 0 skipped | Giá/option/hủy/refresh, thu tiền đúng một lần, đối soát PayOS và hoàn mang về. |
| `dotnet test Tests/Ordering.Tests/Ordering.Tests.csproj -c Release --filter FullyQualifiedName~BillingOrderReadTests` | 3 passed, 0 failed, 0 skipped | Quantity sau hủy, giá snapshot, loại pending; không ghi nguồn. |
| `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --filter FullyQualifiedName~CurrentScope` | 7 passed, 0 failed, 0 skipped | Phân công đang hoạt động, xác thực, target ID; test gRPC host, chưa phải report authorization. |
| `dotnet test Tests/Foundation.Tests/Foundation.Tests.csproj -c Release --filter FullyQualifiedName~RestaurantIsolationTests` | 5 passed, 0 failed, 0 skipped | Filter tenant và chặn ghi/quan hệ sai nhà hàng. |

Tổng **92 test nguồn đạt**, không skipped. Test tiêu biểu: `OnlinePaymentTests.PaidStatusRequiresVerifiedExactAmountAndRepeatedWebhookSettlesOnce`, `CancellationRaceBecomesPendingRefundAndOnlyCashierEvidenceCompletesIt`; `CashPaymentServiceTests.CashReceiptStoresNetSaleChangeAndPaidCheckoutAtomically`; `PayOsPaymentServiceTests.WebhookReplayIsDeduplicatedAndProviderMustConfirmSettlement`; `BillingOrderReadTests.ReadDeductsCancelledQuantitiesPreservesSnapshotsAndExcludesPendingOrders`. Đây không phải 18+1 test report cam kết.

Các test trên xác nhận hành vi nguồn hiện có, không xác nhận API báo cáo. Billing dùng SQLite in-memory/provider giả, Ordering dùng repository/session giả; Restaurant scope chạy gRPC test host với identity giả, Foundation dùng SQLite/isolation test. Không có PayOS thật hoặc SQL Server Docker trong lần test R0.

Docker: đã chạy `docker ps --filter name=sqlserver --format '{{.Names}} {{.Status}}'`; thất bại kết nối `npipe:////./pipe/dockerDesktopLinuxEngine`, `The system cannot find the file specified`. Không khởi động Docker Desktop hoặc thay service trong R0. Chưa đọc metadata/dữ liệu BillingDb thật; chưa gọi Gateway Docker; không xác minh kết quả thu/hoàn ngoài đời.

Build toàn solution: `dotnet build Backend.sln --configuration Release` đạt, 0 warning, 0 error (19,28 giây). Các lần build phụ thuộc khi chạy test trước đó có cảnh báo nullable sẵn có trong Shared.Persistence; không sửa ngoài phạm vi.

## SQL/ERD và bàn giao

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng/cột/PK/FK/UNIQUE/CHECK/index, không thay cardinality, không sửa repo SQL riêng và không thay dữ liệu. Các bảng Refund/RefundLine/BillAdjustment/OnlinePayments là bảng đã có, không coi là bảng mới của WF-05.

File duy nhất của R0: `docs/restaurant-performance-reports.md`. Chưa sửa code/cấu hình/frontend/Compose/CI. Không commit/push. Tên commit tài liệu đề xuất: `Khảo sát nguồn dữ liệu và quy tắc báo cáo WF-05`; mô tả: `Đối chiếu BM04 với nguồn thanh toán, hoàn tiền, snapshot và giá vốn; ghi quyết định doanh thu/múi giờ, giới hạn dữ liệu và kế hoạch nghiệm thu.`

Review độc lập: kiểm lại các nguồn E01–E12, truy tất cả partial khi tìm writer; đặc biệt OnlinePayment không còn Status Paid sau refund, giá vốn không thể lấy từ số lượng FIFO, tiền mặt không cộng Payment+Sale, BillLine.UnitPriceSnapshot đã gồm option, GetById Restaurant không mở cho Manager. Phân biệt phát hiện đã đọc mã, test đã chạy và đề xuất chưa duyệt. R1 có thể bắt đầu phần quyền và biên kỳ đã rõ sau khi tách nhánh phù hợp; chốt DEC-01 trước contract bộ lọc liên quan. GAP giá vốn/hoàn tiền không chặn triển khai phần quyền/kỳ độc lập, nhưng chặn tuyên bố toàn bộ báo cáo tài chính/lợi nhuận đã hoàn chỉnh. Không triển khai công thức phụ thuộc DEC/GAP chưa giải quyết.


## R1 — Quyền xem và kỳ báo cáo (2026-11-25)

Nhánh `feature/restaurant-performance-reports`, tạo từ HEAD `1552753`; giữ tài liệu R0 đã có chưa commit. Các vị trí source trong bảng R0 là vị trí tại baseline đó; sau khi thêm method, dùng bảng dấu vết R1 dưới đây để mở code hiện tại. Chưa commit/push.

### Phần đã triển khai

`GET /api/restaurants/{restaurantId}/reports/period` chỉ xác minh quyền/ngữ cảnh và chuẩn hóa kỳ. Đây chưa phải API tổng hợp doanh thu/lợi nhuận; không trả tổng tiền 0 hoặc danh sách trống giả vờ đã đọc đủ dữ liệu. GroupBy được kiểm tra/chuẩn hóa cho giai đoạn tổng hợp tiếp theo, chưa sinh bucket số liệu.

Luồng thật: Gateway `RestaurantReportController.GetPeriod` → Billing `RestaurantReportGrpcService.GetPeriod` → `RestaurantReportService.GetPeriod` → client `RestaurantReportContextClient.GetAsync` → Restaurant RPC `GetReportContext` → `CurrentRestaurantScopeService.GetReportContext` → xác thực Identity/phân công đang hoạt động và đọc Restaurant.TimeZone. Interceptor tenant hiện có vẫn kiểm tra restaurant trong route/header trước nghiệp vụ. Không đọc database service khác.

- Chỉ vai trò `manager` có phân công đang hoạt động tại đúng nhà hàng được đọc; Platform Admin, admin nhà hàng, Cashier, Waiter, Kitchen và Customer bị từ chối. Giữ nguyên API quản trị nhà hàng yêu cầu admin; không đổi các quyền Billing khác.
- Hai contract gRPC chỉ được bổ sung RPC/message mới; các RPC/route cũ giữ nguyên. Không tạo repository rỗng: R1 chỉ đọc ngữ cảnh từ repository Restaurant hiện có, chưa truy vấn bảng tài chính.
- `from` và `to` bắt buộc là ISO 8601 có `Z` hoặc offset, có giờ/phút/giây. Không nhận ngày thuần hoặc giờ không offset, tránh suy ra múi giờ máy chủ. Kiểm tra from < to theo instant UTC. Không làm tròn hoặc tự kéo biên về đầu/cuối ngày.
- Múi giờ lấy từ nhà hàng; client không thể thay bằng query `timeZone`. Trả cả FromUtc/ToUtc và FromLocal/ToLocal để đối chiếu. Múi giờ cấu hình không hợp lệ trả 503 có kiểm soát, không mặc định UTC/+07.
- `groupBy`: hour/day/week/month, mặc định day; chuẩn hóa chữ thường. Tuần bắt đầu Monday theo quyết định đã chốt. Metadata startInclusive/endExclusive mô tả hợp đồng `[from, to)` cho truy vấn R2; R1 chưa chạy truy vấn lọc doanh số để chứng minh kết quả ở biên.
- Giờ DST lặp được xác định bằng offset tường minh; hai giờ địa phương trùng nhau nhưng offset khác vẫn là hai instant khác nhau. Khi nhà hàng đổi timezone, R1 trả timezone đang lưu, không tuyên bố có lịch sử timezone.
- Gửi bearer và deadline theo cấu hình hiện có; lỗi gRPC được Gateway ánh xạ qua BillingControllerBase; no-store cho phản hồi. Không thêm secret/env hoặc thay cấu hình quyền đang có.

### Request và phản hồi

Ví dụ PowerShell (token của Manager đúng nhà hàng):

```powershell
$gateway = 'http://localhost:15000' # thay bằng Gateway đang chạy của bạn
$restaurantId = '<restaurant-id>'
$accessToken = '<manager-access-token>'
$from = [uri]::EscapeDataString('2026-11-25T00:00:00+07:00')
$to = [uri]::EscapeDataString('2026-11-26T00:00:00+07:00')
Invoke-RestMethod -Uri "$gateway/api/restaurants/$restaurantId/reports/period?from=$from&to=$to&groupBy=week" -Headers @{ Authorization = "Bearer $accessToken" }
```

Dấu `+` trong offset phải URL-encode (`%2B`) khi tự viết query URL; helper ở trên đã làm việc này. Không gửi token vào query.

Kết quả thực tế trên Compose riêng với tài khoản demo Manager, HTTP 200:

```json
{
  "statusCode": 200,
  "message": "Thành công.",
  "isSuccess": true,
  "result": {
    "restaurantId": "20000000-0000-0000-0000-000000000001",
    "timeZone": "Asia/Ho_Chi_Minh",
    "fromUtc": "2026-11-24T17:00:00+00:00",
    "toUtc": "2026-11-25T17:00:00+00:00",
    "fromLocal": "2026-11-25T00:00:00+07:00",
    "toLocal": "2026-11-26T00:00:00+07:00",
    "groupBy": "week",
    "weekStartsOn": "Monday",
    "startInclusive": true,
    "endExclusive": true
  }
}
```

### Kiểm chứng R1

- `dotnet build Backend.sln -c Release`: đạt, 0 warning/0 error.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx`: **1.365 passed, 0 failed, 104 skipped**. Các SQL tests khác cần biến kết nối riêng nên skipped, không tính là đạt; Docker Engine chạy không tự cấp connection cho các SQL test opt-in. Riêng lý thuyết Unicode SQL có điều kiện được bỏ qua khi không cấp biến của bài test đó.
- Test mới: 21 ca `RestaurantReportServiceTests` (ngữ cảnh nhà hàng, biên/offset, UTC/fractional seconds, bốn grouping, DST, timezone hỏng, lỗi nguồn/quyền); 12 ca `GatewayFlowTests.ReportPeriod*` (HTTP Gateway → gRPC Billing → gRPC Restaurant và interceptor thật, SQLite Restaurant/identity giả). Các test này không gọi mock CallInvoker thay cho Billing/Restaurant; nhưng identity ở test host vẫn là giả, không gọi Identity thật.
- Các test scope cũ và regression Billing/Restaurant vẫn được chạy trong toàn solution. Test thêm xác nhận Manager đọc context được nhưng vẫn không gọi được GET chi tiết quản trị nhà hàng.
- Docker Desktop ban đầu thiếu pipe Engine; đã khởi động Desktop nền và xác nhận Engine `29.8.2`. Build từ source hiện tại các image db-init/Identity/Restaurant/Billing/Gateway bằng Compose, dùng project/volume/network riêng và credential demo sinh cho bài thử; không dùng env/volume/database của nhóm. SQL Server 2022 là thật, db-init và seed hiện có chạy thành công, SQL healthy.
- **22 HTTP request kiểm tra báo cáo đạt** trên Gateway Docker, cộng 7 login demo = 29 kiểm tra HTTP: Manager đúng nhà hàng 200; hour/day/week/month 200; grouping sai, thiếu offset/biên, start=end và start>end 400; không đăng nhập/token sai 401; 6 vai trò khác 403; nhà hàng khác, header giả và phân công bị thu hồi 403; timezone lưu sai 503. Các request lỗi không trả result dữ liệu. Billing row counts trước/sau các request báo cáo không đổi. Không coi so sánh số dòng là phép so sánh mọi giá trị của mọi bảng.
- Fixture Docker có thêm nhà hàng B và tạm thu hồi/phục hồi phân công, đổi/phục hồi timezone của nhà hàng A để thử lỗi, chỉ trong SQL test tách biệt. Đây không phải tác dụng ghi dữ liệu của API report. Không gọi PayOS/Gemini/Cloudinary thật trong đợt R1.
- Sau kiểm thử đã dừng/xóa container, network và volume của project thử riêng; không xóa volume hoặc cập nhật container của nhóm. Credential Docker demo trong file env tạm đã được xóa.
- Sau rà style cuối, chạy lại 21 test service và 12 test luồng report: tất cả đạt, không skipped.
- Chưa chạy CI remote, chưa thử FE, chưa kiểm thử thu/hoàn/lợi nhuận qua API report vì R2–R5 chưa triển khai. Chưa nghiệm thu đủ TC-WF05-01–03: phần kỳ rỗng trả tổng 0, actual hourly/daily/weekly/monthly aggregates và bộ lọc dữ liệu còn thuộc giai đoạn truy vấn. Không dùng test mới để tuyên bố đã xong 18+1 ca cam kết.

Docker không cần thêm biến cấu hình/file override chính thức trong R1; các Dockerfile/Compose hiện có đã build và truyền địa chỉ gRPC cho luồng mới. Khi cập nhật môi trường của nhóm, cần rebuild đồng bộ Gateway/Billing/Restaurant từ cùng source (với file env đang dùng), ví dụ:

```powershell
docker compose --env-file '<file-env-dang-dung>' up -d --build --no-deps restaurant billing gateway
```

Lệnh này giả định SQL/Identity và các dependency cũ đã chạy; không dùng down -v. Request sẽ không hoạt động nếu chỉ cập nhật Gateway mà Restaurant còn image cũ chưa có RPC GetReportContext.

### Phần còn giữ lại cho đợt tiếp theo

Không đưa filter dish/payment-method/shift vào API period vì R1 chưa có dữ liệu tổng hợp; chưa âm thầm chọn ngữ nghĩa DEC-01. R2 phải dùng lại biên/kỳ chuẩn hóa và quyền đã kiểm chứng, triển khai truy vấn thực tế; các GAP giá vốn, hoàn tại bàn và ưu đãi từ R0 vẫn giữ nguyên. Không thêm bảng/cột để lấp chỗ thiếu ở đợt này.

**Không có thay đổi schema** ở backend hoặc repo SQL riêng; không đổi PK/FK/index/cardinality. File DTO mới không phải entity/bảng mới. Những thay đổi dữ liệu fixture chỉ thuộc database thử nghiệm tách biệt.

### File của đợt và hướng dẫn review

- `Contracts/Protos/billing.proto`
- `Contracts/Protos/restaurant.proto`
- `Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs`
- `Services/api_gateway/ApiGateway/DTOs/Billing/ReportPeriodDTO.cs`
- `Services/api_gateway/ApiGateway/Extensions/BillingServiceExtensions.cs`
- `Services/billing_service/Billing.API/Clients/RestaurantReportContextClient.cs`
- `Services/billing_service/Billing.API/GrpcServices/RestaurantReportGrpcService.cs`
- `Services/billing_service/Billing.API/Extensions/BillingServiceExtensions.cs`
- `Services/billing_service/Billing.API/Program.cs`
- `Services/billing_service/Billing.BLL/Implements/RestaurantReportService.cs`
- `Services/billing_service/Billing.BLL/Interfaces/IRestaurantReportContextClient.cs`
- `Services/billing_service/Billing.BLL/Interfaces/IRestaurantReportService.cs`
- `Services/billing_service/Billing.Common/DTOs/ReportPeriodDTO.cs`
- `Services/restaurant_service/Restaurant.API/GrpcServices/RestaurantAccessGrpcService.cs`
- `Services/restaurant_service/Restaurant.BLL/Implements/CurrentRestaurantScopeService.cs`
- `Services/restaurant_service/Restaurant.BLL/Interfaces/ICurrentRestaurantScopeService.cs`
- `Services/restaurant_service/Restaurant.Common/DTOs/RestaurantReportContextDto.cs`
- `Tests/Billing.Tests/RestaurantReportServiceTests.cs`
- `Tests/Restaurant.Tests/RestaurantReportFlowTests.cs`
- `docs/database-change-log.md`
- `docs/restaurant-performance-reports.md`

Gemini kiểm tra lại:
1. Đi từ route mới đến GetReportContext và phân công thật; thử anonymous/token hết hạn/các vai trò khác, Manager nhà hàng B, header lệch và thu hồi phân công. Không coi RequireBearerToken ở Gateway là toàn bộ xác thực.
2. Xác nhận API quản trị cũ vẫn RequireAdmin; DTO mới không bổ sung quyền cho admin/cashier hoặc nhận RoleId/UserId/timezone từ client.
3. Chạy test mới và các test scope cũ; thử Z/+07, fractional seconds, DST, start=end, đảo biên, thiếu offset/grouping sai. Xác minh code chỉ kiểm tra context/kỳ, chưa có số liệu hoặc filter financial thực thi.
4. Đọc đăng ký DI/MapGrpcService/client; kiểm tra không có đọc chéo DB, không ghi report, không có schema mới và không thêm dependency công nghệ.
5. Khi báo lỗi, nêu request, source/assertion và phạm vi fake/thật; không dùng việc API period 200 để kết luận báo cáo doanh thu/lợi nhuận đã hoàn thành.

Tên commit đề xuất: **Thêm quyền xem và kiểm tra kỳ báo cáo nhà hàng**.
Mô tả: **Chỉ cho Manager đúng nhà hàng đọc ngữ cảnh báo cáo; chuẩn hóa múi giờ và khoảng thời gian, kiểm tra cách nhóm, bổ sung test Gateway và kiểm chứng Docker. Không thay đổi schema.**


## Hoàn thiện sau review R1 (2026-11-25)

Baseline review là commit **1a64b53** trên `feature/restaurant-performance-reports`, working tree sạch khi bắt đầu. Giữ nguyên commit và lịch sử. Phần này chỉ xử lý các nhận xét nhỏ của R1, chưa chuyển R2.

| Nhận xét | Kết luận tự kiểm chứng | Thay đổi |
| --- | --- | --- |
| F-01 — UTC z thường | Đã tái hiện bằng 2 test mới: code baseline trả BillingException 400 cho hậu tố z (có/không phần lẻ giây), trong khi Z được chấp nhận. Đây là cải thiện tương thích, không phải lỗi tính sai UTC. | `RestaurantReportService.Parse` chuẩn hóa riêng hậu tố z thành Z trước khi kiểm tra offset và TryParseExact. Không sửa timestamp thiếu offset, không sửa khoảng trắng/giá trị sai, không đổi biên/múi giờ. |
| F-02 — dấu + trong query | Đã chạy HTTP: + viết thẳng bị query decoder đổi thành khoảng trắng và bị từ chối 400; %2B hợp lệ trả 200. Đây là yêu cầu encode URL, không phải lý do nới validation. Tài liệu R1 trước đã có lưu ý, nhưng Swagger chưa hiện hướng dẫn. | Thêm EndpointDescription vào đúng action GetPeriod để JSON Swagger có mô tả cho cả from/to. Dùng metadata ASP.NET có sẵn, không thêm package/filter toàn cục hoặc thay định dạng request. |
| F-03 — quyền nhà hàng | Không có lỗi cần sửa trong nhận xét này. | Giữ guard Manager/phân công, chạy hồi quy quyền và request khác nhà hàng. Không tuyên bố việc một số test đạt chứng minh mọi tấn công đều bị chặn. |
| F-04 — khoảng nửa mở | Không có lỗi cần sửa trong nhận xét này. | Giữ startInclusive/endExclusive. Đây vẫn là metadata và validation kỳ; chưa phải bằng chứng truy vấn tổng tiền hoặc grouping đúng biên vì R2 chưa triển khai. |

Đối chiếu [RFC 3339 §5.6](https://www.rfc-editor.org/info/rfc3339/#section-5.6): t/z thường được cho phép trong cú pháp chung, đồng thời một profile có thể giới hạn chữ hoa. Bản sửa chỉ mở tương thích **hậu tố UTC z** như review yêu cầu; không tuyên bố đây là bộ kiểm tra đầy đủ mọi biến thể RFC 3339. Response vẫn chuẩn hóa như trước.

### Bằng chứng test trước/sau

- Trước sửa: `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter FullyQualifiedName~LowercaseUtcDesignatorMatchesUppercaseUtc` → **2 failed, 0 passed**. Cùng test mong đợi z/Z cho cùng kết quả, baseline ném 400 tại Parse.
- Sau sửa: `--filter FullyQualifiedName~RestaurantReportServiceTests` → **23 passed**, 0 failed/skipped (21 cũ + 2 regression z). Test mới `LowercaseUtcDesignatorMatchesUppercaseUtc` so sánh toàn DTO và giữ nguyên request đầu vào.
- `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --filter FullyQualifiedName~ReportPeriod` → **17 passed**, 0 failed/skipped (12 cũ + 4 ca HTTP encoding + 1 ca Swagger).
- `ReportPeriodTimestampEncodingRemainsStrict`: Z/z và %2B07:00 trả 200 với UTC đúng; +07:00 chưa encode trả 400, result null. Chạy qua HTTP Gateway → Billing → Restaurant trên test host, SQLite và identity giả như R1.
- `ReportPeriodSwaggerDocumentsOffsetEncodingForBothBounds`: bật Swagger:Enabled riêng trong test host, đọc JSON Swagger thật và kiểm tra operation description có from, to, %2B và UTC Z. Không chỉ kiểm tra attribute tồn tại trong source; không bật Swagger toàn môi trường bằng thay đổi code này.
- Build `dotnet build Backend.sln -c Release`: đạt, 0 warning/0 error ở lần build cuối. Khi test build lại các dependency từ đầu có cảnh báo nullable cũ; không sửa các cảnh báo ngoài phạm vi.
- Test hồi quy toàn solution: 1372 passed, 0 failed, 104 skipped. Các SQL test opt-in không được tính là đạt khi chưa cấp connection riêng; Docker chạy không tự cấp các biến test đó.

Docker kiểm chứng lại từ source mới bằng Compose project riêng (db-init, Identity, Restaurant, Billing, Gateway, SQL Server 2022, tài khoản demo và volume tách biệt):

| Request/kiểm tra | Kết quả thực tế |
| --- | --- |
| from/to dùng Z | 200, UTC đúng |
| Cả from và to dùng z thường | 200, cùng instant với Z |
| Offset +07:00 đã encode thành %2B07:00 | 200, UTC đúng |
| Dấu + viết thẳng trong query | 400, không tự sửa thành timestamp hợp lệ |
| GET /swagger/v1/swagger.json | 200, có hướng dẫn encode thực tế trong description của endpoint |
| 22 ca báo cáo Docker cũ + 7 login demo | Đạt; giữ các ca sai quyền/khác nhà hàng/revoked/header lệch/biên sai; Billing row counts trước/sau không đổi |

Những kiểm tra mới ở bảng Docker cộng thêm một login demo riêng; không dùng số lần login làm số ca nghiệp vụ. Không gọi PayOS/Gemini thật, không thử FE. Chưa chạy CI remote. Môi trường của nhóm không được cập nhật image/schema/data trong lượt này. Đã dọn container/network/volume của project thử và xóa file credential demo tạm sau kiểm chứng.

### File và bàn giao review

Chỉ sửa sáu file:
- `Services/billing_service/Billing.BLL/Implements/RestaurantReportService.cs`: nhận hậu tố z thường mà vẫn kiểm tra chặt timestamp.
- `Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs`: hướng dẫn encode URL trong Swagger của đúng endpoint.
- `Tests/Billing.Tests/RestaurantReportServiceTests.cs`: hai ca tái hiện/regression z/Z.
- `Tests/Restaurant.Tests/RestaurantReportFlowTests.cs`: bốn ca HTTP encoding và một ca kiểm tra JSON Swagger.
- `docs/restaurant-performance-reports.md`: kết luận từng nhận xét, bằng chứng trước/sau, giới hạn kiểm chứng.
- `docs/database-change-log.md`: ghi rõ không đổi cấu trúc database.

**Không có thay đổi schema**, contract gRPC/route/quyền hoặc cấu hình chung; không sửa repo SQL riêng. Không commit/push hoặc sửa commit 1a64b53. Tên commit đề xuất: **Hoàn thiện định dạng thời gian và hướng dẫn báo cáo**. Mô tả: **Hỗ trợ hậu tố UTC z thường, bổ sung hướng dẫn encode offset trên Swagger và test hồi quy; giữ nguyên quyền truy cập và cấu trúc database.**
