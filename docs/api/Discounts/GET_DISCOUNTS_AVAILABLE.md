# COMMERCE SERVICE - GET /api/v1/discounts/available

> **Feature / Use Case:** Query Available Vouchers for Order Checkout
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `GET`
- **Route Endpoint:** `/api/v1/discounts/available`
- **Quyền hạn (RBAC Policy):** `Buyer`, `Admin`, `Staff` (`[RequireRole("buyer", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** `buyerId` trích xuất trực tiếp từ JWT `HttpContext.GetUserId()`.
- **Controller:** `Commerce.Api/Controllers/Public/DiscountsController.cs`
- **Application Contract:** `Commerce.Application/Services/Discount/IDiscountService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `sellerId` | Query | `Guid?` | Không | UUIDv4 format | Lọc theo Shop bán cụ thể |
| `orderTotal` | Query | `decimal?` | Không | Min: 0 | Tổng giá trị đơn hàng để so sánh điều kiện tối thiểu |

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Discount/DiscountResponses.cs`
- **Tên C# Record:** `List<AvailableVoucherDto>`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (camelCase):**
```json
[
  {
    "voucherCode": "FREESHIP50K",
    "name": "Free Shipping 50K",
    "discountType": "Shipping",
    "discountValue": 50000.0,
    "maxDiscountAmount": 50000.0,
    "minOrderAmount": 200000.0,
    "isEligible": true,
    "ineligibleReason": null,
    "expiredAt": "2026-12-31T23:59:59Z"
  }
]
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Vai trò người dùng không hợp lệ |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `buyerId` từ `HttpContext.GetUserId()`.
2. **Bước 2:** Lấy danh sách voucher hợp lệ từ `DiscountService` (áp dụng theo shop hoặc hệ thống).
3. **Bước 3:** Đánh giá tính khả dụng dựa trên `orderTotal` (gán cờ `isEligible`).
4. **Bước 4:** Trả về danh sách `AvailableVoucherDto` với mã `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã xác nhận chuẩn voucher cho hệ sinh thái đa shop.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Discounts/GET_DISCOUNTS_AVAILABLE.md`.
- [x] **Application:** `DiscountService.GetAvailableVouchersAsync` hoàn thiện.
- [x] **Tests:** `DiscountServiceTests` kiểm thử đầy đủ các điều kiện áp dụng voucher.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 100%.
