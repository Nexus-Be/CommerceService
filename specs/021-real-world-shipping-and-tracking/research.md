# Research & Technical Decisions: Real-World Carrier Logistics and Shipment Tracking

**Feature**: `021-real-world-shipping-and-tracking`  
**Date**: 2026-11-12  
**Status**: Completed  

---

## 1. Technical Decisions & Trade-off Analysis

### Decision 1: Persistent Carrier Tracking Metadata Placement
* **Decision**: Add `CarrierCode` (varchar 50), `TrackingCode` (varchar 100 with B-tree index), `LabelPrintUrl` (varchar 500), `EstimatedDeliveryAt` (timestamptz nullable), and `ShipmentPendingCreation` (boolean default false) directly to `SubOrder` and `AuctionOrder` entities.
* **Rationale**:
  - In Nexus Commerce domain, 1 `SubOrder` corresponds to exactly 1 physical shipment from 1 seller.
  - Adding these columns directly to `SubOrder` avoids an unnecessary 1-to-1 table join (`Shipments`), maximizing query performance when loading order details and summaries.
  - Adding a database B-tree index on `TrackingCode` guarantees sub-millisecond lookups when buyers or logistics carriers query orders by tracking number.
* **Alternatives Considered**:
  - *Separate `Shipments` table*: Creates unnecessary relational complexity, foreign key constraints, and JOIN overhead for a strict 1:1 relationship. Rejected in favor of direct entity columns.
  - *Storing inside `ShippingAddressSnapshot` JSONB*: Makes indexing and SQL querying harder, obscures schema visibility, and violates separation between destination address and carrier dispatch assets. Rejected.

---

### Decision 2: Shipment Timeline Modeling via `OrderStatusHistory`
* **Decision**: Reuse the existing `OrderStatusHistory` domain entity to capture all carrier lifecycle milestone events (`ReadyToPick`, `Picking`, `Packed`, `HandedOver`, `InTransit`, `OutForDelivery`, `DeliveryAttemptFailed`, `Delivered`, `DeliveryFailed`, `Returned`).
* **Rationale**:
  - `OrderStatusHistory` is already mapped in EF Core, supports foreign key references to both `SubOrder` and `AuctionOrder`, and tracks timestamps, actor role, previous status, next status, and descriptive reason notes.
  - Reusing it provides an audit trail with zero schema bloat and maintains architecture consistency across regular orders and auction orders.
* **Alternatives Considered**:
  - *Creating a new `ShipmentTimelineEvent` table*: Adds duplicate audit table with redundant fields (`Timestamp`, `Status`, `Note`). Rejected to preserve database minimalism.

---

### Decision 3: Multi-Shop Master Order State Aggregation Strategy
* **Decision**: In `OrderService.UpdateSubOrderDeliveryStatusAsync`, after updating a `SubOrder`, execute an atomic evaluation of all sibling sub-orders sharing the same `OrderId`:
  - If `all siblings ∈ { Delivered, Completed }` $\rightarrow$ set `Order.Status = OrderStatus.Delivered`.
  - If `all siblings == Cancelled` $\rightarrow$ set `Order.Status = OrderStatus.Cancelled`.
  - Otherwise, maintain current status (e.g. `Paid` / processing).
* **Rationale**:
  - Solves the critical bug where multi-shop master orders remain stuck in `OrderStatus.Paid` indefinitely despite all sellers completing physical deliveries.
  - Immediate in-transaction evaluation guarantees eventual consistency across sub-order updates without distributed messaging or polling queues.
* **Alternatives Considered**:
  - *Background worker polling for order completion*: Introduces polling latency (up to 60s delay) and unnecessary database load. Synchronous evaluation within the webhook transaction is clean, atomic, and fast.

---

### Decision 4: Tiered Delivery Exception & Automated Return/Refund Handling
* **Decision**:
  - `DeliveryAttemptFailed` (e.g. buyer unreachable on attempt 1 or 2): Maintain `SubOrderStatus.Shipped`, append `OrderStatusHistory` record with carrier note, and do not trigger financial refund.
  - `DeliveryFailed` / `Returned` (terminal failure after 3 attempts or package returned to seller warehouse):
    - Update `SubOrder.Status = SubOrderStatus.Cancelled`.
    - Create `OrderCancellation` record with reason `PackageReturned`.
    - If prepaid (`WasPaid == true`), invoke `IWalletClient.RefundSubOrderPaymentAsync` with idempotency key `refund-delivery-returned-{subOrderId}`.
    - Invoke `IFulfillmentClient.ReleaseSubOrderStockAsync` with reason `"PackageReturned"`.
* **Rationale**:
  - Matches physical e-commerce reality (Shopee / Lazada / GHN SLA). Packages are not cancelled on first failed delivery call; only when the carrier returns the package to the seller warehouse is the order cancelled, funds refunded, and inventory restocked.
* **Alternatives Considered**:
  - *Immediate cancellation on first failed delivery call*: Causes premature order cancellation and buyer friction when the customer simply stepped away for an hour. Rejected.

---

### Decision 5: Safe Idempotency & Resilient Shipment Dispatch
* **Decision**:
  - If `IFulfillmentClient.CreateShipmentAsync` succeeds during `SellerAcceptSubOrderAsync`: persist returned credentials, set `ShipmentPendingCreation = false`.
  - If `CreateShipmentAsync` throws a transient 503 error: catch error, log warning, still transition order to `Confirmed`, set `ShipmentPendingCreation = true`.
  - Expose `POST /api/v1/sub-orders/{subOrderId}/shipments/retry` allowing the seller to trigger shipment creation manually once carrier connectivity recovers.
* **Rationale**:
  - Prevents carrier network glitches from blocking the seller's business workflow.
  - Leverages Fulfillment Service's safe idempotency guarantee (calling `/api/v1/internal/shipments` repeatedly for the same `subOrderId` returns the existing shipment without duplicate waybills).
* **Alternatives Considered**:
  - *Fail-closed hard exception aborting seller acceptance*: If GHN has a 10-minute network hiccup, all sellers across the platform are blocked from accepting orders, damaging platform GMV. Rejected.

---

## 2. Best Practices & Governance Verification

- **Constitution Compliance**:
  - Clean Architecture 4 layers respected.
  - Direct EF Core access over `AppDbContext` (no repository pattern).
  - 100% English sentence-case error messages (`"Shipment has already been created for this sub-order."`).
  - SCREAMING_SNAKE_CASE error codes (`SHIPMENT_ALREADY_EXISTS`, `INVALID_SHIPPING_ADDRESS`).
  - Inward-only referencing within `CommerceService`.
- **Fail-Closed Invariant**:
  - No synthetic tracking codes (`$"GHN-{so.SubOrderNo}"`).
  - Genuine carrier credentials or explicit pending status.
