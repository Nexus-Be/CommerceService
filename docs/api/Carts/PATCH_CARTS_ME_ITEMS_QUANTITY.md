# COMMERCE SERVICE - PATCH /api/v1/carts/me/items/{cartItemId}

> **Feature / Use Case:** Update Cart Item Quantity
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PATCH`
- **Route Endpoint:** `/api/v1/carts/me/items/{cartItemId}`
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
- **Tên C# Record:** `UpdateCartItemQuantityRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record UpdateCartItemQuantityRequest(
    [Range(1, 99)] int Quantity
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "quantity": 3
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
  "totalItems": 3,
  "totalAmount": 3600000.0,
  "items": [],
  "rowVersion": 3,
  "updatedAt": "2026-11-21T06:00:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Quantity must be between 1 and 99.` | Số lượng không hợp lệ |
| `404 Not Found` | `CART_ITEM_NOT_FOUND` | `Cart item not found.` | Món không có trong giỏ |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Tìm CartItem trong giỏ.
2. **Bước 2:** Kiểm tra tồn kho mới.
3. **Bước 3:** Cập nhật Quantity và TotalAmount.
4. **Bước 4:** Trả về CartResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Carts/PATCH_CARTS_ME_ITEMS_QUANTITY.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
