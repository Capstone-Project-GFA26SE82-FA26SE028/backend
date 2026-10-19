# Yêu cầu nghiệp vụ hệ thống nhà hàng

Ngày tiếp nhận: 2026-10-16. Tổng cộng **83 chức năng**, từ **8 file** người dùng cung cấp.

## Quy ước sử dụng

Đợt triển khai quản lý đầu tiên: UC_ADM_11, UC_ADM_07–09 theo các trường model hiện có. Chi tiết phạm vi, API, giới hạn và cách kiểm tra tại [restaurant-management.md](restaurant-management.md). Không đánh dấu toàn bộ UC hoàn tất ngoài các phần đã mô tả trong tài liệu triển khai.

- Đây là tài liệu yêu cầu để tham chiếu lâu dài trong repo. Chỉ triển khai chức năng khi người dùng yêu cầu cụ thể; lần tiếp nhận này chỉ lưu tài liệu, không sửa code nghiệp vụ.
- Giữ nguyên mã UC, tên, vai trò và nội dung các cột từ nguồn; chỉ bỏ khoảng trắng cuối bản ghi và đổi định dạng trình bày. Ô ghi chú trống được đánh dấu rõ. Không tự sửa mâu thuẫn hay bổ sung quy tắc còn thiếu.
- Phân chia service dưới đây là **đề xuất kỹ thuật để tra cứu**, chưa phải quyết định nghiệp vụ đã được người dùng duyệt. Một UC có thể cần nhiều service phối hợp.
- Không suy ra chức năng đã hoàn thành từ tài liệu này. Khi triển khai phải đối chiếu code, test và các tài liệu cũ; không tự sửa chức năng đang có chỉ vì nhận bộ yêu cầu mới.
- Luồng tích hợp dự kiến theo kiến trúc repo: Client → Gateway REST → gRPC service → BLL → Repository → DbContext.
- [Danh sách nguồn và SHA-256](requirements/sources/README.md). Bản gốc được giữ nguyên trên máy để đối chiếu; các file TXT đã được gitignore theo yêu cầu người dùng và không có trong bản clone Git. Nội dung nghiệp vụ đầy đủ nằm ngay trong tài liệu Markdown này.

## Phạm vi theo service (đề xuất)

| Service / thành phần | Phạm vi |
| --- | --- |
| Identity | Xác thực nhân viên, phiên, tài khoản, vai trò, audit và quyền truy cập. |
| Restaurant | Thông tin nhà hàng, khu vực, bàn, QR, phiên bàn, đặt bàn, yêu cầu phục vụ. |
| Catalog | Nhóm món, món, giá, tùy chọn, trạng thái bán, công thức và tài liệu chế biến. |
| Ordering | Giỏ hàng, order, xác nhận, KDS, trạng thái chế biến/phục vụ và sự kiện liên quan. |
| Inventory | Nguyên liệu, quy đổi đơn vị, nhập kho, lô/hạn dùng, FIFO, tồn, kiểm kê và cảnh báo. |
| Billing | Thuế/phí, ưu đãi, hóa đơn, thanh toán, PayOS, quỹ/ca và báo cáo tài chính liên quan. |
| Customer | Hồ sơ khách, thành viên, tích/đổi điểm và đánh giá. |
| AI / Monitoring / công nợ NCC | Chưa chốt service sở hữu và phạm vi tích hợp. Không tự tạo service mới. |

## Những điểm cần xác nhận khi triển khai

1. **UC_AI_02:** tên và mô tả là chatbot cho khách nhưng module là AI Forecasting, cột quy tắc lại yêu cầu quyền Manager và dữ liệu dự báo. Giữ nguyên nguồn; cần xác định nội dung đúng.
2. **UC_MGR_03 / UC_KIT_07:** đã chốt ngày 20/10/2026: Manager quản lý trạng thái bán; Kitchen chỉ báo tạm hết món, không được mở bán/ngừng kinh doanh. Giữ nguyên nội dung nguồn bên dưới; phạm vi triển khai và phần tích hợp còn thiếu xem [Trạng thái bán của món](catalog-item-status.md).
3. **UC_MGR_06:** ghi chú trỏ báo cáo giá vốn đến UC_MGR_18, trong khi UC_MGR_18 là khuyến mãi, UC_MGR_17 là chi phí/lợi nhuận. Chưa tự sửa tham chiếu.
4. **UC_MGR_10 và UC_MGR_11:** không có trong file gửi. Không tự tạo thêm hoặc đánh lại số.
5. **UC_AI_02, UC_ADM_02:** giữ nguyên nội dung nguồn, bao gồm phần đầu vào UC_ADM_02 kết thúc ở “tùy chọ”; cần bổ sung nếu bản gốc bị cắt.
6. **UC_ADM_06:** yêu cầu vai trò cố định, Admin không tạo/sửa/xóa vai trò. Cần đối chiếu với chức năng quản lý vai trò hiện có trước khi sửa, không tự thay đổi trong lần lưu tài liệu.
7. **UC_WAI_03 / UC_WAI_06 / UC_CUS_07 / UC_KIT_03:** khách gửi đơn phải chờ Waiter xác nhận; Waiter tạo đơn lại gửi bếp ngay. Cần phân biệt nguồn đơn và bước xác nhận, không cho luồng nhận sự kiện KDS bỏ qua điều kiện.
8. **UC_WAI_06 / UC_KIT_10:** Waiter cho hủy Pending/Sent nhưng Kitchen chỉ nói Pending. Cần thống nhất ý nghĩa Sent và trạng thái nào được hủy.
9. **UC_MGR_18–20 / UC_CAS_03:** cần chốt cách kết hợp khuyến mãi, voucher, điểm và giảm thủ công; nguồn vừa có quy tắc chọn một ưu đãi lớn nhất vừa nhắc ngoại lệ cấu hình cộng voucher. Hạn mức theo vai trò/tài khoản và quyền của Waiter cũng cần chốt.
10. **UC_MGR_09:** công nợ NCC cần xác định nguồn dữ liệu mua hàng, nghĩa vụ thanh toán và service sở hữu; không suy ra chỉ từ luồng nhập nguyên liệu trực tiếp.
11. **UC_MGR_06 / UC_KIT_12 / UC_INV_03:** giá vốn dự kiến dùng đơn giá bình quân, xuất kho dùng FIFO; cần chốt giá vốn thực tế và snapshot BOM/đơn giá. UC_KIT_12 trỏ cảnh báo tới UC_INV_02 nhưng UC_INV_02 là nhập nguyên liệu.
12. **UC_INV_03–04:** cần chốt có chặn sử dụng lô hết hạn/âm kho, ngưỡng cảnh báo, chính sách kiểm kê, ngưỡng và người phê duyệt điều chỉnh.
13. **UC_CUS_01 / UC_WAI_10:** xác định cách phối hợp giữ bàn trước hẹn 30 phút, giữ quá giờ tối đa 15 phút, thời lượng một lượt đặt và xử lý quá giờ/trùng lịch.
14. **UC_WAI_08 / UC_CUS_10 / UC_CAS_04:** cần thống nhất tên trạng thái chờ và quyền xác nhận tiền; luồng thu hộ chỉ hoàn tất khi Cashier nhận tiền. Cần chốt đối soát xác nhận chuyển khoản thủ công với webhook để tránh ghi nhận hai lần.
15. **UC_CAS_09:** “lệch tiền >0” cần xác nhận là chênh lệch tuyệt đối hay chỉ thừa tiền, để xử lý cả thiếu quỹ.
16. **UC_ADM_15 / UC_MGR_01 / UC_MGR_14 / UC_MGR_16:** nguồn còn để mở chỉ số giám sát, phạm vi mã duy nhất, lưu lịch sử tối thiểu 12 tháng và ngưỡng phân loại món; các đề xuất chưa được coi là đã chốt.
17. **UC_CUS_02 / UC_CUS_11:** Cookie mặc định 12 giờ, chỉnh bằng GuestAccess:LifetimeHours; phiên đóng thì hết quyền ngay. Sai mã cho nhập lại, không giới hạn lượt thử theo yêu cầu. Còn cần xác định cách cấp quyền xem hóa đơn/đánh giá sau khi phiên bàn đã đóng.
18. **UC_CAS_07 và các thông báo:** cần chốt nhà cung cấp/API hóa đơn điện tử, tra cứu MST, push/SMS và realtime. Yêu cầu trong tài liệu không đồng nghĩa các tích hợp đã có sẵn.

## Danh mục chức năng

| Mã | Tên chức năng | Vai trò | Service đề xuất |
| --- | --- | --- | --- |
| [UC_ADM_01](#uc_adm_01) | Đăng nhập, đăng xuất | System Administrator | Identity |
| [UC_ADM_02](#uc_adm_02) | Quản lý phiên đăng nhập | System Administrator | Identity |
| [UC_ADM_03](#uc_adm_03) | Xem danh sách tài khoản | System Administrator | Identity |
| [UC_ADM_04](#uc_adm_04) | Tạo mới, cập nhật người dùng | System Administrator | Identity |
| [UC_ADM_05](#uc_adm_05) | Kích hoạt, vô hiệu hóa tài khoản | System Administrator | Identity |
| [UC_ADM_06](#uc_adm_06) | Xem danh mục vai trò, Gán vai trò tài khoản | System Administrator | Identity |
| [UC_ADM_07](#uc_adm_07) | Quản lý khu vực nhà hàng | System Administrator | Restaurant |
| [UC_ADM_08](#uc_adm_08) | Quản lý danh sách bàn | System Administrator | Restaurant |
| [UC_ADM_09](#uc_adm_09) | Gán vị trí bàn vào khu vực | System Administrator | Restaurant |
| [UC_ADM_10](#uc_adm_10) | Khởi tạo, tạo lại QR cho bàn | System Administrator | Restaurant |
| [UC_ADM_11](#uc_adm_11) | Khai báo thông tin nhà hàng | System Administrator | Restaurant |
| [UC_ADM_12](#uc_adm_12) | Cấu hình VAT, phí dịch vụ | System Administrator | Billing |
| [UC_ADM_13](#uc_adm_13) | Bật, tắt phương thức thanh toán | System Administrator | Billing |
| [UC_ADM_14](#uc_adm_14) | Xem, tìm kiếm, lọc Audit Log | System Administrator | Identity |
| [UC_ADM_15](#uc_adm_15) | Dashboard trạng thái hệ thống | System Administrator | Monitoring |
| [UC_MGR_01](#uc_mgr_01) | Quản lý danh mục nhóm món và món ăn | Restaurant Manager | Catalog |
| [UC_MGR_02](#uc_mgr_02) | Thiết lập giá bán theo thời gian và kênh | Restaurant Manager | Catalog |
| [UC_MGR_03](#uc_mgr_03) | Bật/tắt kinh doanh, đánh dấu hết món | Restaurant Manager | Catalog |
| [UC_MGR_04](#uc_mgr_04) | Quản lý nguyên liệu thô và đơn vị đo | Restaurant Manager | Inventory |
| [UC_MGR_05](#uc_mgr_05) | Cấu hình định lượng nguyên liệu (BOM) | Restaurant Manager | Catalog + Inventory |
| [UC_MGR_06](#uc_mgr_06) | Tự động tính giá vốn món ăn (COGS) | Restaurant Manager | Catalog + Inventory |
| [UC_MGR_07](#uc_mgr_07) | Xem tổng quan tồn kho thời gian thực | Restaurant Manager | Inventory |
| [UC_MGR_08](#uc_mgr_08) | Theo dõi, kiểm soát hao hụt nguyên liệu | Restaurant Manager | Inventory + Ordering + Catalog |
| [UC_MGR_09](#uc_mgr_09) | Theo dõi công nợ và tuổi nợ NCC | Restaurant Manager | Chưa chốt |
| [UC_MGR_12](#uc_mgr_12) | Xem sơ đồ bàn và trạng thái phục vụ | Restaurant Manager | Restaurant |
| [UC_MGR_13](#uc_mgr_13) | Giám sát tiến độ chế biến tại KDS | Restaurant Manager | Ordering |
| [UC_MGR_14](#uc_mgr_14) | Kiểm tra lịch sử thao tác nhân viên | Restaurant Manager | Identity + các service |
| [UC_MGR_15](#uc_mgr_15) | Báo cáo doanh thu đa chiều | Restaurant Manager | Billing |
| [UC_MGR_16](#uc_mgr_16) | Phân tích hiệu quả thực đơn | Restaurant Manager | Billing + Ordering + Catalog + Inventory |
| [UC_MGR_17](#uc_mgr_17) | Báo cáo Chi phí & Lợi nhuận | Restaurant Manager | Billing + Inventory |
| [UC_MGR_18](#uc_mgr_18) | Cấu hình quy tắc khuyến mãi | Restaurant Manager | Billing |
| [UC_MGR_19](#uc_mgr_19) | Phát hành, quản lý Voucher/Coupon | Restaurant Manager | Billing |
| [UC_MGR_20](#uc_mgr_20) | Thiết lập hạn mức giảm giá thủ công | Restaurant Manager | Billing + Identity |
| [UC_WAI_01](#uc_wai_01) | Đăng nhập, đăng xuất | Waiter | Identity |
| [UC_WAI_02](#uc_wai_02) | Xem sơ đồ bàn | Waiter | Restaurant |
| [UC_WAI_03](#uc_wai_03) | Xác nhận đơn trước khi chuyển bếp | Waiter | Ordering |
| [UC_WAI_04](#uc_wai_04) | Nhận thông báo món sẵn sàng | Waiter | Ordering |
| [UC_WAI_05](#uc_wai_05) | Xác nhận đã lên món | Waiter | Ordering |
| [UC_WAI_06](#uc_wai_06) | Tạo, điều chỉnh order | Waiter | Ordering |
| [UC_WAI_07](#uc_wai_07) | Đổi bàn cho khách | Waiter | Restaurant + Ordering |
| [UC_WAI_08](#uc_wai_08) | Thu hộ tiền mặt | Waiter | Billing |
| [UC_WAI_09](#uc_wai_09) | Xử lý yêu cầu từ QR Menu | Waiter | Restaurant |
| [UC_WAI_10](#uc_wai_10) | Duyệt yêu cầu đặt bàn | Waiter | Restaurant |
| [UC_CAS_01](#uc_cas_01) | Đăng nhập, đăng xuất, bàn giao ca | Cashier | Identity + Billing |
| [UC_CAS_02](#uc_cas_02) | Kiểm tra đơn trước thanh toán | Cashier | Billing + Ordering |
| [UC_CAS_03](#uc_cas_03) | Áp dụng ưu đãi thanh toán | Cashier | Billing + Customer |
| [UC_CAS_04](#uc_cas_04) | Thanh toán tiền mặt, chuyển khoản | Cashier | Billing |
| [UC_CAS_05](#uc_cas_05) | Tách, gộp hóa đơn | Cashier | Billing + Ordering + Restaurant |
| [UC_CAS_06](#uc_cas_06) | In tạm tính, hóa đơn thanh toán | Cashier | Billing |
| [UC_CAS_07](#uc_cas_07) | Xuất hóa đơn điện tử VAT | Cashier | Billing |
| [UC_CAS_08](#uc_cas_08) | Ghi nhận tiền quỹ trong ca | Cashier | Billing |
| [UC_CAS_09](#uc_cas_09) | Đối soát, chốt Z-Report | Cashier | Billing |
| [UC_CAS_10](#uc_cas_10) | Tra cứu & Tích điểm qua SĐT | Cashier | Customer + Billing |
| [UC_KIT_01](#uc_kit_01) | Đăng nhập / Đăng xuất KDS | Kitchen Staff | Identity |
| [UC_KIT_02](#uc_kit_02) | Xem tổng quan tình trạng hoạt động | Kitchen Staff | Ordering |
| [UC_KIT_03](#uc_kit_03) | Tiếp nhận order mới real-time | Kitchen Staff | Ordering |
| [UC_KIT_04](#uc_kit_04) | Xem chi tiết món và ghi chú chế biến | Kitchen Staff | Ordering |
| [UC_KIT_05](#uc_kit_05) | Sắp xếp hàng đợi chế biến | Kitchen Staff | Ordering |
| [UC_KIT_06](#uc_kit_06) | Cập nhật trạng thái món | Kitchen Staff | Ordering |
| [UC_KIT_07](#uc_kit_07) | Báo hết món / Tạm dừng phục vụ | Kitchen Staff | Catalog |
| [UC_KIT_08](#uc_kit_08) | Báo cáo thiếu / hết nguyên liệu | Kitchen Staff | Inventory |
| [UC_KIT_09](#uc_kit_09) | Xem công thức và hướng dẫn chế biến | Kitchen Staff | Catalog |
| [UC_KIT_10](#uc_kit_10) | Nhận thông báo hủy món | Kitchen Staff | Ordering |
| [UC_KIT_11](#uc_kit_11) | Thông báo món Ready cho Waiter | Kitchen Staff | Ordering |
| [UC_KIT_12](#uc_kit_12) | Chế biến món trừ kho định lượng tự động | Kitchen Staff | Ordering + Catalog + Inventory |
| [UC_KIT_13](#uc_kit_13) | Xem lịch sử món / đơn đã hoàn thành | Kitchen Staff | Ordering |
| [UC_INV_01](#uc_inv_01) | Đăng nhập / Đăng xuất phân hệ Kho | Inventory Staff | Identity |
| [UC_INV_02](#uc_inv_02) | Nhập thêm nguyên liệu | Inventory Staff | Inventory |
| [UC_INV_03](#uc_inv_03) | Quản lý Nguyên Liệu, hạn sử dụng & Trừ kho | Inventory Staff | Inventory |
| [UC_INV_04](#uc_inv_04) | Kiểm kê và điều chỉnh chênh lệch | Inventory Staff | Inventory |
| [UC_CUS_01](#uc_cus_01) | Đặt bàn qua số điện thoại | Customer | Restaurant |
| [UC_CUS_02](#uc_cus_02) | Truy cập menu bằng QR và tham gia phiên bàn | Customer | Restaurant + Customer |
| [UC_CUS_03](#uc_cus_03) | Tra cứu, lọc thực đơn | Customer | Catalog |
| [UC_CUS_04](#uc_cus_04) | Xem chi tiết món | Customer | Catalog |
| [UC_CUS_05](#uc_cus_05) | Tùy chỉnh món | Customer | Catalog |
| [UC_CUS_06](#uc_cus_06) | Xem, thêm món vào giỏ | Customer | Ordering + Catalog |
| [UC_CUS_07](#uc_cus_07) | Gửi order | Customer | Ordering |
| [UC_CUS_08](#uc_cus_08) | Theo dõi tiến độ đơn | Customer | Ordering |
| [UC_CUS_09](#uc_cus_09) | Gọi phục vụ, yêu cầu thanh toán | Customer | Restaurant |
| [UC_CUS_10](#uc_cus_10) | Chọn phương thức thanh toán | Customer | Billing |
| [UC_CUS_11](#uc_cus_11) | Nhận hóa đơn, gửi đánh giá | Customer | Billing + Customer |
| [UC_AI_01](#uc_ai_01) | Gợi ý món ăn kèm, đồ uống | AI Restaurant Assistant | AI + Catalog + Ordering |
| [UC_AI_02](#uc_ai_02) | Chatbot tư vấn món ăn cho khách | AI Restaurant Assistant | AI + Catalog |

## System Administrator — 15 chức năng

Nguồn: 01-admin.txt (bản gốc local, không đưa lên Git).

<a id="uc_adm_01"></a>

### UC_ADM_01 — Đăng nhập, đăng xuất

Service đề xuất: **Identity**.

**Module:**

> Authentication & RBAC

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> xác thực tài khoản Admin bằng email/tên đăng nhập và mật khẩu; kết thúc phiên khi đăng xuất.

**Dữ liệu đầu vào:**

> Email/tên đăng nhập, mật khẩu; yêu cầu đăng xuất.

**Kết quả đầu ra:**

> Phiên truy cập được mở hoặc kết thúc; Dashboard Admin sau đăng nhập.

**Quy tắc nghiệp vụ:**

> Tài khoản phải đang hoạt động và có quyền Admin; thông tin đăng nhập phải hợp lệ.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_02"></a>

### UC_ADM_02 — Quản lý phiên đăng nhập

Service đề xuất: **Identity**.

**Module:**

> Authentication & RBAC

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Xem và theo dõi phiên đăng nhập, lịch sử truy cập web theo tài khoản, thời gian và thiết bị.

**Dữ liệu đầu vào:**

> Từ khóa tìm kiếm (tên tài khoản/email); bộ lọc theo trạng thái phiên, khoảng thời gian, vai trò, loại thiết bị (tùy chọ

**Kết quả đầu ra:**

> Danh sách phiên đăng nhập gồm tài khoản, thời gian đăng nhập, IP, thiết bị và trạng thái phiên.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền được xem dữ liệu phiên; không hiển thị token/bí mật xác thực.

**Ghi chú:**

> Nếu chỉ cần theo dõi lịch sử, chưa cần làm chức năng thu hồi phiên đăng nhập.

<a id="uc_adm_03"></a>

### UC_ADM_03 — Xem danh sách tài khoản

Service đề xuất: **Identity**.

**Module:**

> User Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Admin xem danh sách và thông tin chi tiết tài khoản người dùng; tìm kiếm, lọc và phân trang để tra cứu tài khoản trong hệ thống.

**Dữ liệu đầu vào:**

> Từ khóa tìm kiếm (họ tên, email hoặc tên đăng nhập); bộ lọc theo vai trò và trạng thái tài khoản; thông tin phân trang (số trang, số bản ghi mỗi trang). Các trường tìm kiếm và lọc là tùy chọn.

**Kết quả đầu ra:**

> Danh sách tài khoản phù hợp điều kiện tìm kiếm/lọc; thông tin gồm họ tên, email hoặc tên đăng nhập, vai trò, trạng thái tài khoản. Khi chọn một tài khoản, hệ thống hiển thị thông tin chi tiết của tài khoản đó.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được xem thông tin tài khoản. Không hiển thị mật khẩu, token hoặc thông tin xác thực bí mật. Hệ thống chỉ trả về các tài khoản phù hợp điều kiện lọc.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_04"></a>

### UC_ADM_04 — Tạo mới, cập nhật người dùng

Service đề xuất: **Identity**.

**Module:**

> User Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Admin tạo tài khoản mới hoặc cập nhật thông tin người dùng trong hệ thống nhà hàng.

**Dữ liệu đầu vào:**

> Tạo mới: Họ tên, email/tên đăng nhập, số điện thoại (nếu có), vai trò, avatar(nếu có) thông tin tài khoản cần thiết. Cập nhật:ID tài khoản và các thông tin cần thay đổi.

**Kết quả đầu ra:**

> Tài khoản mới được tạo thành công hoặc thông tin tài khoản được cập nhật; hệ thống hiển thị thông báo kết quả.

**Quy tắc nghiệp vụ:**

> Email/tên đăng nhập không được trùng; thông tin bắt buộc phải hợp lệ; chỉ Admin có quyền mới được tạo và cập nhật tài khoản; ghi nhận lịch sử thay đổi.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_05"></a>

### UC_ADM_05 — Kích hoạt, vô hiệu hóa tài khoản

Service đề xuất: **Identity**.

**Module:**

> User Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Admin kích hoạt tài khoản để nhân viên được phép đăng nhập hoặc vô hiệu hóa tài khoản để ngăn nhân viên tiếp tục đăng nhập hệ thống.

**Dữ liệu đầu vào:**

> Tài khoản được chọn; trạng thái muốn thay đổi (Kích hoạt/Vô hiệu hóa); lý do vô hiệu hóa (nếu có); xác nhận thao tác.

**Kết quả đầu ra:**

> Trạng thái tài khoản được cập nhật; hệ thống hiển thị thông báo kết quả.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được thay đổi trạng thái tài khoản. Tài khoản bị vô hiệu hóa không được phép đăng nhập mới. Không xóa lịch sử hoạt động của tài khoản.

**Ghi chú:**

> Không cho vô hiệu hóa Admin cuối cùng, buộc đăng xuất các phiên đang hoạt động

<a id="uc_adm_06"></a>

### UC_ADM_06 — Xem danh mục vai trò, Gán vai trò tài khoản

Service đề xuất: **Identity**.

**Module:**

> Role & Permission

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Admin xem danh sách các vai trò cố định trong hệ thống và gán vai trò cố định cho tài khoản.

**Dữ liệu đầu vào:**

> Từ khóa tìm kiếm theo tên/mã vai trò (tùy chọn); vai trò được chọn khi xem chi tiết.
> Bộ lọc: Vai trò hiện tại, trạng thái tài khoản (tùy chọn).
> Gán vai trò: Tài khoản được chọn và vai trò cần gán.

**Kết quả đầu ra:**

> Danh sách vai trò gồm mã, tên, mô tả; thông tin chi tiết.
> Vai trò của tài khoản được cập nhật; hệ thống hiển thị thông báo kết quả và ghi nhận lịch sử thay đổi.

**Quy tắc nghiệp vụ:**

> Hệ thống sử dụng các vai trò cố định. Admin không được tạo, sửa hoặc xóa vai trò.
> Mỗi tài khoản chỉ có một vai trò tại một thời điểm. Chỉ Admin có quyền được thay đổi vai trò. Không cho phép thu hồi vai trò Admin của quản trị viên đang hoạt động cuối cùng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_07"></a>

### UC_ADM_07 — Quản lý khu vực nhà hàng

Service đề xuất: **Restaurant**.

**Module:**

> Area Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Tạo mới, sửa, xóa/ngừng sử dụng các khu vực trong nhà hàng.

**Dữ liệu đầu vào:**

> Tìm kiếm: Từ khóa tên/mã khu vực.
> Bộ lọc: Trạng thái khu vực.
> Thêm mới: Mã khu vực, tên khu vực, mô tả (tùy chọn).
> Cập nhật: Khu vực được chọn và thông tin cần chỉnh sửa.
> Xóa/ngừng sử dụng: Khu vực được chọn và xác nhận thao tác.

**Kết quả đầu ra:**

> Danh sách khu vực theo điều kiện tìm kiếm/lọc; thông tin chi tiết khu vực được chọn; khu vực được tạo mới, cập nhật hoặc thay đổi trạng thái thành công; thông báo kết quả thao tác.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được quản lý khu vực. Mã khu vực không được trùng. Không cho phép xóa khu vực đang có bàn được sử dụng hoặc có dữ liệu nghiệp vụ cần giữ lại.

**Ghi chú:**

> Mỗi khu vực là một mục độc lập (ví dụ: Tầng 1, Phòng VIP – Tầng 2). Khu vực đang có bàn không được xóa; nếu cần, Admin chuyển bàn  hoặc ngừng sử dụng khu vực. Chỉ cho phép xóa khi không còn bàn và không có dữ liệu nghiệp vụ cần lưu giữ.

<a id="uc_adm_08"></a>

### UC_ADM_08 — Quản lý danh sách bàn

Service đề xuất: **Restaurant**.

**Module:**

> Table Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Xem danh sách, thêm mới, chỉnh sửa và xóa/ngừng sử dụng bàn.

**Dữ liệu đầu vào:**

> Tìm kiếm: Từ khóa theo mã/tên bàn.
> Bộ lọc: Khu vực, trạng thái quản lý bàn (đang sử dụng/ngừng sử dụng).
> Thêm mới: Mã bàn, tên bàn, sức chứa (số khách tối đa), ghi chú (tùy chọn).
> Cập nhật: Bàn được chọn và thông tin cần chỉnh sửa. Ngừng sử dụng:
> Bàn được chọn và xác nhận thao tác.

**Kết quả đầu ra:**

> Danh sách bàn phù hợp điều kiện tìm kiếm/lọc; thông tin chi tiết bàn; bàn được tạo mới, cập nhật hoặc ngừng sử dụng; thông báo kết quả.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được quản lý bàn. Mã bàn không được trùng trong nhà hàng. Sức chứa phải là số nguyên dương. Không cho ngừng sử dụng bàn khi bàn đang có đơn hàng hoặc phiên phục vụ chưa kết thúc. Không xóa dữ liệu bàn đã có lịch sử đơn hàng cần lưu giữ.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_09"></a>

### UC_ADM_09 — Gán vị trí bàn vào khu vực

Service đề xuất: **Restaurant**.

**Module:**

> Table Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Gán hoặc chuyển vị trí bàn sang khu vực phù hợp.

**Dữ liệu đầu vào:**

> Từ khóa tìm kiếm; bộ lọc khu vực, trạng thái gán; bàn được chọn và khu vực đích.

**Kết quả đầu ra:**

> Danh sách bàn và khu vực tương ứng được cập nhật; thông báo kết quả gán hoặc chuyển bàn.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được gán/chuyển bàn. Bàn và khu vực đích phải tồn tại; khu vực đích phải đang hoạt động. Mỗi bàn chỉ thuộc một khu vực tại một thời điểm. Không cho chuyển bàn khi đang có phiên phục vụ hoặc đơn hàng chưa hoàn tất.

**Ghi chú:**

> Bàn chưa được gán khu vực thì chưa được đưa vào phục vụ.

<a id="uc_adm_10"></a>

### UC_ADM_10 — Khởi tạo, tạo lại QR cho bàn

Service đề xuất: **Restaurant**.

**Module:**

> QR Management

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Tạo mã QR tĩnh hoặc động để khách truy cập menu theo bàn; tạo lại khi cần thay mã.

**Dữ liệu đầu vào:**

> Từ khóa tìm kiếm; bộ lọc khu vực, trạng thái QR; bàn được chọn và yêu cầu tạo/tạo lại QR.

**Kết quả đầu ra:**

> Mã QR và đường dẫn truy cập thực đơn gắn với bàn; thông báo kết quả. Khi tạo lại, mã QR cũ bị vô hiệu hóa

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được tạo/tạo lại QR. Bàn phải tồn tại, đang hoạt động và đã được gán khu vực. QR phải xác định đúng bàn; hệ thống phải kiểm tra hiệu lực QR khi khách truy cập. Khi tạo lại QR, mã cũ không được tiếp tục dùng để truy cập.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_11"></a>

### UC_ADM_11 — Khai báo thông tin nhà hàng

Service đề xuất: **Restaurant**.

**Module:**

> Restaurant Settings

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Tạo/cập nhật thông tin nhận diện, liên hệ và thời gian hoạt động của nhà hàng.

**Dữ liệu đầu vào:**

> Tên, mã số thuế, địa chỉ, logo, hotline, giờ hoạt động.

**Kết quả đầu ra:**

> Hồ sơ nhà hàng được lưu và hiển thị ở các màn hình liên quan.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được cập nhật thông tin. Tên, địa chỉ và số điện thoại không được để trống. Email (nếu nhập) phải đúng định dạng; giờ mở cửa và đóng cửa phải hợp lệ. Logo (nếu tải lên) phải thuộc định dạng và dung lượng hệ thống cho phép.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_12"></a>

### UC_ADM_12 — Cấu hình VAT, phí dịch vụ

Service đề xuất: **Billing**.

**Module:**

> System Configuration

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Thiết lập tỷ lệ thuế và phí dịch vụ áp dụng khi tính tiền.

**Dữ liệu đầu vào:**

> Tỷ lệ VAT, tỷ lệ phí dịch vụ, thời điểm hiệu lực.

**Kết quả đầu ra:**

> Cấu hình được lưu; hệ thống hiển thị thông báo kết quả và áp dụng cấu hình cho các giao dịch mới theo thời điểm có hiệu lực.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được thay đổi cấu hình. Mức VAT phải phù hợp quy định áp dụng; tỷ lệ phí dịch vụ phải hợp lệ và không âm. Khi tắt phí dịch vụ, hệ thống không tính khoản phí này. Thay đổi cấu hình không được làm thay đổi số tiền của hóa đơn đã hoàn tất.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_13"></a>

### UC_ADM_13 — Bật, tắt phương thức thanh toán

Service đề xuất: **Billing**.

**Module:**

> Payment Configuration

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Admin xem danh sách phương thức thanh toán và bật/tắt phương thức được phép sử dụng khi khách thanh toán.

**Dữ liệu đầu vào:**

> Bộ lọc trạng thái (tùy chọn); phương thức thanh toán được chọn, trạng thái bật/tắt và xác nhận thay đổi.

**Kết quả đầu ra:**

> Trạng thái phương thức thanh toán được cập nhật; danh sách lựa chọn thanh toán của khách được điều chỉnh; thông báo kết quả.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được thay đổi cấu hình. Chỉ cho bật thanh toán PayOS khi kết nối đã được cấu hình hợp lệ. Phải duy trì ít nhất một phương thức thanh toán khả dụng. Việc tắt một phương thức chỉ ngăn tạo giao dịch mới qua phương thức đó, không tự hủy các giao dịch đang xử lý.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_adm_14"></a>

### UC_ADM_14 — Xem, tìm kiếm, lọc Audit Log

Service đề xuất: **Identity**.

**Module:**

> System Security

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Tra cứu nhật ký thao tác, người thực hiện, thời gian, IP và dữ liệu trước/sau thay đổi.

**Dữ liệu đầu vào:**

> Bộ lọc thời gian, tài khoản, hành động, đối tượng, IP; phân trang.

**Kết quả đầu ra:**

> Danh sách nhật ký phù hợp điều kiện tìm kiếm/lọc; chi tiết gồm người thực hiện, thao tác, thời gian, đối tượng bị tác động.

**Quy tắc nghiệp vụ:**

> Chỉ Admin có quyền mới được xem Audit Log. Nhật ký được hệ thống ghi tự động; Admin không được sửa hoặc xóa bản ghi. Không ghi mật khẩu, API Key, token hay thông tin bí mật vào nhật ký.

**Ghi chú:**

> ghi các thao tác liên quan đến tài khoản, phân quyền, bàn, QR, thanh toán và cấu hình hệ thống.

<a id="uc_adm_15"></a>

### UC_ADM_15 — Dashboard trạng thái hệ thống

Service đề xuất: **Monitoring**.

**Module:**

> System Monitoring

**Vai trò:**

> System Administrator

**Mô tả nghiệp vụ:**

> Hiển thị uptime, độ trễ API, dung lượng database và số kết nối WebSocket theo thời gian.

**Dữ liệu đầu vào:**

> Khoảng thời gian, dịch vụ/môi trường, chu kỳ cập nhật.

**Kết quả đầu ra:**

> Chỉ số và biểu đồ trạng thái; thời điểm cập nhật gần nhất.

**Quy tắc nghiệp vụ:**

> Dữ liệu lấy từ nguồn giám sát; khi mất nguồn phải báo không có dữ liệu, không coi là giá trị 0.

**Ghi chú:**

> Cần chốt cách tính uptime, độ trễ trung bình/P95, phạm vi kết nối và ngưỡng cảnh báo.

## Restaurant Manager — 18 chức năng

Nguồn: 02-manager.txt (bản gốc local, không đưa lên Git).

<a id="uc_mgr_01"></a>

### UC_MGR_01 — Quản lý danh mục nhóm món và món ăn

Service đề xuất: **Catalog**.

**Module:**

> Menu Management

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Tạo mới, chỉnh sửa và xóa/ngừng sử dụng nhóm món, món ăn trong thực đơn.

**Dữ liệu đầu vào:**

> Tạo nhóm món: Tên nhóm, mô tả (nếu có).
> Tạo món ăn: Tên món, nhóm món, mô tả, hình ảnh.
> Chỉnh sửa/ngừng sử dụng: Nhóm hoặc món được chọn, thông tin cần cập nhật hoặc trạng thái cần thay đổi.

**Kết quả đầu ra:**

> Danh sách nhóm món và món ăn được cập nhật; hệ thống thông báo kết quả thao tác.

**Quy tắc nghiệp vụ:**

> Mã nhóm và mã món do hệ thống tạo, không trùng nhau. Mỗi món phải thuộc một nhóm hợp lệ. Không xóa vĩnh viễn nhóm/món đã có dữ liệu liên quan để bảo toàn lịch sử đơn hàng. Không cho ngừng sử dụng nhóm nếu vẫn còn món đang kinh doanh trong nhóm đó.

**Ghi chú:**

> Cần chốt mã duy nhất theo nhà hàng/chi nhánh, trường bắt buộc và xóa mềm/cứng.

<a id="uc_mgr_02"></a>

### UC_MGR_02 — Thiết lập giá bán theo thời gian và kênh

Service đề xuất: **Catalog**.

**Module:**

> Pricing Management

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager thiết lập và cập nhật giá bán chuẩn của món ăn; có thể cấu hình giá riêng theo khung giờ hoặc kênh bán. Hệ thống tự xác định giá áp dụng khi khách đặt món.

**Dữ liệu đầu vào:**

> Mã món; giá chuẩn; giá theo khung giờ/kênh; thời gian và phạm vi áp dụng, yêu cầu lưu hoặc cập nhật giá.

**Kết quả đầu ra:**

> Bảng giá được cập nhật; hệ thống hiển thị giá áp dụng cho món theo thời gian và kênh bán tương ứng.

**Quy tắc nghiệp vụ:**

> Giá bán phải lớn hơn hoặc bằng 0. Mỗi món phải có giá chuẩn trước khi mở bán. Nếu có nhiều mức giá, hệ thống áp dụng theo thứ tự ưu tiên: giá theo khung giờ và kênh bán cụ thể → giá theo khung giờ → giá theo kênh bán → giá chuẩn. Không cho phép hai cấu hình giá có cùng phạm vi và thời gian hiệu lực chồng lấn. Giá của món đã được khách xác nhận đặt phải được lưu trong đơn, không tự thay đổi khi Manager cập nhật bảng giá.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_03"></a>

### UC_MGR_03 — Bật/tắt kinh doanh, đánh dấu hết món

Service đề xuất: **Catalog**.

**Module:**

> Menu Availability

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager bật/tắt bán món hoặc đánh dấu hết hàng tức; đồng bộ trạng thái đến các kênh liên quan.

**Dữ liệu đầu vào:**

> Món ăn được chọn; trạng thái cần thay đổi (Đang bán / Tạm hết món / Ngừng kinh doanh); lý do thay đổi (nếu có).

**Kết quả đầu ra:**

> Trạng thái món được cập nhật trên giao diện Manager và thực đơn QR; hệ thống thông báo kết quả thao tác.

**Quy tắc nghiệp vụ:**

> Chỉ Manager có quyền thay đổi trạng thái bán. Món ở trạng thái Tạm hết món hoặc Ngừng kinh doanh không được nhận lượt đặt mới; Phải kiểm tra lại trạng thái khi khách xác nhận đặt món. Thay đổi trạng thái không tự động hủy món đã đặt trước đó. Món chỉ được mở bán khi có giá bán hợp lệ.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_04"></a>

### UC_MGR_04 — Quản lý nguyên liệu thô và đơn vị đo

Service đề xuất: **Inventory**.

**Module:**

> Ingredient Management

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager tạo mới, xem và cập nhật danh mục nguyên liệu cùng đơn vị đo, quy đổi đơn vị để phục vụ việc nhập kho, xuất kho và thiết lập công thức món ăn.

**Dữ liệu đầu vào:**

> Tạo nguyên liệu: Tên nguyên liệu, đơn vị cơ sở, đơn vị mua (nếu khác), hệ số quy đổi (nếu có).
> Cập nhật: Nguyên liệu được chọn và thông tin cần thay đổi.
> Tra cứu: Từ khóa tìm kiếm theo tên/mã nguyên liệu.

**Kết quả đầu ra:**

> Danh mục nguyên liệu được cập nhật; hiển thị đơn vị đo và hệ số quy đổi tương ứng; hệ thống thông báo kết quả thao tác.

**Quy tắc nghiệp vụ:**

> Mã nguyên liệu do hệ thống tạo và không trùng nhau. Tên nguyên liệu không được để trống. Hệ số quy đổi phải lớn hơn 0. Mỗi nguyên liệu phải có một đơn vị cơ sở thống nhất để tính tồn kho và định lượng công thức. Không cho thay đổi đơn vị cơ sở nếu việc thay đổi làm sai dữ liệu kho hoặc công thức đã ghi nhận.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_05"></a>

### UC_MGR_05 — Cấu hình định lượng nguyên liệu (BOM)

Service đề xuất: **Catalog + Inventory**.

**Module:**

> Recipe & BOM

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager thiết lập và cập nhật công thức nguyên liệu cần dùng để chế biến một phần món ăn. Hệ thống lưu công thức để phục vụ tính giá vốn và theo dõi tiêu hao nguyên liệu.

**Dữ liệu đầu vào:**

> Tạo công thức: Món ăn được chọn; danh sách nguyên liệu; số lượng cần dùng và đơn vị đo của từng nguyên liệu.
> Cập nhật công thức: Món ăn được chọn; nguyên liệu cần thêm, sửa hoặc loại bỏ; định lượng mới; thời điểm bắt đầu áp dụng (nếu có).

**Kết quả đầu ra:**

> Công thức món ăn được lưu hoặc cập nhật; hiển thị danh sách nguyên liệu và định lượng tương ứng.

**Quy tắc nghiệp vụ:**

> Mỗi nguyên liệu trong công thức phải tồn tại trong danh mục nguyên liệu. Định lượng phải lớn hơn 0 và đơn vị đo phải quy đổi được về đơn vị cơ sở. Không được để thay đổi công thức làm sai dữ liệu tiêu hao đã ghi nhận trước đó.

**Ghi chú:**

> BOM là công thức định lượng nguyên liệu

<a id="uc_mgr_06"></a>

### UC_MGR_06 — Tự động tính giá vốn món ăn (COGS)

Service đề xuất: **Catalog + Inventory**.

**Module:**

> Cost Management

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Hệ thống tự động tính chi phí nguyên liệu để chế biến một phần món ăn dựa trên công thức (BOM) và đơn giá nguyên liệu. Manager xem giá vốn và chi tiết chi phí của từng món.

**Dữ liệu đầu vào:**

> Manager chọn món ăn cần xem.
> Hệ thống lấy công thức món ăn, định lượng từng nguyên liệu, đơn giá nguyên liệu và thời điểm tính giá vốn.

**Kết quả đầu ra:**

> Giá vốn nguyên liệu của một phần món ăn; bảng chi tiết chi phí từng nguyên liệu; thời điểm tính giá vốn.

**Quy tắc nghiệp vụ:**

> Chỉ tính giá vốn khi món có công thức và các nguyên liệu có đơn giá hợp lệ. Số lượng nguyên liệu phải được quy đổi về cùng đơn vị trước khi tính. Nếu thiếu công thức hoặc đơn giá, hệ thống phải thông báo chưa đủ dữ liệu để tính, không tự coi chi phí thiếu là 0 đồng.

**Ghi chú:**

> dùng đơn giá nguyên liệu bình quân để tính và thống nhất cách xác định giá này với chức năng quản lý kho. UC này tính giá vốn nguyên liệu dự kiến cho một phần món; báo cáo giá vốn các món đã bán trong kỳ thuộc UC_MGR_18

<a id="uc_mgr_07"></a>

### UC_MGR_07 — Xem tổng quan tồn kho thời gian thực

Service đề xuất: **Inventory**.

**Module:**

> Inventory Monitoring

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager theo dõi số lượng nguyên liệu còn trong kho, xem chi tiết biến động và thời điểm dữ liệu được cập nhật để hỗ trợ vận hành nhà hàng.

**Dữ liệu đầu vào:**

> nhập từ khóa tìm kiếm theo tên/mã nguyên liệu;
> bộ lọc theo trạng thái tồn kho (Tất cả / Sắp hết / Hết hàng),
> kho (nếu có nhiều kho) và khoảng thời gian khi xem lịch sử biến động kho; chọn nguyên liệu để xem chi tiết.

**Kết quả đầu ra:**

> Danh sách nguyên liệu và số lượng tồn hiện tại; đơn vị đo; thời điểm cập nhật gần nhất; lịch sử biến động của nguyên liệu được chọn.

**Quy tắc nghiệp vụ:**

> Số lượng tồn chỉ được tính từ các giao dịch kho đã ghi nhận thành công. Các số lượng phải được quy đổi về đơn vị cơ sở của nguyên liệu. Manager chỉ được xem dữ liệu trong phạm vi được phân quyền. Nếu dữ liệu chưa đồng bộ, hệ thống phải hiển thị thời điểm cập nhật gần nhất, không trình bày số liệu cũ như dữ liệu mới.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_08"></a>

### UC_MGR_08 — Theo dõi, kiểm soát hao hụt nguyên liệu

Service đề xuất: **Inventory + Ordering + Catalog**.

**Module:**

> Inventory Control

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> So sánh tiêu hao thực tế với định mức, kiểm kê và lượng xuất hủy để phát hiện chênh lệch.

**Dữ liệu đầu vào:**

> bộ lọc: khoảng thời gian (từ ngày – đến ngày), nguyên liệu, kho (nếu có nhiều kho), trạng thái chênh lệch (Tất cả / Có chênh lệch / Không chênh lệch).
>

**Kết quả đầu ra:**

> Báo cáo lượng nguyên liệu dự kiến sử dụng, lượng thực tế ghi nhận, số lượng và tỷ lệ chênh lệch; danh sách nguyên liệu cần kiểm tra.

**Quy tắc nghiệp vụ:**

> Dữ liệu so sánh phải thuộc cùng khoảng thời gian và được quy đổi về cùng đơn vị đo. Phải phân biệt hao hụt thông thường đã tính trong BOM với hao hụt phát sinh ngoài dự kiến. Không tính trùng lượng nguyên liệu xuất hủy với lượng đã ghi nhận trong các giao dịch khác. Báo cáo chỉ giúp phát hiện chênh lệch, không tự động sửa số lượng tồn kho.

**Ghi chú:**

> Hệ thống tính lượng nguyên liệu dự kiến theo công thức BOM và số món đã chế biến, sau đó so sánh với lượng sử dụng thực tế được xác định từ dữ liệu xuất kho và kiểm kê. Tỷ lệ chênh lệch = (Lượng thực tế − Lượng dự kiến) / Lượng dự kiến × 100%, chỉ tính khi lượng dự kiến lớn hơn 0. Báo cáo chỉ cảnh báo chênh lệch, không tự kết luận nguyên nhân hao hụt.

<a id="uc_mgr_09"></a>

### UC_MGR_09 — Theo dõi công nợ và tuổi nợ NCC

Service đề xuất: **Chưa chốt**.

**Module:**

> Supplier Payables

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Tổng hợp số tiền phải trả, đã trả và quá hạn theo NCC tại ngày báo cáo.

**Dữ liệu đầu vào:**

> Manager tìm kiếm/lọc: Tên hoặc mã nhà cung cấp; khoảng thời gian; trạng thái công nợ (Tất cả / Chưa đến hạn / Quá hạn / Đã thanh toán); ngày chốt báo cáo.

**Kết quả đầu ra:**

> Danh sách công nợ theo nhà cung cấp; tổng tiền phải trả, số tiền đã thanh toán, số tiền còn nợ; ngày đến hạn; số ngày quá hạn và báo cáo nhóm tuổi nợ.

**Quy tắc nghiệp vụ:**

> Chỉ ghi nhận công nợ khi giao dịch mua hàng đã phát sinh nghĩa vụ thanh toán theo quy trình đã chốt; không tính đơn mua hàng (PO) mới tạo là nợ thực tế. Số tiền còn nợ = Số tiền phải trả − Số tiền đã thanh toán và các khoản giảm trừ hợp lệ. Số ngày quá hạn được tính dựa trên ngày đến hạn và ngày chốt báo cáo. Một khoản thanh toán không được ghi nhận trùng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_12"></a>

### UC_MGR_12 — Xem sơ đồ bàn và trạng thái phục vụ

Service đề xuất: **Restaurant**.

**Module:**

> Restaurant Operations

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> xem sơ đồ bàn theo khu vực, theo dõi trạng thái từng bàn và thông tin phiên phục vụ để nắm tình hình hoạt động của nhà hàng.

**Dữ liệu đầu vào:**

> bộ lọc: khu vực (Tất cả / Tầng 1 / Tầng 2… nếu có), trạng thái bàn (Tất cả / Bàn trống / Đang phục vụ / Chờ thanh toán); chọn một bàn để xem chi tiết.
>

**Kết quả đầu ra:**

> Sơ đồ bàn theo khu vực; trạng thái từng bàn; thông tin chi tiết của bàn được chọn như mã bàn, đơn hàng đang phục vụ và trạng thái thanh toán (nếu có).

**Quy tắc nghiệp vụ:**

> Trạng thái bàn phải được lấy từ dữ liệu vận hành của hệ thống, không tự suy đoán. Khi bàn đổi trạng thái, sơ đồ phải được cập nhật; nếu chưa đồng bộ kịp thời, phải hiển thị thời điểm cập nhật gần nhất. Manager chỉ được xem thông tin trong phạm vi quyền được cấp.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_13"></a>

### UC_MGR_13 — Giám sát tiến độ chế biến tại KDS

Service đề xuất: **Ordering**.

**Module:**

> Kitchen Monitoring

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> theo dõi tiến độ chế biến các món ăn, đồ uống tại bếp hoặc quầy pha chế để biết món nào đang chờ làm, đang chế biến, đã hoàn thành hoặc bị chậm.

**Dữ liệu đầu vào:**

> Manager tìm kiếm/lọc: Mã đơn hàng hoặc mã bàn; khoảng thời gian (từ ngày – đến ngày); trạng thái món (Tất cả / Chờ chế biến / Đang chế biến / Đã hoàn thành); chọn đơn hoặc món để xem chi tiết.
>

**Kết quả đầu ra:**

> Danh sách đơn hàng và món ăn theo bộ lọc; trạng thái từng món; thời gian chờ hoặc chế biến; cảnh báo món chậm nếu vượt ngưỡng đã thiết lập.

**Quy tắc nghiệp vụ:**

> Trạng thái món phải phản ánh dữ liệu thực tế từ KDS. Một đơn có nhiều món có thể có các trạng thái khác nhau, không được coi cả đơn đã hoàn thành chỉ vì một món đã xong. Manager chỉ được xem dữ liệu theo quyền; UC này không tự cho phép Manager thay đổi trạng thái chế biến.

**Ghi chú:**

> cảnh báo khi món ở trạng thái Chờ chế biến quá 15 phút. Nhân viên bếp chịu trách nhiệm cập nhật trạng thái Chờ chế biến → Đang chế biến → Đã hoàn thành trên KDS; Manager chỉ xem tiến độ và cảnh báo.

<a id="uc_mgr_14"></a>

### UC_MGR_14 — Kiểm tra lịch sử thao tác nhân viên

Service đề xuất: **Identity + các service**.

**Module:**

> Staff Activity

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> tra cứu lịch sử các thao tác của nhân viên như mở bàn, gộp bàn, hủy món và giảm giá để kiểm tra hoạt động của nhà hàng.

**Dữ liệu đầu vào:**

> Manager tìm kiếm/lọc: Tên hoặc mã nhân viên; khoảng thời gian (từ ngày – đến ngày) hoặc ca làm; mã bàn hoặc mã đơn hàng; loại thao tác (Tất cả / Mở bàn / Gộp bàn / Hủy món / Giảm giá).
> Manager chọn một bản ghi để xem chi tiết.
>

**Kết quả đầu ra:**

> Danh sách lịch sử thao tác theo bộ lọc; thông tin người thực hiện, thời điểm, loại thao tác, bàn/đơn liên quan, lý do và nội dung thay đổi (nếu có).

**Quy tắc nghiệp vụ:**

> Hệ thống tự ghi nhận người thực hiện và thời điểm thao tác. Manager chỉ được xem lịch sử trong phạm vi được phân quyền và không được chỉnh sửa hoặc xóa lịch sử ngay trên màn hình tra cứu. Thông tin nhạy cảm phải được che hoặc giới hạn quyền xem.

**Ghi chú:**

> Đề xuất lưu lịch sử thao tác tối thiểu 12 tháng. Nhân viên bắt buộc nhập lý do khi hủy món hoặc giảm giá thủ công. Hệ thống tự ghi nhận các thao tác đã triển khai, bao gồm người thực hiện, thời điểm và nội dung thay đổi.

<a id="uc_mgr_15"></a>

### UC_MGR_15 — Báo cáo doanh thu đa chiều

Service đề xuất: **Billing**.

**Module:**

> Revenue Reporting

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Xem và phân tích doanh thu theo thời gian, ca làm, kênh bán và phương thức thanh toán.

**Dữ liệu đầu vào:**

> bộ lọc: khoảng thời gian (từ ngày – đến ngày), ca làm (Tất cả / Ca sáng / Ca chiều / Ca tối, nếu có), phương thức thanh toán (Tất cả / Tiền mặt / Chuyển khoản hoặc PayOS), trạng thái thanh toán (Tất cả / Đã thanh toán / Đã hoàn tiền, nếu cần).
>

**Kết quả đầu ra:**

> Tổng doanh thu trong kỳ; số đơn đã thanh toán; số tiền giảm giá và hoàn tiền; doanh thu theo ngày, ca làm và phương thức thanh toán; bảng hoặc biểu đồ chi tiết theo bộ lọc.

**Quy tắc nghiệp vụ:**

> Báo cáo phải xác định rõ cách tính doanh thu và phân biệt với số tiền thực thu. Đơn bị hủy chưa phát sinh thanh toán không được tính vào doanh thu đã thanh toán. Khoản giảm giá và hoàn tiền phải được xử lý nhất quán, không trừ hai lần. Một đơn thanh toán bằng nhiều phương thức chỉ được tính một lần vào tổng doanh thu; số tiền được phân bổ đúng cho từng phương thức thanh toán.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_mgr_16"></a>

### UC_MGR_16 — Phân tích hiệu quả thực đơn

Service đề xuất: **Billing + Ordering + Catalog + Inventory**.

**Module:**

> Menu Analytics

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> xem và phân tích hiệu quả kinh doanh của từng món dựa trên số lượng bán và lợi nhuận đóng góp từ món để hỗ trợ điều chỉnh thực đơn.

**Dữ liệu đầu vào:**

> bộ lọc: khoảng thời gian (từ ngày – đến ngày), nhóm món (Tất cả / Đồ ăn / Đồ uống hoặc các nhóm đã tạo), tên hoặc mã món.
>

**Kết quả đầu ra:**

> Danh sách món kèm số lượng bán, doanh thu, giá vốn và lợi nhuận đóng góp; biểu đồ phân tích theo mức độ bán chạy và lợi nhuận đóng góp trên mỗi phần món; nhóm món cần xem xét.

**Quy tắc nghiệp vụ:**

> Chỉ tính các món đã bán hợp lệ, không tính món bị hủy; món hoàn tiền phải được xử lý thống nhất với báo cáo doanh thu. So sánh các món trong cùng khoảng thời gian và nhóm món. Lợi nhuận đóng góp mỗi phần = doanh thu bình quân mỗi phần − giá vốn nguyên liệu mỗi phần. Nếu thiếu giá vốn, hệ thống phải cảnh báo. Món bán chạy chưa chắc có lợi nhuận cao.

**Ghi chú:**

> Hệ thống chia món thành 4 nhóm dựa trên số lượng bán và lợi nhuận mỗi phần: bán chạy/lợi nhuận cao, bán chạy/lợi nhuận thấp, bán chậm/lợi nhuận cao, bán chậm/lợi nhuận thấp. Đề xuất xác định mức cao/thấp bằng cách so sánh các món trong cùng nhóm và cùng kỳ báo cáo. Chức năng này tính toán từ dữ liệu bán hàng, không bắt buộc sử dụng AI.

<a id="uc_mgr_17"></a>

### UC_MGR_17 — Báo cáo Chi phí & Lợi nhuận

Service đề xuất: **Billing + Inventory**.

**Module:**

> Profitability Reporting

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager xem báo cáo doanh thu, giá vốn nguyên liệu của các món đã bán và tác động của khuyến mãi để biết nhà hàng còn bao nhiêu lợi nhuận gộp trong kỳ.

**Dữ liệu đầu vào:**

> bộ lọc: khoảng thời gian (từ ngày – đến ngày), nhóm món (nếu muốn xem theo nhóm), tên hoặc mã món (nếu muốn xem từng món).
>

**Kết quả đầu ra:**

> Báo cáo doanh thu thuần, tổng giá vốn nguyên liệu (COGS), giá trị khuyến mãi, lợi nhuận gộp và tỷ suất lợi nhuận gộp; bảng hoặc biểu đồ chi tiết theo bộ lọc.

**Quy tắc nghiệp vụ:**

> Chỉ sử dụng các giao dịch hợp lệ trong kỳ. Khoản giảm giá đã trừ khỏi doanh thu thuần không được trừ thêm lần nữa khi tính lợi nhuận. Giá vốn của món tặng chỉ được tính một lần. Nếu thiếu dữ liệu giá vốn, hệ thống phải cảnh báo thay vì coi giá vốn bằng 0.

**Ghi chú:**

> Lợi nhuận gộp = Doanh thu thuần − Giá vốn nguyên liệu của các món đã bán. Báo cáo này chưa tính các chi phí như lương nhân viên, tiền thuê mặt bằng hoặc điện nước, nên lợi nhuận gộp không phải lợi nhuận ròng.

<a id="uc_mgr_18"></a>

### UC_MGR_18 — Cấu hình quy tắc khuyến mãi

Service đề xuất: **Billing**.

**Module:**

> Promotion Management

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> tạo, xem, cập nhật, bật/tắt các chương trình khuyến mãi. Hệ thống tự kiểm tra điều kiện và áp dụng ưu đãi khi khách đặt món hoặc thanh toán.

**Dữ liệu đầu vào:**

> Tìm kiếm/lọc: Tên chương trình, trạng thái (Tất cả / Sắp diễn ra / Đang áp dụng / Đã kết thúc).
> Tạo/cập nhật: Tên chương trình; loại khuyến mãi (Giảm theo % / Giảm số tiền / Mua A tặng B / Combo); mức giảm hoặc món tặng; món/nhóm món áp dụng; điều kiện áp dụng (nếu có); thời gian bắt đầu và kết thúc; trạng thái bật/tắt.

**Kết quả đầu ra:**

> Danh sách chương trình khuyến mãi; thông tin và trạng thái chương trình được cập nhật; ưu đãi được hệ thống tính khi đơn hàng đáp ứng điều kiện.

**Quy tắc nghiệp vụ:**

> Backend xác minh điều kiện; giảm không làm giá trị thanh toán âm; không áp dụng ngoài hiệu lực.

**Ghi chú:**

> Không cộng dồn nhiều chương trình khuyến mãi cho cùng một món; nếu món đủ điều kiện nhận nhiều ưu đãi thì áp dụng chương trình giảm được nhiều tiền nhất. Giá theo khung giờ tại UC_MGR_02 là giá bán dùng làm cơ sở tính khuyến mãi. Nếu món tặng hết hàng, hệ thống không áp dụng chương trình tặng món đó và thông báo cho khách.

<a id="uc_mgr_19"></a>

### UC_MGR_19 — Phát hành, quản lý Voucher/Coupon

Service đề xuất: **Billing**.

**Module:**

> Voucher & Coupon

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> tạo mã giảm giá, thiết lập điều kiện sử dụng, bật/tắt mã và theo dõi số lượt khách đã sử dụng. Khách nhập mã khi đặt món hoặc thanh toán để nhận ưu đãi nếu đủ điều kiện.

**Dữ liệu đầu vào:**

> Tìm kiếm/lọc: Mã voucher, trạng thái (Tất cả / Sắp có hiệu lực / Đang áp dụng / Hết hạn / Đã tắt).
> Tạo/cập nhật: Mã voucher hoặc số lượng mã cần tạo; loại giảm giá (% hoặc số tiền); mức giảm; giá trị đơn hàng tối thiểu (nếu có); thời gian bắt đầu và kết thúc; tổng số lượt sử dụng tối đa; trạng thái bật/tắt.

**Kết quả đầu ra:**

> Danh sách voucher và thông tin chi tiết; mã được tạo hoặc cập nhật; số lượt đã sử dụng và số lượt còn lại; kết quả áp dụng mã trên đơn hàng của khách.

**Quy tắc nghiệp vụ:**

> Mã voucher không được trùng. Chỉ áp dụng mã đang bật, còn hạn, chưa hết lượt và đáp ứng điều kiện đơn hàng. Backend phải kiểm tra lại điều kiện khi khách xác nhận sử dụng mã. Giá trị giảm không được làm số tiền thanh toán âm. Một lượt sử dụng chỉ được ghi nhận một lần; các lượt sử dụng đồng thời không được vượt quá giới hạn.

**Ghi chú:**

> Mỗi đơn chỉ áp dụng một ưu đãi giữa voucher và chương trình khuyến mãi; nếu đủ điều kiện nhiều ưu đãi thì chọn mức giảm lớn nhất. Voucher chỉ được tính một lượt khi thanh toán thành công; đơn hủy hoặc thanh toán thất bại không làm mất lượt.

<a id="uc_mgr_20"></a>

### UC_MGR_20 — Thiết lập hạn mức giảm giá thủ công

Service đề xuất: **Billing + Identity**.

**Module:**

> Discount Authorization

**Vai trò:**

> Restaurant Manager

**Mô tả nghiệp vụ:**

> Manager quy định mức giảm tối đa mà Thu ngân/Phục vụ được phép thực hiện.

**Dữ liệu đầu vào:**

> Tìm kiếm/lọc: Tên hoặc mã nhân viên, vai trò. Thiết lập hạn mức:
> Chọn vai trò hoặc tài khoản nhân viên; chọn hình thức giới hạn (theo % hoặc số tiền); nhập mức giảm tối đa; chọn phạm vi áp dụng (theo món hoặc toàn đơn); thời điểm bắt đầu áp dụng (nếu có).

**Kết quả đầu ra:**

> Hạn mức giảm giá được lưu và hiển thị theo vai trò hoặc tài khoản. Khi nhân viên giảm giá thủ công, hệ thống kiểm tra và thông báo thao tác có nằm trong hạn mức được phép hay không.

**Quy tắc nghiệp vụ:**

> Chỉ Manager có quyền mới được thiết lập hạn mức. Nhân viên không được tự thay đổi hoặc vượt hạn mức của mình. Backend phải kiểm tra quyền và mức giảm khi nhân viên thực hiện thao tác. Số tiền sau giảm không được âm. Mọi lần giảm giá thủ công phải ghi nhận người thực hiện, thời điểm, mức giảm và lý do.

**Ghi chú:**

> Chỉ cấp quyền giảm giá thủ công cho Thu ngân; nhân viên Phục vụ không được giảm giá nếu chưa được cấp quyền. Hạn mức áp dụng trên toàn đơn.

## Waiter — 10 chức năng

Nguồn: 03-waiter.txt (bản gốc local, không đưa lên Git).

<a id="uc_wai_01"></a>

### UC_WAI_01 — Đăng nhập, đăng xuất

Service đề xuất: **Identity**.

**Module:**

> Authentication

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Đăng nhập/Đăng xuất ứng dụng Phục vụ trên thiết bị di động.

**Dữ liệu đầu vào:**

> Email/Phone/Username, Password

**Kết quả đầu ra:**

> JWT Token, Thông tin Waiter

**Quy tắc nghiệp vụ:**

> Tài khoản phải active và đúng role.
> Không đăng nhập 1 tài khoản trên 2 thiết bị cùng lúc.

**Ghi chú:**

> Mật khẩu mã hóa SSL/TLS

<a id="uc_wai_02"></a>

### UC_WAI_02 — Xem sơ đồ bàn

Service đề xuất: **Restaurant**.

**Module:**

> Table Service

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Xem sơ đồ bàn trực quan thời gian thực (Bàn trống, bàn có khách, bàn chờ trả món, bàn cần dọn).

**Dữ liệu đầu vào:**

> Bộ lọc: Khu vực (Zone ID)

**Kết quả đầu ra:**

> Danh sách bàn kèm Trạng thái (Status Code: 0-Trống, 1-Có khách, 2-Chờ trả món, 3-Cần dọn)

**Quy tắc nghiệp vụ:**

> Tự động cập nhật real-time mỗi 3–5 giây.

**Ghi chú:**

> Màu sắc tương ứng trạng thái

<a id="uc_wai_03"></a>

### UC_WAI_03 — Xác nhận đơn trước khi chuyển bếp

Service đề xuất: **Ordering**.

**Module:**

> Ordering

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Phục vụ xem đơn khách gửi, kiểm tra bàn, món, số lượng và ghi chú; xác nhận để chuyển đơn đến Bếp và Thu ngân.

**Dữ liệu đầu vào:**

> OrderId, phiên bản dữ liệu đơn (Version); thông tin Waiter xác nhận và thời điểm xác nhận

**Kết quả đầu ra:**

> Đơn được xác nhận; lưu Waiter/thời điểm xác nhận, chuyển đơn đến bếp để chế biến

**Quy tắc nghiệp vụ:**

> Chỉ xác nhận đơn đang chờ và phiên bàn còn mở. Chưa xác nhận thì chưa tạo task bếp, chưa chuyển sang tính tiền. Mỗi đơn gọi thêm phải được xác nhận riêng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_wai_04"></a>

### UC_WAI_04 — Nhận thông báo món sẵn sàng

Service đề xuất: **Ordering**.

**Module:**

> Notifications

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Nhận thông báo tức thì (Push notification/Rung) khi Bếp bấm hoàn thành món (Ready to serve).

**Dữ liệu đầu vào:**

> Event Payload từ Bếp: TableID, ItemID, Quantity, Note

**Kết quả đầu ra:**

> Push Notification, Rung/Chuông cảnh báo trên App

**Quy tắc nghiệp vụ:**

> Lặp lại rung/chuông mỗi 30s nếu chưa xem.
> Thông báo hiển thị chi tiết món + số bàn.

**Ghi chú:**

> Bật quyền Notification

<a id="uc_wai_05"></a>

### UC_WAI_05 — Xác nhận đã lên món

Service đề xuất: **Ordering**.

**Module:**

> Table Service

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Xác nhận trạng thái "Đã lên món" (Served) trên hệ thống sau khi bê món ra cho khách.

**Dữ liệu đầu vào:**

> Món đã mang ra cho khách (OrderItemID, QuantityServed, WaiterID)

**Kết quả đầu ra:**

> Cập nhật Status: Served cho OrderItem

**Quy tắc nghiệp vụ:**

> Chỉ xác nhận Served sau khi đã mang món ra cho khách.
> Chỉ áp dụng cho món có trạng thái Ready.
> Cho phép xác nhận lên từng phần (Partial Served).

**Ghi chú:**

> Cập nhật DB tức thì

<a id="uc_wai_06"></a>

### UC_WAI_06 — Tạo, điều chỉnh order

Service đề xuất: **Ordering**.

**Module:**

> Tableside Ordering

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Hỗ trợ khách order trực tiếp tại bàn hoặc điều chỉnh order (Thêm/bớt món, thêm ghi chú) khi có yêu cầu.

**Dữ liệu đầu vào:**

> TableID, Danh sách ItemID, Số lượng, Notes, ReasonCancel

**Kết quả đầu ra:**

> OrderRecord mới/cập nhật, Print/Display Command xuống Bếp (KDS)

**Quy tắc nghiệp vụ:**

> Tạo/Thêm món: Tự động gửi lệnh chế biến xuống Bếp.
> Hủy món CHƯA LÀM (Trạng thái Pending/Sent): Cho phép Waiter hủy trực tiếp, hệ thống gửi lệnh hủy xuống Bếp.
> Hủy món ĐÃ/ĐANG LÀM (Trạng thái Cooking/Ready): Tuyệt đối KHÔNG CHO HỦY. Nút "Hủy món" bị vô hiệu hóa (disabled) trên App Phục vụ.

**Ghi chú:**

> Đồng bộ ngay với Bếp/KDS

<a id="uc_wai_07"></a>

### UC_WAI_07 — Đổi bàn cho khách

Service đề xuất: **Restaurant + Ordering**.

**Module:**

> Table Service

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Thực hiện thao tác đổi bàn cho khách

**Dữ liệu đầu vào:**

> Bàn hiện tại, bàn muốn chuyển (CurrentTableID, TargetTableID, ReasonNote)

**Kết quả đầu ra:**

> OrderRecord được cập nhật TableID mới, TargetTable status = Occupied

**Quy tắc nghiệp vụ:**

> TargetTable phải là bàn "Trống".
> Không đổi bàn khi đã in Hóa đơn tạm tính.

**Ghi chú:**

> Lưu Log đổi bàn

<a id="uc_wai_08"></a>

### UC_WAI_08 — Thu hộ tiền mặt

Service đề xuất: **Billing**.

**Module:**

> Payment

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Waiter khai báo đã nhận tiền mặt từ khách trên App (Mức 1), sau đó cầm tiền giao ngay cho Thu ngân để duyệt thành công (Mức 2).

**Dữ liệu đầu vào:**

> OrderID, TotalAmount, CashReceived (Mức 1); Thao tác Thu ngân bấm duyệt (Mức 2)

**Kết quả đầu ra:**

> Trạng thái đơn: Pending_Settlement (Sau Mức 1) -> Paid (Sau Mức 2); Hóa đơn hoàn tất

**Quy tắc nghiệp vụ:**

> Khai báo Mức 1 (Waiter): Ngay khi cầm tiền từ khách, Waiter nhập số tiền khách đưa lên App, Hệ thống hiển thị tiền thối (nếu có) và chuyển trạng thái đơn sang Chờ nộp tiền (Pending_Settlement).
> Xác nhận Mức 2 (Cashier): Waiter cầm tiền mặt giao liền cho Cashier. Cashier đếm tiền và bấm "Xác nhận đã nhận", Đơn hàng chuyển sang Đã thanh toán (Paid).
> Ràng buộc giao nhận: Đơn hàng chỉ hoàn tất và đóng lại sau khi Thu ngân hoàn thành Mức 2.

**Ghi chú:**

> Luồng giao tiền trực tiếp, không tích lũy công nợ theo ca

<a id="uc_wai_09"></a>

### UC_WAI_09 — Xử lý yêu cầu từ QR Menu

Service đề xuất: **Restaurant**.

**Module:**

> Service Requests

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Nhận và xử lý tín hiệu gọi phục vụ từ QR Menu của khách (Lấy thêm đá, đũa, nước sốt, gọi thanh toán).

**Dữ liệu đầu vào:**

> Yêu cầu thêm đá/đũa/nước sốt/gọi thanh toán
> (RequestID, TableID, RequestType)

**Kết quả đầu ra:**

> Yêu cầu được tiếp nhận và xử lý

**Quy tắc nghiệp vụ:**

> Xử lý theo cơ chế FIFO.
> Cảnh báo Manager nếu trễ quá 3 phút.

**Ghi chú:**

> Tự động ẩn khi Done

<a id="uc_wai_10"></a>

### UC_WAI_10 — Duyệt yêu cầu đặt bàn

Service đề xuất: **Restaurant**.

**Module:**

> Table Service

**Vai trò:**

> Waiter

**Mô tả nghiệp vụ:**

> Tiếp nhận, xác nhận hoặc từ chối các yêu cầu đặt bàn trước của khách từ App/Website.

**Dữ liệu đầu vào:**

> ReservationID, TableID, CustomerInfo, TimeSlot, PaxCount

**Kết quả đầu ra:**

> Trạng thái Đặt bàn: Confirmed / Rejected, Cập nhật Sơ đồ bàn.

**Quy tắc nghiệp vụ:**

> chỉ cho phép duyệt vào bàn trống trong khung giờ; tự động giữ chỗ (Lock/Reserved) trước giờ hẹn 30 phút.

**Ghi chú:**

> Gửi thông báo/tin nhắn xác nhận về cho khách hàng.

## Cashier — 10 chức năng

Nguồn: 04-cashier.txt (bản gốc local, không đưa lên Git).

<a id="uc_cas_01"></a>

### UC_CAS_01 — Đăng nhập, đăng xuất, bàn giao ca

Service đề xuất: **Identity + Billing**.

**Module:**

> Shift Management

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Đăng nhập/Đăng xuất quầy thu ngân, thực hiện thao tác bàn giao ca (Shift Handover).

**Dữ liệu đầu vào:**

> Email/Phone/UserName, Password, ShiftID, CashOnHand (Tiền giao)

**Kết quả đầu ra:**

> Phiên quầy thu ngân và kết quả bàn giao ca
> (Shift_Log, Session_Status (Active/Closed))

**Quy tắc nghiệp vụ:**

> Bắt buộc hoàn tất Z-Report ca trước (UC_CAS_09) mới cho phép bàn giao.
> 1 quầy chỉ mở 1 phiên làm việc active tại một thời điểm.

**Ghi chú:**

> Mật khẩu mã hóa SSL/TLS

<a id="uc_cas_02"></a>

### UC_CAS_02 — Kiểm tra đơn trước thanh toán

Service đề xuất: **Billing + Ordering**.

**Module:**

> Payment

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Chọn bàn/đơn hàng, kiểm tra danh sách món ăn, số lượng và tổng chi phí.

**Dữ liệu đầu vào:**

> TableID hoặc OrderID

**Kết quả đầu ra:**

> OrderDetails (Danh sách món, Số lượng, Giá, Trạng thái món, Tạm tính)

**Quy tắc nghiệp vụ:**

> Chỉ chọn được các đơn chưa đóng thanh toán (Unpaid hoặc Pending_Settlement).
> Cảnh báo nếu còn món Bếp chưa chế biến xong (In-Progress).

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cas_03"></a>

### UC_CAS_03 — Áp dụng ưu đãi thanh toán

Service đề xuất: **Billing + Customer**.

**Module:**

> Promotions

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Áp dụng mã giảm giá, voucher, điểm thưởng tích lũy hoặc giảm giá thủ công.

**Dữ liệu đầu vào:**

> VoucherCode, CustomerPhone (tích/đổi điểm), DiscountPercent/Amount

**Kết quả đầu ra:**

> DiscountAmount, FinalTotalAmount (Tổng tiền sau giảm)

**Quy tắc nghiệp vụ:**

> Voucher/Code: Hệ thống tự validate hạn dùng, điều kiện áp dụng.
> Tích/Đổi điểm: Bắt buộc nhập SDT Khách hàng thành viên.
> Không cộng dồn 2 Voucher trên cùng 1 đơn (trừ khi có cấu hình cho phép).

**Ghi chú:**

> Đồng bộ cổng Voucher từ Manager

<a id="uc_cas_04"></a>

### UC_CAS_04 — Thanh toán tiền mặt, chuyển khoản

Service đề xuất: **Billing**.

**Module:**

> Payment

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Thực hiện thanh toán đa dạng: Tiền mặt, chuyển khoản

**Dữ liệu đầu vào:**

> Đơn; phương thức; thông tin thanh toán (OrderID, PaymentMethod (Cash/BankTransfer), CashReceived, BankTransactionID)

**Kết quả đầu ra:**

> Kết quả thanh toán (PaymentStatus = Paid, ChangeAmount (Tiền thừa), PaymentReceipt)

**Quy tắc nghiệp vụ:**

> Tiền mặt: Tiền khách đưa >=Tổng tiền đơn.
> Xác nhận từ Waiter (2 bước): Duyệt các đơn Waiter thu hộ (Mức 1) chuyển sang Paid (Mức 2).
> Chuyển khoản: Tự động bắt webhook từ Ngân hàng/VietQR hoặc Cashier xác nhận thủ công nếu có Bill.

**Ghi chú:**

> Dùng PayOS

<a id="uc_cas_05"></a>

### UC_CAS_05 — Tách, gộp hóa đơn

Service đề xuất: **Billing + Ordering + Restaurant**.

**Module:**

> Payment

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Thực hiện thanh toán gộp nhiều bàn cùng lúc, tách hóa đơn riêng lẻ, hoặc chuyển món ăn từ bàn/hóa đơn này sang bàn/hóa đơn khác.

**Dữ liệu đầu vào:**

> SourceTableIDs (Danh sách bàn chọn gộp), SourceOrderID, TargetOrderID, ItemID_List (Danh sách món chuyển)

**Kết quả đầu ra:**

> Hóa đơn gộp tạm thời, hoặc các Hóa đơn mới/cập nhật sau khi tách/chuyển món

**Quy tắc nghiệp vụ:**

> 1. Thanh toán gộp nhiều bàn (VD: Bàn 1 + Bàn 2):
> * Cho phép chọn nhiều bàn cùng lúc trên màn hình để tạo một lệnh Thanh toán gộp.
> * Hệ thống gom toàn bộ món của các bàn được chọn vào 1 hóa đơn tổng mà không làm mất thông tin bàn gốc của từng món.
> * Sau khi thanh toán gộp thành công, tất cả các bàn trong nhóm gộp đều đồng thời chuyển sang trạng thái Trống/Cần dọn.
> 2. Tách món / Chuyển món giữa các hóa đơn:
> * Lựa chọn thao tác: Chọn Bàn A -> Chọn món $X, Y$ -> Bấm "Chuyển món" sang Bàn B (đã có khách) hoặc Tạo Hóa đơn mới (Bàn A-2).
> * Ràng buộc: Chỉ cho phép chuyển các món chưa thanh toán (Unpaid). Món đang áp dụng Ưu đãi/Combo nguyên cụm bắt buộc phải chuyển toàn bộ Combo.
> * Cập nhật lại giá: Hệ thống tự động tính lại Tổng tiền, Thuế VAT và Tiền giảm giá (nếu có) trên cả 2 hóa đơn ngay sau khi hoàn tất chuyển món.

**Ghi chú:**

> Hỗ trợ linh hoạt cho khách đi nhóm đông ngồi nhiều bàn hoặc tách bill lẻ

<a id="uc_cas_06"></a>

### UC_CAS_06 — In tạm tính, hóa đơn thanh toán

Service đề xuất: **Billing**.

**Module:**

> Billing

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> In hóa đơn tạm tính (Pre-bill) và hóa đơn thanh toán (Final Receipt).

**Dữ liệu đầu vào:**

> Thông tin đơn, loại hóa đơn (OrderID, (Pre-bill / Final-Receipt))

**Kết quả đầu ra:**

> Pre-bill hoặc Final Receipt được in

**Quy tắc nghiệp vụ:**

> Pre-bill: Không làm đổi trạng thái đơn, dùng để đưa khách kiểm tra.
> Final Receipt: Chỉ in được sau khi trạng thái đơn chuyển sang Paid.
> In lại hóa đơn (Reprint) phải ghi rõ chữ "IN LẠI" trên Bill.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cas_07"></a>

### UC_CAS_07 — Xuất hóa đơn điện tử VAT

Service đề xuất: **Billing**.

**Module:**

> Billing

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Nhập thông tin doanh nghiệp (Tên công ty, MST, Email) để xuất Hóa đơn điện tử (VAT) cho khách.

**Dữ liệu đầu vào:**

> Tên công ty, MST, Email, dữ liệu hóa đơn

**Kết quả đầu ra:**

> Hóa đơn điện tử VAT cho khách

**Quy tắc nghiệp vụ:**

> Chỉ xuất cho hóa đơn đã thanh toán (Paid) trong ngày.
> Tự động tra cứu tên Cty/Địa chỉ khi nhập đúng MST qua API Tổng cục Thuế.
> Đơn hàng phải có giá trị >0.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cas_08"></a>

### UC_CAS_08 — Ghi nhận tiền quỹ trong ca

Service đề xuất: **Billing**.

**Module:**

> Shift Management

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Khai báo tiền quỹ đầu ca, ghi nhận tiền mặt thu trong ca và số tiền nộp lại cuối ca.

**Dữ liệu đầu vào:**

> Tiền đầu ca, tiền mặt thu; tiền nộp cuối ca

**Kết quả đầu ra:**

> Sổ tiền quỹ theo ca

**Quy tắc nghiệp vụ:**

> Quỹ đầu ca phải xác nhận đúng số tiền nhận từ ca trước.
> Mọi khoản thu/chi phát sinh ngoài tiền bán hàng (VD: Rút quỹ mua đồ) phải ghi lý do.
> Tự động tổng hợp dữ liệu từ các giao dịch Tiền mặt trong ca.

**Ghi chú:**

> Khóa chỉnh sửa khi chốt ca

<a id="uc_cas_09"></a>

### UC_CAS_09 — Đối soát, chốt Z-Report

Service đề xuất: **Billing**.

**Module:**

> Shift Management

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Kiểm đếm tiền mặt thực tế, đối soát lệch tiền mặt/chuyển khoản và chốt báo cáo doanh thu ca (Z-Report).

**Dữ liệu đầu vào:**

> Tiền thực tế, giao dịch tiền mặt/chuyển khoản, Lý do lệch nếu có

**Kết quả đầu ra:**

> Chênh lệch đối soát và báo cáo doanh thu ca

**Quy tắc nghiệp vụ:**

> Nhập số tiền mặt thực đếm -> Hệ thống tính chênh lệch (System_Cash vs Actual_Cash).
> Nếu lệch tiền >0, bắt buộc nhập Lý do giải trình.
> Báo cáo Z-Report được tự động gửi cho Manager.

**Ghi chú:**

> Đóng phiên làm việc trên thiết bị

<a id="uc_cas_10"></a>

### UC_CAS_10 — Tra cứu & Tích điểm qua SĐT

Service đề xuất: **Customer + Billing**.

**Module:**

> Customer Loyalty

**Vai trò:**

> Cashier

**Mô tả nghiệp vụ:**

> Tìm kiếm thông tin khách hàng bằng SĐT để tích điểm sau thanh toán hoặc đăng ký mới nếu chưa có trong hệ thống.

**Dữ liệu đầu vào:**

> CustomerPhone, CustomerName (nếu đăng ký mới), OrderID, FinalAmount

**Kết quả đầu ra:**

> CustomerProfile (Tên, Hạng thẻ, Điểm tích lũy), PointTransaction (Bản ghi cộng điểm)

**Quy tắc nghiệp vụ:**

> Tìm kiếm: Nhập SĐT, Hệ thống hiển thị Tên, Hạng thành viên, Số điểm hiện có.
> Tạo mới: Nếu SĐT chưa tồn tại, hệ thống cho phép tạo nhanh thông tin khách hàng mới ngay tại màn hình.
> Tỷ lệ tích điểm: Điểm được cộng dựa trên Số tiền thanh toán thực tế (sau khi trừ chiết khấu/voucher) theo công thức cấu hình (VD: 100.000 VNĐ = 1 điểm).
> Thời điểm cộng điểm: Điểm chỉ chính thức cộng vào tài khoản khách sau khi đơn hàng chuyển trạng thái Paid.

**Ghi chú:**

> (Trống trong nguồn.)

## Kitchen Staff — 13 chức năng

Nguồn: 05-kitchen.txt (bản gốc local, không đưa lên Git).

<a id="uc_kit_01"></a>

### UC_KIT_01 — Đăng nhập / Đăng xuất KDS

Service đề xuất: **Identity**.

**Module:**

> Authentication

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Nhân viên bếp/bar đăng nhập vào giao diện KDS và đăng xuất khi không còn sử dụng thiết bị.

**Dữ liệu đầu vào:**

> Email/Phone/Username, Password

**Kết quả đầu ra:**

> Phiên KDS hợp lệ hoặc được kết thúc

**Quy tắc nghiệp vụ:**

> Chỉ tài khoản có vai trò Kitchen và đang hoạt động được truy cập KDS

**Ghi chú:**

> Mật khẩu mã hóa SSL/TLS

<a id="uc_kit_02"></a>

### UC_KIT_02 — Xem tổng quan tình trạng hoạt động

Service đề xuất: **Ordering**.

**Module:**

> Kitchen Dashboard

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Theo dõi tổng món chờ, món đang làm và thời gian hoàn thành trung bình.

**Dữ liệu đầu vào:**

> Kitchen area, KDS ticket, khoảng thời gian theo dõi

**Kết quả đầu ra:**

> Dashboard tình trạng bếp

**Quy tắc nghiệp vụ:**

> Chỉ tính ticket thuộc phạm vi bếp của người dùng; số liệu cập nhật real-time.Đối chiếu số món Pending/Cooking và thời gian trung bình với dữ liệu thực tế. Kiểm tra dashboard cập nhật real-time liên tục

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_03"></a>

### UC_KIT_03 — Tiếp nhận order mới real-time

Service đề xuất: **Ordering**.

**Module:**

> Order Reception

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Nhận các món mới được gửi từ QR Menu xuống KDS.

**Dữ liệu đầu vào:**

> Order/new ticket

**Kết quả đầu ra:**

> Ticket Pending xuất hiện trên KDS

**Quy tắc nghiệp vụ:**

> Không tạo trùng ticket khi nhận lại sự kiện; phải bảo toàn thứ tự và mã order.Kiểm tra order từ và QR Menu xuất hiện đúng một lần trên KDS. Mô phỏng mất kết nối/gửi lại sự kiện để bảo đảm không tạo ticket trùng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_04"></a>

### UC_KIT_04 — Xem chi tiết món và ghi chú chế biến

Service đề xuất: **Ordering**.

**Module:**

> Order Details

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Xem món, số bàn và các yêu cầu đặc biệt của khách.

**Dữ liệu đầu vào:**

> Ticket, item, table, note

**Kết quả đầu ra:**

> Thông tin chế biến đầy đủ

**Quy tắc nghiệp vụ:**

> Ghi chú phải gắn đúng line item; thay đổi order sau khi gửi phải hiển thị rõ.Kiểm tra số bàn, số lượng, ghi chú và tùy chỉnh món hiển thị đúng line item. Order chỉnh sửa sau khi gửi phải được đánh dấu rõ ràng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_05"></a>

### UC_KIT_05 — Sắp xếp hàng đợi chế biến

Service đề xuất: **Ordering**.

**Module:**

> Queue Management

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Sắp xếp ticket theo thời gian đặt, món trùng hoặc mức ưu tiên/VIP.

**Dữ liệu đầu vào:**

> Danh sách ticket, tiêu chí ưu tiên

**Kết quả đầu ra:**

> Thứ tự xử lý được cập nhật

**Quy tắc nghiệp vụ:**

> Quy tắc ưu tiên phải minh bạch; không làm mất ticket khỏi hàng đợi.Kiểm tra thứ tự hàng đợi theo từng tiêu chí ưu tiên và trường hợp nhiều ticket cùng thời điểm. Không được làm mất hoặc ẩn ticket ngoài ý muốn.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_06"></a>

### UC_KIT_06 — Cập nhật trạng thái món

Service đề xuất: **Ordering**.

**Module:**

> Cooking Workflow

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Chuyển trạng thái Pending -> Cooking -> Ready cho từng món.

**Dữ liệu đầu vào:**

> Line item, trạng thái mới

**Kết quả đầu ra:**

> Trạng thái món được đồng bộ

**Quy tắc nghiệp vụ:**

> Chỉ cho phép chuyển theo luồng trạng thái hợp lệ; mọi thay đổi lưu thời gian và người thao tác.Kiểm tra các chuyển trạng thái hợp lệ và chặn chuyển trạng thái sai thứ tự. Xác nhận thời gian, người thao tác và đồng bộ sang các màn hình liên quan.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_07"></a>

### UC_KIT_07 — Báo hết món / Tạm dừng phục vụ

Service đề xuất: **Catalog**.

**Module:**

> Menu Availability

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Đánh dấu món hết hàng hoặc tạm ngưng để QR Menu khóa nhận món mới.

**Dữ liệu đầu vào:**

> Món, trạng thái, lý do

**Kết quả đầu ra:**

> Món bị khóa không thể order

**Quy tắc nghiệp vụ:**

> Không ảnh hưởng món đã đặt; trạng thái phải đồng bộ real-time.Kiểm tra món hết hàng/tạm dừng bị khóa trên POS/QR Menu ngay sau cập nhật. Các món đã được order trước đó vẫn phải giữ nguyên.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_08"></a>

### UC_KIT_08 — Báo cáo thiếu / hết nguyên liệu

Service đề xuất: **Inventory**.

**Module:**

> Inventory Alert

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Gửi cảnh báo thiếu nguyên liệu đến Inventory Staff và Restaurant Manager.

**Dữ liệu đầu vào:**

> Nguyên liệu, mức thiếu, ghi chú

**Kết quả đầu ra:**

> Cảnh báo/yêu cầu xử lý được gửi

**Quy tắc nghiệp vụ:**

> Cảnh báo phải ghi nguồn gửi, thời gian và trạng thái xử lý.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_09"></a>

### UC_KIT_09 — Xem công thức và hướng dẫn chế biến

Service đề xuất: **Catalog**.

**Module:**

> Recipe

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Tra cứu recipe, định lượng chuẩn và tài liệu hình ảnh/video decor.

**Dữ liệu đầu vào:**

> Món/Recipe ID

**Kết quả đầu ra:**

> Công thức và tài liệu hướng dẫn

**Quy tắc nghiệp vụ:**

> Nhân viên chỉ xem phiên bản recipe đang hiệu lực; nội dung thay đổi cần versioning nếu áp dụng.Kiểm tra đúng phiên bản recipe đang hiệu lực, định lượng và media hướng dẫn. Tài khoản không đủ quyền không được sửa recipe từ KDS.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_10"></a>

### UC_KIT_10 — Nhận thông báo hủy món

Service đề xuất: **Ordering**.

**Module:**

> Cancellation Handling

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Nhận sự kiện hủy món từ phục vụ/khách để dừng hoặc xử lý chế biến.

**Dữ liệu đầu vào:**

> Order item, lý do hủy, trạng thái hiện tại

**Kết quả đầu ra:**

> Ticket được đánh dấu hủy hoặc yêu cầu xác nhận

**Quy tắc nghiệp vụ:**

> Chỉ cho phép hủy khi món ở trạng thái Pending (chưa nấu).
> Chặn hủy khi món ở trạng thái Cooking, Ready, Served.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_11"></a>

### UC_KIT_11 — Thông báo món Ready cho Waiter

Service đề xuất: **Ordering**.

**Module:**

> Service Notification

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Khi món chuyển Ready, hệ thống gửi thông báo/chuông đến thiết bị phục vụ.

**Dữ liệu đầu vào:**

> Sự kiện trạng thái Ready, bàn, món

**Kết quả đầu ra:**

> Push notification đến Waiter

**Quy tắc nghiệp vụ:**

> Chỉ gửi một thông báo chính cho mỗi lần chuyển Ready; retry khi gửi thất bại.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_kit_12"></a>

### UC_KIT_12 — Chế biến món trừ kho định lượng tự động

Service đề xuất: **Ordering + Catalog + Inventory**.

**Module:**

> Inventory Integration

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Khi Bếp bấm "Chế biến" món (Pending -> Cooking), hệ thống bóc tách công thức (BOM) và trừ thẳng nguyên liệu trong kho.

**Dữ liệu đầu vào:**

> ID Món ăn, Số lượng món, Công thức (BOM)

**Kết quả đầu ra:**

> Tồn kho nguyên liệu giảm

**Quy tắc nghiệp vụ:**

> Tự động bóc tách định lượng và trừ kho theo FIFO (UC_INV_03). Nếu kho chạm/dưới ngưỡng tối thiểu, tự động phát cảnh báo sang Kho (UC_INV_02).

**Ghi chú:**

> Xử lý ngầm tự động khi Bếp chuyển trạng thái sang Cooking.

<a id="uc_kit_13"></a>

### UC_KIT_13 — Xem lịch sử món / đơn đã hoàn thành

Service đề xuất: **Ordering**.

**Module:**

> History

**Vai trò:**

> Kitchen Staff

**Mô tả nghiệp vụ:**

> Tra cứu các món và order đã xử lý trên KDS theo khoảng thời gian.

**Dữ liệu đầu vào:**

> Khoảng thời gian, khu bếp, bàn/order

**Kết quả đầu ra:**

> Danh sách lịch sử KDS

**Quy tắc nghiệp vụ:**

> Chỉ hiển thị dữ liệu thuộc quyền/khu bếp; lịch sử không được chỉnh sửa trực tiếp và được lưu liên tục 24/24.

**Ghi chú:**

> .

## Inventory Staff — 4 chức năng

Nguồn: 06-inventory.txt (bản gốc local, không đưa lên Git).

<a id="uc_inv_01"></a>

### UC_INV_01 — Đăng nhập / Đăng xuất phân hệ Kho

Service đề xuất: **Identity**.

**Module:**

> Authentication

**Vai trò:**

> Inventory Staff

**Mô tả nghiệp vụ:**

> Nhân viên kho truy cập phân hệ quản lý kho và kết thúc phiên.

**Dữ liệu đầu vào:**

> Email/Phone/Username, Password

**Kết quả đầu ra:**

> Phiên quản lý kho hợp lệ

**Quy tắc nghiệp vụ:**

> Chỉ tài khoản Inventory đang hoạt động được truy cập các chức năng kho.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_inv_02"></a>

### UC_INV_02 — Nhập thêm nguyên liệu

Service đề xuất: **Inventory**.

**Module:**

> Inbound

**Vai trò:**

> Inventory Staff

**Mô tả nghiệp vụ:**

> Nhân viên mua bổ sung nguyên liệu ngoài thị trường về và nhập thêm trực tiếp vào kho (thường khi có cảnh báo gần hết).

**Dữ liệu đầu vào:**

> Nguyên liệu, số lượng thực nhận, đơn vị tính, hạn sử dụng.

**Kết quả đầu ra:**

> Tồn kho tăng, Cảnh báo gần hết được tắt

**Quy tắc nghiệp vụ:**

> Nhập trực tiếp số lượng vào kho.
> Bắt buộc nhập đầy đủ thông tin và Hạn sử dụng (Expiry) cho mỗi lần nhập hàng.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_inv_03"></a>

### UC_INV_03 — Quản lý Nguyên Liệu, hạn sử dụng & Trừ kho

Service đề xuất: **Inventory**.

**Module:**

> Outbound / Lot Management

**Vai trò:**

> Inventory Staff

**Mô tả nghiệp vụ:**

> hạn sử dụng và xuất kho theo quy tắc FIFO.

**Dữ liệu đầu vào:**

> Ngày nhập, Expiry, Số lượng nguyên liệu, Tín hiệu Bếp bấm Cooking

**Kết quả đầu ra:**

> Tồn kho Nguyên liệu,Tự động trừ kho Nguyên liệu khi bếp sử dụng nguyên liệu

**Quy tắc nghiệp vụ:**

> Tự động trừ kho theo FIFO ngay khi Bếp chuyển món sang Cooking.
> Món bị hủy ở trạng thái Pending (chưa nấu) không phát sinh giao dịch trừ kho.
>  hệ thống ưu tiên trừ nguyên liệu theo FIFO và phát cảnh báo khi nguyên liệu sắp hết hạn hoặc đã hết hạn.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_inv_04"></a>

### UC_INV_04 — Kiểm kê và điều chỉnh chênh lệch

Service đề xuất: **Inventory**.

**Module:**

> Stocktaking

**Vai trò:**

> Inventory Staff

**Mô tả nghiệp vụ:**

> Tạo kỳ kiểm kê, nhập tồn thực tế và lập điều chỉnh chênh lệch.

**Dữ liệu đầu vào:**

> Kho, danh sách nguyên liệu, số lượng thực tế, lý do chênh lệch

**Kết quả đầu ra:**

> Phiếu kiểm kê và phiếu điều chỉnh

**Quy tắc nghiệp vụ:**

> Kỳ kiểm kê phải khóa/kiểm soát giao dịch theo chính sách; điều chỉnh cần phê duyệt nếu vượt ngưỡng.Kiểm tra chốt tồn thực tế, tính chênh lệch và luồng phê duyệt điều chỉnh. Trong kỳ kiểm kê cần kiểm tra ảnh hưởng của các giao dịch phát sinh.

**Ghi chú:**

> (Trống trong nguồn.)

## Customer — 11 chức năng

Nguồn: 07-customer.txt (bản gốc local, không đưa lên Git).

<a id="uc_cus_01"></a>

### UC_CUS_01 — Đặt bàn qua số điện thoại

Service đề xuất: **Restaurant**.

**Module:**

> Table Reservations

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Đặt bàn thông qua số điện thoại

**Dữ liệu đầu vào:**

> Số điện thoại; thông tin đặt bàn

**Kết quả đầu ra:**

> Yêu cầu đặt bàn

**Quy tắc nghiệp vụ:**

> Thời gian đặt trước (30 phút – 30 ngày) và số lượng khách hợp lệ; Đơn đặt bàn cần được Nhân viên (Waiter/Cashier) kiểm tra và duyệt; Tự động giữ bàn tối đa 15 phút so với giờ hẹn.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_02"></a>

### UC_CUS_02 — Truy cập menu bằng QR và tham gia phiên bàn

Service đề xuất: **Restaurant + Customer**.

**Module:**

> QR Menu

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Khách quét QR hợp lệ tại bàn. Nếu bàn chưa có phiên phục vụ đang mở, hệ thống tự tạo phiên ngay khi quét, không cần nhân viên mở trước và không chờ nhập thông tin tham gia. Khách đầu tiên nhập đầy đủ SĐT hoặc 3 số cuối để thiết lập mã tham gia. Các thiết bị tiếp theo nhập đúng 3 số cuối để tham gia cùng phiên. Hệ thống lưu cookie để khách tiếp tục sử dụng mà không phải nhập lại trong thời hạn truy cập.

**Dữ liệu đầu vào:**

> Token QR; SĐT đầy đủ hoặc 3 số cuối; lựa chọn tích điểm nếu nhập đầy đủ SĐT.

**Kết quả đầu ra:**

> Hiển thị menu gắn với đúng phiên bàn; cấp quyền truy cập và cookie riêng cho từng trình duyệt; liên kết khách thành viên nếu chọn tích điểm.

**Quy tắc nghiệp vụ:**

> QR phải hợp lệ, bàn và khu vực đang hoạt động. Nếu bàn chưa có phiên mở, hệ thống tự tạo phiên ngay khi quét QR hợp lệ. Việc thiết lập mã tham gia và cấp quyền gọi món được thực hiện sau khi khách nhập thông tin hợp lệ. Nếu bàn đã có phiên mở, khách phải tham gia phiên hiện tại. Các yêu cầu đồng thời không được tạo nhiều phiên mở cho cùng bàn. Khách đầu nhập full SĐT thì dùng 3 số cuối làm mã tham gia; nếu chỉ nhập 3 số thì không liên kết thành viên. Mã chỉ được thiết lập một lần trong phiên, không cho yêu cầu đồng thời ghi đè. Thiết bị khác nhập đúng mã mới được cấp quyền và không được thay đổi người nhận điểm. Cookie hợp lệ cho phép truy cập lại mà không nhập mã; mọi thao tác gọi món phải kiểm tra quyền và phiên còn hiệu lực. Khi đóng phiên, thu hồi toàn bộ quyền truy cập; cookie cũ không dùng được cho phiên mới.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_03"></a>

### UC_CUS_03 — Tra cứu, lọc thực đơn

Service đề xuất: **Catalog**.

**Module:**

> QR Menu

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Xem thực đơn theo nhóm món, tìm kiếm món, lọc món (Món chay, món cay, Best-seller).

**Dữ liệu đầu vào:**

> Nhóm món; từ khóa; bộ lọc chay/cay/best-seller

**Kết quả đầu ra:**

> Danh sách món phù hợp

**Quy tắc nghiệp vụ:**

> Món ngưng bán sẽ tự động ẩn; món hết hàng bị làm mờ và hiển thị trạng thái hết hàng; Tìm kiếm theo từ khóa không phân biệt chữ hoa/thường và hỗ trợ tìm gần đúng/có dấu tiếng Việt; Nhãn "Best-seller" do hệ thống tự tổng hợp theo doanh số hoặc theo cấu hình từ Quản lý

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_04"></a>

### UC_CUS_04 — Xem chi tiết món

Service đề xuất: **Catalog**.

**Module:**

> QR Menu

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Xem chi tiết hình ảnh, đơn giá, thành phần nguyên liệu và mô tả món ăn.

**Dữ liệu đầu vào:**

> Món được chọn

**Kết quả đầu ra:**

> Hình ảnh; đơn giá; thành phần; mô tả món

**Quy tắc nghiệp vụ:**

> Hiển thị chính xác đơn giá, mô tả và danh sách tùy chọn (size, topping) thuộc về món ăn; Món hết hàng vẫn cho xem chi tiết nhưng vô hiệu hóa nút thêm vào giỏ; Tùy chọn/topping hết hàng sẽ tự động làm mờ và không cho phép chọn

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_05"></a>

### UC_CUS_05 — Tùy chỉnh món

Service đề xuất: **Catalog**.

**Module:**

> QR Menu

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Khách hàng lựa chọn các thuộc tính đính kèm (size, topping, tùy chọn) và nhập ghi chú đặc biệt cho món ăn.

**Dữ liệu đầu vào:**

> Món ăn, size, tùy chọn/topping, số lượng món, ghi chú đặc biệt.

**Kết quả đầu ra:**

> Món ăn hoàn chỉnh kèm theo các tùy chọn và ghi chú đã thiết lập.

**Quy tắc nghiệp vụ:**

> Tùy chọn, size, topping được chọn phải thuộc đúng món ăn đó và đang ở trạng thái khả dụng;Số lượng đặt của món phải lớn hơn 0; Đơn giá món ăn sẽ tự động cộng thêm chi phí của các tùy chọn/topping đi kèm.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_06"></a>

### UC_CUS_06 — Xem, thêm món vào giỏ

Service đề xuất: **Ordering + Catalog**.

**Module:**

> Shopping Cart

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Xem danh sách món trong giỏ hàng, thêm món vào giỏ hàng, kiểm tra tổng tiền tạm tính.

**Dữ liệu đầu vào:**

> Món ăn đã chọn, tùy chọn/topping, số lượng, dữ liệu giỏ hàng.

**Kết quả đầu ra:**

> Danh sách món và tổng tiền tạm tính

**Quy tắc nghiệp vụ:**

> Số lượng món thêm vào giỏ phải lớn hơn 0; Giỏ hàng phải chứa ít nhất 1 món ăn hợp lệ trước khi tiến hành đặt hàng; Giá hiển thị trên giỏ hàng là tạm tính; Backend sẽ tính toán lại giá chính thức khi chốt đơn

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_07"></a>

### UC_CUS_07 — Gửi order

Service đề xuất: **Ordering**.

**Module:**

> Ordering

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Nhấn gửi order đến waiter xác nhận rồi mới xuống hệ thống Bếp và Thu ngân.

**Dữ liệu đầu vào:**

> Giỏ hàng; bàn; tùy chọn món

**Kết quả đầu ra:**

> Order gửi đến Waiter sau đó đến Bếp và Thu ngân

**Quy tắc nghiệp vụ:**

> Order được gửi đến Waiter và chờ xác nhận xong mới đến hệ thống Bếp và Thu ngân; Chặn tạo đơn trùng lặp khi khách bấm gửi liên tục nhiều lần.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_08"></a>

### UC_CUS_08 — Theo dõi tiến độ đơn

Service đề xuất: **Ordering**.

**Module:**

> Ordering

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Theo dõi tiến độ đơn hàng thời gian thực: Đã nhận -> Đang chế biến -> Món đã xong -> Đã phục vụ.

**Dữ liệu đầu vào:**

> Đơn hàng; trạng thái chế biến/phục vụ

**Kết quả đầu ra:**

> Tiến độ thời gian thực từ đã nhận đến đã phục vụ

**Quy tắc nghiệp vụ:**

> Khách hàng chỉ được xem tiến độ, không thể tự thao tác thay đổi trạng thái đơn; Trạng thái hiển thị tự động đồng bộ theo thao tác xử lý từ Bếp (KDS) và Phục vụ (Waiter).

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_09"></a>

### UC_CUS_09 — Gọi phục vụ, yêu cầu thanh toán

Service đề xuất: **Restaurant**.

**Module:**

> Service Requests

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Nhấn nút "Gọi phục vụ" hoặc "Yêu cầu thanh toán" gửi thông báo trực tiếp đến máy di động Waiter.

**Dữ liệu đầu vào:**

> Bàn; loại yêu cầu

**Kết quả đầu ra:**

> Thông báo trực tiếp đến thiết bị Waiter

**Quy tắc nghiệp vụ:**

> Yêu cầu bắt buộc phải gắn với mã bàn và phiên bàn đang hoạt động; Giới hạn tần suất gửi yêu cầu để chống spam (chặn gửi lại cùng một loại yêu cầu trong 30 giây)

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_10"></a>

### UC_CUS_10 — Chọn phương thức thanh toán

Service đề xuất: **Billing**.

**Module:**

> Payment

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Lựa chọn phương thức thanh toán trên di động (Thanh toán tiền mặt tại bàn hoặc Chuyển khoản VietQR/Ví điện tử).

**Dữ liệu đầu vào:**

> Tiền mặt tại bàn; VietQR; ví điện tử

**Kết quả đầu ra:**

> Phương thức thanh toán khách lựa chọn

**Quy tắc nghiệp vụ:**

> Chỉ hiển thị các phương thức thanh toán do nhà hàng đang kích hoạt; Thanh toán tiền mặt sẽ giữ trạng thái PENDING cho tới khi Nhân viên xác nhận đã thu đủ tiền; Thanh toán trực tuyến được hệ thống xác thực tự động qua cổng thanh toán/Webhook trước khi đổi sang PAID.

**Ghi chú:**

> (Trống trong nguồn.)

<a id="uc_cus_11"></a>

### UC_CUS_11 — Nhận hóa đơn, gửi đánh giá

Service đề xuất: **Billing + Customer**.

**Module:**

> Billing & Feedback

**Vai trò:**

> Customer

**Mô tả nghiệp vụ:**

> Nhận hóa đơn điện tử ngay trên màn hình và gửi đánh giá (Star rating + Nhận xét) về món ăn/dịch vụ.

**Dữ liệu đầu vào:**

> Hóa đơn; số sao; nhận xét món/dịch vụ

**Kết quả đầu ra:**

> Hóa đơn trên màn hình và đánh giá được gửi

**Quy tắc nghiệp vụ:**

> Chỉ mở tính năng gửi đánh giá khi đơn hàng ở trạng thái đã thanh toán (PAID); Mỗi đơn hàng hoàn tất thanh toán chỉ được phép gửi đánh giá 1 lần; Số sao đánh giá là bắt buộc (thang điểm 1–5 sao); nội dung nhận xét là tùy chọn

**Ghi chú:**

> (Trống trong nguồn.)

## AI Restaurant Assistant — 2 chức năng

Nguồn: 08-ai.txt (bản gốc local, không đưa lên Git).

<a id="uc_ai_01"></a>

### UC_AI_01 — Gợi ý món ăn kèm, đồ uống

Service đề xuất: **AI + Catalog + Ordering**.

**Module:**

> AI Recommendations

**Vai trò:**

> AI Restaurant Assistant

**Mô tả nghiệp vụ:**

> Phân tích các món ăn hiện có trong giỏ hàng của khách tại QR Menu để tự động đưa ra gợi ý món ăn kèm (side dishes) hoặc đồ uống phù hợp theo thời gian thực.

**Dữ liệu đầu vào:**

> Giỏ hàng của khách tại QR Menu

**Kết quả đầu ra:**

> Danh sách món ăn kèm/đồ uống gợi ý theo thời gian thực (kèm nút thêm nhanh)

**Quy tắc nghiệp vụ:**

> AI chỉ gợi ý các món ăn/đồ uống đang còn hàng (In stock) và đang hoạt động.
> Không gợi ý lại các món đã có sẵn trong giỏ hàng của khách.
> Giá và thông tin món gợi ý phải do Backend xác thực chính xác trước khi thêm vào giỏ.

**Ghi chú:**

> Hiển thị dạng Pop-up hoặc thanh gợi ý nhanh khi khách thêm món vào giỏ.

<a id="uc_ai_02"></a>

### UC_AI_02 — Chatbot tư vấn món ăn cho khách

Service đề xuất: **AI + Catalog**.

**Module:**

> AI Forecasting

**Vai trò:**

> AI Restaurant Assistant

**Mô tả nghiệp vụ:**

> Khách hàng tương tác qua khung chat trên QR Menu để hỏi về thông tin món ăn, nhờ tư vấn theo khẩu vị, ngân sách, chế độ ăn kiêng (chay, ít cay, dị ứng) hoặc tìm kiếm món ăn.

**Dữ liệu đầu vào:**

> Câu hỏi/Yêu cầu bằng văn bản (nhập tay hoặc chọn prompt mẫu) của khách hàng

**Kết quả đầu ra:**

> Chatbot chỉ tư vấn các món ăn có trong thực đơn của nhà hàng.
> Bắt buộc kiểm tra trạng thái còn hàng/hết hàng trước khi giới thiệu món cho khách.
> Thẻ món ăn đính kèm phải hiển thị giá thực tế từ Backend.
> Tuyệt đối không trả lời các câu hỏi ngoài phạm vi thực đơn và dịch vụ của nhà hàng.

**Quy tắc nghiệp vụ:**

> Bắt buộc phân quyền Quản lý để truy cập tính năng dự báo; Dự báo dựa trên dữ liệu tiêu thụ thực tế từ hệ thống, không tự phát sinh số liệu khống; Kết quả dự báo mang tính trợ lý quyết định, không tự động tạo đơn nhập kho

**Ghi chú:**

> Tích hợp trực tiếp trên giao diện QR Menu của khách hàng.
