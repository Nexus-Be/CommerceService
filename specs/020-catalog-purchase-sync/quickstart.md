# Quickstart & Verification Guide: Purchase Record Synchronization

**Feature Branch**: `020-catalog-purchase-sync`  
**Date**: 2026-11-11  

This guide provides automated test commands and manual cURL requests to verify the purchase record synchronization flow between `CommerceService` and `CatalogService`.

---

## 1. Automated Verification (Unit Tests)

### Run CatalogClient Unit Tests
Validates that `CatalogClient.SyncPurchaseRecordAsync` handles 201 Created, 200 OK, 400 Bad Request, 404 Not Found, and 500/503 network timeouts with non-blocking semantics:

```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~CatalogClientTests"
```

### Run Order Lifecycle Tests
Validates that `BuyerConfirmSubOrderReceiptAsync` and `CompleteOrderAsync` extract distinct `ProductId`s and trigger purchase record synchronization:

```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~OrderLifecycleTests"
```

### Run Auction Order Service Tests
Validates that `BuyerConfirmAuctionDeliveryAsync` triggers purchase record synchronization for `auctionOrder.ProductId`:

```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~AuctionOrderServiceTests"
```

### Verify Clean Build
Verify zero errors and zero warnings across the entire solution:

```powershell
dotnet build Commerce.sln -c Release -nodeReuse:false -m:1 --no-restore
```

---

## 2. Manual cURL Testing (Local Development)

### Direct Smoke Test to CatalogService
Verify that CatalogService's internal endpoint is accessible and idempotent:

```bash
curl -X POST "http://localhost:8002/api/v1/internal/purchases" \
     -H "Content-Type: application/json" \
     -d '{
       "buyerId": "dddddddd-4444-4444-4444-444444444444",
       "orderId": "99000000-0000-0000-0000-000000000002",
       "productId": "11000000-0000-0000-0000-000000000001",
       "completedAt": "2026-11-11T20:30:00Z"
     }'
```

**Expected Response**:
- First call: `201 Created`
- Second call with identical payload: `200 OK` (idempotent duplicate, no error).

### Sub-Order Receipt Confirmation via CommerceService
Confirm delivery receipt of a sub-order as the authenticated buyer:

```bash
curl -X POST "http://localhost:5105/api/v1/sub-orders/99000000-0000-0000-0000-000000000002/confirm-receipt" \
     -H "Content-Type: application/json" \
     -H "x-user-id: dddddddd-4444-4444-4444-444444444444" \
     -H "x-user-roles: buyer"
```

**Expected Response (`200 OK`)**:
```json
{
  "subOrderId": "99000000-0000-0000-0000-000000000002",
  "status": "Completed",
  "completedAt": "2026-11-11T20:30:00Z",
  "escrowSettled": true,
  "amountSettled": 250000.00
}
```
*And verify in application logs:*
`[OrderService] Successfully synchronized 1 purchase records to CatalogService for SubOrder '99000000-0000-0000-0000-000000000002'.`
