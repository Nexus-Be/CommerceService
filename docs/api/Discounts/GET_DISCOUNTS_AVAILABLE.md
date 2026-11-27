# COMMERCE SERVICE - GET /api/v1/discounts/available

> **Feature / Use Case:** Query Available Vouchers for Checkout
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/discounts/available`
- **Quyền hạn (RBAC Policy):** Buyer, Admin, Staff (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId từ HttpContext.GetUserId()
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `sellerId` | Query | `Guid?` | Không | UUIDv4 | Lọc theo Shop |
| `orderTotal` | Query | `decimal?` | Không | Min: 0 | Tổng tiền đơn để thẩm định |

### 2.2 Request Body Record DTO
Không có (Phương thức không yêu cầu body payload).

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `List<AvailableVoucherDto>`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
[
  {
    "voucherCode": "FREESHIP50K",
    "name": "Free Shipping 50K",
    "isEligible": true
  }
]
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu JWT |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Lấy danh sách voucher hợp lệ của shop hoặc toàn sàn.
2. **Bước 2:** Đánh giá điều kiện đơn tối thiểu gán cờ isEligible.
3. **Bước 3:** Trả về List<AvailableVoucherDto>.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Discounts/GET_DISCOUNTS_AVAILABLE.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
