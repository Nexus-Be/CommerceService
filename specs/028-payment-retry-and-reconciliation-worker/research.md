# Research & Architectural Decisions: Payment Retry & Reconciliation Engine

**Feature**: `028-payment-retry-and-reconciliation-worker`  
**Date**: 2026-11-17  
**Status**: Completed  

---

## 1. Repay Pending Order Architecture (`POST /api/v1/orders/{orderId}/pay`)

### Decision
Implement `RepayOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct)` in `OrderService.cs` and expose via `POST /api/v1/orders/{orderId}/pay` on `OrdersController`.

### Rationale
- During initial checkout, transient failures (network timeouts, WalletService blips) leave an order in `OrderStatus.PendingPayment` with an active 15-minute stock reservation.
- Allowing buyers to repay the master order directly via `_walletClient.PayOrderAsync(order.Id, order.BuyerId, order.TotalAmount)` restores user flow without needing to re-add items or re-issue quotes.
- If payment succeeds:
  1. `order.Status = OrderStatus.Paid` and `order.WalletTransactionId` is recorded.
  2. All child `SubOrder` records update to `SubOrderStatus.Paid` with `PaidAt = UtcNow`.
  3. `_fulfillmentClient.CommitStockAsync(order.Id)` is invoked to finalize stock deductions.
  4. Status histories are recorded for auditability.
- If payment fails due to insufficient balance, HTTP 400 `INSUFFICIENT_WALLET_BALANCE` is returned with sentence-case message `"Your wallet balance is insufficient to pay for this order."`.
- If deadline (`order.PaymentDeadlineAt`) has passed, HTTP 400 `ORDER_PAYMENT_DEADLINE_EXPIRED` is returned with `"Payment deadline for this order has expired."`.

### Alternatives Considered
- *Cancel and force re-checkout*: Poor user experience; forces buyer to rebuild cart and risk losing reserved items.
- *Per-SubOrder repay*: Complex and disjointed because master order was created as a single financial checkout transaction.

---

## 2. Reconciliation & Self-Healing Worker Strategy

### Decision
Integrate financial self-healing directly into `PaymentTimeoutWorker.cs` by introducing two dedicated sweeps:
1. `ReconcilePendingRefundsAsync`: Retries `OrderRefunds` with `Status == RefundStatus.Requested`.
2. `ReconcileUnsettledEscrowsAsync`: Retries `SubOrders` with `Status == SubOrderStatus.Completed && !IsEscrowSettled && PaymentMethod == PaymentMethod.Wallet && SellerReceiveAmount > 0`.

### Rationale
- `PaymentTimeoutWorker` already manages order lifecycle sweeps using scoped DI services (`ICommerceDbContext`, `IWalletClient`, `IFulfillmentClient`).
- Co-locating the self-healing sweeps within the background worker avoids extra timer thread overhead and provides a unified background log.
- All wallet retry calls use deterministic idempotency keys:
  - Refund retry: `$"refund-retry-{refund.Id}"` or existing idempotency key.
  - Settlement retry: `$"settle-{subOrder.Id}"`.
- Fail-safe: Any individual failure in the loop is logged with warning; the loop continues processing subsequent records without throwing out of the background worker.

### Alternatives Considered
- *Separate BackgroundService (`FinancialReconciliationWorker`)*: Unnecessary thread overhead for an OJT student project; adds deployment configuration complexity without measurable throughput benefits.
- *Message Queue / Outbox retry*: Strictly prohibited by CommerceService Constitution (Rule II: Synchronous REST only, zero RabbitMQ/MassTransit).

---

## 3. Escrow Settlement Tracking (`SubOrder.IsEscrowSettled`)

### Decision
Add `public bool IsEscrowSettled { get; set; } = false;` to `SubOrder` entity and configure in `SubOrderConfiguration.cs` with default `false`.

### Rationale
- Existing code determines settlement dynamically via `_walletClient.SettleSellerSubOrderAsync`, but if the call fails transiently, there is no persistent flag tracking whether funds were disbursed.
- Setting `IsEscrowSettled = true`:
  - When `SettleSellerSubOrderAsync` succeeds during `ConfirmSubOrderReceiptAsync`.
  - When COD orders complete (`PaymentMethod.Cod` does not use digital escrow; courier handles cash directly, so `IsEscrowSettled` is set to `true` immediately to prevent phantom retries).
  - When the reconciliation sweep successfully executes `SettleSellerSubOrderAsync`.

### Alternatives Considered
- *Querying WalletService API on-demand for every sub-order*: Severe N+1 inter-service latency and potential cascading failures if WalletService is busy.

---

## 4. Financial Transparency Endpoints

### Decision
1. `GET /api/v1/orders/{orderId}/refunds`:
   - Controller: `OrdersController.cs`
   - Authorization: Current user must be buyer (`order.BuyerId == currentUserId`) or admin/staff.
   - Return: `List<OrderRefundResponse>` with fields `id`, `subOrderId`, `orderId`, `amount`, `refundType`, `status`, `reasonCode`, `reasonNote`, `requestedAt`, `completedAt`.
2. `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown`:
   - Controller: `OrdersController.cs`
   - Authorization: Current user must be seller (`subOrder.SellerId == currentUserId`) or admin/staff.
   - Return: `SubOrderFinancialBreakdownResponse` with fields `subOrderId`, `subOrderNo`, `sellerId`, `totalAmount`, `itemsAmount`, `productDiscountAmount`, `shopDiscountAmount`, `systemDiscountAmount`, `shippingFeeAmount`, `platformFeeAmount`, `sellerReceiveAmount`, `isEscrowSettled`, `status`, `paymentMethod`, `completedAt`.

### Rationale
- Meets the requirement for transparent, audit-ready financial inspection for both buyers and sellers.
- Follows existing RFC 9110 error protocols and thin controller patterns.

### Alternatives Considered
- *Exposing raw DB entity*: Violates pure DTO return rule and leaks navigation properties.
