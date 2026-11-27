# COMMERCE SERVICE - POST /api/v1/discounts

> **Feature / Use Case:** Create New Discount / Voucher
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/discounts`
- **Quyền hạn (RBAC Policy):** Seller, Admin (`[RequireRole("admin", "seller")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller chỉ tạo voucher của Shop; Admin tạo voucher toàn sàn
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `CreateDiscountRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record CreateDiscountRequest(
    [Required] string Code,
    [Required] string Name,
    DiscountType Type,
    decimal Value,
    decimal MinOrderAmount,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "code": "SHOP10K",
  "name": "Shop Discount 10K",
  "type": "FixedAmount",
  "value": 10000.0,
  "minOrderAmount": 100000.0,
  "startsAt": "2026-11-21T00:00:00Z",
  "endsAt": "2026-12-31T23:59:59Z"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `DiscountDetailResponse`
- **Mã HTTP thành công:** `201 Created`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "discountId": "d1000000-0000-0000-0000-000000000001",
  "code": "SHOP10K",
  "status": "Active"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Discount code already exists.` | Mã trùng lặp |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh tính duy nhất của mã voucher.
2. **Bước 2:** Tạo bản ghi Discount trong DB.
3. **Bước 3:** Trả về DiscountDetailResponse với mã 201 Created.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Discounts/POST_DISCOUNTS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
