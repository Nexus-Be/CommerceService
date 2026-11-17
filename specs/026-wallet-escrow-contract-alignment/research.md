# Research & Decision Log: Wallet Escrow Contract Alignment & Advanced Financial Error Handling

**Feature**: `026-wallet-escrow-contract-alignment`  
**Date**: 2026-11-17  
**Status**: Completed  

---

## 1. Master Order Escrow Drawdown Strategy

### Context
When a buyer pays for an e-commerce order containing items from multiple sellers, `CommerceService` calls `POST /api/v1/wallets/orders/{orderId}/payment` with the master order ID `order.Id` and the total amount. Inside `WalletService`, the working-tree implementation attaches the `OrderEscrow` record to the master `orderId`.

When a buyer cancels the entire order before shipping, previous code looped through all `SubOrder` records and called `POST /api/v1/wallets/sub-orders/{subOrderId}/refund`. As documented in `commerce-wallet-handoff.md` (Section 6.1 & 6.3), Wallet's strict escrow handler checks whether the ID in the route matches an existing `OrderEscrow`. Because `subOrderId` does not match the master `orderId`, Wallet either falls back to an unbacked legacy credit or returns `409 ORDER_ESCROW_INSUFFICIENT`.

### Decision
When cancelling the entire master order (`isBuyer == true` on master order cancellation), `CommerceService` MUST call:
```http
POST /api/v1/wallets/orders/{orderId}/refund
Content-Type: application/json

{
  "userId": "<buyerGuid>",
  "amount": <order.TotalAmount>,
  "reason": "<reasonText>"
}
```
Only when cancelling an individual sub-order (e.g. single shop rejection or single sub-order cancellation) does Commerce call `POST /api/v1/wallets/sub-orders/{subOrderId}/refund`.

### Rationale
- Renders the refund 100% compliant with Wallet's `OrderEscrow` parent ledger.
- Prevents escrow depletion conflicts and multiple partial deduction race conditions.
- Minimizes network round-trips from N HTTP requests down to 1 HTTP request.

### Alternatives Considered
- *Continue calling sub-order refund in a loop*: Rejected because Wallet documentation explicitly warns that child paths do not guarantee backing by the parent payment and will fail under strict escrow checks.
- *Request Wallet to add parent order parameter to child refund*: Good long-term request, but for full master order cancellation, calling the master order refund endpoint is the cleaner and canonical design.

---

## 2. Advanced ProblemDetails Interpretation & Error Matrix

### Context
Wallet returns `application/problem+json` with RFC 9110 compliant schemas. In addition to standard `400 INSUFFICIENT_BALANCE`, the updated Wallet handoff introduces specific financial collision codes:
- `409 ORDER_ESCROW_INSUFFICIENT`: Order escrow has insufficient funds to satisfy payout or refund.
- `409 REFUND_EXCEEDS_PAYMENT`: Refund request amount is strictly greater than original debit.
- `409 SETTLEMENT_CONFLICT`: A settlement was already processed for this reference with different seller or amount.
- `409 PAYMENT_ORDER_CONFLICT`: Wallet debit conflicts with an existing order payment.
- `503 FEATURE_DISABLED`: Wallet feature flag `ReleaseFeatures:LegacyCommerceCreditsEnabled` is false.

### Decision
Enhance `WalletClient.ParseProblemDetails` to explicitly parse the `errorCode` field and populate `WalletPaymentResult` and logging channels.
- When `503 FEATURE_DISABLED` is received: Log an explicit `CRITICAL` configuration alert naming `LegacyCommerceCreditsEnabled`.
- When `409 ORDER_ESCROW_INSUFFICIENT` or `409 REFUND_EXCEEDS_PAYMENT` is received: Mark `OrderRefund.Status = Requested`, do NOT blindly retry in background loops, and flag for financial audit reconciliation.

### Rationale
Prevents silent masking of configuration errors and halts futile retry loops on unrecoverable accounting errors.

---

## 3. Idempotency & Replay Mechanism

### Context
`WalletService` does NOT read an `Idempotency-Key` HTTP header for order payments, seller settlements, or refunds. Instead, Wallet relies on the URL path parameter (`orderId` or `subOrderId`) as the definitive business reference:
- If a subsequent request has the **identical ID, recipient, and amount**, Wallet returns the existing transaction (`200 OK`) without double-charging or double-crediting.
- If a request has the same ID but a **different amount or recipient**, Wallet returns `409 CONFLICT`.

### Decision
CommerceService will rely on route-based idempotency:
- When retrying or executing compensation, ensure the exact original `orderId`, `buyerId`, and `amount` are supplied.
- Avoid passing arbitrary random UUIDs in headers expecting Wallet to key on them.

---

## 4. Freezing Direct Stripe Checkout

### Context
Wallet's working tree contains an experimental Direct Stripe Checkout integration (`POST /api/v1/wallets/checkout/direct`). However:
1. Wallet has NO outbound webhook or callback mechanism to notify Commerce that Stripe payment succeeded.
2. The cancellation endpoint `POST .../checkout/direct/{orderId}/cancel` is unimplemented.
3. Wallet cannot refund Stripe payments (`Refund a direct Stripe payment is not currently exposed`).

### Decision
`CommerceService` will strictly freeze and not expose Stripe direct checkout. The valid `PaymentMethod` enum remains:
- `PaymentMethod.Wallet (0)`: Fully supported with Escrow and automated refund.
- `PaymentMethod.Cod (2)`: Fully supported with cash-on-delivery lifecycle and zero wallet interaction.

---

## 5. Final Sale Invariant Post-Delivery

### Context
The user explicitly specified: *"Hệ thống tôi không có ý định triển khai feature hoàn tiền cho khách khi shipper giao hàng thành công"*.

### Decision
Once a sub-order reaches `SubOrderStatus.Delivered` or `SubOrderStatus.Completed`:
- No buyer refund can be initiated via REST API.
- Any attempt to call cancel endpoints returns HTTP 400 `ORDER_CANNOT_BE_CANCELLED`.
- After 3 days, escrow is released to the seller via `PaymentTimeoutWorker`. All sales are final upon delivery.
