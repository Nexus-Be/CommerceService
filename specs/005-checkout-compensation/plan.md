# Implementation Plan: Checkout Database Failure Rollback & Compensation Mechanism

**Feature Identifier**: `005-checkout-compensation`  
**Status**: Ready for Implementation  
**Date**: 2026-11-02  

---

## 1. Technical Architecture & Component Changes

```
┌────────────────────────────────────────────────────────┐
│                   OrderService                         │
│               (CreateOrderAsync)                       │
└──────────────────────────┬─────────────────────────────┘
                           │
       ┌───────────────────┴───────────────────┐
       ▼                                       ▼
1. Reserve & Commit Stock             2. Direct Wallet Payment
(FulfillmentClient: :8005)              (WalletClient: :8008)
       │                                       │
       └───────────────────┬───────────────────┘
                           │
                           ▼
              3. dbContext.SaveChangesAsync()
                           │
         ┌─────────────────┴─────────────────┐
     [Success]                           [Exception]
         │                                   │
         ▼                                   ▼
4. Return 201 Created          COMPENSATION CIRCUIT BREAKER:
                               ├─► ReleaseStockAsync(orderId)
                               ├─► RefundOrderPaymentAsync(orderId, buyerId, amount)
                               └─► Throw CommerceDomainException(500)
```

### Component 1: `IWalletClient` & `WalletClient`
- Thêm phương thức `RefundOrderPaymentAsync(Guid orderId, string userId, decimal amount, string reason, string? idempotencyKey, CancellationToken ct)` để thực hiện hoàn tiền đơn hàng cha trong tình huống lỗi persistence cấp cao nhất.
- Endpoint đích tại Wallet Service: `POST api/v1/wallets/refund`. Payload: `{ "orderId": orderId, "userId": userId, "amount": amount, "reason": reason }`.

### Component 2: `OrderService.CreateOrderAsync`
- Bọc toàn bộ quá trình `SaveChangesAsync()` và dọn dẹp Cart trong khối `try ... catch (Exception dbEx)`.
- Khi xảy ra lỗi:
  1. Ghi log cảnh báo mức `CRITICAL`: `"CRITICAL: Database persistence failed for Order '{OrderId}'. Initiating distributed compensation rollback."`.
  2. Bù trừ tồn kho: `await _fulfillmentClient.ReleaseStockAsync(orderId, CancellationToken.None);`.
  3. Bù trừ tài chính: Nếu `paid == true`, gọi `await _walletClient.RefundOrderPaymentAsync(orderId, buyerId.ToString(), order.TotalAmount, "Order database persistence failure", $"compensation-db-{orderId}", CancellationToken.None);`.
  4. Ném `CommerceDomainException("ORDER_CREATION_FAILED", "An error occurred while saving the order. All inventory allocations and payments have been safely rolled back.", 500)`.

### Component 3: Unit Tests (`OrderServiceTests.cs`)
- Viết test case: `CreateOrderAsync_ShouldRollbackStockAndRefundWallet_WhenDatabaseSaveFails`.
- Xác nhận:
  - `_fulfillmentClient.ReleaseStockAsync` được gọi đúng 1 lần.
  - `_walletClient.RefundOrderPaymentAsync` được gọi đúng 1 lần nếu đã thanh toán ví.
  - Ném ngoại lệ với status code 500 và mã lỗi `ORDER_CREATION_FAILED`.
