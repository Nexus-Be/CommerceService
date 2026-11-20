# Research & Architectural Decisions: Fulfillment Integration Remediation

**Feature Directory**: `specs/012-commerce-fulfillment-remediation`  
**Phase**: Phase 0 (Research & Technical Decisions)  
**Date**: 2026-11-04  

---

## Decision 1: Base URL & Port Fallback Alignment

- **Decision**: Update `Commerce.Infrastructure/DependencyInjection.cs` to set the default fallback URL for `IFulfillmentClient` to `http://localhost:8006/` (replacing legacy `http://localhost:8005/`).
- **Rationale**: 
  - Port `8006` is the standardized runtime port for `FulfillmentService` in `launchSettings.json` and platform service registry.
  - Port `8005` conflicts with test stubs and legacy Docker compose configurations.
  - The configuration hierarchy checks `configuration["Services:Fulfillment:Url"]` -> `Environment.GetEnvironmentVariable("FULFILLMENT_SERVICE_URL")` -> fallback `http://localhost:8006/`.
- **Alternatives Considered**:
  - Retaining port `8005`: Rejected; causes immediate connection refused errors during local multi-service testing.

---

## Decision 2: Elimination of Mock Fallbacks & Strict Fail-Closed (503)

- **Decision**: Completely remove synthetic fallbacks in `FulfillmentClient.cs`:
  1. Remove hardcoded `30000m` fallback in `CalculateShopShippingFeeAsync`. On network or 5xx error, throw `CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Fulfillment service is temporarily unreachable for shipping fee calculation.", 503)`.
  2. Remove mock tracking code `GHN-{yyyyMMdd}-...` and mock label PDF URL in `CreateShipmentAsync`. On failure, throw `CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "Unable to create shipment with fulfillment service.", 503)`.
  3. Remove `_failOpen = true` in `BatchReleaseStockAsync`. Propagate domain exceptions on non-success status codes.
- **Rationale**:
  - Mock fallbacks conceal platform outages and generate phantom shipment tracking numbers that cannot be tracked with GHN.
  - Throwing 503 on shipment creation allows `SellerAcceptSubOrderAsync` to roll back transactionally, preventing a sub-order from entering `Confirmed` without a valid carrier tracking code.
- **Alternatives Considered**:
  - Partial Fail-Open: Rejected by governance guardrails; financial calculations and legal carrier documents require strict accuracy.

---

## Decision 3: Partial Reservation Release on SubOrder Rejection

- **Decision**: Maintain Master `orderId` as the reservation `referenceId` established during checkout. When a shop rejects a sub-order (`SellerRejectSubOrderAsync`), CommerceService calls:
  `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected`
  passing JSON body:
  ```json
  {
    "items": [
      {
        "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
        "quantity": 2
      }
    ]
  }
  ```
- **Rationale**:
  - Resolves `CRITICAL-04` where CommerceService previously passed `subOrderId` to `/release`, causing Fulfillment to return 404 / AlreadyReleased because reservations are keyed by `orderId`.
  - Passing the specific `itemsToRelease` allows FulfillmentService to release only the inventory of the rejected shop, leaving the reservations of other shops under the same master order intact.
- **Alternatives Considered**:
  - Creating independent reservation records per `subOrderId` during checkout: Rejected because it multiplies network calls during checkout and complicates atomic order compensation.

---

## Decision 4: Active Stock Release in PaymentTimeoutWorker with Failure Resilience

- **Decision**: In `PaymentTimeoutWorker.cs`:
  1. Eliminate the "Zero-HTTP call" assumption.
  2. Actively collect expired order IDs and call `await _fulfillmentClient.BatchReleaseStockAsync(orderIds, ct)` targeting `/api/v1/internal/inventories/reservations/batch-release`.
  3. If the HTTP call fails or times out, log a warning, proceed with database cancellation, and set `InventoryReleased = false` on `OrderCancellation` records.
  4. Subsequent worker executions will query unreleased cancellations and retry releasing inventory.
- **Rationale**:
  - Resolves `WARNING-01`. While FulfillmentService has an auto-expiration job, relying solely on it creates window inconsistencies where stock is held longer than 15 minutes. Active release provides immediate stock replenishment.
  - The `InventoryReleased = false` flag prevents network failures from blocking database order cancellation.
- **Alternatives Considered**:
  - Rolling back order cancellation on HTTP failure: Rejected; leaves customer orders stuck in `PendingPayment` indefinitely during network partitions.

---

## Decision 5: Complete Delivery Status Webhook Lifecycle Mapping

- **Decision**: Update `OrderService.UpdateSubOrderDeliveryStatusAsync` to handle all 9 Fulfillment `ShipmentStatus` states:
  - `ReadyToPick`, `Picking`, `Packed`: Idempotent 200 OK, maintain `SubOrderStatus.Confirmed`, update `UpdatedAt`.
  - `HandedOver`, `InTransit`, `Shipped`: Update `SubOrderStatus.Shipped` and record `ShippedAt`.
  - `Delivered`: Update `SubOrderStatus.Delivered` and record `DeliveredAt`.
  - `DeliveryFailed`, `Returned`: Update `SubOrderStatus.Cancelled` and `CancelledAt`; insert `OrderCancellation` with `ReasonCode` (`DeliveryFailed` / `PackageReturned`); create `OrderRefund` with `RefundStatus.Requested` if the order was paid.
  - Invalid / Unknown Status: Throw `CommerceDomainException("INVALID_DELIVERY_STATUS", "The provided delivery status is not supported.", 400)`.
- **Rationale**:
  - Resolves `WARNING-02` without requiring a risky PostgreSQL enum migration for `SubOrderStatus`.
  - Matches the established pattern in `SellerRejectSubOrderAsync` where `OrderCancellation` and `OrderRefund` represent granular dispute and return metadata.
- **Alternatives Considered**:
  - Adding `DeliveryFailed` and `Returned` values to `SubOrderStatus` enum: Evaluated and rejected to avoid altering database enum mappings across existing migrations.

---

## Decision 6: Constitution v1.4.0 Ratification & Architecture Spec Deprecation

- **Decision**: 
  1. Increment `.specify/memory/constitution.md` to **Version 1.4.0**.
  2. Replace Principle II ("Distributed Sagas via MassTransit") with "Synchronous Typed HTTP REST Orchestration & Transactional Compensation".
  3. Add a Deprecation banner to `docs/COMMERCE_EVENTS_AND_ASYNC_SPEC.md` pointing to `docs/api_catalog.md` and synchronous REST contracts.
- **Rationale**:
  - Resolves `SPEC-DRIFT-01`. Synchronizes technical documentation with the actual code in `Commerce.sln`.
- **Alternatives Considered**:
  - Deleting `COMMERCE_EVENTS_AND_ASYNC_SPEC.md`: Deprecating is preferred over deleting to preserve commit audit history.
