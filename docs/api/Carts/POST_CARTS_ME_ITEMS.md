# COMMERCE SERVICE - POST /api/v1/carts/me/items

> **Feature / Use Case:** Add Item to Shopping Cart
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/carts/me/items`
- **Quyền hạn (RBAC Policy):** Buyer (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId từ HttpContext.GetUserId()
- **Controller:** `Commerce.Api/Controllers/Public/CartsController.cs`
- **Application Contract:** `Commerce.Application/Services/Cart/ICartService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `AddCartItemRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record AddCartItemRequest(
    [Required] Guid ProductId,
    [Required] Guid SkuId,
    [Range(1, 99)] int Quantity,
    [Required] Guid SellerId
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "productId": "p1000000-0000-0000-0000-000000000001",
  "skuId": "s1000000-0000-0000-0000-000000000001",
  "quantity": 2,
  "sellerId": "99999999-8888-7777-6666-555555555555"
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
  "totalAmount": 1200000.0,
  "items": [],
  "rowVersion": 2,
  "updatedAt": "2026-11-21T06:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Quantity must be between 1 and 99.` | Số lượng ngoài khoảng 1-99 |
| `400 Bad Request` | `CART_LIMIT_EXCEEDED` | `Cart cannot exceed maximum limit of 50 different items.` | Vượt quá 50 món khác nhau |
| `400 Bad Request` | `CART_ITEM_LIMIT_EXCEEDED` | `Total quantity for a single product cannot exceed 99.` | Vượt quá 99 cái/SKU |
| `400 Bad Request` | `INSUFFICIENT_STOCK` | `Requested quantity exceeds available stock.` | Vượt tồn kho |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra tồn kho qua FulfillmentClient.
2. **Bước 2:** Kiểm tra giới hạn 50 loại hàng và 99 sản phẩm/loại.
3. **Bước 3:** Thêm hoặc cộng dồn CartItem.
4. **Bước 4:** Trả về CartResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Carts/POST_CARTS_ME_ITEMS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
