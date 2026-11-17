# Research & Decision Log: Comprehensive Order Cancellation & Resource Rollback Lifecycle

**Feature**: `027-order-cancellation-and-resource-lifecycle`  
**Date**: 2026-11-17  
**Status**: Completed  

---

## 1. Pre-Shipping Cancellation Window & Boundary Guard

### Context
In e-commerce, allowing cancellations while an order is in transit creates severe physical and financial discrepancies: packages are already on the delivery vehicle, shipping labels have been printed and dispatched, and carrier fees are incurred.

### Decision
`CommerceService` enforces a strict pre-shipping cancellation window:
- For **Master Orders** (`CancelOrderAsync`): Rejection occurs if `Status` is not `PendingPayment` or `Paid`, OR if ANY child `SubOrder` has reached `Shipping`, `Delivered`, or `Completed`.
- For **Sub-Orders** (`CancelSubOrderAsync`, `SellerRejectSubOrderAsync`): Rejection occurs if `Status` is not `PendingPayment`, `Paid`, or `Confirmed`.
- Error returned: HTTP 400 with `errorCode: "ORDER_CANNOT_BE_CANCELLED"` and message `"Order cannot be cancelled once shipped, delivered, or completed."`

### Alternatives Considered
- *Allow cancellation during Shipping with delivery recall*: Rejected because GHN carrier APIs do not guarantee synchronous package intercept, leading to lost inventory and double refunds.

---

## 2. Invariant: Zero Post-Delivery Buyer Refund

### Context
The platform's operational policy explicitly specifies: *"Hệ thống tôi không có ý định triển khai feature hoàn tiền cho khách khi shipper giao hàng thành công"*.

### Decision
- Once an order reaches `SubOrderStatus.Delivered`, the only allowable progression is to `SubOrderStatus.Completed` (either immediately when the buyer clicks *"Confirm Receipt"* or automatically after 3 days via `PaymentTimeoutWorker`).
- When `Completed`, `SellerReceiveAmount` is disbursed from System Escrow to the Seller's wallet.
- The platform does NOT expose an automated buyer return/refund API post-delivery.
- All sales are final upon delivery confirmation or delivery expiration.

### Alternatives Considered
- *Implement 7-day return window*: Rejected per explicit business requirement.

---

## 3. High-Throughput Batch Release for Payment Timeout Worker

### Context
When orders expire after 15 minutes of non-payment, the background worker processes batches of up to 50 orders at a time. Making individual HTTP calls to Fulfillment Service and WalletService for each order would cause severe N+1 overhead.

### Decision
- **Fulfillment**: Collect all expired `orderIds` into a single `List<Guid>` and invoke `_fulfillmentClient.BatchReleaseStockAsync(orderIds)` in a single outbound REST call.
- **Wallet**: Zero outbound calls. Orders in `PendingPayment` were never debited, so no escrow exists and no refund is needed.
- **Vouchers**: Batch update `DiscountCodeUsages` from `Reserved` to `Released` and decrement `ReservedCount` in one transaction.

### Rationale
- Eliminates 50 redundant inter-service calls per batch.
- Guarantees instant stock recovery for other shoppers.

---

## 4. COD Financial Isolation Guardrails

### Context
If a cancellation path checks only `PaidAt.HasValue` without verifying `PaymentMethod == PaymentMethod.Wallet`, an edge case or manual status flip on a COD order could trigger a false wallet refund, causing real financial loss.

### Decision
Every cancellation path (`CancelOrderAsync`, `CancelSubOrderAsync`, `SellerRejectSubOrderAsync`, and `UpdateSubOrderDeliveryStatusAsync` on `Returned`) MUST strictly verify:
```csharp
if (wasPaid && so.PaymentMethod == PaymentMethod.Wallet)
{
    // Initiate wallet refund
}
else if (so.PaymentMethod == PaymentMethod.Cod)
{
    // Log and skip wallet call completely
}
```
`OrderRefund` records are only created when real wallet money is returned.
