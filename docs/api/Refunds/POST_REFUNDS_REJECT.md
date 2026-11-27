# COMMERCE SERVICE - POST /api/v1/refunds/{id}/reject

> **Feature / Use Case:** Reject Order Refund Request
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/refunds/{id}/reject`
- **Quyền hạn (RBAC Policy):** Seller, Admin, Staff (`[RequireRole("seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller sở hữu shop hoặc Admin/Staff
- **Controller:** `Commerce.Api/Controllers/Public/RefundsController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `id` | Route | `Guid` | Có | UUIDv4 | Mã hoàn tiền |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `RejectRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record RejectRefundRequest([Required] string Reason, string? Note = null);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "reason": "Products returned are damaged by buyer."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `RefundDetailResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "id": "r1000000-0000-0000-0000-000000000001",
  "status": "Rejected"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Reason is required.` | Thiếu lý do từ chối |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh quyền từ chối.
2. **Bước 2:** Cập nhật trạng thái Refund sang Rejected kèm lý do.
3. **Bước 3:** Trả về RefundDetailResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Refunds/POST_REFUNDS_REJECT.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
