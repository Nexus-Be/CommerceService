# COMMERCE SERVICE - POST /api/v1/orders

> **Feature / Use Case:** Order Creation & Placement
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/orders`
- **Quyền hạn (RBAC Policy):** `Buyer` (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** `buyerId` trích xuất trực tiếp từ JWT `HttpContext.GetUserId()`.
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
Không có (Endpoint nhận payload qua Request Body).

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Order/OrderRequests.cs`
- **Tên C# Record:** `CreateOrderRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record CreateOrderRequest(
    [Required] Guid QuoteId,
    [Required] PaymentMethod PaymentMethod,
    [Required, StringLength(500, MinimumLength = 5)] string ShippingAddress,
    [Required, Phone] string ContactPhone,
    string? Note = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "quoteId": "b1000000-0000-0000-0000-000000000001",
  "paymentMethod": "Wallet",
  "shippingAddress": "123 Nguyen Hue, Ben Nghe Ward, District 1, Ho Chi Minh City",
  "contactPhone": "0901234567",
  "note": "Please ring doorbell upon arrival."
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Order/OrderResponses.cs`
- **Tên C# Record:** `CreateOrderResponse`
- **Mã HTTP thành công:** `201 Created`
- **Khai báo C# Record:**
```csharp
public sealed record CreateOrderResponse(
    Guid OrderId,
    string OrderNo,
    decimal TotalAmount,
    string Status,
    DateTimeOffset PaymentDeadlineAt,
    string? PaymentUrl,
    List<Guid> SubOrderIds,
    DateTimeOffset CreatedAt,
    List<PriceDropDto>? AppliedPriceDrops = null
);
```

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "orderId": "01000000-0000-0000-0000-000000000001",
  "orderNo": "ORD-20261121-0001",
  "totalAmount": 1250000.0,
  "status": "PendingPayment",
  "paymentDeadlineAt": "2026-11-21T03:45:00Z",
  "paymentUrl": null,
  "subOrderIds": [
    "02000000-0000-0000-0000-000000000001"
  ],
  "createdAt": "2026-11-21T03:30:00Z",
  "appliedPriceDrops": []
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `One or more validation errors occurred.` | Vi phạm DataAnnotations trên request model |
| `400 Bad Request` | `QUOTE_EXPIRED` | `Checkout quote has expired. Please recalculate your cart.` | Mã báo giá `quoteId` không tìm thấy trong cache hoặc đã quá hạn 15 phút |
| `400 Bad Request` | `INSUFFICIENT_STOCK` | `One or more items in your order are out of stock.` | Giữ kho thất bại qua FulfillmentService (khi `_failOpen = false`) |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Không có JWT hợp lệ hoặc thiếu `x-user-id` |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access or modify this resource.` | Người gọi không có vai trò hợp lệ |
| `500 Internal Error` | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred while processing the request.` | Ngoại lệ cơ sở dữ liệu hoặc lỗi không xác định |

### 4.1 Bảng Ánh xạ Enum liên quan
| Tên Enum C# | Giá trị DB (Integer) | Giá trị API (PascalCase) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :--- | :--- |
| `OrderStatus.PendingPayment` | `0` | `"PendingPayment"` | Chờ thanh toán (hạn 15 phút) |
| `OrderStatus.Confirmed` | `1` | `"Confirmed"` | Đã thanh toán / Đã xác nhận |
| `PaymentMethod.Wallet` | `0` | `"Wallet"` | Thanh toán qua Ví Nexus |
| `PaymentMethod.COD` | `1` | `"COD"` | Thanh toán khi nhận hàng |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `buyerId` từ `HttpContext.GetUserId()`.
2. **Bước 2:** Lấy báo giá tạm thời `CheckoutQuoteResponse` từ `IDistributedCache` qua `quoteId`. Nếu hết hạn, ném `QUOTE_EXPIRED`.
3. **Bước 3:** Gọi `IFulfillmentClient.ReserveStockAsync` giữ tồn kho với TTL 15 phút (chế độ Fail-Closed mặc định).
4. **Bước 4:** Tạo bản ghi `Order` cha và các bản ghi `SubOrder` theo từng Shop (Seller).
5. **Bước 5:** Xóa các `CartItem` đã đặt mua khỏi giỏ hàng của Buyer.
6. **Bước 6:** Trả về `201 Created` kèm `CreateOrderResponse` chứa `paymentDeadlineAt` (15 phút).

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã đối soát code có sẵn trong CommerceService để tái sử dụng DTO/Enum/Naming.
- [x] **Pre-Code:** Tài liệu API nội bộ này đã được viết và lưu tại `docs/api/Orders/POST_ORDERS.md`.
- [x] **Persistence:** Entity `Order`, `SubOrder`, `OrderItem` và EF Core Configuration đã hoàn thiện.
- [x] **Application:** Service `OrderService.CreateOrderAsync` hoàn chỉnh.
- [x] **Api:** `OrdersController.CreateOrder` trả DTO thuần, HTTP 201 Created.
- [x] **Tests:** Đã viết unit tests bao phủ tạo đơn, hết hạn báo giá, và giữ kho.
- [x] **Verify:** `dotnet build Commerce.sln -c Release` vượt qua với 0 lỗi.
