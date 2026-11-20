# COMMERCE SERVICE - POST /api/v1/refunds/{id}/approve

> **Feature / Use Case:** Approve Order Refund & Credit Buyer Wallet
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/refunds/{id:guid}/approve`
- **Quyền hạn (RBAC Policy):** `Seller`, `Admin`, `Staff` (`[RequireRole("seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** Seller chỉ được duyệt đơn hoàn tiền của shop mình (`refund.SubOrder.SellerId == currentUserId`); Admin/Staff có quyền duyệt mọi yêu cầu hoàn tiền trên hệ thống.
- **Controller:** `Commerce.Api/Controllers/Public/RefundsController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
| Tham số | Vị trí | Kiểu C# | Bắt buộc | Ràng buộc / Giới hạn | Ghi chú |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `id` | Route | `Guid` | Có | UUIDv4 format | Mã định danh bản ghi `OrderRefund` |

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Order/OrderRequests.cs`
- **Tên C# Record:** `ApproveRefundRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record ApproveRefundRequest(
    string? Note = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "note": "Items returned in original condition. Refund approved."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Order/OrderResponses.cs`
- **Tên C# Record:** `RefundDetailResponse`
- **Mã HTTP thành công:** `200 OK`
- **JSON Response Body mẫu (camelCase):**
```json
{
  "id": "r1000000-0000-0000-0000-000000000001",
  "subOrderId": "02000000-0000-0000-0000-000000000001",
  "subOrderNo": "SUB-20261121-0001",
  "buyerId": "11111111-2222-3333-4444-555555555555",
  "sellerId": "99999999-8888-7777-6666-555555555555",
  "amount": 850000.0,
  "reason": "Buyer received damaged product",
  "status": "Completed",
  "sellerNote": "Items returned in original condition. Refund approved.",
  "rejectReason": null,
  "requestedAt": "2026-11-21T01:00:00Z",
  "completedAt": "2026-11-21T01:30:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to approve this refund request.` | Seller không sở hữu đơn hàng con này |
| `404 Not Found` | `REFUND_NOT_FOUND` | `OrderRefund not found.` | Không tìm thấy bản ghi hoàn tiền theo ID |
| `400 Bad Request` | `INVALID_REFUND_STATUS` | `Only pending refund requests can be approved.` | Trạng thái yêu cầu hoàn tiền không phải là `Requested` |
| `400 Bad Request` | `WALLET_REFUND_FAILED` | `Failed to process refund payment via wallet service.` | Wallet Service gặp sự cố khi giải ngân vào ví người mua |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `currentUserId` và kiểm tra quyền sở hữu đối với Shop.
2. **Bước 2:** Lấy bản ghi `OrderRefund` kèm thông tin `SubOrder`.
3. **Bước 3:** Kiểm tra trạng thái yêu cầu phải là `RefundStatus.Requested`.
4. **Bước 4:** Gọi `_walletClient.RefundSubOrderPaymentAsync` để cộng tiền vào ví người mua. Nếu thất bại, ném `WALLET_REFUND_FAILED`.
5. **Bước 5:** Cập nhật `OrderRefund.Status = Completed` và `SubOrder.Status = Refunded`.
6. **Bước 6:** Ghi nhật ký chuyển trạng thái `OrderStatusHistory` với `ActorRole = Seller` (hoặc `SupportStaff`).
7. **Bước 7:** Lưu thay đổi vào DB và trả về `RefundDetailResponse` với mã `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã xác nhận cơ chế hoàn tiền ví và quy tắc kiểm tra quyền sở hữu người bán.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Refunds/POST_REFUNDS_APPROVE.md`.
- [x] **Application:** `OrderService.ApproveRefundAsync` hoàn thiện.
- [x] **Tests:** `RefundGovernanceTests` kiểm thử đầy đủ duyệt và từ chối hoàn tiền.
- [x] **Verify:** `dotnet test` và `dotnet build` đạt 100%.
