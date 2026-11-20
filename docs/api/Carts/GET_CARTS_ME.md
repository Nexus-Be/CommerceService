# COMMERCE SERVICE - GET /api/v1/carts/me

> **Feature / Use Case:** Retrieve Active Shopping Cart
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/carts/me`
- **Quyền hạn (RBAC Policy):** `Buyer` (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** `buyerId` trích xuất trực tiếp từ JWT `HttpContext.GetUserId()`.
- **Controller:** `Commerce.Api/Controllers/Public/CartsController.cs`
- **Application Contract:** `Commerce.Application/Services/Cart/ICartService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
Không có (Endpoint nhận diện người mua qua Access Token).

### 2.2 Request Body Record DTO
Không có (Phương thức GET).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Cart/CartResponses.cs`
- **Tên C# Record:** `CartResponse`
- **Mã HTTP thành công:** `200 OK`
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

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "cartId": "ca100000-0000-0000-0000-000000000001",
  "buyerId": "11111111-2222-3333-4444-555555555555",
  "totalItems": 2,
  "totalAmount": 1500000.0,
  "items": [
    {
      "cartItemId": "c1000000-0000-0000-0000-000000000001",
      "sellerId": "99999999-8888-7777-6666-555555555555",
      "shopName": "Official Tech Store",
      "productId": "p1000000-0000-0000-0000-000000000001",
      "skuId": "s1000000-0000-0000-0000-000000000001",
      "productName": "Wireless Earbuds Pro",
      "skuCode": "WEP-BLK",
      "attributes": "Color: Black",
      "imageUrl": "https://img.nexus.vn/products/earbuds.png",
      "unitPrice": 1200000.0,
      "quantity": 1,
      "lineTotal": 1200000.0,
      "availableStock": 45,
      "isAvailable": true
    }
  ],
  "rowVersion": 1,
  "updatedAt": "2026-11-21T03:30:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hoặc token hết hạn |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Vai trò người dùng không hợp lệ |
| `404 Not Found` | `CART_NOT_FOUND` | `Active shopping cart not found.` | Người dùng chưa có giỏ hàng (hệ thống sẽ tự động khởi tạo giỏ rỗng) |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Lấy `buyerId` từ `HttpContext.GetUserId()`.
2. **Bước 2:** Tra cứu giỏ hàng đang hoạt động trong `CommerceDbContext.Carts` kèm danh sách `CartItems`.
3. **Bước 3:** Nếu chưa có giỏ hàng, tự động tạo mới giỏ hàng rỗng cho người mua.
4. **Bước 4:** Đồng bộ giá và tồn kho thời gian thực qua `ICatalogClient` và `IFulfillmentClient`.
5. **Bước 5:** Tính tổng tiền và trả về `CartResponse` dạng Pure DTO với mã `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã xác nhận cấu trúc DTO giỏ hàng thuần, không bọc ResultFilter.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Carts/GET_CARTS_ME.md`.
- [x] **Application:** `CartService.GetMyCartAsync` hoàn thiện.
- [x] **Tests:** Unit tests giỏ hàng đạt 100% pass rate.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 0 lỗi.
