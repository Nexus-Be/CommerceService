# COMMERCE SERVICE - PUT /api/v1/discounts/{discountId}

> **Feature / Use Case:** Update Discount Information
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `PUT`
- **Route Endpoint:** `/api/v1/discounts/{discountId}`
- **Quyền hạn (RBAC Policy):** Admin, Seller (`[RequireRole("admin", "seller")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller chỉ sửa voucher của mình; Admin sửa toàn sàn
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `discountId` | Route | `Guid` | Có | UUIDv4 | Mã khuyến mãi |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `UpdateDiscountRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record UpdateDiscountRequest(
    [Required] string Name,
    decimal MinOrderAmount,
    DateTimeOffset EndsAt
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "name": "Updated Voucher Name",
  "minOrderAmount": 200000.0,
  "endsAt": "2026-12-31T23:59:59Z"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `DiscountDetailResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "discountId": "d1000000-0000-0000-0000-000000000001",
  "name": "Updated Voucher Name"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `404 Not Found` | `DISCOUNT_NOT_FOUND` | `Discount not found.` | Không tìm thấy |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra quyền sở hữu.
2. **Bước 2:** Cập nhật thông tin voucher.
3. **Bước 3:** Trả về DiscountDetailResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Discounts/PUT_DISCOUNTS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
