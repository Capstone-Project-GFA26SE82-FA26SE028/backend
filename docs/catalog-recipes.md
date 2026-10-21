# Công thức món ăn (Recipe), định lượng nguyên liệu (BOM) và tài liệu chế biến (SOP)

Triển khai trên Catalog cho **UC_MGR_05** (Manager cấu hình BOM) và **UC_KIT_09** (Kitchen xem công thức và hướng dẫn chế biến), theo luồng Gateway → gRPC → BLL → repository → CatalogDbContext. Dùng schema database hiện có (`RecipeVersion`, `RecipeIngredient`, `RecipeMedium`), không chạy migration.

---

## 1. Quyền và phản hồi

Tất cả endpoint yêu cầu header `Authorization: Bearer <accessToken>`.

| Thao tác | Role được phép | Ghi chú |
| :--- | :--- | :--- |
| Tạo phiên bản công thức mới | `MenuManagerRoleIds` (`manager`) | Tự động tăng `Version` và đóng `EffectiveTo` của phiên bản cũ đang hoạt động |
| Xem lịch sử công thức của món | `MenuManagerRoleIds` (`manager`) | Trả về toàn bộ danh sách phiên bản sắp xếp theo Version giảm dần |
| Xem công thức đang hiệu lực | `MenuManagerRoleIds` + `KitchenRoleIds` (`manager`, `kitchen`) | Trả về phiên bản thỏa mãn `EffectiveFrom <= at < EffectiveTo` |
| Xem chi tiết một phiên bản | `MenuManagerRoleIds` + `KitchenRoleIds` (`manager`, `kitchen`) | Kitchen chỉ xem phiên bản đang hiệu lực; Manager xem mọi phiên bản |

Tham số `at` chỉ dành cho Manager; Kitchen phải bỏ `at` để dùng thời điểm hiện tại của server.

Tạo thành công trả HTTP 201 kèm header `Location: /api/recipes/{recipeVersionId}`. Đọc thành công trả HTTP 200. Dữ liệu nằm trong `ResponseDTO.result`.

---

## 2. Danh sách API

### 2.1. Công thức cho Món (Item Recipes)
| Method | Route | Chức năng | Quyền |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/items/{itemId}/recipes` | Tạo phiên bản công thức mới cho món | Manager |
| `GET` | `/api/items/{itemId}/recipes/active` | Lấy công thức đang hiệu lực của món | Manager, Kitchen |
| `GET` | `/api/items/{itemId}/recipes` | Xem toàn bộ lịch sử các phiên bản công thức của món | Manager |

### 2.2. Công thức cho Tùy chọn / Topping / Size (Option Recipes)
| Method | Route | Chức năng | Quyền |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/options/{optionId}/recipes` | Tạo phiên bản công thức mới cho option | Manager |
| `GET` | `/api/options/{optionId}/recipes/active` | Lấy công thức đang hiệu lực của option | Manager, Kitchen |
| `GET` | `/api/options/{optionId}/recipes` | Xem toàn bộ lịch sử các phiên bản công thức của option | Manager |

### 2.3. Xem chi tiết công thức
| Method | Route | Chức năng | Quyền |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/recipes/{recipeVersionId}` | Lấy chi tiết công thức theo ID phiên bản | Manager, Kitchen |


---

## 3. Cấu trúc dữ liệu

### Body POST `/api/items/{itemId}/recipes`:

```json
{
  "effectiveFrom": "2026-10-20T08:00:00Z",
  "instructions": "1. Luộc trân châu trong 20 phút...\n2. Thêm sốt đường đen...",
  "ingredients": [
    {
      "ingredientRef": "e1f1c2b3-1111-2222-3333-444455556666",
      "unitRef": "a1b2c3d4-1111-2222-3333-444455556666",
      "quantity": 50.0,
      "conversionFactor": 1.0
    }
  ],
  "media": [
    {
      "mediaType": "image",
      "url": "https://example.com/images/step1.jpg",
      "displayOrder": 1
    }
  ]
}
```

### Result trả về (`RecipeVersionDTO`):

```json
{
  "recipeVersionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "itemId": "7d9dd887-e49c-4d66-885f-c3b138cc2560",
  "optionId": null,
  "version": 1,
  "effectiveFrom": "2026-10-20T08:00:00Z",
  "effectiveTo": null,
  "instructions": "1. Luộc trân châu trong 20 phút...\n2. Thêm sốt đường đen...",
  "ingredients": [
    {
      "recipeIngredientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "recipeVersionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "ingredientRef": "e1f1c2b3-1111-2222-3333-444455556666",
      "unitRef": "a1b2c3d4-1111-2222-3333-444455556666",
      "quantity": 50.0,
      "baseQuantitySnapshot": 50.0,
      "conversionFactorSnapshot": 1.0
    }
  ],
  "media": [
    {
      "recipeMediaId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "recipeVersionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
      "mediaType": "image",
      "url": "https://example.com/images/step1.jpg",
      "displayOrder": 1
    }
  ]
}
```

---

## 4. Quy tắc nghiệp vụ (Business Rules)

1. **Ràng buộc Món**: `itemId` phải tồn tại trong bảng `Item`.
2. **Quy tắc Versioning**:
   - Số phiên bản `Version` tự tăng (`max(Version) + 1`), phiên bản đầu tiên là 1.
   - `effectiveFrom` phải có múi giờ (`Z` hoặc offset), nằm trong khoảng SQL `datetime`. Backend chuẩn hóa UTC và độ chính xác SQL `datetime` trước khi so sánh/lưu.
   - Phiên bản mới phải có `effectiveFrom` lớn hơn thời điểm bắt đầu của tất cả phiên bản đã tạo cho cùng món/option. Trùng thời điểm (kể cả trùng sau khi làm tròn SQL) hoặc chèn ngược lịch trả 409, không sửa lịch sử.
   - Có thể lên lịch phiên bản mới trong tương lai. Nếu đã lên lịch ngày 25, tạo tiếp phiên bản ngày 23 bị từ chối; không tự hủy lịch ngày 25.
   - Khi tạo phiên bản mới có thời điểm `effectiveFrom`, hệ thống tự động tìm các phiên bản cũ còn mở và gán `EffectiveTo = new.EffectiveFrom` trong transaction Serializable.
   - Tra cứu phiên bản hiện tại lấy bản ghi thỏa mãn `EffectiveFrom <= at < (EffectiveTo ?? vô hạn)`.
3. **Định lượng nguyên liệu (`RecipeIngredient`)**:
   - Một công thức phải có ít nhất 1 nguyên liệu.
   - `quantity > 0`, `conversionFactor > 0` (mặc định 1.0 nếu null).
   - Snapshot đơn vị cơ sở: `baseQuantitySnapshot = quantity * conversionFactorSnapshot`. Snapshot này cố định tại thời điểm lưu công thức để bảo đảm dữ liệu tính giá vốn và trừ kho không bị sai lệch khi tỷ lệ quy đổi trong kho thay đổi sau này.
   - Không được chứa nguyên liệu trùng lặp (`ingredientRef` duy nhất trong cùng 1 phiên bản).
4. **Tài liệu hình ảnh/video (`RecipeMedium`)**:
   - `mediaType` không rỗng, tối đa 50 ký tự (ví dụ: `image`, `video`).
   - `url` không rỗng, tối đa 2048 ký tự.
   - `displayOrder` là số không âm, phục vụ hiển thị tuần tự các bước chế biến.

Món chỉ được chuyển sang `Available` khi có đúng một công thức đang hiệu lực. Menu và validate order cũng kiểm tra lại điều kiện này. Option không bắt buộc có công thức; nếu có công thức đang hiệu lực thì validate order trả `recipeVersionId` của option để Ordering lưu cùng đơn. Tham chiếu nguyên liệu/đơn vị và hệ số quy đổi vẫn chưa được xác minh với Inventory.

---

## 5. Kiểm thử

```powershell
dotnet test Tests/Catalog.Tests/Catalog.Tests.csproj -c Release
```

Test `RecipeFlowTests.cs` bao phủ:
- Tạo công thức hợp lệ, kiểm tra sinh Version, tính toán snapshot định lượng và header Location.
- Tạo phiên bản kế tiếp: kiểm tra tự tăng Version và tự động đóng `effectiveTo` của phiên bản trước.
- Kitchen lấy công thức đang hiệu lực (thành công 200); Kitchen cố tình tạo mới trả 403 Forbidden.
- Bắt lỗi dữ liệu: món không tồn tại (404), danh sách nguyên liệu rỗng (400), định lượng <= 0 (400), trùng nguyên liệu (400).
- Xem lịch sử các phiên bản công thức theo thứ tự giảm dần.

Chi tiết phân quyền và giới hạn phiên bản công thức: [Phân quyền backend](backend-permissions.md).
