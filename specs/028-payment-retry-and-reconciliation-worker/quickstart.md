# Quickstart & Verification Guide: Payment Retry & Reconciliation Engine

**Feature**: `028-payment-retry-and-reconciliation-worker`  
**Date**: 2026-11-17  
**Status**: Ready for Verification  

---

## 1. Prerequisites

- CommerceService running on port `8003` / `5105`.
- PostgreSQL database `commerce_db` on port `5435`.
- Active JWT access token for buyer and seller.
- Mock WalletService and FulfillmentService responding.

---

## 2. Verification Scenarios

### Scenario 1: Repaying a Pending Order within 15-Minute Deadline
Buyer retries paying for an order that previously encountered a transient failure.
```powershell
# 1. Seed or select an order in PendingPayment status (PaymentMethod: Wallet)
# 2. Call the repay endpoint:
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5105/api/v1/orders/$orderId/pay" `
  -Headers @{ "Authorization" = "Bearer $buyerToken"; "Content-Type" = "application/json" }

# Expected Response:
# HTTP 200 OK
# {
#   "orderId": "$orderId",
#   "status": "Paid",
#   "totalAmount": 250000.0,
#   "paidAt": "2026-11-17T14:30:00Z",
#   "walletTransactionId": "..."
# }
```

### Scenario 2: Repay Rejected when Deadline Expired
Verify that an order with expired deadline cannot be paid via repay.
```powershell
# 1. Order has PaymentDeadlineAt in the past
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5105/api/v1/orders/$expiredOrderId/pay" `
  -Headers @{ "Authorization" = "Bearer $buyerToken"; "Content-Type" = "application/json" }

# Expected Response:
# HTTP 400 Bad Request
# ErrorCode: "ORDER_PAYMENT_DEADLINE_EXPIRED"
# Detail: "Payment deadline for this order has expired."
```

### Scenario 3: Reconciliation Worker Retrying `Requested` Refunds
Verify background worker reconciles stalled refunds.
```powershell
# 1. Seed OrderRefund with Status = Requested (0)
# 2. Trigger worker iteration
# 3. Verify:
#    - _walletClient.RefundSubOrderPaymentAsync (or RefundOrderPaymentAsync) invoked
#    - OrderRefund.Status updated to Completed (1)
#    - OrderRefund.CompletedAt is set to current timestamp
```

### Scenario 4: Reconciliation Worker Settling Unsettled Completed Sub-Orders
Verify background worker resolves missed escrow settlement.
```powershell
# 1. Seed SubOrder with Status = Completed, IsEscrowSettled = false, PaymentMethod = Wallet
# 2. Trigger worker iteration
# 3. Verify:
#    - _walletClient.SettleSellerSubOrderAsync invoked with SellerReceiveAmount
#    - SubOrder.IsEscrowSettled updated to true in database
```

### Scenario 5: Financial Transparency APIs
```powershell
# Query refund history (Buyer):
Invoke-RestMethod -Method Get `
  -Uri "http://localhost:5105/api/v1/orders/$orderId/refunds" `
  -Headers @{ "Authorization" = "Bearer $buyerToken" }

# Query financial breakdown (Seller):
Invoke-RestMethod -Method Get `
  -Uri "http://localhost:5105/api/v1/sub-orders/$subOrderId/financial-breakdown" `
  -Headers @{ "Authorization" = "Bearer $sellerToken" }
```
