# Quickstart & Verification Guide: QA E2E Remediation & Lifecycle Hardening

**Feature**: `036-qa-e2e-remediation-and-lifecycle-hardening`  
**Date**: 2026-11-21  

---

## 1. Prerequisites & Environment Setup

- `.NET SDK 8.0` installed.
- PostgreSQL database accessible (`localhost:5435`, database `commerce_db`).
- Working directory: `D:\CommerceService`.

---

## 2. Build Verification

Verify that the entire solution compiles with 0 errors:

```powershell
dotnet build Commerce.sln -c Release
```

---

## 3. Automated Test Verification

Run all unit and integration test suites:

```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release
```

To run specifically the tests touching the remediation features:

```powershell
dotnet test tests/Commerce.UnitTests --filter "FullyQualifiedName~AuctionOrder|FullyQualifiedName~FulfillmentClient|FullyQualifiedName~DisputeRefund"
```

---

## 4. End-to-End Simulation Scenarios

### Scenario 1: Buy Now Settlement with Nullable `WinningBidId` (D-14)
```bash
curl -X POST http://localhost:8003/api/v1/auction-orders/settlement \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: <INTERNAL_SERVICE_SECRET>" \
  -d '{
    "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
    "source": "BUY_NOW",
    "winnerId": "11111111-2222-3333-4444-555555555555",
    "sellerId": "99999999-8888-7777-6666-555555555555",
    "winningBidId": null,
    "productId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "productName": "MacBook Pro M3 Max",
    "winningAmount": 50000000.0,
    "depositAmount": 50000000.0,
    "paymentDeadlineAt": "2026-11-22T18:00:00Z"
  }'
```
*Expected Response (HTTP 201 Created):*
```json
{
  "orderId": "<generated-order-id>",
  "orderNo": "AO-20261121-...",
  "auctionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
  "buyerId": "11111111-2222-3333-4444-555555555555",
  "status": "PendingPayment",
  "paymentDeadlineAt": "2026-11-22T18:00:00Z"
}
```

### Scenario 2: Fail-Closed Stock Reservation (D-01)
Simulate order placement when `FulfillmentService` reservation fails:
```bash
# With StockPolicy:FailOpen=false, attempt checkout
curl -X POST http://localhost:8003/api/v1/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <buyer-jwt>" \
  -H "Idempotency-Key: <unique-uuid>" \
  -d '{ ... }'
```
*Expected Response (HTTP 400 Bad Request):*
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "errorCode": "INSUFFICIENT_STOCK",
  "detail": "One or more items in your order are out of stock."
}
```

### Scenario 3: Delivery Webhook "Returning" and Buyer Penalty on "Returned" (D-07, D-08)
```bash
# Step 1: Carrier notifies return in progress
curl -X POST http://localhost:8003/api/v1/sub-orders/<subOrderId>/delivery-status \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: <INTERNAL_SERVICE_SECRET>" \
  -d '{
    "status": "Returning",
    "note": "Package returning to seller after 3 failed attempts."
  }'

# Step 2: Carrier confirms return completed at merchant warehouse
curl -X POST http://localhost:8003/api/v1/sub-orders/<subOrderId>/delivery-status \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: <INTERNAL_SERVICE_SECRET>" \
  -d '{
    "status": "Returned",
    "note": "Package received back at merchant facility."
  }'
```
*Expected Outcome:* SubOrder transitioned to `Cancelled`, wallet refunded, and Buyer reputation penalized by -5 points via `UserService`.

### Scenario 4: S2S Internal Dispute Refund (D-06)
```bash
curl -X POST http://localhost:8003/api/v1/internal/sub-orders/<subOrderId>/dispute-refund \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: <INTERNAL_SERVICE_SECRET>" \
  -d '{
    "disputeId": "d1000000-0000-0000-0000-000000000001",
    "refundAmount": 850000.0,
    "reason": "Support validated damage in shipping",
    "note": "Direct refund authorization from dispute review"
  }'
```
*Expected Response (HTTP 200 OK):*
```json
{
  "subOrderId": "<subOrderId>",
  "refundId": "<generated-refund-id>",
  "amount": 850000.0,
  "status": "Completed",
  "completedAt": "2026-11-21T02:00:00Z"
}
```
