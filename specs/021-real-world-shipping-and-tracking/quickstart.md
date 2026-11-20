# Quickstart Validation Guide: Real-World Carrier Logistics & Shipment Tracking

**Feature**: `021-real-world-shipping-and-tracking`  
**Date**: 2026-11-12  

This quickstart guide documents verification scenarios to validate the implementation end-to-end.

---

## Prerequisites

- Solution builds successfully: `dotnet build Commerce.sln -c Release`
- Test suite passing: `dotnet test Commerce.sln -c Release`

---

## Validation Scenarios

### Scenario 1: Seller Acceptance & Real Carrier Shipment Dispatch
1. **Setup**: Create an order in `Paid` status with 1 sub-order.
2. **Execute**:
   - Authenticate as the seller.
   - Send `POST /api/v1/orders/{orderId}/accept` with body `{ "subOrderId": "<guid>" }`.
3. **Verify**:
   - Response contains authentic `trackingCode` (e.g. `GHN-SGN-1029384`), `carrierCode = "GHN"`, and `labelPrintUrl`.
   - Inspect database: `SubOrders` row has non-null `TrackingCode`, `CarrierCode`, `LabelPrintUrl`.
   - `OrderStatusHistories` contains transition `FromStatus = Paid`, `ToStatus = Confirmed`.
   - Call `GET /api/v1/orders/{orderId}/shipments` and assert `trackingCode` equals `GHN-SGN-1029384` (zero occurrences of `GHN-{subOrderNo}`).

---

### Scenario 2: Chronological Timeline Events from Carrier Webhook
1. **Setup**: Sub-order is in `Confirmed` status.
2. **Execute Callbacks**:
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "Packed" }`.
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "HandedOver" }`.
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "InTransit", "location": "Tan Binh Hub" }`.
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "Delivered" }`.
3. **Verify**:
   - Query `GET /api/v1/sub-orders/{id}/shipments/timeline`.
   - Assert `timeline` array has 5 sequential events (`Confirmed`, `Packed`, `Shipped`, `InTransit`, `Delivered`).
   - Event `InTransit` includes location `"Tan Binh Hub"`.
   - Final status of sub-order is `Delivered`.

---

### Scenario 3: Master Order Multi-Shop Synchronization
1. **Setup**: Master Order with 2 SubOrders (Shop A and Shop B).
2. **Execute**:
   - Deliver SubOrder A: `POST /api/v1/sub-orders/{subOrderA}/delivery-status` with `{ "status": "Delivered" }`.
   - Check Master Order status: remains `Paid` / processing.
   - Deliver SubOrder B: `POST /api/v1/sub-orders/{subOrderB}/delivery-status` with `{ "status": "Delivered" }`.
3. **Verify**:
   - Query Master Order details.
   - Assert `Order.Status` has automatically transitioned to `OrderStatus.Delivered`.

---

### Scenario 4: Delivery Failure & Automated Return/Refund
1. **Setup**: Paid SubOrder out for delivery.
2. **Execute**:
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "DeliveryAttemptFailed", "note": "Customer rescheduled" }`.
   - Assert `SubOrder.Status` remains `Shipped` (not cancelled).
   - `POST /api/v1/sub-orders/{id}/delivery-status` with `{ "status": "Returned" }`.
3. **Verify**:
   - `SubOrder.Status` updates to `Cancelled`.
   - `OrderCancellation` record created with reason `PackageReturned`.
   - `OrderRefund` record created with status `Requested` / `Completed` for 100% of sub-order total.
   - Mock verification: `IWalletClient.RefundSubOrderPaymentAsync` was invoked.
   - Mock verification: `IFulfillmentClient.ReleaseSubOrderStockAsync` was invoked with `"PackageReturned"`.

---

### Scenario 5: Shipment Creation Retry
1. **Setup**: Sub-order accepted when carrier service was temporarily offline (`ShipmentPendingCreation = true`).
2. **Execute**:
   - Seller calls `POST /api/v1/sub-orders/{id}/shipments/retry`.
3. **Verify**:
   - Outbound call to Fulfillment succeeds.
   - Response returns newly populated `trackingCode` and `labelPrintUrl`.
   - `ShipmentPendingCreation` is cleared to `false`.
