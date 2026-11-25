# COMMERCE SERVICE - POST /api/v1/carts/me/items

> **Feature / Use Case:** Add Item to Shopping Cart
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/carts/me/items`
- **Quyền hạn (RBAC Policy):** `Buyer` (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** `buyerId` trích xuất trực tiếp từ JWT `HttpContext.GetUserId()`.
- **Controller:** `Commerce.Api/Controllers/Public/CartsController.cs`
- **Application Contract:** `Commerce.Application/Services/Cart/ICartService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
Không có (nhận qua Request Body).

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Cart/CartRequests.cs`
- **Tên C# Record:** `AddCartItemRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record AddCartItemRequest(
    [Required] Guid ProductId,
    [Required] Guid SkuId,
    [Range(1, 99, ErrorMessage = "Quantity must be between 1 and 99.")] int Quantity,
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

- **Vị trí file:** `Commerce.Application/Services/Cart/CartResponses.cs`
- **Tên C# Record:** `CartResponse`
- **Mã HTTP thành công:** `200 OK` (hoặc `201 Created`)
- **Khai báo C# Record:**
```csharp
public sealed record CartResponse(
    Guid CartId,
    Guid BuyerId,
    int TotalItems,
    decimal TotalAmount,
    List<CartItemResponse> Items,
    long RowVersion,
    DateTimeOffset UpdatedAt
);
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Vi phạm giới hạn `Quantity` (1 - 99) |
| `400 Bad Request` | `CART_LIMIT_EXCEEDED` | `Cart cannot exceed maximum limit of 50 different items.` | Giỏ hàng đã đạt giới hạn tối đa 50 mặt hàng khác nhau |
| `400 Bad Request` | `CART_ITEM_LIMIT_EXCEEDED` | `Total quantity for a single product cannot exceed 99.` | Tổng số lượng của SKU đó trong giỏ vượt quá 99 |
| `400 Bad Request` | `INSUFFICIENT_STOCK` | `Requested quantity exceeds available stock.` | Số lượng yêu cầu vượt quá tồn kho khả dụng hiện tại |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ |
| `404 Not Found` | `PRODUCT_NOT_FOUND` | `Product variant not found or inactive.` | Không tìm thấy thông tin SKU tại Catalog Service |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `buyerId` từ `HttpContext.GetUserId()`.
2. **Bước 2:** Tra cứu SKU qua `ICatalogClient` để lấy thông tin sản phẩm, giá bán thực tế, và ảnh thumbnail.
3. **Bước 3:** Kiểm tra tồn kho khả dụng qua `IFulfillmentClient`.
4. **Bước 4:** Kiểm tra giới hạn giỏ hàng: tối đa 50 mặt hàng (`CART_LIMIT_EXCEEDED`) và tối đa 99 cái/SKU (`CART_ITEM_LIMIT_EXCEEDED`).
5. **Bước 5:** Nếu SKU đã có trong giỏ, cộng dồn số lượng. Nếu chưa có, thêm mới `CartItem`.
6. **Bước 6:** Lưu thay đổi vào DB, cập nhật `RowVersion`, và trả về `CartResponse` với mã `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã rà soát rào chắn số lượng và giới hạn 50 món / 99 cái.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Carts/POST_CARTS_ME_ITEMS.md`.
- [x] **Application:** `CartService.AddItemAsync` hoàn chỉnh.
- [x] **Tests:** Đã viết unit test bao phủ giới hạn giỏ hàng và kiểm tra tồn kho.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 100%.
