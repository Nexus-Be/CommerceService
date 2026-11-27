# COMMERCE SERVICE - PATCH /api/v1/carts/me/items/{cartItemId}/variant

> **Feature / Use Case:** Change Product Variant in Cart
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PATCH`
- **Route Endpoint:** `/api/v1/carts/me/items/{cartItemId}/variant`
- **Quyền hạn (RBAC Policy):** Buyer (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId sở hữu giỏ
- **Controller:** `Commerce.Api/Controllers/Public/CartsController.cs`
- **Application Contract:** `Commerce.Application/Services/Cart/ICartService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `cartItemId` | Route | `Guid` | Có | UUIDv4 | ID món hàng |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `ChangeCartItemVariantRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record ChangeCartItemVariantRequest(
    [Required] Guid NewSkuId,
    [Range(1, 99)] int? NewQuantity = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "newSkuId": "s2000000-0000-0000-0000-000000000002",
  "newQuantity": 1
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `CartResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "cartId": "ca100000-0000-0000-0000-000000000001",
  "totalItems": 1,
  "totalAmount": 1500000.0,
  "items": [],
  "rowVersion": 4,
  "updatedAt": "2026-11-21T06:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `404 Not Found` | `CART_ITEM_NOT_FOUND` | `Cart item not found.` | Món không có trong giỏ |
| `400 Bad Request` | `INVALID_VARIANT` | `Selected variant does not belong to the same product.` | Biến thể không cùng sản phẩm |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Tra cứu SKU mới qua Catalog.
2. **Bước 2:** Xác minh cùng sản phẩm gốc.
3. **Bước 3:** Cập nhật SkuId và thuộc tính.
4. **Bước 4:** Trả về CartResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Carts/PATCH_CARTS_ME_ITEMS_VARIANT.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
