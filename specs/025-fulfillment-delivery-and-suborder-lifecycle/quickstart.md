# Quickstart & Verification Guide: Fulfillment Delivery Synchronization and Sub-Order Lifecycle

**Feature Branch**: `025-fulfillment-delivery-and-suborder-lifecycle`  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Prerequisites & Environment Setup

1. **Service Running**:
   - CommerceService active on `http://localhost:8003`.
   - PostgreSQL accessible on port `5435` (`commerce_db`).
2. **Environment Variables**:
   - `INTERNAL_SERVICE_SECRET=eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
3. **Reference Contracts**:
   - [Delivery Webhook Contract](./contracts/fulfillment-delivery-webhook-api.yaml)
   - [Sub-Order Cancellation Contract](./contracts/suborder-cancellation-api.yaml)
   - [Data Model & State Architecture](./data-model.md)

---

## 2. Validation Scenarios

### Scenario 1: Delivery Webhook Ingestion with POD and Courier Metadata
*Verify that carrier delivery callbacks update sub-order status to `Delivered` and record full POD audit trails without prematurely triggering escrow payouts.*

```bash
# Invoke logistics delivery webhook from Fulfillment Service
curl -X POST "http://localhost:8003/api/v1/sub-orders/{subOrderId}/delivery-status" \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e" \
  -d '{
    "status": "Delivered",
    "carrierCode": "GHN",
    "trackingCode": "GHN-SGN-1029384",
    "note": "Package handed directly to recipient",
    "location": "Ben Nghe Ward, District 1, HCMC",
    "deliveredAt": "2026-11-17T10:00:00Z",
    "podUrl": "https://cdn.ghn.vn/pod/20261117/sign_001.jpg",
    "shipperName": "Nguyen Van C",
    "shipperPhone": "0987654321"
  }'
```
**Expected Outcome**:
- HTTP 200 OK: `{"subOrderId": "...", "status": "Delivered"}`.
- SubOrder status is `Delivered`; `DeliveredAt` is recorded.
- Escrow funds remain strictly in platform escrow (`so.Status != Completed`).
- `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline` displays the courier name, phone, and POD URL.

---

### Scenario 2: Guarded Buyer Delivery Confirmation
*Verify that premature receipt confirmation is strictly rejected while items are still in transit (`Shipped`), and only allowed after `Delivered`.*

```bash
# 1. Attempt confirmation while SubOrder is still in 'Shipped' status
curl -X POST "http://localhost:8003/api/v1/sub-orders/{subOrderId}/confirm-receipt" \
  -H "Authorization: Bearer {buyerJwt}"

# Expected: HTTP 400 Bad Request with errorCode: "INVALID_STATUS"
# Detail: "Sub-order cannot be confirmed until it is delivered."

# 2. Confirm receipt after SubOrder transitions to 'Delivered'
curl -X POST "http://localhost:8003/api/v1/sub-orders/{subOrderId}/confirm-receipt" \
  -H "Authorization: Bearer {buyerJwt}"

# Expected: HTTP 200 OK with status: "Completed", escrowSettled: true
```
**Expected Outcome**:
- Premature confirmation is blocked.
- Post-delivery confirmation transitions status to `Completed`, releases escrow funds to seller wallet, and grants catalog review permissions.

---

### Scenario 3: Independent Sub-Order Cancellation by Buyer
*Verify that a buyer can cancel an unfulfilled sub-order in a multi-shop order without cancelling active sibling shipments.*

```bash
# Cancel an unfulfilled SubOrder (e.g. Shop B in Paid status)
curl -X POST "http://localhost:8003/api/v1/sub-orders/{subOrderIdB}/cancel" \
  -H "Authorization: Bearer {buyerJwt}" \
  -H "Content-Type: application/json" \
  -d '{
    "reasonCode": "BuyerChangedMind",
    "reasonNote": "Shop delayed fulfillment, cancelling this sub-order."
  }'
```
**Expected Outcome**:
- HTTP 200 OK: SubOrder B transitions to `Cancelled`.
- Reserved inventory for SubOrder B is released via `ReleaseSubOrderStockAsync`.
- 100% wallet refund for SubOrder B is deposited back to buyer's wallet.
- SubOrder A (sibling) remains in `Shipped` status and continues delivery uninterrupted.

---

### Scenario 4: Automated Completion for Delivered Orders
*Verify that delivered sub-orders left unconfirmed past the grace period are automatically completed by the background worker.*

1. In `appsettings.json`, set:
   ```json
   "OrderLifecycle": {
     "AutoCompleteDeliveredMinutes": 3
   }
   ```
2. Seed or update a sub-order in `Delivered` status with `DeliveredAt` older than 3 minutes.
3. Allow `PaymentTimeoutWorker` to execute its scheduled cycle (or invoke worker execution in test harness).
4. Query sub-order detail:
   - SubOrder status is automatically `Completed`.
   - Seller receive amount is settled to seller wallet via `IWalletClient`.
   - Status history contains automated completion log entry.

---

## 3. Automated Test Execution

Run the targeted unit and regression test suite:
```bash
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~OrderLifecycleTests"
```
