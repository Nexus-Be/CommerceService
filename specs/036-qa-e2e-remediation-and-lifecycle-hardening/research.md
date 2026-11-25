# Research Findings: QA E2E Remediation, Buy Now Contract Parity, Stock Resilience & Lifecycle Hardening

**Feature**: `036-qa-e2e-remediation-and-lifecycle-hardening`  
**Date**: 2026-11-21  
**Context**: Alignment with QA Phase 2 E2E test report across CommerceService, AuctionService, FulfillmentService, and UserService.  

---

## 1. Technical Decisions & Research Topics

### Decision 1: Nullable `WinningBidId` for Buy Now Settlements

- **Context**: In Buy Now transactions, the user purchases the item immediately at the seller's fixed Buy Now price. No bid is placed in the bid ledger. `AuctionService` sets `WinningBidId = null`. However, Commerce's `AuctionSettlementRequest` marks `WinningBidId` as `[Required] Guid`, and `AuctionOrder` entity defines `required Guid WinningBidId`, causing model validation to reject settlements with HTTP 400.
- **Decision**:
  - Update `AuctionSettlementRequest` to define `Guid? WinningBidId = null`.
  - In `AuctionOrderService.ReceiveSettlementAsync`: enforce that `WinningBidId` is required if and only if `source != AuctionOrderSource.BuyNow`. If `source` is `WINNER` or `SECOND_CHANCE` and `WinningBidId` is null/empty, throw HTTP 400 `VAL_INVALID_SETTLEMENT`.
  - Update entity `AuctionOrder.WinningBidId` to `Guid?`.
  - In idempotency matching: adjust the comparison `identicalOrder.WinningBidId != request.WinningBidId` so that when both are null, they are considered equal and do not trigger a `SETTLEMENT_CONFLICT` 409 exception.
- **Rationale**:
  - Eliminates the block on Buy Now settlements without breaking contract safety for standard auctions.
  - Matches upstream `AuctionService` behavior where Buy Now transactions have no associated bid record.
- **Alternatives Considered**:
  - *Alternative A: Generate a synthetic/dummy Guid in AuctionService for Buy Now.* Rejected because generating artificial bid IDs pollutes the data model and creates phantom references that do not exist in the bid ledger.

---

### Decision 2: Fail-Closed Stock Reservation Policy

- **Context**: `FulfillmentClient` currently sets `_failOpen = true` by default if `StockPolicy:FailOpen` is missing or empty. When the warehouse inventory reservation service fails (e.g. 500 error), Commerce logs a warning and proceeds with order checkout, resulting in unreserved orders and overselling.
- **Decision**:
  - Change `_failOpen` default to `false` in non-development environments.
  - `_failOpen` will only be `true` if `StockPolicy:FailOpen` is explicitly set to `"true"`, or if `ASPNETCORE_ENVIRONMENT == "Development"` and the setting is absent.
  - Elevate the reservation failure log from `LogWarning` to `LogError`.
  - When reservation fails in fail-closed mode, return `false`, causing `OrderService` to throw HTTP 400 `INSUFFICIENT_STOCK`. On connection errors, throw HTTP 503 `STOCK_RESERVATION_FAILED`.
- **Rationale**:
  - Adheres strictly to Constitution Article II: "Clients MUST operate fail-closed: never swallow critical errors (`_failOpen = false`)."
  - Protects merchant inventory integrity and stops selling products that cannot be physically fulfilled.
- **Alternatives Considered**:
  - *Alternative A: Keep fail-open for local and dev, but hardcode fail-closed in code.* Chosen as the solution via environment check (`ASPNETCORE_ENVIRONMENT`).

---

### Decision 3: Handling Carrier Status `"Returning"` & Auction Order Lifecycle Guardrails

- **Context**: GHN delivery lifecycle includes interim delivery attempts (`DeliveryAttemptFailed`, `DeliveryFailed`), a return-in-progress phase (`Returning`), and final return receipt (`Returned`). Currently, Commerce's webhook handler does not handle `"Returning"`. Furthermore, for `AuctionOrder`, receiving `DeliveryFailed` prematurely cancelled the order while the package was still with the courier.
- **Decision**:
  - Add `Returning = 8` to `SubOrderStatus` domain enum.
  - In `OrderService.UpdateSubOrderDeliveryStatusAsync`, add a branch for `status == "Returning"`: update `SubOrder.Status = SubOrderStatus.Returning` (or keep in `Shipped` with history record `"Returning"`), update `UpdatedAt`, and add an `OrderStatusHistory` entry.
  - In `UpdateAuctionOrderDeliveryStatusAsync`: remove `DeliveryFailed` from the immediate cancellation condition. Only transition to `Cancelled` when status is definitively `"Returned"`.
- **Rationale**:
  - Accurately tracks the physical transit of goods. The merchant and buyer both see that the package is being returned to the warehouse, avoiding premature refunds or inventory restocking before physical receipt.
- **Alternatives Considered**:
  - *Alternative A: Treat `"Returning"` identically to `"Returned"` immediately.* Rejected because the package is still with the carrier. If the package is lost or damaged during return, different compensation rules apply.

---

### Decision 4: Automated Buyer Reputation Penalty on Delivery Failure ("Bom Hàng")

- **Context**: When a package is marked `"Returned"` due to recipient refusal or 3 failed delivery attempts, the seller incurs round-trip shipping costs. Commerce previously refunded the buyer and restocked inventory without applying any penalty to the buyer.
- **Decision**:
  - In `OrderService.UpdateSubOrderDeliveryStatusAsync`, when entering the `Returned` branch:
    - Check if `_userClient != null`.
    - Dispatch `_userClient.AdjustReputationAsync`:
      - `UserId`: `so.BuyerId`
      - `TargetRole`: `"Buyer"`
      - `PartnerUserId`: `so.SellerId`
      - `ScoreDelta`: `-5`
      - `Reason`: `"PackageReturnedDeliveryFailed"`
      - `ReferenceType`: `"Order"`
      - `ReferenceId`: `so.SubOrderNo`
      - `Note`: `"Automated penalty for uncollected package returned to seller."`
    - Wrap the call in a try/catch block: log errors if `UserService` is unreachable, but do not abort order cancellation and inventory release.
- **Rationale**:
  - Mirror the existing reputation penalty applied to sellers who cancel orders (`ScoreDelta: -5`).
  - Deter malicious ordering and delivery refusal.
- **Alternatives Considered**:
  - *Alternative A: Only penalize if the order was COD.* Rejected because even with wallet payment, the merchant loses shipping fees and time; serial refusal should affect user trust score regardless of payment method.

---

### Decision 5: Clean DB Migration Foreign Key Remediation

- **Context**: Migration `20261106065814_FixSubOrderJsonSeed.cs` attempts to delete seed row `02000000-0000-0000-0000-000000000001` in table `SubOrders`. However, `DiscountCodeUsages`, `OrderInvoices`, and `OrderStatusHistories` inserted in `InitialCreate` retain foreign key constraints pointing to this `SubOrderId`. When applying migrations on a fresh database, PostgreSQL throws error `23503: FK_DiscountCodeUsages_SubOrders_SubOrderId`.
- **Decision**:
  - Update `20261106065814_FixSubOrderJsonSeed.cs`:
    - Before calling `migrationBuilder.DeleteData(table: "SubOrders", ...)`, delete the dependent child rows:
      - `DiscountCodeUsages` key: `d2000000-0000-0000-0000-000000000001`
      - `OrderInvoices` key: `05000000-0000-0000-0000-000000000001`
      - `OrderStatusHistories` key: `04000000-0000-0000-0000-000000000001`
      - `SubOrderItems` key: `03000000-0000-0000-0000-000000000001`
    - Then safely delete `SubOrders` and `Orders`.
- **Rationale**:
  - Guarantees referential integrity order (children deleted before parent).
  - Ensures clean deployment from an empty database without requiring manual seed deletion.

---

### Decision 6: Internal Dispute Resolution S2S Refund Endpoint

- **Context**: In Phase 2 flow P2-05 (Disputes/Reports), when `UserService` resolves a report in favor of the buyer, it must trigger an immediate refund on the delivered order. Currently, `OrdersController` requires buyer JWT authorization, and `RefundsController` requires seller/admin JWT session. `UserService` runs as an asynchronous background worker and cannot obtain buyer or seller JWT tokens.
- **Decision**:
  - Expose `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` on `OrdersController`.
  - Decorate with `[AllowAnonymous]` and execute `InternalServiceKeyGuard.Check(Request, configuration)` to enforce `X-Internal-Service-Key` authentication.
  - Implement `DisputeRefundSubOrderAsync` in `OrderService`:
    - Validates sub-order exists and is in `Delivered` or `Completed` status.
    - Creates or updates `OrderRefund` with `RefundType.Full`, `ReasonCode = "DISPUTE_RESOLUTION"`, `Status = RefundStatus.Completed`.
    - Dispatches `_walletClient.RefundSubOrderPaymentAsync` to credit buyer's wallet.
    - Transitions `SubOrder.Status = SubOrderStatus.Refunded`.
- **Rationale**:
  - Provides a clean, secure service-to-service channel for inter-microservice dispute settlement.
  - Ensures full transactional compensation in compliance with Constitution Article II.
