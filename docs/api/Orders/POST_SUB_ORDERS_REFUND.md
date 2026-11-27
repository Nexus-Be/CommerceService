# COMMERCE SERVICE - POST /api/v1/sub-orders/{subOrderId}/refund

> **Feature / Use Case:** Request Sub-Order Refund
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/sub-orders/{subOrderId}/refund`
- **Quyền hạn (RBAC Policy):** Buyer, Seller, Admin, Staff (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Buyer sở hữu đơn
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `subOrderId` | Route | `Guid` | Có | UUIDv4 | ID đơn con |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `SubOrderRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record SubOrderRefundRequest([Required] string Reason, string? Note = null);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "reason": "Damaged goods."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `OkResult`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "message": "Refund request submitted successfully."
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `INVALID_REFUND_STATUS` | `Sub-order status is not eligible for refund request.` | Chưa giao hàng |
| `409 Conflict` | `REFUND_REQUEST_ALREADY_EXISTS` | `A pending refund request already exists for this sub-order.` | Đã có yêu cầu |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác minh đơn con ở Delivered hoặc Completed.
2. **Bước 2:** Tạo bản ghi OrderRefund trạng thái Requested.
3. **Bước 3:** Trả về 200 OK.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/POST_SUB_ORDERS_REFUND.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
