# Research & Technical Decisions: WalletService Integration Contract Alignment

**Feature Branch**: `017-wallet-contract-alignment`  
**Date**: 2026-11-10  
**Status**: Completed  
**Reference Document**: `c:\Users\Admin\Downloads\integration-contract.md` (WalletService verified 2026-11-10)

---

## 1. Identity Propagation & Security Boundary

### Problem Statement
CommerceService currently attempts to forward a `Bearer <token>` JWT header when calling `WalletClient.PayOrderAsync`. However, WalletService has disabled internal service key validation and does NOT validate incoming JWTs. WalletService strictly trusts identity headers forwarded by the API Gateway: `x-user-id` (UUID) and `x-user-roles`. Missing identity returns HTTP 401 `UNAUTHORIZED`.

### Decision
1. **User-Context Calls (`PayOrderAsync`, `GetWalletBalanceAsync`, `InitiateDirectCheckoutAsync`, `GetDirectCheckoutStatusAsync`)**:
   - Must explicitly transmit the `x-user-id: <buyer-uuid>` HTTP header.
   - Signature of `IWalletClient.PayOrderAsync` is updated to `PayOrderAsync(Guid orderId, Guid buyerId, decimal amount, CancellationToken cancellationToken = default)`.
   - Fallback extraction from `_httpContextAccessor?.HttpContext` if `buyerId` is omitted.
2. **Service-to-Service Calls (`SettleSellerSubOrderAsync`, `RefundOrderPaymentAsync`, `RefundSubOrderPaymentAsync`)**:
   - In accordance with Section 1 and Section 3 of the integration contract, S2S routes (`/settle`, `/refund`) have **no identity or role check at all**.
   - These calls must NOT transmit `Authorization` or `x-user-id` headers; they execute strictly within the private container network.

### Rationale
- Aligns 100% with WalletService's trust boundary specification.
- Prevents immediate HTTP 401 `UNAUTHORIZED` failures during checkout and balance lookup.

### Alternatives Considered
- *Attempting to pass JWT Bearer token*: Rejected. WalletService does not inspect or validate JWTs.
- *Transmitting internal service key (`X-Internal-Service-Key`)*: Rejected. Middleware is permanently disabled in both services.

---

## 2. Problem Details RFC 9110 Parsing & Status Code Mapping

### Problem Statement
Commerce's `WalletClient.cs` currently uses a naive status-code fallback where HTTP 409 is mapped to `INSUFFICIENT_BALANCE`. WalletService's contract defines that insufficient balance returns **HTTP 400** with `application/problem+json` payload containing `"errorCode": "INSUFFICIENT_BALANCE"`. HTTP 409 is reserved exclusively for logical conflicts (`PAYMENT_ORDER_CONFLICT`, `SETTLEMENT_CONFLICT`, `REFUND_CONFLICT`, `REFUND_EXCEEDS_PAYMENT`).

### Decision
1. `WalletClient` parses the JSON error body according to RFC 9110 Problem Details:
   - Primary: Extract `errorCode` (string, `SCREAMING_SNAKE_CASE`).
   - Secondary: Extract `detail` (string, English Sentence-case description).
2. Status code and error code mapping:
   - `errorCode == "INSUFFICIENT_BALANCE"` (HTTP 400) $\rightarrow$ non-transient balance failure.
   - `errorCode == "PAYMENT_ORDER_CONFLICT"` (HTTP 409) $\rightarrow$ order was already paid or has a conflicting payment mode.
   - `errorCode == "WALLET_NOT_FOUND"` (HTTP 404) $\rightarrow$ buyer has no wallet.
   - `HTTP 5xx` or connection timeouts $\rightarrow$ `IsTransientFailure = true`.

### Rationale
- Complies with Section 2.1 ("Branch on `errorCode`, not on `detail` text").
- Eliminates erroneous misclassification of HTTP 400 validation failures and HTTP 409 conflict states.

---

## 3. Idempotency Architecture: Business Key Anchoring

### Problem Statement
Commerce currently attempts to pass custom `Idempotency-Key` headers (e.g. `Idempotency-Key: settle-{subOrderId}`). WalletService does not read this header; instead, idempotency is anchored directly on business keys in the route URL (`{orderId}` and `{subOrderId}`).

### Decision
- Remove the redundant `Idempotency-Key` header from all S2S calls in `WalletClient`.
- Rely entirely on the URL path business key:
  - `/api/v1/wallets/orders/{orderId}/payment` $\rightarrow$ Idempotent on `{orderId}`.
  - `/api/v1/wallets/sub-orders/{subOrderId}/settle` $\rightarrow$ Idempotent on `{subOrderId}`.
  - `/api/v1/wallets/orders/{orderId}/refund` $\rightarrow$ Idempotent on `{orderId}`.
  - `/api/v1/wallets/sub-orders/{subOrderId}/refund` $\rightarrow$ Idempotent on `{subOrderId}`.
- Replaying any of these calls with the exact same parameters returns HTTP 200 with the original transaction record.

### Rationale
- Eliminates dead header noise and avoids false assumptions about header-based deduplication.
- Guarantees safe retryability upon network timeout.

---

## 4. Direct VNPay Integration & Reconciliation Polling Strategy

### Problem Statement
Commerce's `OrderService.cs` (lines 539-541) currently hardcodes a dummy sandbox URL:
`$"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?orderId={order.Id}"`.
Furthermore, Commerce has no background worker to verify when the buyer has paid VNPay via IPN, meaning orders can remain stuck in `PendingPayment` until expired by `PaymentTimeoutWorker`.

### Decision
1. **Initiate Direct Checkout**:
   - Expose `IWalletClient.InitiateDirectCheckoutAsync(Guid orderId, Guid buyerId, decimal amount, string? returnUrl, CancellationToken ct)`.
   - Calls `POST /api/v1/wallets/checkout/direct` with header `x-user-id: <buyer-uuid>` and body:
     ```json
     { "orderId": "<uuid>", "amount": 100000.00, "returnUrl": "https://..." }
     ```
   - Receives `{ "paymentUrl": "...", "transactionId": "...", "status": "Pending" }`.
   - Populates the genuine `paymentUrl` in `CreateOrderResponse`.
2. **Asynchronous Payment Verification via Polling**:
   - Implement `VnPayPaymentPollingWorker` (`BackgroundService`) executing every 30 seconds.
   - Queries `GET /api/v1/wallets/checkout/direct/{orderId}` for orders where `PaymentMethod == PaymentMethod.VnPay` and `Status == OrderStatus.PendingPayment`.
   - If response `status == "Success"`:
     - Transition `order.Status = OrderStatus.Paid`.
     - Transition all `subOrders.Status = SubOrderStatus.Paid`.
     - Record `order.WalletTransactionId = response.TransactionId`.
     - Execute `_fulfillmentClient.CommitStockAsync(orderId)`.
   - If response `status == "Failed"` or deadline expires:
     - Mark order cancelled and trigger `_fulfillmentClient.BatchReleaseStockAsync`.

### Rationale
- Complies strictly with Section 7: "VNPay-paid order: gateway calls POST .../checkout/direct ... -> VNPay calls the signed IPN -> Commerce polls GET .../checkout/direct/{orderId} until status is Success or Failed. Never treat the browser return as proof of payment."

---

## 5. Automated Escrow Settlement & Refund Synchronization

### Problem Statement
`OrderService.BuyerConfirmSubOrderReceiptAsync` and `ConfirmDeliveryMasterOrderAsync` have stubs for `_walletClient.SettleSellerSubOrderAsync`, and `SellerRejectSubOrderAsync` has stubs for `RefundSubOrderPaymentAsync`. We must ensure they handle the exact contract responses (`200 OK` with `WalletOrderTransactionResponse` and `409` conflicts).

### Decision
1. **Escrow Settlement**:
   - Call `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` with body `{ "sellerId": "<uuid>", "amount": <amount> }`.
   - If `200 OK`: settlement completed.
   - If `409 SETTLEMENT_CONFLICT`: check if already settled; if so, log idempotent success.
2. **Sub-Order Refund**:
   - Call `POST /api/v1/wallets/sub-orders/{subOrderId}/refund` with body `{ "userId": "<buyer-uuid>", "amount": <amount>, "reason": "<reason>" }`.
   - Sub-order refund skips master payment lookup (Commerce owns the amount).
   - Upon `200 OK`: set `OrderRefund.Status = RefundStatus.Completed` and `CompletedAt = UtcNow`.
3. **Master Order Refund (Compensation)**:
   - Call `POST /api/v1/wallets/orders/{orderId}/refund` with body `{ "userId": "<buyer-uuid>", "amount": <amount>, "reason": "<reason>" }`.
   - Requires prior `PurchaseDebit` on that order.

### Rationale
- Completely closes GAP-01 (Automated Refund) and GAP-02 (Escrow Settlement) identified in `docs/WALLET_CAPABILITY_GAP_REPORT.md`.
