# COMMERCE SERVICE - POST /api/v1/checkouts/quote

> **Feature / Use Case:** Checkout Preview & Shipping Fee Quotation (15-min TTL)
> **Trạng thái:** ✅ VERIFIED
> **Ngày tạo:** 2026-11-21
> **Tác giả:** AI Assistant (via `service-api-doc` skill)

---

## 1. Thông tin Định tuyến & RBAC (Routing & Authorization)
- **HTTP Method:** `POST`
- **Route Endpoint:** `/api/v1/checkouts/quote`
- **Quyền hạn (RBAC Policy):** `Buyer` (`[RequireRole("buyer", "seller", "admin", "staff")]`)
- **Quy tắc Sở hữu (Resource Ownership):** `buyerId` trích xuất trực tiếp từ JWT `HttpContext.GetUserId()`.
- **Controller:** `Commerce.Api/Controllers/Public/OrdersController.cs`
- **Application Contract:** `Commerce.Application/Services/Order/IOrderService.cs`

---

## 2. Đầu vào & Quy chuẩn Validation (Input & Validation Schema)

### 2.1 Route Parameters & Query Parameters
Không có (nhận qua Request Body).

### 2.2 Request Body Record DTO
- **Vị trí file:** `Commerce.Application/Services/Order/OrderRequests.cs`
- **Tên C# Record:** `CheckoutQuoteRequest`
- **Khai báo C# & DataAnnotations:**
```csharp
public sealed record CheckoutQuoteRequest(
    [Required, MinLength(1)] List<Guid> CartItemIds,
    ShippingAddressInput? ShippingAddress = null,
    string? ContactPhone = null,
    PaymentMethod PaymentMethod = PaymentMethod.Wallet,
    string? CarrierCode = null,
    List<string>? VoucherCodes = null,
    Guid? AddressId = null
);
```

- **JSON Request Body mẫu (camelCase):**
```json
{
  "cartItemIds": [
    "c1000000-0000-0000-0000-000000000001",
    "c2000000-0000-0000-0000-000000000002"
  ],
  "addressId": "ad100000-0000-0000-0000-000000000001",
  "paymentMethod": "Wallet",
  "voucherCodes": ["FREESHIP50K", "DISCOUNT10"]
}
```

---

## 3. Đầu ra & Hợp đồng DTO thuần (Response Contract - Pure DTO)

- **Vị trí file:** `Commerce.Application/Services/Order/OrderResponses.cs`
- **Tên C# Record:** `CheckoutQuoteResponse`
- **Mã HTTP thành công:** `200 OK`
- **Khai báo C# Record:**
```csharp
public sealed record CheckoutQuoteResponse(
    Guid QuoteId,
    decimal GrandTotal,
    decimal WalletBalance,
    List<ShopOrderQuote> Shops,
    List<string> AppliedVouchers,
    List<string>? Warnings,
    DateTimeOffset ExpiredAt
);
```

- **JSON Response Body mẫu (Pure DTO - camelCase):**
```json
{
  "quoteId": "b1000000-0000-0000-0000-000000000001",
  "grandTotal": 1250000.0,
  "walletBalance": 5000000.0,
  "shops": [
    {
      "sellerId": "99999999-8888-7777-6666-555555555555",
      "shopName": "Official Tech Store",
      "items": [
        {
          "cartItemId": "c1000000-0000-0000-0000-000000000001",
          "productId": "p1000000-0000-0000-0000-000000000001",
          "skuId": "s1000000-0000-0000-0000-000000000001",
          "productName": "Wireless Earbuds Pro",
          "quantity": 1,
          "unitPrice": 1200000.0,
          "lineTotal": 1200000.0
        }
      ],
      "itemsAmount": 1200000.0,
      "productDiscountAmount": 0.0,
      "shopDiscountAmount": 0.0,
      "systemDiscountAmount": 0.0,
      "discountAmount": 0.0,
      "shippingFeeAmount": 50000.0,
      "totalAmount": 1250000.0
    }
  ],
  "appliedVouchers": ["FREESHIP50K"],
  "warnings": null,
  "expiredAt": "2026-11-21T03:55:00Z"
}
```

---

## 4. Ma trận Mã lỗi RFC 9110 (Error Matrix)

| HTTP Status | Error Code | Message (Sentence case) | Điều kiện kích hoạt (Trigger Condition) |
| :---: | :--- | :--- | :--- |
| `400 Bad Request` | `VAL_INVALID_INPUT` | `Cart item IDs cannot be empty.` | Danh sách `cartItemIds` bị rỗng |
| `400 Bad Request` | `CART_IS_EMPTY` | `No valid cart items found to generate quote.` | Các sản phẩm trong giỏ không hợp lệ hoặc đã bị xóa |
| `401 Unauthorized` | `UNAUTHORIZED` | `Authentication is required to access this resource.` | Thiếu JWT xác thực người dùng |
| `403 Forbidden` | `FORBIDDEN` | `You do not have permission to access these cart items.` | Món hàng trong giỏ thuộc về tài khoản khác |
| `503 Service Unavailable` | `FULFILLMENT_UNAVAILABLE` | `Unable to calculate shipping fee with logistics carrier.` | Dịch vụ tính cước GHN / Fulfillment không phản hồi |

---

## 5. Kế hoạch Thực thi & Checklist Kiểm chứng (Pre-Code Plan & Checklist)

### 5.1 Các bước thực thi nghiệp vụ (Execution Flow Plan)
1. **Bước 1:** Trích xuất `buyerId` từ context.
2. **Bước 2:** Lấy danh sách `CartItem` từ database, kiểm tra tính hợp lệ và quyền sở hữu của buyer.
3. **Bước 3:** Nhóm các sản phẩm theo từng `SellerId` (đơn hàng đa người bán).
4. **Bước 4:** Gọi `IFulfillmentClient.CalculateShippingFeeAsync` để tính cước vận chuyển GHN cho từng shop.
5. **Bước 5:** Áp dụng công cụ tính Voucher/Discount qua `IVoucherCalculationEngine`.
6. **Bước 6:** Đóng gói `CheckoutQuoteResponse`, lưu vào Redis Cache với thời hạn 15 phút (TTL 15m), trả về `200 OK`.

### 5.2 Checklist hoàn thành
- [x] **Pre-Code:** Đã rà soát hợp đồng tính cước và phân rã đơn hàng đa người bán.
- [x] **Pre-Code:** Tài liệu lưu tại `docs/api/Orders/POST_CHECKOUTS_QUOTE.md`.
- [x] **Application:** `OrderService.QuoteCheckoutAsync` tích hợp cache và cước phí.
- [x] **Tests:** Đã viết unit test bao phủ tính cước, voucher và kiểm tra hết hạn.
- [x] **Verify:** `dotnet test` và `dotnet build` hoàn tất 100%.
