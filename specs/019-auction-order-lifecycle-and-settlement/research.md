# Research & Technical Decisions: Auction Order Lifecycle, Settlement & Automated Reversal

**Feature**: `019-auction-order-lifecycle-and-settlement`  
**Date**: 2026-11-11  
**Status**: Completed  

---

## 1. Technical Decisions & Research Matrix

### Decision 1: Settlement Ingestion & Multi-Winner Idempotency
- **Decision**: Validate settlement idempotency using the composite pair `(AuctionId, WinnerId)`.
  - If an `AuctionOrder` with the identical `(AuctionId, WinnerId)` exists $\rightarrow$ Return `HTTP 200 OK` with existing order data (idempotent duplicate request).
  - If an `AuctionOrder` for `AuctionId` exists with a *different* `WinnerId`:
    - If the previous order is `Status == SubOrderStatus.Cancelled` (original winner defaulted after 24h) $\rightarrow$ Allow creating a new `AuctionOrder` for the runner-up winner.
    - If the previous order is NOT `Cancelled` (active or in progress) $\rightarrow$ Reject with `HTTP 409 Conflict` and code `AUCTION_ALREADY_SETTLED`.
  - If no order exists for `AuctionId` $\rightarrow$ Create a new `AuctionOrder` with `Status = SubOrderStatus.PendingPayment`.
- **Rationale**: Directly aligns with Auction Service contract v1.1.0 (Section 2.3). DB schema analysis confirmed that PostgreSQL table `AuctionOrders` has a unique constraint only on `OrderNo`, NOT on `AuctionId`. This allows multiple historical records for the same `AuctionId` as long as at most one is active.
- **Alternatives Considered**:
  - *Enforcing a unique index on `AuctionId`*: Rejected because it would cause database constraint violations when Auction Service invites the runner-up bidder after the first winner defaults.

---

### Decision 2: 5 Callbacks Sequential Workflow & Outbound REST Client
- **Decision**: Introduce typed HTTP client `IAuctionClient` / `AuctionClient` in `Commerce.Infrastructure/Services/Auction/` targeting `http://auction-service:8005/api/v1/auctions/{auctionId}/settlement/payment-status`. Callbacks are dispatched sequentially:
  1. `"Paid"`: Dispatched immediately upon buyer wallet debit. Payload MUST include `paidAt` (ISO-8601 UTC) and `amount = WinningAmount` (excluding shipping fee).
  2. `"SellerAccepted"`: Dispatched when the seller confirms the order within 24 hours. Auction Service uses this callback to invoke Catalog `mark-auctioned` to permanently close the product.
  3. `"SellerRejected"`: Dispatched when the seller rejects the order. Triggers 100% wallet refund to buyer first. Auction Service marks auction `Failed`, unlocks catalog product, and penalizes seller.
  4. `"SellerExpired"`: Dispatched by background worker when the seller fails to accept within 24 hours (`SellerRespondDeadlineAt < UtcNow`). Triggers 100% wallet refund to buyer first.
  5. `"PaymentTimedOut"`: Dispatched by background worker when the buyer fails to pay within 24 hours (`PaymentDeadlineAt < UtcNow`). Auction Service penalizes buyer and invites runner-up bidder.
- **Rationale**: Satisfies Auction Service contract v1.1.0 Section 3.2 without using message brokers.
- **Alternatives Considered**:
  - *Commerce calling Catalog `mark-auctioned` directly*: Rejected because Auction Service owns the auction lifecycle and catalog product locks.

---

### Decision 3: Financial Fail-Safe Rule for Wallet Reversals
- **Decision**: The buyer's wallet refund (`_walletClient.RefundOrderPaymentAsync`) and database status update (`Status = Cancelled`, `OrderRefund.Status = Completed`) MUST commit to the database before the outbound callback to Auction Service is sent. Outbound callback network failures (5xx, timeout) are logged as warnings with `eventId` for safe retry, but NEVER roll back the buyer's refund.
- **Rationale**: Customer funds must never remain locked due to inter-service network blips. Retrying the callback with the identical `eventId` ensures idempotency on Auction Service.
- **Alternatives Considered**:
  - *Wrapping refund and callback in a single transaction*: Rejected because HTTP calls inside database transactions cause connection pool exhaustion, and failure of a 3rd party notification must not undo a successful money return.

---

### Decision 4: Delivery Webhook Resolution for Auction Orders
- **Decision**: Update `OrdersController.UpdateDeliveryStatus` / `OrderService.UpdateSubOrderDeliveryStatusAsync` to check `SubOrders` first, and if not found, fallback to check `AuctionOrders`. When found, update `auctionOrder.Status` to `Shipped` or `Delivered` and record timestamps (`ShippedAt`, `DeliveredAt`).
- **Rationale**: Reuses existing webhook route `POST /api/v1/sub-orders/{subOrderId}/delivery-status` without changing carrier integration or contract.
- **Alternatives Considered**:
  - *Creating a separate webhook route `POST /api/v1/auction-orders/{id}/delivery-status`*: Rejected because carrier and Fulfillment Service already dispatch status updates to the unified delivery webhook.

---

### Decision 5: Feature Co-location & 1-to-1 Controller Pairing
- **Decision**: Implement a dedicated feature package `Commerce.Application/Services/AuctionOrder/` containing:
  - `IAuctionOrderService.cs`
  - `AuctionOrderService.cs`
  - `AuctionOrderRequests.cs`
  - `AuctionOrderResponses.cs`
  - `AuctionOrderDtos.cs`
  Pair 1-to-1 with thin controller `Commerce.Api/Controllers/Public/AuctionOrdersController.cs`.
- **Rationale**: Complies with Constitution Principle I and local rules `.agents/rules/01_clean_architecture_layers.md`. Prevents bloating `OrderService.cs` (already 1400+ lines) and eliminates regression risk on regular cart/checkout flows.
- **Alternatives Considered**:
  - *Adding auction logic directly into `OrderService.cs`*: Rejected due to violation of Single Responsibility Principle and high regression risk.

---

### Decision 6: Remediation of Latent Bug in `PaymentTimeoutWorker`
- **Decision**: Fix line 160 of `PaymentTimeoutWorker.cs`: replace `SubOrderId = auctionOrder.Id` with `AuctionOrderId = auctionOrder.Id` and `SubOrderId = null`. Add second scanning loop for `Status == SubOrderStatus.Paid && SellerRespondDeadlineAt < now` to auto-cancel and refund unresponsive seller orders.
- **Rationale**: Fixes latent Foreign Key constraint violation `FK_OrderCancellations_SubOrders_SubOrderId` in PostgreSQL and implements Job ORD-09 requirement.

---

## 2. Dependency & Configuration Matrix

| Dependency / Config | Target URL / Value | Handled By |
|---|---|---|
| Auction Service | `http://auction-service:8005` | `IAuctionClient` / `AuctionClient` |
| Fulfillment Service | `http://fulfillment-service:8006` | `IFulfillmentClient` / `FulfillmentClient` |
| Wallet Service | `http://wallet-service:8008` | `IWalletClient` / `WalletClient` |
| Security Header | `X-Internal-Service-Key` | `InternalServiceAuthHandler` |
| Environment Variable | `AUCTION_SERVICE_URL` | `docker-compose.yml`, `appsettings.json` |
