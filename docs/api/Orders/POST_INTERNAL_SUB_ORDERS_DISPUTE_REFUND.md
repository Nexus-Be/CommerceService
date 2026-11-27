# COMMERCE SERVICE - POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund

> **Feature / Use Case:** Dispute Resolution Refund S2S
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/internal/sub-orders/{subOrderId}/dispute-refund`
- **Quyền hạn (RBAC Policy):** AllowAnonymous (`X-Internal-Service-Key`)
- **Quy tắc Sở hữu (Resource Ownership):** S2S từ UserService
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `subOrderId` | Route | `Guid` | Có | UUIDv4 | ID đơn con |

### 2.2 Request Body Record DTO
- **Tên C# Record:** `DisputeRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record DisputeRefundRequest(
    [Required] Guid DisputeId,
    [Range(0.01, double.MaxValue)] decimal RefundAmount,
    [Required] string Reason,
    string? Note = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "disputeId": "a1000000-0000-0000-0000-000000000001",
  "refundAmount": 850000.0,
  "reason": "Damaged goods in transit"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `DisputeRefundResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "refundId": "r1000000-0000-0000-0000-000000000001",
  "amount": 850000.0,
  "status": "Completed"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Sai key |
| `400 Bad Request` | `INVALID_REFUND_STATUS` | `Sub-order status is not eligible for dispute refund.` | Chưa giao hàng |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Xác thực khóa nội bộ.
2. **Bước 2:** Kiểm tra đơn ở Delivered hoặc Completed.
3. **Bước 3:** Hoàn tiền ví Buyer qua WalletClient.
4. **Bước 4:** Chuyển đơn sang Refunded.
5. **Bước 5:** Trả về DisputeRefundResponse.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/POST_INTERNAL_SUB_ORDERS_DISPUTE_REFUND.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
