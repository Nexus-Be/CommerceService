# Quickstart & Verification Guide: Order Cancellation & Resource Lifecycle

**Feature**: `027-order-cancellation-and-resource-lifecycle`  
**Date**: 2026-11-17  
**Status**: Ready for Verification  

---

## 1. Prerequisites

- CommerceService running on port `8003` / `5105`.
- PostgreSQL database `commerce_db` available.
- Mock Fulfillment and Wallet clients configured.

---

## 2. Verification Scenarios

### Scenario 1: Rejection of Cancellation for Shipped / Delivered Order
Verify that an order in `Shipping` or `Delivered` status cannot be cancelled.
```powershell
# 1. Have an order with SubOrder.Status = Shipping
# 2. Call cancel endpoint:
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5105/api/v1/orders/$orderId/cancel" `
  -Headers @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" } `
  -Body '{"reasonCode": "BuyerChangedMind", "reasonText": "Cancel shipped order"}'

# Expected:
# HTTP 400 Bad Request
# ErrorCode: "ORDER_CANNOT_BE_CANCELLED"
# Detail: "Order cannot be cancelled once shipped, delivered, or completed."
```

### Scenario 2: Batch Inventory & Voucher Release on Payment Timeout
Verify that `PaymentTimeoutWorker` cancels expired pending orders without calling WalletService.
```powershell
# 1. Seed order in PendingPayment with PaymentDeadlineAt 16 minutes in the past
# 2. Trigger worker iteration
# 3. Verify:
#    - Fulfillment BatchReleaseStockAsync called with the orderId in the array
#    - DiscountCodeUsage.Status updated to Released
#    - SubOrder.Status updated to Cancelled
#    - WalletClient.RefundSubOrderPaymentAsync NOT called (zero calls)
```

### Scenario 3: Package Return on COD Order
Verify that when carrier marks a COD package as `Returned`, stock is released and no wallet refund is initiated.
```powershell
# 1. Update delivery status to "Returned" for a COD sub-order
# 2. Verify:
#    - SubOrder.Status = Cancelled
#    - Fulfillment ReleaseSubOrderStockAsync invoked
#    - OrderRefund table has 0 records for this sub-order
```
