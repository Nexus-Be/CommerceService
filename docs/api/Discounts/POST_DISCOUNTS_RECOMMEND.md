# COMMERCE SERVICE - POST /api/v1/discounts/recommend

> **Feature / Use Case:** Recommend Optimal Voucher Combination (Shopee Best Deal)
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/discounts/recommend`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId từ HttpContext.GetUserId()
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `RecommendVouchersRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record RecommendVouchersRequest(
    [Required] IReadOnlyList<Guid> CartItemIds,
    PaymentMethod? PaymentMethod = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "cartItemIds": ["c1000000-0000-0000-0000-000000000001"]
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `RecommendVouchersResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "bestCombination": ["FREESHIP50K", "DISCOUNT10"],
  "totalSaved": 150000.0
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu JWT |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Chạy thuật toán tối ưu hóa tổ hợp voucher.
2. **Bước 2:** Trả về RecommendVouchersResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Discounts/POST_DISCOUNTS_RECOMMEND.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
