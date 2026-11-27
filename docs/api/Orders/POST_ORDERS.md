# COMMERCE SERVICE - POST /api/v1/orders

> **Feature / Use Case:** Create Master Order & SubOrders
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/orders`
- **Quyền hạn (RBAC Policy):** Buyer (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** buyerId từ HttpContext.GetUserId()
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)
### 2.1 Route Parameters & Query Parameters
Không có tham số route hay query bắt buộc.

### 2.2 Request Body Record DTO
- **Tên C# Record:** `CreateOrderRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record CreateOrderRequest(
    [Required] Guid QuoteId,
    [Required] PaymentMethod PaymentMethod,
    [Required] string ShippingAddress,
    [Required] string ContactPhone,
    string? Note = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "quoteId": "b1000000-0000-0000-0000-000000000001",
  "paymentMethod": "Wallet",
  "shippingAddress": "123 Nguyen Hue",
  "contactPhone": "0901234567"
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)
> **LƯU Ý:** CommerceService CHỈ trả về DTO thuần hoặc `PagedData<T>`, KHÔNG bọc qua `ApiResponse<T>`. Vỏ envelope sẽ do Gateway tự động đóng gói tại biên.

- **Tên C# Record:** `CreateOrderResponse`
- **Mã HTTP thành công:** `201 Created`
- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "01000000-0000-0000-0000-000000000001",
  "orderNo": "ORD-20261121-0001",
  "totalAmount": 1250000.0,
  "status": "PendingPayment"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `QUOTE_EXPIRED` | `Checkout quote has expired.` | Hết hạn 15m |
| `400 Bad Request` | `INSUFFICIENT_STOCK` | `One or more items in your order are out of stock.` | Hết hàng kho (Fail-Closed) |
| `400 Bad Request` | `VOUCHER_EVALUATION_FAILED` | `Unable to validate or reserve the selected voucher codes. Please request a new quote.` | Lỗi kiểm tra hoặc giữ chỗ voucher |
| `409 Conflict` | `VOUCHER_NO_LONGER_VALID` | `One or more selected vouchers can no longer be applied. Please review your vouchers and request a new quote.` | Quote chứa mã bị từ chối hoặc mã không còn hiệu lực khi tạo đơn |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Lấy quoteId từ cache.
2. **Bước 2:** Khóa tồn kho qua FulfillmentClient.
3. **Bước 3:** Tạo Master Order và SubOrders.
4. **Bước 4:** Dọn giỏ hàng.
5. **Bước 5:** Trả về 201 Created.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/POST_ORDERS.md`.
- [x] **Application:** Tầng Application và DTOs đã hoàn thiện.
- [x] **Api:** Thin Controller đã ghép cặp 1-to-1, trả DTO thuần.
- [x] **Tests:** Đã viết unit/integration tests bao phủ luồng chính và mã lỗi.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua 100% với 0 lỗi.
