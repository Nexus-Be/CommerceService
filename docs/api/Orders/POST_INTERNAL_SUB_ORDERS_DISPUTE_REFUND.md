# COMMERCE SERVICE - POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund

> **Feature / Use Case:** Service-to-Service Dispute Resolution Refund
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/internal/sub-orders/{subOrderId:guid}/dispute-refund`
- **Quyền hạn (RBAC Policy):** `AllowAnonymous` (Protected by `X-Internal-Service-Key`)
- **Bảo mật:** `InternalServiceKeyGuard.Check(Request, configuration)` xác thực qua header `X-Internal-Service-Key`.
- **Caller Service:** `UserService` (Worker giải quyết khiếu nại giữa Người mua và Người bán).
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `subOrderId` | Route | `Guid` | Có | UUIDv4 format | Mã định danh đơn hàng con của Shop |
| `X-Internal-Service-Key` | Header | `string` | Có | Chuỗi bí mật nội bộ | Khóa bảo mật giao tiếp giữa các service |

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Order/OrderRequests.cs`
- **Tên C# Record:** `DisputeRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record DisputeRefundRequest(
    [Required] Guid DisputeId,
    [Range(0.01, double.MaxValue)] decimal RefundAmount,
    [Required, StringLength(255, MinimumLength = 1)] string Reason,
    string? Note = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "disputeId": "a1000000-0000-0000-0000-000000000001",
  "refundAmount": 850000.0,
  "reason": "Damaged goods in transit confirmed by support",
  "note": "Resolved in favor of buyer under Dispute #DISP-20261121-001"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Order/OrderResponses.cs`
- **Tên C# Record:** `DisputeRefundResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record DisputeRefundResponse(
    Guid SubOrderId,
    Guid RefundId,
    decimal Amount,
    string Status,
    DateTimeOffset CompletedAt
);
```

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "refundId": "r1000000-0000-0000-0000-000000000001",
  "amount": 850000.0,
  "status": "Completed",
  "completedAt": "2026-11-21T03:35:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `INTERNAL_KEY_INVALID` | `Internal service authentication is required.` | Thiếu hoặc sai header `X-Internal-Service-Key` |
| `404 Not Found` | `SUB_ORDER_NOT_FOUND` | `SubOrder was not found.` | Đơn hàng con không tồn tại trong cơ sở dữ liệu |
| `400 Bad Request` | `INVALID_REFUND_STATUS` | `Sub-order status is not eligible for dispute refund.` | Đơn hàng chưa giao (`Delivered`) hoặc chưa xong (`Completed`), hoặc đã hủy/hoàn |
| `400 Bad Request` | `WALLET_REFUND_FAILED` | `Failed to process refund payment via wallet service.` | WalletService từ chối hoàn tiền vào ví người mua |
| `503 Service Unavailable` | `INTERNAL_CONFIGURATION_ERROR` | `Internal service authentication is not configured.` | Môi trường chưa cấu hình biến `INTERNAL_SERVICE_SECRET` |

### 4.1 Bảng Ánh xạ Enum liên quan
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `SubOrderStatus.Delivered` | `4` | `"Delivered"` | Đã giao hàng thành công (đủ điều kiện khiếu nại) |
| `SubOrderStatus.Completed` | `5` | `"Completed"` | Đã hoàn tất nhận hàng (đủ điều kiện khiếu nại) |
| `SubOrderStatus.Refunded` | `7` | `"Refunded"` | Đơn hàng đã được giải ngân hoàn tiền |
| `RefundStatus.Completed` | `1` | `"Completed"` | Yêu cầu hoàn tiền đã hoàn tất |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Kiểm tra `InternalServiceKeyGuard.Check(Request, configuration)`. Nếu sai trả về 401, thiếu config trả về 503.
2. **Bước 2:** Tra cứu `SubOrder` theo `subOrderId`. Nếu không có, ném `SUB_ORDER_NOT_FOUND` (404).
3. **Bước 3:** Kiểm tra trạng thái: chỉ cho phép hoàn tiền nếu `Status` là `Delivered` hoặc `Completed`. Nếu không ném `INVALID_REFUND_STATUS` (400).
4. **Bước 4:** Kiểm tra xem đã có bản ghi `OrderRefund` (trạng thái `Requested`) trước đó hay chưa. Nếu có thì chuyển sang `Completed`, nếu chưa thì tạo mới với `ReasonCode = "DISPUTE_RESOLUTION"`.
5. **Bước 5:** Gọi `_walletClient.RefundSubOrderPaymentAsync` với Idempotency Key `dispute-refund-{DisputeId}`.
6. **Bước 6:** Cập nhật `SubOrder.Status = SubOrderStatus.Refunded`, ghi `OrderStatusHistory` với `ActorRole = System`.
7. **Bước 7:** Commit transaction cơ sở dữ liệu và trả về `200 OK` kèm `DisputeRefundResponse`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã rà soát hợp đồng S2S giữa UserService và CommerceService.
- [x] **Pre-Code:** Tài liệu API nội bộ lưu tại `docs/api/Orders/POST_INTERNAL_SUB_ORDERS_DISPUTE_REFUND.md`.
- [x] **Application:** DTOs `DisputeRefundRequest`, `DisputeRefundResponse` và method `DisputeRefundSubOrderAsync`.
- [x] **Api:** Endpoint `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` bảo vệ bởi `InternalServiceKeyGuard`.
- [x] **Tests:** `DisputeRefundEndpointTests` kiểm thử đầy đủ 13 kịch bản bảo mật, trạng thái hợp lệ và lỗi ví.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` và `dotnet test` vượt qua 100%.
