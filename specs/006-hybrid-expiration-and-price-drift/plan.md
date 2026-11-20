# Implementation Plan: Hybrid Payment Expiration & Asymmetric Price Drift Policy

**Feature Identifier**: `006-hybrid-expiration-and-price-drift`  
**Status**: Ready for Implementation  
**Date**: 2026-11-02  

---

## 1. Technical Architecture & Components

```
                      TẠO ĐƠN HÀNG (CreateOrderAsync)
                                     │
         ┌───────────────────────────┴───────────────────────────┐
         ▼                                                       ▼
1. KIỂM TRA GIÁ CATALOG                                 2. KIỂM TRA QUÁ HẠN (Lazy Guard)
   - Lấy giá hiện tại từ Catalog                           - Nếu đơn cũ quá hạn 15m
   - Phân loại: Tăng giá vs Giảm giá                       - Tự động hủy và nhả kho
         │                                                       │
   ┌─────┴─────┐                                                 ▼
[Tăng giá]   [Giảm giá / Bằng giá]                      PASSIVE GUARD ON GET
   │           │                                        - GetOrderDetail
   ▼           ▼                                        - GetSubOrderDetail
Chặn (409)   Tự áp dụng giá rẻ hơn
             và tiếp tục tạo đơn
```

### Component 1: Asymmetric Price Drift in `OrderService.CreateOrderAsync`
- Duyệt qua từng sản phẩm trong Quote, gọi `_catalogClient.GetSkuDetailsAsync(item.SkuId)`.
- Xác định:
  - `priceIncreases = items.Where(i => catalogPrice > i.UnitPrice).ToList()`
  - `priceDrops = items.Where(i => catalogPrice < i.UnitPrice).ToList()`
- **Nếu `priceIncreases.Count > 0`**:
  * Tìm giỏ hàng của Buyer (`_dbContext.Carts.Include(c => c.Items)`).
  * Cập nhật `UnitPriceSnapshot = catalogPrice` cho tất cả items có thay đổi.
  * Cập nhật `cart.SubtotalAmount` và `cart.LastActivityAt`.
  * `await _dbContext.SaveChangesAsync(ct)`.
  * Ném `CommerceDomainException("PRICE_CHANGED", "One or more item prices have increased. Please review updated prices before ordering.", 409)`.
- **Nếu `priceIncreases.Count == 0`**:
  * Với các item trong `priceDrops`: Cập nhật lại `item.UnitPrice = catalogPrice`, tính lại `LineTotal`.
  * Tính lại `ItemsAmount`, `TotalAmount` của `SubOrder` và `Order`.
  * Nếu khách thanh toán Ví (`PaymentMethod.Wallet`): số tiền trừ ví sẽ tính theo `TotalAmount` mới rẻ hơn.
  * Trả về danh sách `AppliedPriceDrops` trong `CreateOrderResponse`.

### Component 2: Passive Guard & Shared Expiration Logic
- Trong `OrderService.cs`:
  Thêm phương thức dùng chung `ExpireOrderIfOverdueAsync(Order order, CancellationToken ct)`.
  - Điều kiện: `order.Status == OrderStatus.PendingPayment && order.PaymentDeadlineAt < DateTimeOffset.UtcNow`.
  - Thực thi:
    * `order.Status = OrderStatus.Cancelled`.
    * Với mỗi `so` trong `order.SubOrders`: `so.Status = SubOrderStatus.Cancelled`, `so.CancelledAt = DateTimeOffset.UtcNow`.
    * Thêm `OrderCancellation` (ActorRole: `System`, ReasonCode: `PaymentTimeout`).
    * Nhả tồn kho qua REST: `await _fulfillmentClient.ReleaseStockAsync(order.Id, CancellationToken.None)`.
    * Nhả voucher: Cập nhật `DiscountCodeUsages` sang `Released` và giảm `DiscountCode.ReservedCount`.
    * `await _dbContext.SaveChangesAsync(ct)`.
- Tích hợp Lazy Check vào:
  - `GetOrderDetailAsync(currentUserId, orderId)`: Gọi `ExpireOrderIfOverdueAsync` trước khi map sang DTO.
  - `GetSubOrderDetailAsync(currentUserId, subOrderId)`: Tương tự.

### Component 3: Active Sweeper Enhancement (`PaymentTimeoutWorker.cs`)
- Nâng cấp `PaymentTimeoutWorker`:
  - Quét `Orders` quá hạn 15 phút.
  - Quét `AuctionOrders` quá hạn 24 giờ (`Status == SubOrderStatus.PendingPayment && PaymentDeadlineAt < now`).
  - Gọi giải phóng voucher và nhả tồn kho.

### Component 4: Contracts & DTOs
- `CreateOrderResponse`: Bổ sung trường `List<PriceDropDto>? AppliedPriceDrops`.
- `PriceDropDto`: `{ Guid SkuId, string ProductName, decimal OldPrice, decimal NewPrice, decimal SavedAmount }`.
