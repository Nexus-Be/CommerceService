# COMMERCE SERVICE - POST /api/v1/discounts/validate

> **Feature / Use Case:** Validate Single Voucher Against Cart
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/discounts/validate`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId từ HttpContext.GetUserId()
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `ValidateVoucherRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record ValidateVoucherRequest(
    [Required] string Code,
    [Required, MinLength(1)] List<Guid> CartItemIds,
    ShippingAddressInput? ShippingAddress = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "code": "FREESHIP50K",
  "cartItemIds": ["c1000000-0000-0000-0000-000000000001"]
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `ValidateVoucherResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "isValid": true,
  "discountId": "d1000000-0000-0000-0000-000000000001",
  "code": "FREESHIP50K",
  "scopeType": "Shop",
  "estimatedDiscountAmount": 50000.0,
  "ownerSellerId": "s1000000-0000-0000-0000-000000000001",
  "discountType": "FixedAmount",
  "discountValue": 50000.0,
  "minOrderAmount": 100000.0,
  "maxDiscountAmount": null
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VOUCHER_INVALID` | `Voucher is invalid or expired.` | Mã không tồn tại hoặc hết hạn |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Tra cứu mã voucher.
2. **Bước 2:** Kiểm tra điều kiện áp dụng với danh sách CartItemIds.
3. **Bước 3:** Trả về ValidateVoucherResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Discounts/POST_DISCOUNTS_VALIDATE.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
