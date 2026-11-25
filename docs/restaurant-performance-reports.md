# Báo cáo hiệu quả hoạt động nhà hàng

Trạng thái cập nhật 2026-11-25: R1 và bản hoàn thiện đã được commit đến 8e8d872. R2/R3/R4 được gom trong commit này; R4 đã bổ sung bảng món, nguồn snapshot Ordering và kiểm chứng Gateway/Docker. R5 sẽ được bàn giao trong commit riêng, chưa nằm trong phần R2–R4 này. Giá vốn vận hành còn thiếu writer, xem mục R4 cuối tài liệu. Các trạng thái bàn giao cũ bên dưới được giữ theo thời điểm kiểm chứng.

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
| GAP-01 | Chưa có giá nhập/giá xuất lịch sử hoặc writer CogsSnapshot. R4 đã chốt CogsSnapshot là giá vốn tổng dòng; chưa có nguồn vận hành để điền. | Chưa tính được lợi nhuận đầy đủ. Đề xuất giai đoạn báo cáo trả incomplete đúng BM04; nguồn giá vốn cần một phần triển khai riêng, trình thiết kế/schema cụ thể trước khi sửa Inventory/Catalog. Không chọn FIFO valuation thay bình quân hoặc ngược lại trong R0. |
| GAP-02 | Refund tại bàn chưa có writer/định nghĩa trạng thái xác minh; RefundLine.Amount chưa chỉ rõ bao gồm VAT/phí hay không. | Không dùng model này làm “hoàn tiền đã xác minh” ngay. Cần chốt nguồn xác minh và phân bổ tiền món/VAT/phí nếu triển khai; chưa đề xuất thêm cột khi chưa đối chiếu nghiệp vụ. |
| GAP-03 | Ưu đãi/món tặng có bảng nhưng chưa có luồng ghi; dữ liệu mang về chưa có cấu phần VAT/phí/discount lịch sử. | Có thể kiểm thử công thức bằng fixture ghi rõ là giả lập, nhưng không coi demo đó chứng minh luồng áp ưu đãi/thuế mang về thật đã có. Không viết code tạo ưu đãi trong WF-05 một cách âm thầm. |
| DEC-01 | Ngữ nghĩa lọc món và lọc ca kết hợp các tổng toàn nhà hàng. | Đề xuất lọc món chỉ áp dụng phần món/ranking/lợi nhuận món; tổng thu/hoàn vẫn theo phạm vi kỳ/phương thức/ca. Nếu muốn tổng thu chỉ của bill chứa món thì phải đặt tên rõ vì đó không phải doanh thu riêng món. Đã chốt ở R4: lọc ItemId chỉ tác động API bảng món riêng, không đổi tổng thu/hoàn API sales; ca/kênh giữ phạm vi R3. |
| DEC-02 | Order đã Paid rồi bị từ chối/hủy: phân biệt dòng tiền, doanh thu hợp lệ, số đơn và số lượng món. | BM04 yêu cầu dòng tiền thu/hoàn theo hai thời điểm và loại đơn bị từ chối khỏi món. Không lọc mất tiền đã thu, cũng không loại doanh thu vì hủy rồi trừ hoàn thêm lần hai. Đã chốt cho R2: giữ theo kỳ thanh toán, chỉ giảm tại kỳ hoàn được xác minh; chưa có nguồn thì incomplete/null. R4 phải loại lượng hủy/từ chối khỏi báo cáo món, không trừ tiền lần hai. |
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


## R2 — Doanh thu và tiền thu theo kỳ (2026-11-25)

Baseline: **8e8d872**, nhánh `feature/restaurant-performance-reports`; working tree sạch khi bắt đầu. Không sửa lịch sử, không commit/push. Chỉ triển khai phần đủ nguồn trong Billing; chưa làm lọc phương thức/ca/món (R3), thống kê món (R4), giá vốn/lợi nhuận (R5). Không thay đổi các luồng thu, hoàn, hủy đơn hoặc database của service khác.

### Quyết định áp dụng

- Đơn đã thu rồi hủy/từ chối vẫn giữ tiền thu và doanh thu ở kỳ thanh toán. Chỉ ghi tiền hoàn ở kỳ có xác minh hoàn; không loại tiền thu theo trạng thái đơn hiện tại rồi trừ hoàn lần nữa.
- Đơn mang về có trạng thái Paid/RefundPending/Refunded vẫn dùng PaidAt cho tiền thu. Refunded chỉ tạo số hoàn đã biết khi đủ RefundedAt, người xác minh, tham chiếu và bằng chứng HTTPS theo luồng hoàn hiện có.
- Refund tại bàn chưa có writer/nguồn xác minh. Không dùng tên trạng thái hay CompletedAt đơn thuần để công nhận hoàn thành. Chưa bổ sung nguồn nghiệp vụ hoặc schema để lấp thiếu.
- Doanh thu món sau giảm giá = SubTotal − DiscountAmount, tách VAT/phí. RoundingAdjustment = TotalAmount − (doanh thu món + VAT + phí), phản ánh phần làm tròn VND đã lưu, không sửa hóa đơn lịch sử.

### API và hợp đồng kết quả

```http
GET /api/restaurants/{restaurantId}/reports/sales?from=2026-11-25T00:00:00%2B07:00&to=2026-11-26T00:00:00%2B07:00&groupBy=day
Authorization: Bearer <token-manager>
```

Luồng: Gateway GetSales → RestaurantReportGrpc.GetSales → RestaurantSalesReportService.GetSales → R1 GetPeriod (kiểm tra Manager/phân công và kỳ qua Restaurant) → RestaurantReportRepository.GetSourceAsync (chỉ BillingDb) → kiểm tra chứng cứ/snapshot → tổng và nhóm → protobuf → DTO Gateway. Không gọi trực tiếp DB Restaurant/Ordering, không gọi PayOS hoặc tạo giao dịch để lấy báo cáo.

- `period`: metadata kỳ chuẩn hóa R1, [from,to), múi giờ nhà hàng.
- `currency`: VND. `groups` thưa, chỉ có nhóm có sự kiện đã tính; không tự tạo các nhóm 0.
- `totals` và mỗi nhóm gồm collections, refunds, netCollections, dishRevenue, netDishRevenue, discounts, vat, serviceFees, paidOrders, roundingAdjustment.
- Tiền có `knownAmount`, `amount`, `isComplete`; số đơn có `knownCount`, `count`, `isComplete`. Trường đầy đủ null khi chưa chứng minh được. **Không dùng knownAmount như tổng đầy đủ hoặc thay null thành 0 trên FE**. Với netDishRevenue, knownAmount chỉ là phần tiền món sau giảm giá đã biết; chưa có cấu phần hoàn nên không coi đây là lợi nhuận hay doanh thu ròng hoàn chỉnh.
- Collections lấy một lần từ Payment/OnlinePayment hợp lệ. Không cộng CashReceived (gồm tiền thừa), CashMovement Sale lần hai, OpeningCash hoặc CashIn ngoài bill vào doanh thu.
- Cash phải Paid, bill khớp, CashHandover Confirmed đủ người/thời gian/số tiền, đúng một Sale khớp. PayOS tại bàn dùng metadata QueryVerified và bill Paid khớp; không đòi Refunds rỗng để khỏi làm mất lịch sử thu.
- Đối chiếu tổng dòng, giảm giá, VAT/phí phân bổ và tổng bill làm tròn. Snapshot không nhất quán làm phần doanh thu incomplete; không làm mất khoản thu đã đủ chứng cứ. Không tính lại thuế từ chính sách hiện tại.
- Số đơn dùng OrderId trong snapshot dòng có Quantity > 0, distinct; không dùng số dòng hoặc BillId. JSON thiếu/sai báo count null. Đơn hoàn trong kỳ sau không trở thành đơn bán mới.
- OnlinePayment chưa có tách tiền món/VAT/phí/discount lịch sử: chỉ cộng collections và số đơn có chứng cứ; phần phân rã có amount null. Thiếu ngày trả warning, không gán ngày tạo/hiện tại.
- Do thiếu nguồn hoàn tại bàn, **refunds.amount, netCollections.amount, netDishRevenue.amount luôn null ở bản này**, kể cả kỳ rỗng. Kỳ rỗng có các phần đã biết bằng 0 và groups=[]; chưa đáp ứng ý nghĩa “mọi tổng đầy đủ đều 0” của toàn bộ nghiệm thu khi nguồn còn thiếu.
- Warning có code ổn định: DineInRefundsUnavailable, MissingPaymentTime, UnverifiedRefundRecords, UnverifiedCollection, InconsistentBillRevenue, MissingOrderSnapshot, TakeawayRevenueBreakdownUnavailable, RefundRevenueBreakdownUnavailable.
- hour dùng `yyyy-MM-ddTHH:00:00±hh:mm` để phân biệt giờ lặp DST; day/week/month dùng ngày yyyy-MM-dd, week thứ Hai, month ngày đầu tháng. Phần giao nhau với khoảng request được tính, không tự kéo dài ra đủ tuần/tháng.

Repository dùng transaction chỉ đọc Serializable cho nhiều truy vấn EF và AsNoTrackingWithIdentityResolution cho include vòng Bill.Payments. Dùng khóa/range lock SQL có thể làm writer phải chờ lúc đọc kỳ lớn; chưa có benchmark tải lớn, chưa thêm index hoặc giới hạn kỳ tùy ý. Hai GET đồng thời đã thử không thay thế kiểm thử tải/thu tiền đồng thời. Dữ liệu settlement bất thường/trùng reference trong các nguồn của kỳ bị loại khỏi known và báo incomplete; không tự sửa dữ liệu lịch sử.

### File thay đổi

| File | Mục đích |
| --- | --- |
| Contracts/Protos/billing.proto | Thêm GetSales và message số tiền nullable, tổng/nhóm/warning; giữ GetPeriod. |
| Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs | Endpoint sales, Swagger và ánh xạ null đúng qua protobuf. |
| Services/api_gateway/ApiGateway/DTOs/Billing/RestaurantSalesReportDTO.cs | Response của Gateway. |
| Services/billing_service/Billing.Common/DTOs/RestaurantSalesReportDTO.cs | DTO nghiệp vụ tiền/số đơn/nhóm/warning. |
| Services/billing_service/Billing.BLL/Interfaces/IRestaurantSalesReportService.cs | Hợp đồng service. |
| Services/billing_service/Billing.BLL/Implements/RestaurantSalesReportService.cs | Quyền/kỳ, tổng, nhóm, snapshot, incomplete. |
| Services/billing_service/Billing.BLL/Implements/ReportPaymentValidation.cs | Kiểm tra chứng cứ thu/hoàn dùng trong báo cáo, không thay writer thanh toán. |
| Services/billing_service/Billing.DAL/Interfaces/IRestaurantReportRepository.cs | Hợp đồng đọc nguồn. |
| Services/billing_service/Billing.DAL/DTOs/RestaurantReportSource.cs | Kết quả đọc nguồn, không phải entity. |
| Services/billing_service/Billing.DAL/Implements/RestaurantReportRepository.cs | Truy vấn theo nhà hàng/kỳ, ngày thiếu, transaction nhất quán. |
| Services/billing_service/Billing.DAL/Extensions/PersistenceExtensions.cs | Đăng ký repository. |
| Services/billing_service/Billing.API/Extensions/BillingServiceExtensions.cs | Đăng ký service. |
| Services/billing_service/Billing.API/GrpcServices/RestaurantReportGrpcService.cs | RPC và map số tiền/count nullable. |
| Tests/Billing.Tests/RestaurantSalesReportServiceTests.cs | Test công thức, bằng chứng, thiếu nguồn, nhóm/kỳ; repository giả. |
| Tests/Billing.Tests/RestaurantReportRepositoryTests.cs | Repository thật với SQLite, tenant/biên/tracking/hoàn. |
| Tests/Restaurant.Tests/RestaurantSalesReportFlowTests.cs | HTTP Gateway → gRPC Billing/Restaurant, EF SQLite thật, Identity giả. |
| docs/restaurant-performance-reports.md | Hợp đồng, quyết định, bằng chứng và giới hạn R2. |
| docs/database-change-log.md | Ghi rõ không đổi schema. |

### Kiểm chứng R2

- `dotnet build Backend.sln -c Release`: **0 warning, 0 error**.
- `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter "FullyQualifiedName~RestaurantSalesReportServiceTests|FullyQualifiedName~RestaurantReportRepositoryTests"`: **25 passed, 0 failed, 0 skipped** (22 ca service + 3 ca repository SQLite). Có test thiếu bằng chứng hoàn, settlement sai, hai Paid attempts, snapshot sai, tiền mặt chờ bàn giao, tổng dòng không nhân tiền thu, hai kỳ hoàn/thu, DST và không ghi dữ liệu.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx`: **1.405 passed, 0 failed, 104 skipped** trên 9 test projects. Các bài SQL opt-in chưa được cấp biến kết nối vẫn skipped, không tính là đạt; SQL Server thật của R2 được kiểm riêng bằng HTTP Docker bên dưới.
- Test `SalesReport*` trong Restaurant.Tests: **8 passed, 0 failed, 0 skipped**. Dữ liệu 108 tại bàn +50 online, hoàn50 ngày sau; ánh xạ null qua gRPC/JSON, quyền và nhà hàng khác. Đây là host kiểm thử với Identity giả, không phải Docker.
- Docker Compose build db-init/Identity/Restaurant/Billing/Gateway từ source, SQL Server 2022 thật, project/network/volume riêng. Không sửa cấu hình hoặc dữ liệu backend-* của nhóm. Fixture SQL tổng hợp: 2 bill tại bàn (PayOS108 và Cash108, handover200/trả92, Sale108), 1 online50 hoàn ngày sau, nhiều dòng chung OrderId; ca đã Closed vẫn nằm trong lịch sử.
- **12 HTTP kiểm tra report +2 login demo, cộng 2 GET đồng thời đều đạt** qua Gateway Docker. Bốn grouping, empty period, thu/hoàn khác ngày, anonymous401/cashier403/nhà hàng khác403, kỳ/grouping sai400. Không coi fixture metadata QueryVerified là đã chạy thu PayOS thật hoặc hoàn ngân hàng thật.
- Kết quả thực tế kỳ 25/11: collections.amount=266; dishRevenue.knownAmount=200, amount=null do online thiếu phân rã; vat.knownAmount=16, amount=null; paidOrders.count=3; refunds.knownAmount=0, amount=null. Kỳ 26/11: collections.amount=0, refunds.knownAmount=50, netCollections.knownAmount=-50, amount=null; paidOrders.count=0. Không trừ hoàn ở cả hai kỳ.
- Request Docker đầu tiên đo **2.188 ms (~2,19 giây)**; đây là một lần đầu có chi phí khởi động, không phải trung bình/p95 hoặc cam kết hiệu năng. Không benchmark tải lớn.
- Sau rà code cuối: rebuild solution 0 warning/0 error, chạy lại 25 core +8 flow đều đạt; rebuild Billing/Gateway Docker, đối chiếu container image ID với image mới và gọi GET sales lại 200 với tổng266; Swagger mới có response400/401/403/503. Sau đó đã xóa container/network/volume và file credential của project thử riêng; các container backend-* của nhóm vẫn chạy.
- So sánh row counts toàn Billing trước/sau request không thay đổi; unit tests cũng kiểm tra trạng thái nguồn giữ nguyên. Không coi row counts là so sánh mọi giá trị database.
- Chưa thử FE, CI remote, PayOS thật; chưa có nguồn hoàn tại bàn/tách VAT-phí online nên không tuyên bố báo cáo tài chính đầy đủ hoặc toàn bộ nghiệm thu BM04 hoàn tất.

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng/cột/default/PK/FK/UNIQUE/CHECK/index, không đổi quan hệ/cardinality hoặc dây ERD. Không sửa SQL tạo mới/upgrade hoặc repository SQL riêng. DTO nguồn không phải bảng. SQL fixture chỉ thêm dữ liệu tổng hợp trong database thử nghiệm tách biệt.

### Hướng dẫn Gemini review độc lập

1. Theo route sales qua GetPeriod/Restaurant assignment trước khi repository đọc; thử Manager khác nhà hàng, header giả, quyền bị thu hồi, role khác và kỳ sai. Giữ quyền API quản trị cũ.
2. Dùng hai bill nhiều dòng và một online refunded: kiểm tiền thu không nhân dòng, cash không cộng200 hoặc Sale lần hai; thu kỳ gốc, hoàn kỳ sau. Thử PaidAt/RefundedAt đúng biên, missing date, hoàn không có bằng chứng, bill sai snapshot và hai Paid attempts.
3. Đọc assertion về null/knownAmount/count, online thiếu breakdown, hoàn tại bàn chưa xác minh; không đề xuất chuyển null thành0 để làm đẹp UI. Thử DST và thứ Hai.
4. Chạy 25 test core và 8 test flow, ghi đúng SQLite/Identity giả; khi thử Docker dùng volume/project riêng và fixture đã nêu, không gọi provider hoặc sửa DB nhóm. Kiểm tra Serializable có phù hợp tải thực tế trước mở rộng báo cáo lớn.
5. Chưa đưa nhận xét R3–R5 thành lỗi đã xác minh của R2; ghi rõ lỗi tái hiện, nghi vấn và đề xuất. Không commit/push.

Tên commit đề xuất: **Bổ sung báo cáo doanh thu và tiền thu theo kỳ**.
Mô tả: **Tổng hợp khoản thu và hoàn có bằng chứng theo thời điểm, tách doanh thu món/VAT/phí và thể hiện dữ liệu thiếu bằng null; bổ sung API Gateway, kiểm thử quyền, dữ liệu và Docker. Không thay đổi schema.**


## R3 — Phương thức và ca thu ngân; phần kênh đang chờ chốt (2026-11-25)

**Trạng thái: chưa hoàn thành toàn bộ R3.** HEAD vẫn 8e8d872, nhánh feature/restaurant-performance-reports. Khi bắt đầu, toàn bộ R2 vẫn chưa commit; giữ nguyên và phát triển tiếp trên working tree, không tự commit/push/stage hoặc sửa lịch sử. Không coi diff so với HEAD là riêng R3 vì còn chứa R2.

### Phần đã triển khai

Giữ endpoint GET `/api/restaurants/{restaurantId}/reports/sales`, thêm query:

| Tham số | Giá trị | Ý nghĩa |
| --- | --- | --- |
| methodCode | all (mặc định), Cash, BankTransfer | Lọc theo phương thức đã lưu; BankTransfer là luồng PayOS hiện có. Không nhìn IsEnabled hiện tại để loại mất lịch sử. Cash dùng mã CashPayment.MethodCode đã cấu hình. |
| shift | all (mặc định), unassigned, UUID khác Guid.Empty | all giữ cả giao dịch gắn ca và không gắn ca; unassigned chỉ OnlinePayments; UUID chọn đúng Payment.CashShiftId lịch sử, không gán ca đang mở. UUID không có giao dịch đúng nhà hàng trả dữ liệu rỗng, không tiết lộ ca của nhà hàng khác. |
| channel | all | Các giá trị DineIn/Takeaway hiện bị chặn 409 vì chưa đủ nguồn phân loại đúng toàn bộ bill. Giá trị không thuộc tập dự kiến trả400. Không dùng bộ lọc này cho nghiệm thu kênh. |

Ví dụ (token Manager đúng nhà hàng, biên kỳ như R1):

```http
GET /api/restaurants/{restaurantId}/reports/sales?from=2026-11-25T00:00:00%2B07:00&to=2026-11-26T00:00:00%2B07:00&methodCode=Cash&shift={cashShiftId}
Authorization: Bearer <token-manager>
```

- Response thêm `filters` đã chuẩn hóa, `methods`, `shifts`, `channels`. Mỗi phân tổ dùng key/totals như nhóm thời gian, chỉ có nhóm phát sinh; cộng knownAmount của các nhóm theo cùng chiều phải khớp knownAmount tổng. Không cộng các chiều với nhau vì đây là các cách nhìn của cùng dữ liệu.
- `methods.key` là mã phương thức; `shifts.key` là UUID ca hoặc unassigned, không giả tên ca/người phụ trách. Ca đã đóng vẫn được báo cáo khi có giao dịch trong kỳ.
- `channels.key` của Payment theo phiên bàn tạm là **Unclassified** kèm **OrderChannelUnavailable**. OnlinePayment từ luồng mang về có key Takeaway. Đây là thông tin chưa đầy đủ, không phải kết luận mọi bill theo phiên bàn là DineIn.
- Bộ lọc áp dụng trong repository trước khi tải/tính; cả số bản ghi thiếu PaidAt/RefundedAt và Refund tại bàn chưa xác minh cũng theo bộ lọc. Giao dịch ngoài bộ lọc không làm nhiễu cờ thiếu ngày của bộ lọc đang xem.
- Hoàn mang về ở kỳ RefundedAt được phân tổ BankTransfer/unassigned, không kéo về ca hiện tại hoặc kỳ PaidAt. Không đổi nghiệp vụ xác minh hoàn.
- Giữ chính sách thận trọng R2: refunds.amount/netCollections.amount/netDishRevenue.amount vẫn null khi chưa có nguồn hoàn tại bàn; chưa tự nâng completeness của chỉ tiêu này theo bộ lọc hẹp. Cộng khớp ở đây là số **knownAmount**, không biến dữ liệu thiếu thành0.
- Đếm OrderId phân biệt nguồn/kênh (Unclassified/Takeaway) thay vì chỉ Guid, tránh gộp hai nguồn trùng Guid. Không nhân theo dòng.
- gRPC chỉ thêm field, giữ tên RPC, request type và số field cũ. Không đổi GetPeriod hoặc quyền API cũ. Không thêm package/service hoặc cấu hình bắt buộc mới.

### Điểm mới cần người dùng chốt

Đã đọc lại `Ordering.BLL/Implements/OrderService.cs` (Create): request.Channel có thể là DineIn hoặc Takeaway, trong khi Order.TableSessionRef vẫn gán sessionId. `Ordering.DAL/Implements/OrderRepository.cs:GetForBillingAsync` lọc theo TableSessionRef, không giới hạn DineIn. `Ordering.BLL/Implements/BillingOrderService.cs:GetForRestaurant` đưa các đơn này vào snapshot nhưng không trả Channel. `BillService` chỉ snapshot BillItemSnapshot, cũng chưa có Channel. Vì vậy **không thể dùng loại bảng Payment để suy ra mọi đơn là DineIn**.

Đề xuất đã gửi hỏi, **chưa được chốt tại thời điểm ghi mục này**: bổ sung RPC chỉ đọc lấy Order.Channel theo OrderId, có kiểm tra phạm vi nhà hàng; bill có cả hai kênh xếp Mixed, không tự chia khoản thu/VAT/phí; thiếu nguồn xếp chưa xác định. Chưa thêm RPC, chưa sửa Ordering, chưa thêm cột Channel hoặc tính tỷ lệ chia tiền. Phần kênh TC-WF05-07 và quyết định bill hỗn hợp còn chờ. Không mở rộng sang bộ lọc món/giá vốn R4/R5.

### File của R3

- Mới: `Services/billing_service/Billing.Common/DTOs/SalesReportFilterDTO.cs`, `Services/api_gateway/ApiGateway/DTOs/Billing/SalesReportFilterDTO.cs`: query filter.
- Cập nhật hai `RestaurantSalesReportDTO.cs` ở Common/Gateway: filter và các chiều phân tổ.
- `Contracts/Protos/billing.proto`: field request/filter và response phân tổ, bổ sung tương thích.
- `Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs`: query, mapping, Swagger và409 cho nhánh kênh chưa xác minh.
- `Services/billing_service/Billing.API/GrpcServices/RestaurantReportGrpcService.cs`: map filter và các nhóm qua RPC.
- `Services/billing_service/Billing.BLL/Interfaces/IRestaurantSalesReportService.cs`, `Implements/RestaurantSalesReportService.cs`: chuẩn hóa filter, partition thu/hoàn, chặn suy đoán kênh.
- `Services/billing_service/Billing.DAL/Interfaces/IRestaurantReportRepository.cs`, `Implements/RestaurantReportRepository.cs`: lọc nguồn và các count theo cùng phạm vi.
- `Tests/Billing.Tests/RestaurantSalesReportServiceTests.cs`: tổng phân tổ, số đơn, filter sai.
- `Tests/Billing.Tests/RestaurantReportRepositoryTests.cs`: thiếu ngày ở ngoài filter không nhiễu nguồn được chọn.
- `Tests/Restaurant.Tests/RestaurantSalesReportFlowTests.cs`: SQL EF SQLite thật qua Gateway/gRPC; filter phương thức/ca, hoàn khác kỳ, guard kênh409. Identity giả.
- Hai tài liệu hiện có: `docs/restaurant-performance-reports.md`, `docs/database-change-log.md`. Không tạo thêm MD.

### Kiểm chứng hiện tại

- `dotnet build Backend.sln -c Release`: 0 warning/0 error.
- Core R1–R3: `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RestaurantReportServiceTests|FullyQualifiedName~RestaurantReportRepositoryTests|FullyQualifiedName~RestaurantSalesReportServiceTests"` → **54 passed, 0 failed, 0 skipped**.
- Flow R1–R3: `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ReportPeriod|FullyQualifiedName~SalesReport"` → **32 passed, 0 failed, 0 skipped**, gồm 5 ca filter phương thức/ca và2 ca guard kênh mới.
- Docker project thử riêng, SQL Server/Identity/Restaurant/Billing/Gateway thật; dữ liệu thu/hoàn fixture tổng hợp như R2, không gọi PayOS/Gemini thật. Rebuild Billing/Gateway sau khi thêm guard kênh.
- **26 request kiểm tra report +2 login demo đạt**: filter Cash/BankTransfer, ca lịch sử đã đóng/unassigned/UUID không có giao dịch, kết hợp phương thức–ca, hoàn ở kỳ sau; các tổng phân tổ cộng khớp. Filter sai400, kênh chưa đủ nguồn409, anonymous401/cashier403/nhà hàng khác403. Swagger có Channel/MethodCode/Shift. Các test kênh trên bản nháp trước khi phát hiện vấn đề **không được dùng làm bằng chứng đã phân loại đúng kênh thực tế**.
- Số liệu Docker: tổng thu266; Cash108 + BankTransfer158 =266; ca có216 + unassigned50 =266. Hoàn50 kỳ sau thuộc BankTransfer/unassigned; ca lịch sử không nhận khoản hoàn này. Group Unclassified216 + Takeaway50 phản ánh **nguồn chưa phân loại đầy đủ**, không phải báo cáo kênh đã nghiệm thu.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx` trên bản sau guard: **1.418 passed, 0 failed, 104 skipped**, đủ 9 project. Các SQL tests opt-in skipped không được tính là đạt. Không dùng kết quả bản nháp trước guard thay thế kết quả này.
- Đã đối chiếu container image ID Billing/Gateway với image vừa build, gọi GET tổng266 lại200 và xác nhận Swagger mới. Sau đó xóa container/network/volume và file credential của project thử riêng; các backend-* của nhóm giữ nguyên. `git diff --check` đạt; HEAD vẫn8e8d872, không commit/push.
- Chưa thử FE/CI remote hoặc PayOS thật; chưa kiểm thử phân loại DineIn/Takeaway/Mixed theo nguồn Ordering; chưa benchmark writer thanh toán đồng thời với báo cáo kỳ lớn. Serializable và rủi ro range lock từ review R2 vẫn giữ, chưa tự bật Snapshot hoặc đổi database.

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Không thêm/sửa/xóa bảng/cột/kiểu/NULL/DEFAULT/PK/FK/UNIQUE/CHECK/index/quan hệ/cardinality hoặc dây ERD. Không sửa SQL tạo mới/upgrade hay repo SQL riêng. Dữ liệu fixture chỉ ở project Docker thử tách biệt.

### Gemini kiểm tra lại

1. Kiểm tra filter phương thức/ca đi xuyên HTTP–gRPC–repository và count thiếu ngày. Thử ca cũ, unassigned, Guid rỗng/sai, mã ca khác nhà hàng và quyền Manager.
2. Đối chiếu sums theo methods/shifts với tổng known thu/hoàn, CashReceived200/Change92 không cộng thành200 hoặc nhân Sale108 lần hai. Hoàn kỳ sau nằm unassigned/BankTransfer.
3. Đọc OrderService.Create trước khi đề xuất coi Payment=DineIn. Kiểm tra guard409 và nhãn Unclassified; **không kết luận R3 đã hoàn thành kênh**. Chờ quyết định RPC/Mixed trước triển khai phụ thuộc.
4. Không sửa nguồn Ordering, database hoặc nghiệp vụ hoàn để làm test qua. Phân biệt SQLite/Identity giả, Docker dữ liệu tổng hợp và provider thật.

Tên commit dự kiến cho phần độc lập: **Bổ sung báo cáo theo phương thức và ca thu ngân**. Mô tả: **Lọc và phân tổ khoản thu/hoàn theo phương thức và ca lịch sử, giữ nhóm không gắn ca, kiểm tra quyền và dữ liệu thiếu; chặn suy đoán kênh khi chưa có nguồn. Không thay đổi schema.** Chưa đề nghị commit toàn R3 khi phần kênh còn chờ quyết định.


## Hoàn thiện kênh R3 (2026-11-25)

Người dùng yêu cầu tiếp tục và giữ “giống như luồng đã code rồi đó”. Bản này giữ nguyên việc lập bill, số tiền, thu tiền, xác minh hoàn và trạng thái đơn; chỉ thêm nguồn đọc cho báo cáo. Các mục R3 tạm chặn409 ở trên là **lịch sử bản trước**, không còn mô tả hành vi hiện tại. HEAD vẫn 8e8d872, toàn bộ R2/R3 chưa commit.

### Hành vi hiện tại

- `channel=all/DineIn/Takeaway/Mixed/Unclassified` (không phân biệt hoa thường). Bỏ guard409 tạm; giá trị ngoài tập trả400. Bộ lọc methodCode/shift và quyền Manager giữ như phần trước.
- Billing lấy tập OrderId từ các dòng có Quantity>0 trong snapshot bill, gọi RPC mới `BillingOrderGrpc.GetReportChannels` của Ordering. Ordering kiểm tra scope qua interceptor và gọi lại Restaurant.GetReportContext để bảo đảm **Manager được phân công đúng nhà hàng**, rồi repository chỉ đọc OrderId/Channel với `RestaurantRef == restaurantId` và danh sách ID được yêu cầu. Không đọc chéo database.
- Không lọc Order.Status khi lấy Channel: thanh toán lịch sử không biến mất vì đơn sau đó hủy/từ chối. Channel hiện chỉ được gán khi tạo đơn trong source đã đối chiếu; không lấy giá/menu hiện tại hoặc viết Channel ngược vào bill.
- Tất cả OrderId có cùng kênh: bill thuộc kênh đó. Có cả DineIn và Takeaway: **Mixed**, giữ nguyên Payment.Amount, không chia tỷ lệ doanh thu/VAT/phí/giảm giá hoặc làm tròn lại. Một đơn thiếu/khác nhà hàng/giá trị kênh không hợp lệ: toàn bill **Unclassified**.
- OnlinePayments từ luồng mang về giữ Takeaway, BankTransfer, unassigned; sự kiện hoàn vẫn ở RefundedAt, không chuyển kỳ.
- Lọc kênh là lọc đúng nhóm: DineIn/Takeaway không chứa Mixed/Unclassified. Có `ExclusiveChannelFilter` giải thích cần xem all để đối chiếu toàn bộ tiền; all có đủ các nhóm. Không cộng nhóm Mixed lần nữa vào hai kênh thuần.
- Khi thiếu nguồn kênh: all vẫn giữ khoản thu đã xác minh, có `OrderChannelUnavailable`. Với bộ lọc kênh khác Unclassified, kết quả known chỉ gồm bill phân loại được, collections.amount=null để không tuyên bố đã tìm đủ bill thuộc kênh. Unclassified có thể xem riêng toàn khoản chưa phân loại.
- Đếm đơn dùng `(nguồn tại phiên/online, OrderId)`, không dùng nhãn kênh có thể chuyển từ Unclassified sang Mixed khi nguồn hoạt động lại. Không đổi số đơn chỉ vì nguồn kênh phục hồi.
- Client gửi tối đa100 ID khác nhau/RPC, dùng **một deadline chung cho toàn bộ các batch**, không retry. Forward bearer hiện tại, không tin role/restaurant do client tự nhận. Unavailable/DeadlineExceeded/Unimplemented/Internal trả phần kênh đọc được trước đó, còn lại chưa xác định; caller cancellation được tôn trọng. **401/403 không bị nuốt thành fallback thành công**. Reply sai nhà hàng, thừa ID, ID trùng (kể cả định dạng UUID N/D), kênh lạ không được dùng để phân loại.
- Các giới hạn số liệu R2 vẫn giữ: hoàn tại bàn chưa có writer xác minh, phần phân rã VAT/phí online còn thiếu nên các chỉ tiêu đầy đủ liên quan vẫn null. Không tuyên bố toàn bộ báo cáo tài chính/lợi nhuận hay 18+1 ca cam kết đã hoàn chỉnh.

Ví dụ:

```http
GET /api/restaurants/{restaurantId}/reports/sales?from=2026-11-25T00:00:00%2B07:00&to=2026-11-26T00:00:00%2B07:00&channel=Mixed&methodCode=BankTransfer
Authorization: Bearer <token-manager>
```

### File bổ sung/cập nhật trong lượt hoàn thiện kênh

| File | Mục đích |
| --- | --- |
| Contracts/Protos/ordering.proto | RPC đọc channel theo tập OrderId, không sửa RPC cũ. |
| Services/ordering_service/Ordering.Common/DTOs/OrderReportChannelDTO.cs | DTO đọc, không phải entity/bảng mới. |
| Services/ordering_service/Ordering.DAL/Interfaces/IOrderRepository.cs | Hợp đồng đọc theo nhà hàng và IDs. |
| Services/ordering_service/Ordering.DAL/Implements/OrderRepository.cs | Truy vấn projection AsNoTracking, giữ tenant filter. |
| Services/ordering_service/Ordering.BLL/Interfaces/IBillingOrderService.cs | Hợp đồng đọc báo cáo cùng lớp Order hiện có. |
| Services/ordering_service/Ordering.BLL/Implements/BillingOrderService.cs | Kiểm tra batch/permission rồi đọc nguồn; không đổi GetBySession. |
| Services/ordering_service/Ordering.BLL/Interfaces/IOrderReportAccess.cs | Quyền báo cáo chỉ Manager. |
| Services/ordering_service/Ordering.API/Clients/OrderReportAccess.cs | Dùng Restaurant.GetReportContext xác minh Manager. |
| Services/ordering_service/Ordering.API/GrpcServices/BillingOrderGrpcService.cs | RPC và map DTO. |
| Services/ordering_service/Ordering.API/Extensions/OrderingServiceExtensions.cs | DI quyền report, giữ DI/quyền cũ. |
| Services/billing_service/Billing.BLL/Interfaces/IRestaurantReportOrderClient.cs | Hợp đồng nguồn kênh. |
| Services/billing_service/Billing.API/Clients/RestaurantReportOrderClient.cs | Batching, deadline chung, validation response và fallback có kiểm soát. |
| Services/billing_service/Billing.API/Extensions/BillingServiceExtensions.cs | DI client nguồn. |
| Services/billing_service/Billing.BLL/Implements/RestaurantSalesReportService.cs | Phân loại nguyên bill, lọc sau phân loại, incomplete và cảnh báo. |
| Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs | Swagger kênh mới, bỏ mô tả409 tạm. |
| Tests/Billing.Tests/RestaurantReportOrderClientTests.cs | 10 test transport giả: timeout/unavailable,401/403, batch, dữ liệu sai/trùng/ngoài yêu cầu. |
| Tests/Billing.Tests/TestReportOrderClient.cs | Nguồn kênh giả cho unit tests, không mặc định DineIn. |
| Tests/Billing.Tests/RestaurantSalesReportServiceTests.cs | DineIn/Takeaway/Mixed/thiếu nguồn, giữ nguyên khoản thu và lọc thuần. |
| Tests/Ordering.Tests/AllowOrderReportAccess.cs | Permission giả cho test nguồn cũ, không dùng production. |
| Tests/Ordering.Tests/BillingOrderReadTests.cs | Channel Takeaway trong phiên bàn, khác nhà hàng, quyền trước khi đọc, batch0/101. |
| Tests/Ordering.Tests/CancellationRequestTests.cs | Truyền dependency quyền report vào test cũ, giữ assertion hủy món. |
| Tests/Restaurant.Tests/RestaurantReportFlowTests.cs | Nguồn kênh giả cho host regression cũ; không gọi network ngoài test. |
| Tests/Restaurant.Tests/RestaurantSalesReportFlowTests.cs | Thay test409 tạm bằng test bộ lọc giữ unknown/incomplete qua Gateway. |
| docs/restaurant-performance-reports.md | Hành vi mới, file, test và giới hạn. |
| docs/database-change-log.md | Xác nhận không đổi SQL/ERD. |

### Bằng chứng kiểm chứng

- `dotnet build Backend.sln -c Release`: **0 warning, 0 error**.
- `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter "FullyQualifiedName~RestaurantSalesReportServiceTests|FullyQualifiedName~RestaurantReportOrderClientTests|FullyQualifiedName~RestaurantReportRepositoryTests"`: **45 passed, 0 failed, 0 skipped**. Nguồn kênh/transport giả, repository SQLite theo test tương ứng.
- `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --filter "FullyQualifiedName~ReportPeriod|FullyQualifiedName~SalesReport"`: **32 passed, 0 failed, 0 skipped**. Gateway/gRPC Billing/Restaurant thật trong TestServer, Identity và kênh giả; chưa coi đây là test RPC Ordering thật.
- `dotnet test Tests/Ordering.Tests/Ordering.Tests.csproj -c Release --filter "FullyQualifiedName~BillingOrderReadTests|FullyQualifiedName~CancellationRequestTests"`: **29 passed, 0 failed, 0 skipped**, có hồi quy luồng hủy và snapshot cũ; các assertion nguồn dùng fake hoặc SQLite theo fixture.
- Docker Compose project/network/volume riêng: build db-init/Identity/Restaurant/Billing/Ordering/Gateway từ source, SQL Server2022 thật. Source fixture có2 bill (Cash108, PayOS108), online50; bill PayOS có2 OrderId thuộc hai kênh, nguồn Ordering ghi đúng RestaurantRef. Đây là dữ liệu fixture tổng hợp ở database thử, **không phải giao dịch PayOS thật** hoặc thao tác gọi món từ UI.
- **14 HTTP kiểm tra report +2 login demo đạt** cho bản có RPC thật: all266 và4 đơn; DineIn108, Mixed108, Takeaway50, Unclassified0; cộng các chiều khớp266; hoàn50 kỳ sau thuộc Takeaway. Sai vai trò403/anonymous401, kênh không hỗ trợ400.
- Khi chuyển RestaurantRef của một Order nguồn sang nhà hàng thử khác: báo cáo all giữ266, bill108 vào Unclassified; Mixed filtered known0/amountnull. Sau đó phục hồi fixture. Không dùng dữ liệu nhà hàng khác để phân loại.
- Dừng riêng Ordering của project thử: Gateway vẫn200, thu266; Unclassified216, Takeaway50, warning thiếu nguồn. DineIn filtered known0/amountnull. Sau đó khởi động lại. Không dừng backend-* của nhóm.
- Rebuild Billing cuối, đối chiếu image ID Billing/Ordering/Gateway với image mới và gọi lại Mixed: HTTP200, amount108, đúng key Mixed. Billing row counts trước/sau các request không đổi; không coi đây là so sánh mọi giá trị/mọi database.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx`: **1.436 passed, 0 failed, 104 skipped** trên9 projects. Các SQL tests opt-in chưa cấp biến kết nối vẫn skipped, không tính là đạt; SQL thật trong đợt này kiểm qua Compose/HTTP riêng.
- Đã dọn container/network/volume và file credential của project thử riêng; backend-* của nhóm vẫn chạy, không thay volume/dữ liệu của nhóm. `git diff --check` đạt. Không commit/push/stage hoặc đổi HEAD8e8d872.
- Chưa chạy FE, CI remote hoặc thanh toán/hoàn PayOS thật. Chưa benchmark SQL writer đồng thời với kỳ báo cáo lớn; Serializable giữ nguyên, không tự bật Snapshot hay thêm index. Chưa làm R4/R5/R6.

### SQL/ERD, đồng bộ và review

**Không có thay đổi schema.** Không đổi bảng/cột/kiểu/NULL/default/PK/FK/UNIQUE/CHECK/index/cardinality hoặc dây ERD; không sửa SQL tạo mới/upgrade hoặc repo SQL riêng. RPC/DTO mới không phải bảng. Các INSERT/UPDATE fixture chỉ trên database thử riêng, không áp vào database nhóm.

Dockerfile/Compose hiện có đã build và nối RPC mới được, không cần thêm secret/config. Khi cập nhật môi trường dev đã có dependency chạy, rebuild đồng bộ bằng file env hiện dùng:

```powershell
docker compose --env-file '<file-env-dang-dung>' up -d --build --no-deps ordering billing gateway
```

Không dùng down -v. Nếu Ordering còn image cũ, RPC Unimplemented được báo thiếu nguồn/Unclassified, không đoán kênh.

Gemini review: truy RPC GetReportChannels qua interceptor và RequireManager trước repository; thử mã đơn ngoài nhà hàng, thiếu đơn, batch0/101, reply sai/ID trùng UUID khác format và401/403. Đối chiếu all với tổng DineIn/Takeaway/Mixed/Unclassified, bill Mixed không chia tiền; thử Ordering dừng và caller cancel. Giữ phân biệt test fake, Docker fixture và provider thật. Không sửa luồng lập/thu/hoàn/hủy hoặc database để làm báo cáo qua; không tự commit/push.

Tên commit đề xuất: **Hoàn thiện báo cáo theo kênh bán, phương thức và ca thu ngân**.
Mô tả: **Đọc kênh đơn từ Ordering theo quyền Manager và nhà hàng; phân loại nguyên hóa đơn DineIn/Takeaway/Mixed, giữ khoản chưa rõ nguồn ở Unclassified; bổ sung bộ lọc, cảnh báo và kiểm thử Gateway/Docker. Không thay đổi schema hoặc luồng thanh toán.**


## Hoàn thiện sau review kênh R3 (2026-11-25)

Giữ HEAD8e8d872 trên feature/restaurant-performance-reports; working tree đã có R2/R3 chưa commit khi bắt đầu. Lượt này chỉ sửa F-01 đã tái hiện và bổ sung kiểm chứng liên quan; không sửa frontend hoặc tự thêm danh mục ca/danh sách nhà hàng.

| Review | Tự kiểm chứng và xử lý |
| --- | --- |
| F-01: isComplete của kênh trong all | **Đã tái hiện trước sửa** bằng test mới: all có Payment108 chưa phân loại + online50, group Takeaway.amount trả50 trong khi filtered Takeaway.amount=null. Sửa ChannelBreakdown để khi còn Unclassified, các nhóm kênh khác có amount=null/IsComplete=false; nhóm Unclassified, tổng toàn kỳ, nhóm thời gian, methods/shifts vẫn dùng cờ chứng cứ thu gốc. Không đổi số tiền, contract hoặc cách phân loại. |
| F-02: metadata/danh mục ca | Đây là đề xuất tích hợp, chưa phải lỗi tính sai khoản thu. Chưa mở API hoặc bổ sung metadata mới ngoài phạm vi; cần chọn danh mục lịch sử hay chỉ ca có giao dịch trước đợt riêng. |
| F-03: danh sách nhà hàng và header | Giữ kiểm tra header khớp route, không nới quyền hoặc sửa Identity/FE. Thêm test Manager được gán A và B: route B/header A bị403, route B/header B được200. Việc expose danh sách nhà hàng được phân công là phạm vi API mới, chưa triển khai. |
| F-04: Serializable | Giữ nguyên; chưa tái hiện blocking/deadlock hoặc benchmark tải ghi. Không tự bật Snapshot hoặc đổi SQL/index để xử lý nghi vấn. |
| F-05: thiếu pipeline Ordering tự động | Đã bổ sung TestServer Gateway→Billing→Ordering→Restaurant thật, Billing/Ordering/Restaurant dùng SQLite. Đi qua BillingOrderGrpcService, RestaurantScopeInterceptor, OrderReportAccess và OrderRepository thật; Identity giả. Không còn dựa riêng vào Transport/IRestaurantReportOrderClient giả cho test này. |

### Code và test thay đổi trong lượt review

- `Services/billing_service/Billing.BLL/Implements/RestaurantSalesReportService.cs`: GetSales dùng ChannelBreakdown riêng; nhóm kênh đã biết chỉ đầy đủ khi không còn bill Unclassified và chứng cứ thu đầy đủ. Totals/Breakdown phương thức–ca giữ nguyên.
- `Tests/Billing.Tests/RestaurantSalesReportServiceTests.cs`: thêm `UnclassifiedBillsMakeKnownChannelBreakdownsIncompleteWithoutInvalidatingOverallCollections` (TC-NEW-01) và `SecondBatchTimeoutKeepsAnEntire101OrderBillUnclassified` (TC-NEW-03); fixture cho phép gắn client transport thật của Billing với transport gRPC giả.
- `Tests/Billing.Tests/RestaurantReportOrderClientTests.cs`: mở helper test Client/Transport ở mức internal để test ghép client+service; không đổi implementation production client.
- `Tests/Restaurant.Tests/RestaurantReportOrderingFlowTests.cs`: file mới, `SalesReportTraversesRealOrderingGrpcScopeAuthorizationAndRepository` (TC-NEW-02 và TC-NEW-04): host Ordering thật, dữ liệu nguồn4 order; nhóm DineIn108, Mixed108, Unclassified108, onlineTakeaway50. Tổng374 đủ; ba nhóm kênh đã biết có amountnull vì còn108 chưa phân loại; Unclassified.amount108. RPC trực tiếp với Cashier trả PermissionDenied; Manager A/B và header kiểm tra như trên. Repository không trả order thuộc nhà hàng khác, số row nguồn giữ4 sau request.
- `docs/restaurant-performance-reports.md`: ghi kết quả review/bằng chứng và giới hạn.
- `docs/database-change-log.md`: xác nhận không thay schema.

### Bằng chứng trước và sau

1. `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter FullyQualifiedName~UnclassifiedBillsMakeKnownChannelBreakdownsIncomplete`:
   - Trước sửa: **1 failed**, Assert.Null thất bại, Actual50.
   - Sau sửa: **1 passed**, không skipped; totals.collections.amount158, Takeaway.known50/amountnull, cùng SalesReportTotals với filteredTakeaway; Unclassified.amount108, methods.amount158, shifts vẫn complete.
2. Test ghép batch101: batch1 trả100 kênh DineIn, batch2 DeadlineExceeded; cả bill108 giữ Unclassified, không chia100/101 tiền. DineIn filter known0/amountnull và warning thiếu nguồn. Hai report gọi đúng4 RPC (2 batch/report), không retry.
3. `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --filter FullyQualifiedName~SalesReportTraversesRealOrderingGrpc`: **1 passed, 0 failed, 0 skipped**. Dùng client generated từ Billing vì Ordering.API generate server-only; không đổi cấu hình generate/contract để phục vụ test. Chưa mutation-test bằng cách xóa DI hoặc đổi proto; các assertion phân loại sẽ không chấp nhận silent fallback toàn bộ sang Unclassified.
4. Hai unit test mới chạy chung: **2 passed, 0 failed, 0 skipped**. Build toàn solution: **0 warning, 0 error**.
5. Docker Engine ban đầu chưa chạy: compose build báo thiếu pipe `dockerDesktopLinuxEngine`. Đã mở Docker Desktop nền và kiểm tra Server29.8.2, sau đó build từ source và chạy project thử riêng có Gateway/Billing/Ordering/Restaurant/Identity/SQL Server2022 thật.
6. Hồi quy Docker R2: **12 report checks +2 login demo, cộng2 GET đồng thời đạt**. F-01 qua HTTP thật: all tổng266; Unclassified216 đủ; Takeawayknown50/amountnull, toàn bộ totals của phân tổ Takeaway bằng filteredTakeaway; methods/shifts vẫn có amount đầy đủ. Đã đối chiếu image IDs Billing/Ordering/Gateway.
7. Chạy lại Docker kênh: **14 report checks +2 login demo đạt**, DineIn108+Mixed108+Takeaway50=266, hoàn kỳ sau, nguồn khác nhà hàng và dừng/khởi động riêng Ordering. Dữ liệu thanh toán/order là fixture SQL tổng hợp, không gọi provider thật.

8. Toàn solution: `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx` → **1.439 passed, 0 failed, 104 skipped** trên9 projects. Các SQL opt-in skipped không tính là đạt; Docker thật được thử riêng như trên. Đã dọn project thử, network/volume và file credential, giữ nguyên backend-* của nhóm. `git diff --check` đạt; không commit/push/stage, HEAD8e8d872.

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Không đổi bảng/cột/kiểu/NULL/DEFAULT/khóa/ràng buộc/index/quan hệ/cardinality/ERD; không sửa SQL Docker/upgrade hay repo SQL riêng. DbContext SQLite mới chỉ là fixture test, không phải migration hoặc thay đổi model production. Không đổi dữ liệu database nhóm, quyền API quản trị, luồng thanh toán hoặc frontend.

Phần còn thiếu: F-02/F-03 chưa mở API mới; F-04 chưa benchmark writer; chưa chạy FE, CI remote, PayOS thật. Không dùng test fake/SQLite thay bằng chứng Docker hoặc giao dịch ngân hàng. Không tuyên bố mọi giới hạn báo cáo/giá vốn ở R2–R5 đã được giải quyết.

Gemini kiểm tra lại: chạy test tái hiện F-01 và test pipeline mới, so sánh totals kênh all/filter khi có Unclassified; xác nhận scope/Manager ở Ordering không bị thay client giả. Thử lỗi batch2 theo TC-NEW-03 và Manager2 nhà hàng/header theo TC-NEW-04. Không tự mở rộng F-02/F-03 hay đổi SQL để xử lý F-04 chưa tái hiện.

Tên commit đề xuất: **Sửa trạng thái đầy đủ của phân tổ kênh báo cáo**.
Mô tả: **Giữ tổng thu đã xác minh và đánh dấu các kênh chưa đủ dữ liệu khi còn hóa đơn chưa phân loại; bổ sung test trước/sau, lỗi batch và pipeline gRPC Ordering thật. Không thay đổi schema hoặc luồng thanh toán.**


## R4 — Bảng món bán và xếp hạng theo snapshot (2026-11-25)

Baseline giữ HEAD **8e8d872**, nhánh feature/restaurant-performance-reports. Khi bắt đầu đã có R2/R3 chưa commit; giữ nguyên, không tự stage/commit/push hoặc sửa lịch sử. Chỉ Backend. Không triển khai R5/R6, không sửa nghiệp vụ lập/thu/hoàn, Catalog hoặc Inventory.

### Quyết định người dùng và nguồn còn thiếu

- Đã chốt **API bảng món riêng**: ItemId chỉ lọc bảng món/xếp hạng, không thay tổng thu/hoàn toàn nhà hàng trong API sales.
- Đã chốt **BillLine.CogsSnapshot là giá vốn tổng dòng**, không nhân lại Quantity. Không điền0 khi NULL, không lấy giá bán làm giá vốn.
- Nếu phần số lượng hợp lệ đã thay đổi sau bill, chưa có nguồn phân bổ giá vốn/giảm giá cho phần còn bán: trả null cho phần ảnh hưởng, không tự chia tỷ lệ. Dòng Quantity0 hoặc đã hủy hết không cần giá vốn để tính lượng bán hợp lệ.
- Repo vẫn chưa có writer giá vốn lịch sử từ BOM/đơn giá nguyên liệu bình quân. Query chỉ đọc trên BillingDb của container dev hiện tại ghi nhận BillLine0 dòng/COGS khác NULL0 tại lúc kiểm tra; điều này không chứng minh database khác hoặc luồng giá vốn đã hoàn thiện. Các giá vốn50/20 trong test là **fixture**, không giả vờ được tạo từ nghiệp vụ nhập/xuất kho thật.

### API, quyền và luồng

```http
GET /api/restaurants/{restaurantId}/reports/dishes?from=2026-11-25T00:00:00%2B07:00&to=2026-11-26T00:00:00%2B07:00&channel=all&methodCode=all&shift=all&itemId={itemId}
Authorization: Bearer <token-manager>
X-Restaurant-Id: <restaurantId>
```

ItemId là tùy chọn. Sai/Guid.Empty trả400. Kỳ, múi giờ và quyền dùng R1; phương thức/ca/kênh dùng R3. Chỉ Manager được phân công đúng nhà hàng. Bảng món tổng hợp toàn bộ [from,to), theo PaidAt; không đếm RefundAt thành lượt bán mới. `groupBy` là ngữ cảnh kỳ đã chuẩn hóa, endpoint này không trả biểu đồ từng bucket thời gian; API sales vẫn phục vụ phần đó.

Gateway.GetDishes → RestaurantReportGrpc.GetDishes → RestaurantDishReportService → R1 kiểm quyền/kỳ → repository Billing đọc nguồn đã thu → client.GetReportItems RPC → Ordering interceptor + OrderReportAccess.RequireManager → OrderRepository đọc Order/OrderItem/option snapshot → tính các dòng hợp lệ → aggregate/sort → gRPC → Gateway JSON. Mỗi service chỉ đọc DB của chính nó. Không gọi model AI, Catalog hiện tại hoặc Inventory để đoán giá vốn. Không gọi provider để xác minh lại thanh toán khi xem báo cáo.

RPC mới `BillingOrderGrpc.GetReportItems` nhận tối đa100 OrderId khác nhau/batch, chỉ trả nhà hàng và OrderId yêu cầu. Client dùng chung một deadline cho các batch, không retry, kiểm tra owner/IDs trùng/thừa, parse decimal/Guid và duplicate OrderItemIds. Lỗi thiếu nguồn/timeout/Unimplemented/ResourceExhausted trả nguồn thiếu có cảnh báo; caller cancellation và lỗi401/403 không bị nuốt thành báo cáo thành công. Repository Ordering dùng một câu SQL AsSingleQuery + AsNoTracking để đọc trạng thái/lượng hủy/options cùng lúc; không giữ transaction Billing trong lúc gọi RPC.

### Tính số lượng, giá và lợi nhuận món

1. Chỉ các Payment/OnlinePayment đủ chứng cứ R2 mới là nguồn. Tham chiếu thanh toán hoặc OrderItemId lặp bị loại và đánh dấu incomplete, không chọn một bản tùy ý để cộng.
2. Tại phiên bàn: liên kết `BillLine.OrderItemRef` với dòng nguồn đúng `OrderId` và `ItemId`. Lượng hợp lệ = min(BillLine.Quantity, OrderItem.Quantity − CancelledQuantity); item Status Cancelled thì0. **Không trừ CancelledQuantity thêm một lần từ BillLine.Quantity**, vì bill đã có thể chụp lượng sau hủy. Không cộng phần gọi thêm vượt snapshot bill Paid.
3. Order Cancelled/Rejected bị loại; PendingPayment/PendingConfirmation chưa được nhân viên xác nhận bị loại và có ghi chú. Các trạng thái Confirmed/Cooking/Ready/Served/Accepted/ReadyForPickup/Completed được giữ: khách có thể trả tiền trước khi phục vụ. Trạng thái/lượng bất thường hoặc thiếu nguồn làm quantity/ranking incomplete, không biến thành0 đầy đủ.
4. Bill Paid không bị sửa: giá/Name/options từ BillItemSnapshot; UnitPriceSnapshot đã gồm option. Kiểm tra base+option khớp unit, chỉ nhân một lần với lượng hợp lệ. Không lấy giá mới từ nguồn khác thay bill. Kiểm tra tổng/discount/VAT/phí/rounding bằng cùng helper R2; nếu tổng bill không khớp, NetSales/GrossProfit không được coi đầy đủ.
5. Mang về: lấy giá gốc và option snapshot từ Ordering, tính base + tổng(option.quantity × extraPrice), rồi nhân lượng còn hợp lệ. Đối chiếu **tổng quote gốc theo Quantity trước hủy** với OnlinePayment.Amount đúng công thức TakeawayPaymentClient hiện có; lệch thì không dùng nguồn đó như món đầy đủ. Không biến dữ liệu VAT/phí/discount còn thiếu thành0.
6. `grossSales`: giá snapshot × lượng hợp lệ, chưa VAT/phí. `netSales` tại bill = gross − DiscountAllocated khi lượng khớp bill; nếu lượng giảm và discount0 thì dùng gross; lượng giảm có discount thì null vì chưa biết phân bổ. Mang về chưa có discount/tax breakdown lịch sử nên netSales null.
7. Cost dùng nguyên CogsSnapshot khi có số >=0 và lượng hợp lệ đúng lượng bill đã chụp; giảm lượng sau đó thì cost null, không tự chia. Mang về không có nguồn COGS. GrossProfit = netSales − cost **chỉ ở các dòng đủ cả hai**.
8. Các dòng cùng ItemId được gộp; Names và Prices giữ các snapshot/option khác nhau, không coi chúng là giá hiện tại hoặc cộng tiền giữa các price variant. Sắp xếp giảm dần KnownQuantity, tie theo ItemId cho ổn định; không áp quy tắc xếp hạng tie tài chính mới. QuantityComplete=false nghĩa danh sách/thứ tự dựa trên phần đã biết, không tuyên bố ranking đầy đủ.
9. Lọc channel giữ ngữ nghĩa bill của R3: bill Mixed được chọn nguyên bill, sau đó lọc ItemId; không chỉ lấy nửa bill ứng với kênh của một order. Thiếu kênh ở bộ lọc thuần làm kết quả chưa đầy đủ. Lọc method/shift dùng giao dịch lịch sử, online không gắn ca.

### Response và cách hiểu dữ liệu thiếu

- `period`, `filters`, `itemId`, `quantityComplete`, `items`, `warnings`.
- Mỗi item: ItemId, Names[], Quantity{knownQuantity,quantity,isComplete}, GrossSales/NetSales/Cost/GrossProfit dùng ReportAmountDTO, Prices[{unitPrice,options[]}]. Tiền VND như nguồn Billing được kiểm chứng; kiểu decimal xuyên proto string, không chuyển double.
- Không ép amount/quantity null thành0. KnownCost là tổng chi phí các dòng có chứng cứ, không phải chi phí đầy đủ. **KnownProfit chỉ cộng dòng có đủ netSales và cost**, không lấy KnownNetSales − KnownCost từ hai tập hỗ trợ khác nhau.
- QuantityComplete là độ phủ nguồn số lượng, không có nghĩa mọi chỉ tiêu tiền/giá vốn đều đầy đủ. Khi thiếu nguồn ở kỳ, cờ lượng của các item có thể được hạ thận trọng; warnings chỉ rõ giới hạn. Kỳ rỗng đủ nguồn trả items=[]/QuantityComplete=true.
- Bảng món loại hủy/từ chối theo trạng thái nguồn hiện có; API sales giữ tiền thu kỳ gốc và hoàn ở kỳ xác minh theo quyết định trước. Vì thế **không dùng tổng gross/net của bảng món thay tiền thu/hoàn** hoặc tự trừ tiền lần hai.

### Lỗi scoping phát hiện và đã kiểm chứng trước/sau

Request gRPC mới ban đầu bọc mã nhà hàng trong query. Interceptor dùng restaurant_id cấp gốc, nên Manager gán cả A/B, route A nhưng header B có thể nhận200 rỗng thay vì403. Test `DishReportUsesRealOrderingSnapshotsAndMapsQuantityCostsOptionsAndCancellationThroughGateway` **đã chạy trước sửa: failed, Expected Forbidden/Actual OK**.

Đã thêm restaurant_id cấp gốc trong DishReportRequest, Gateway gán từ route; gRPC kiểm owner gốc khớp query trước service. **Sau sửa: test đạt**. Có assertion thêm RPC owner A/query B → InvalidArgument, source RPC GetReportItems bằng Cashier → PermissionDenied. Không sửa interceptor chung, Identity, quyền hoặc nới header.

Docker với Identity thật, Manager có RestaurantStaff tại A/B: headerB/routeA →403; headerA/routeA →200 và lượngA4; headerB/routeB →200 rỗng (không thấyA); không có header vẫn chọnA từ owner gốc. Đây là kiểm tra trong dataset riêng, không tự cấp phân công mới cho người dùng môi trường nhóm.

### File của R4

| File | Mục đích |
| --- | --- |
| Contracts/Protos/billing.proto | GetDishes, request có owner cấp gốc và typed response lượng/tiền/price variants. |
| Contracts/Protos/ordering.proto | RPC GetReportItems và snapshot trạng thái/dòng/options. |
| Services/api_gateway/ApiGateway/Controllers/Billing/RestaurantReportController.cs | Route dishes, Swagger, query và mapping nullable. |
| Services/api_gateway/ApiGateway/DTOs/Billing/RestaurantDishReportDTO.cs | DTO phản hồi Gateway. |
| Services/billing_service/Billing.Common/DTOs/RestaurantDishReportDTO.cs | DTO bảng món và lượng nullable. |
| Services/billing_service/Billing.Common/DTOs/ReportOrderSnapshotDTO.cs | Nguồn chỉ đọc Ordering cho Billing. |
| Services/billing_service/Billing.BLL/Interfaces/IRestaurantDishReportService.cs | Hợp đồng service. |
| Services/billing_service/Billing.BLL/Interfaces/IRestaurantReportItemsClient.cs | Hợp đồng nguồn. |
| Services/billing_service/Billing.BLL/Implements/RestaurantDishReportService.cs | Chọn nguồn thu, đối chiếu hủy, snapshot, cost, incomplete. |
| Services/billing_service/Billing.BLL/Implements/DishReportAggregation.cs | Gộp ItemId, quantity/financial coverage, price variants, sort. |
| Services/billing_service/Billing.BLL/Implements/ReportFilterRules.cs | Dùng chung chuẩn hóa filter và phân loại bill R3/R4. |
| Services/billing_service/Billing.BLL/Implements/BillReportValidation.cs | Dùng chung kiểm tra snapshot tiền bill R2/R4, giữ nguyên công thức. |
| Services/billing_service/Billing.BLL/Implements/RestaurantSalesReportService.cs | Chuyển đúng các helper trên sang dùng chung, không thay API/số tiền sales. |
| Services/billing_service/Billing.API/Clients/RestaurantReportItemsClient.cs | Typed RPC, batching/deadline, tenant/payload guard và fallback. |
| Services/billing_service/Billing.API/Extensions/BillingServiceExtensions.cs | DI service/client mới. |
| Services/billing_service/Billing.API/GrpcServices/RestaurantReportGrpcService.cs | RPC và mapping lượng/tiền nullable, kiểm owner gốc/query. |
| Services/ordering_service/Ordering.Common/DTOs/OrderReportSnapshotDTO.cs | DTO nguồn, không phải entity/bảng. |
| Services/ordering_service/Ordering.DAL/Interfaces/IOrderRepository.cs | Thêm hợp đồng đọc vào repository Order sẵn có. |
| Services/ordering_service/Ordering.DAL/Implements/OrderRepository.cs | Single-query đúng nhà hàng/IDs, không tracking. |
| Services/ordering_service/Ordering.BLL/Interfaces/IBillingOrderService.cs | Hợp đồng nguồn items. |
| Services/ordering_service/Ordering.BLL/Implements/BillingOrderService.cs | Tái sử dụng guard Manager/batch cho hai RPC report. |
| Services/ordering_service/Ordering.API/GrpcServices/BillingOrderGrpcService.cs | Map source items/options; không đổi nghiệp vụ Order cũ. |
| Tests/Billing.Tests/RestaurantDishReportServiceTests.cs | 18 ca quantity/cancel/price/options/total-line COGS/ranking/filter/gap/duplicate/role. |
| Tests/Billing.Tests/RestaurantReportItemsClientTests.cs | 10 ca client: quyền, unavailable, batching, owner/ID/payload sai. |
| Tests/Ordering.Tests/BillingOrderReadTests.cs | Bổ sung method của interface vào fake hiện có; test nguồn thật nằm ở pipeline. |
| Tests/Restaurant.Tests/RestaurantReportOrderingFlowTests.cs | Dùng chung host test R3/R4, nối client thật; model SQLite có default Version cho OrderItem test. |
| Tests/Restaurant.Tests/RestaurantDishReportFlowTests.cs | Pipeline Gateway/Billing/Ordering/Restaurant thật, SQLite, quyền/cost/option/cancel/header. |
| docs/restaurant-performance-reports.md | Quyết định, API, công thức, bằng chứng, giới hạn và review. |
| docs/database-change-log.md | Không đổi schema, ghi semantic COGS đã chốt. |

### Kiểm chứng R4

- Build bản cuối: `dotnet build Backend.sln -c Release` → **0 warning, 0 error**.
- `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter "FullyQualifiedName~RestaurantDishReportServiceTests|FullyQualifiedName~RestaurantReportItemsClientTests"` → **28 passed, 0 failed, 0 skipped** (18 nghiệp vụ +10 client); source/repository giả trong unit, không coi là service thật.
- TestServer `DishReportUsesRealOrderingSnapshotsAndMapsQuantityCostsOptionsAndCancellationThroughGateway`: **1 passed, 0 failed, 0 skipped** sau bản sửa scope. Gateway/gRPC/DI/permission/repository Billing và Ordering thật với SQLite, Restaurant thật trong TestServer, Identity giả. Fixture seed dùng scope đúng nhà hàng cho child rows; không tắt tenant filter để làm test qua.
- TestServer dữ liệu: Bill.Quantity2 nhưng Order.Quantity3/Cancelled1 → lượng2, giá base80+option20 → unit100, gross200; Cost tổng dòng50 → profit150. Hủy thêm còn1 → Cost.Amountnull, hủy hết → items[] và không báo thiếu giá vốn; sales vẫn thu216, bill vẫn Paid/Quantity2. Có kiểm thử ItemId, invalid input, Cashier403, khác nhà hàng403, Manager hai nhà hàng/header và source RPC403.
- Docker Engine29.8.2, project/network/volume riêng: build từ source db-init/Identity/Restaurant/Billing/Ordering/Gateway, SQL Server2022 thật. Khởi tạo/seed bằng script hiện có rồi thêm **fixture tổng hợp**, không gọi PayOS thật hoặc source giá vốn kho thật.
- Hồi quy R2 trên Docker: **12 report checks +2 login, cộng2 GET đồng thời đạt** trước khi sửa fixture cho R4.
- R4 Docker: **22 report checks +2 login đạt**: ItemA lượng4/gross300/knownCost70/costnull; ItemB lượng1/gross25/net/cost/profitnull; price variants50 và100. Lọc DineIn cho bill Qty2, cost50 và profit150 (không nhân cost thành100); Mixed lấy cả hai dòng bill; ItemId không sửa money sales374. Kỳ hoàn không thành lượt bán món mới, giá nguồn thay đổi không thay giá bill, hủy thêm không chia cost, child nguồn khác tenant bị loại, Ordering dừng báo incomplete, từ chối/hủy hết không tính món/cost giả, tiền Paid vẫn374.
- Lần chạy đầu ca restart Ordering gặp nguồn chưa sẵn sàng và trả partial đúng thiết kế; harness đã bổ sung poll qua API thật đến QuantityComplete=true trước khi kiểm tra ca happy-path sau restart. Đã chạy lại từ volume fixture riêng sạch và toàn bộ ca trên đạt. Không thêm retry vào backend.
- Bản scope cuối Docker: **5 report checks +1 login đạt**, Manager gán A/B, mismatch403/match200, chọn root scope không cần header và Qty2/cost50/profit150 qua image cuối. Đã so image IDs Billing/Gateway/Ordering với image test mới.
- Toàn solution trên bản owner-scope cuối: `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx` → **1.468 passed, 0 failed, 104 skipped**, 9 projects. Các SQL opt-in skipped chưa được cấp kết nối riêng, không tính là đạt; SQL Server thật của R4 đã thử riêng qua Docker như trên.
- Sau rà style, chạy lại28 core/client và1 pipeline R4 đều đạt. Đã xóa container/network/volume và file credential của project thử riêng; không xóa volume nhóm hoặc cập nhật image/container backend-* của nhóm. `git diff --check` đạt. Giữ HEAD8e8d872, không commit/push/stage; toàn working tree còn tích lũy R2/R3 trước đó, không coi toàn diff là riêng R4.
- Không sửa/tải FE, chưa chạy CI remote hoặc ngân hàng/provider thật. Chưa benchmark tải ghi đồng thời với kỳ dài; Serializable của repository R2 vẫn giữ, không tự đổi SQL config/index. Chưa có writer COGS/BOM và chưa nghiệm thu lợi nhuận vận hành đầy đủ; R5/R6 chưa triển khai.

### Các thay đổi cần cập nhật trên SQL/ERD

**Không có thay đổi schema.** Cột có sẵn BillingDb.BillLine.CogsSnapshot vẫn decimal(18,2) NULL, không DEFAULT; chỉ chốt nghĩa đọc là tổng dòng. Không thêm/sửa/xóa bảng/cột/PK/FK/UNIQUE/CHECK/index/cardinality/quan hệ/dây ERD, không scaffold model production. Không sửa SQL tạo mới/upgrade hoặc repo Database-SQLServer riêng. DTO/proto mới và DbContext SQLite test không phải bảng/model production mới. SQL fixture chỉ trong project Docker riêng, không đổi database nhóm.

Compose/Dockerfile hiện có dùng được; không cần env/secret mới. Với dependency SQL/Identity/Restaurant đã chạy, rebuild đồng bộ bằng env của môi trường:

```powershell
docker compose --env-file '<file-env-dang-dung>' up -d --build --no-deps ordering billing gateway
```

Không dùng down -v trên môi trường nhóm. Ordering image cũ chưa có GetReportItems được báo thiếu nguồn thay vì đoán lượng; Gateway và Billing phải cùng bản request owner gốc mới.

### Hướng dẫn Gemini review R4

1. Truy owner route→proto cấp gốc→scope interceptor→R1 permission trước repository; test Manager được gán A/B với header lệch, root/query owner khác nhau, source RPC bằng Cashier, ItemId Guid.Empty và kỳ sai.
2. Tính độc lập BillQty2, OrderQty3/Cancel1 phải ra2; Cancel2 ra1 và Costnull; Cancel3/OrderRejected không có món. Không lấy BillQty2−Cancel1=1, không bỏ món chưa phục vụ nhưng đã xác nhận.
3. Dùng bill base80+option20, unit100, qty2, CogsSnapshot50 tổng dòng: gross200/cost50/profit150; NULL cost/profit phải giữnull. Dòng0 không tạo thiếu cost. Giá nguồn thay đổi không thay snapshot Bill. Bill total/discount sai hoặc quote online lệch phải báo thiếu/chưa tính, không lấy giá Catalog để sửa.
4. Kiểm duplicate OrderItemId/provider reference, ItemId grouping/historical names/prices, thứ tự lượng, nguồn thiếu và toàn kỳ rỗng. Mixed filter giữ nguyên bill; ItemId chỉ bảng món, không đổi sales. Không cộng knownNet−knownCost để bịa knownProfit từ các tập khác nhau.
5. Chạy unit/client tests và pipeline thật trong TestServer; phân biệt Identity giả/SQLite với Docker fixture/SQL Server và provider thật. Không thay nghiệp vụ, nguồn dữ liệu hoặc SQL/tenant filter để làm test qua. Ghi phần thiếu writer COGS và chưa benchmark, không tuyên bố toàn bộ lợi nhuận đã hoàn chỉnh.

Tên commit đề xuất: **Bổ sung bảng món bán và xếp hạng theo snapshot**.
Mô tả: **Đối chiếu lượng hợp lệ sau hủy với snapshot đã thanh toán, giữ giá/option lịch sử và giá vốn tổng dòng khi đủ nguồn; thêm RPC đọc Ordering, lọc món riêng và kiểm thử quyền/Gateway/Docker. Không thay đổi schema hoặc luồng thu tiền.**

### Hoàn thiện sau review R4 — 2026-11-25

- **R4-F01 đã xác minh và sửa:** test `KnownOtherItemGapDoesNotChangeAHealthyItemsMetricsBetweenAllAndFiltered` thất bại trước sửa (Expected 2, Actual null), đạt sau sửa. `RestaurantDishReportService` phân biệt thiếu nguồn xác định được ItemId với thiếu nguồn chưa xác định; `DishReportAggregation.Build` đánh giá riêng từng món. Kiểm tra duplicate trước lọc ItemId. Bảng toàn bộ vẫn incomplete khi Y thiếu dữ liệu; X giữ nguyên lượng/doanh thu/giá vốn/lợi nhuận như khi lọc X. Nguồn không xác định vẫn làm X incomplete, không che thiếu dữ liệu.
- **R4-F02:** thêm Currency=VND qua DTO, gRPC reply field 7 và Gateway; lọc kênh riêng có cảnh báo ExclusiveChannelFilter. Không đổi số field proto hiện có.
- **R4-F03:** Names là tập tên lịch sử từ snapshot, sắp xếp ordinal; không phải tên mới nhất hoặc tên hiện tại trong Catalog. Không đổi nghiệp vụ này.
- File sửa trực tiếp đợt review: Contracts/Protos/billing.proto; hai DTO RestaurantDishReportDTO tại Billing.Common và ApiGateway; RestaurantDishReportService; DishReportAggregation; RestaurantReportGrpcService; RestaurantReportController; RestaurantDishReportServiceTests; RestaurantDishReportFlowTests; hai tài liệu báo cáo và nhật ký database này. Các thay đổi R2/R3/R4 có sẵn được giữ nguyên.

Kiểm chứng:

- `dotnet build Backend.sln -c Release`: đạt, 0 warning, 0 error.
- `dotnet test Tests/Billing.Tests/Billing.Tests.csproj -c Release --filter FullyQualifiedName~RestaurantDishReportServiceTests`: 26 đạt. Có test knownProfit chỉ cộng các dòng có đồng thời net/cost: knownNet=300, knownCost=100, knownProfit=140, tổng đầy đủ vẫn null.
- `dotnet test Tests/Restaurant.Tests/Restaurant.Tests.csproj -c Release --filter FullyQualifiedName~DishReportUsesRealOrderingSnapshots`: 1 đạt; TestServer qua Gateway/Billing/Ordering/Restaurant, SQLite và Identity giả lập.
- `dotnet test Backend.sln -c Release --no-build --no-restore -m:1 --logger trx`: 1476 đạt, 0 lỗi, 104 bỏ qua. Test bỏ qua không tính là đạt.
- Docker image mới, SQL Server/Identity/Gateway thật trong project thử riêng: R2 12 kiểm tra báo cáo +2 login và2 GET đồng thời; R4 24 HTTP gồm2 login; scope hồi quy6 HTTP gồm login; review mới7 HTTP gồm login đều đạt. Review xác minh X lượng2/gross200/cost50/profit150 giống nhau giữa xem toàn bộ/lọc X, Y thiếu nguồn; mất nguồn không xác định vẫn lan cảnh báo; VND, cảnh báo kênh, itemId rỗng200 và Guid.Empty400. Image ID của Billing/Gateway/Ordering khớp bản vừa build.
- Fixture thanh toán/giá vốn là dữ liệu tổng hợp trong database thử, không gọi PayOS thật, không thử FE/CI. Không truy cập database service khác trực tiếp trong code production.

**Không có thay đổi schema.** Không sửa SQL/model/upgrade/database nhóm. Chưa có writer COGS vận hành; nguồn VAT/phí/hoàn thiếu vẫn báo rõ; chưa benchmark tải lớn Serializable. Không tuyên bố lợi nhuận vận hành đầy đủ.

Gemini kiểm tra lại: chạy hai test chọn lọc ở trên; đối chiếu X/Y và nguồn chưa xác định, duplicate trước lọc, knownProfit theo giao các dòng đủ nguồn, currency/cảnh báo và ý nghĩa Names lịch sử. Không thay tenant filter hay dữ liệu để vượt lỗi.

Tên commit đề xuất: **Sửa độ đầy đủ từng món và đồng bộ phản hồi báo cáo**.
Mô tả: **Tách lỗi nguồn theo món khỏi lỗi chưa xác định, giữ số liệu nhất quán khi lọc; bổ sung tiền tệ VND, cảnh báo kênh và kiểm thử hồi quy qua Gateway/Docker. Không thay đổi schema.**

Giữ nguyên branch feature/restaurant-performance-reports, HEAD 8e8d872; chưa commit/push.
