# Quickstart & Verification Guide: Fulfillment Integration Remediation

**Feature Directory**: `specs/012-commerce-fulfillment-remediation`  
**Phase**: Phase 1 (Design & Verification)  
**Date**: 2026-11-04  

This document describes runnable validation scenarios that prove the remediation works end-to-end without requiring full staging infrastructure.

---

## Scenario 1: Default Port Fallback Verification

**Goal**: Verify that when `Services:Fulfillment:Url` is unset, `FulfillmentClient` resolves to `http://localhost:8006/`.

```bash
# Verify DependencyInjection configuration via unit test or reflection
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~FulfillmentClientTests.Constructor_WhenNoConfig_ShouldUsePort8006"
```
**Expected Outcome**: Base address of HttpClient is `http://localhost:8006/`.

---

## Scenario 2: Fail-Closed Shipping Fee & Shipment Creation (No Mocks)

**Goal**: Verify that `CalculateShopShippingFeeAsync` and `CreateShipmentAsync` never return dummy values when Fulfillment fails.

1. **Calculate Shipping Fee on 500 / Network Error**:
   - Mock handler returns HTTP 500 or throws `HttpRequestException`.
   - Call `CalculateShopShippingFeeAsync(...)`.
   - Assert: Throws `CommerceDomainException` with `ErrorCode == "FULFILLMENT_SERVICE_UNAVAILABLE"` and `StatusCode == 503`.
   - Assert: Value `30000m` is NEVER returned.

2. **Create Shipment on 500 / Network Error**:
   - Mock handler returns HTTP 500 or throws `HttpRequestException`.
   - Call `CreateShipmentAsync(...)`.
   - Assert: Throws `CommerceDomainException` with `ErrorCode == "FULFILLMENT_SERVICE_UNAVAILABLE"` and `StatusCode == 503`.
   - Assert: Synthetic string `GHN-...` is NEVER returned.

---

## Scenario 3: Partial Reservation Release on SubOrder Rejection

**Goal**: Verify that rejecting a sub-order releases inventory using the Master `orderId` and specific items list.

1. **Setup**:
   - Create an order with 2 sub-orders under Master `orderId`.
   - Seller rejects sub-order A (`SellerRejectSubOrderAsync`).
2. **Execute**:
   - Inspect outbound request to Fulfillment:
     - URL: `POST api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected`
     - Body: contains items from sub-order A only.
3. **Assert**:
   - Reservation for sub-order B remains active.
   - Sub-order A status is `SubOrderStatus.Cancelled`.
   - `OrderCancellation` record is persisted with `ReasonCode == "SELLER_REJECTED"`.

---

## Scenario 4: Active Stock Release in PaymentTimeoutWorker

**Goal**: Verify that `PaymentTimeoutWorker` sends HTTP batch release and handles network partitions safely.

1. **Happy Path**:
   - Create expired pending orders.
   - Run worker `ProcessExpiredOrdersAsync`.
   - Assert: Calls `BatchReleaseStockAsync` targeting `/api/v1/internal/inventories/reservations/batch-release`.
   - Assert: All orders marked `Cancelled` with `InventoryReleased = true`.

2. **Failure Resilience Path**:
   - Mock Fulfillment batch-release to throw network exception / 503.
   - Run worker `ProcessExpiredOrdersAsync`.
   - Assert: Orders are still marked `Cancelled` in DB (not stuck in `PendingPayment`).
   - Assert: `OrderCancellation.InventoryReleased` is recorded as `false`.

---

## Scenario 5: Webhook Delivery Status Lifecycle Mapping

**Goal**: Verify that all 9 Fulfillment delivery statuses are processed according to domain rules.

1. **Preparation States**:
   - Call webhook with `status: "Picking"`.
   - Assert: Sub-order status remains `Confirmed`, returns HTTP 200 OK.
2. **In-Transit States**:
   - Call webhook with `status: "InTransit"` (or `"HandedOver"`).
   - Assert: Sub-order status becomes `Shipped`, `ShippedAt` is recorded.
3. **Delivered State**:
   - Call webhook with `status: "Delivered"`.
   - Assert: Sub-order status becomes `Delivered`, `DeliveredAt` is recorded.
4. **Failure States**:
   - Call webhook with `status: "DeliveryFailed"` (or `"Returned"`).
   - Assert: Sub-order status becomes `Cancelled`, `CancelledAt` recorded, `OrderCancellation` created, and `OrderRefund` created with status `Requested` if paid.
5. **Invalid State**:
   - Call webhook with `status: "UnknownStatus123"`.
   - Assert: Throws `CommerceDomainException("INVALID_DELIVERY_STATUS", ..., 400)`.

---

## Scenario 6: Full Solution Build & Unit Test Verification

```bash
# 1. Full Solution Build (Release)
dotnet build Commerce.sln -c Release

# 2. Complete Unit & E2E Test Suite Pass
dotnet test Commerce.sln -c Release
```
**Expected Outcome**: 0 Warnings, 0 Errors, 100% Tests Passed.
