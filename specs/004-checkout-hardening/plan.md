# Implementation Plan: Checkout Hardening & Remediation

**Feature Identifier**: `004-checkout-hardening`  

---

## 1. Architecture Approach

### 1.1 Cache Abstraction (`IDistributedCache`)
Sử dụng `IDistributedCache` abstraction chuẩn của .NET 8:
- Trong `Commerce.Infrastructure`: Đăng ký `services.AddDistributedMemoryCache()` làm bộ nhớ đệm phân tán mặc định (dễ dàng hoán đổi sang Redis `AddStackExchangeRedisCache` qua file cấu hình môi trường Production).
- Trong `OrderService`: Inject `IDistributedCache`, serialize/deserialize `CachedQuote` với JSON serializer và `AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)`.

### 1.2 Atomic Pop & Double-Submit Protection
Khi nhận `CreateOrderRequest`:
1. Đọc key `checkout_quote:{quoteId}` từ `_distributedCache`.
2. Nếu không tìm thấy $\rightarrow$ ném lỗi `QUOTE_EXPIRED`.
3. Xóa key ngay lập tức (`await _distributedCache.RemoveAsync(cacheKey, ct)`).
4. Khi gọi thanh toán ví, truyền `idempotencyKey = $"quote-wallet-{request.QuoteId}"`.

### 1.3 Sub-Order Stock Release
- Thêm `ReleaseSubOrderStockAsync(Guid subOrderId, List<StockReservationItem> items, CancellationToken ct)` vào `IFulfillmentClient` và `FulfillmentClient`.
- Trong `SellerRejectSubOrderAsync`: Chỉ nhả các SKU thuộc về `SubOrder` đó.

### 1.4 Automatic Refund on Reject
- Thêm `RefundSubOrderPaymentAsync(Guid subOrderId, string userId, decimal amount, string reason, string? idempotencyKey, CancellationToken ct)` vào `IWalletClient` và `WalletClient`.
- Khi `so.PaidAt.HasValue`, tạo `OrderRefund` và gọi refund ví.

### 1.5 Price Drift Check & Catalog Inactivity
- Trong `CreateOrderAsync`: Duyệt qua các items trong Quote, gọi `_catalogClient.GetSkuDetailsAsync`. Nếu `!sku.IsPurchasable` $\rightarrow$ ném `CATALOG_SKU_INACTIVE`. Nếu `sku.UnitPrice != item.UnitPrice` $\rightarrow$ ném `PRICE_CHANGED`.

### 1.6 Cart Quantity Stock Check
- Trong `CartService.UpdateItemQuantityAsync`: Tìm `CartItem` theo ID, gọi `_fulfillmentClient.CheckStockAvailabilityAsync(item.SkuId, request.Quantity)`. Nếu không đủ hàng $\rightarrow$ ném `INSUFFICIENT_STOCK`.

### 1.7 Real Wallet Balance
- Thêm `GetWalletBalanceAsync(Guid userId, CancellationToken ct)` vào `IWalletClient` & `WalletClient`.
- Gán vào `CheckoutQuoteResponse.WalletBalance`.
