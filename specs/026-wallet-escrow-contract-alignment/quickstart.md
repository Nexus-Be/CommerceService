# Quickstart & Verification Guide: Wallet Escrow Alignment & Advanced Financial Error Handling

**Feature**: `026-wallet-escrow-contract-alignment`  
**Date**: 2026-11-17  
**Status**: Ready for Verification  

---

## 1. Prerequisites & Environment

1. **Service Configuration**:
   - CommerceService running on port `8003` (Container) or `5105` (Local HTTP).
   - PostgreSQL `commerce_db` on port `5435`.
   - Mock or Live WalletService on port `8008`.

2. **Feature Flags**:
   - WalletService must have `ReleaseFeatures:LegacyCommerceCreditsEnabled = true` to allow `/settle` and `/refund`.

---

## 2. Validation Scenarios

### Scenario 1: Master Order Full Refund on Pre-Shipping Cancel
Prove that cancelling a multi-shop master order calls `POST /api/v1/wallets/orders/{orderId}/refund` once with `order.TotalAmount`.
```powershell
# 1. Place an order paid via Wallet
# POST /api/v1/orders
# Status transitions: PendingPayment -> Paid

# 2. Cancel the order as Buyer before shipping
$orderId = "<ORDER_ID>"
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5105/api/v1/orders/$orderId/cancel" `
  -Headers @{ "Authorization" = "Bearer $buyerToken"; "Content-Type" = "application/json" } `
  -Body '{"reasonCode": "CancellationWithoutReason", "reasonText": "Buyer changed mind"}'

# 3. Expected Outcome:
# - HTTP 200 OK
# - Wallet received: POST api/v1/wallets/orders/{orderId}/refund with total amount
# - Order.Status is Cancelled
# - OrderRefund record exists with Status == Completed
```

### Scenario 2: Error Recognition on Escrow Depletion
Prove that when Wallet returns `409 ORDER_ESCROW_INSUFFICIENT`, CommerceService flags for audit and does not crash.
```powershell
# Mock Wallet to respond with HTTP 409 ORDER_ESCROW_INSUFFICIENT
# Attempt settlement or refund
# Verify:
# - Log shows: [REST Wallet Client] SubOrder settlement failed with status 409: ORDER_ESCROW_INSUFFICIENT
# - Service does not throw unhandled exception
# - Reconciliation flag is logged
```

### Scenario 3: Verify Automated Test Suite
Run the full test suite to guarantee 0 regressions:
```powershell
dotnet test Commerce.sln -c Release --filter "FullyQualifiedName~OrderServiceTests|FullyQualifiedName~WalletClientTests"
```
Expected output:
```text
Passed! - Failed: 0, Passed: >200, Skipped: 0
```
