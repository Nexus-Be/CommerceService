# Quickstart & Verification Guide: WalletService Integration Contract Alignment

**Feature Branch**: `017-wallet-contract-alignment`  
**Date**: 2026-11-10  
**Status**: Ready for Verification  
**Related Specs**: [spec.md](spec.md) | [data-model.md](data-model.md) | [contracts/wallet-integration-api.yaml](contracts/wallet-integration-api.yaml)

---

## 1. Prerequisites & Environment Setup

Verify the service environment configurations:

```powershell
# Set environment variables for local testing
$env:WALLET_SERVICE_URL = "http://localhost:8008/"
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Clean build solution
dotnet build Commerce.sln -c Release
```

Ensure `commerce_db` PostgreSQL is running on port `5435`:
```powershell
# Connectivity check
Test-NetConnection -ComputerName localhost -Port 5435
```

---

## 2. Runnable Verification Scenarios

### Scenario 1: Wallet Checkout with Identity Header (`x-user-id`)
**Goal**: Verify `PayOrderAsync` transmits `x-user-id` and correctly transitions an order to `Paid`.

```http
POST http://localhost:8003/api/v1/orders
Content-Type: application/json
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: buyer

{
  "quoteId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "paymentMethod": "Wallet"
}
```
**Expected Outcome**:
- Status: `200 OK`
- `order.status`: `"Paid"`
- `order.walletTransactionId`: Valid UUID from WalletService
- Outbound request to `http://localhost:8008/api/v1/wallets/orders/{orderId}/payment` contains header `x-user-id: 11111111-1111-1111-1111-111111111111`.

---

### Scenario 2: Direct VNPay Checkout & Reconciliation Polling
**Goal**: Verify genuine `paymentUrl` creation and asynchronous polling resolution.

```http
POST http://localhost:8003/api/v1/orders
Content-Type: application/json
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: buyer

{
  "quoteId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "paymentMethod": "VnPay"
}
```
**Expected Outcome**:
- Status: `200 OK`
- Response body contains authentic `paymentUrl`: `"https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?..."` (signed by WalletService).
- Initial order status: `"PendingPayment"`.
- `VnPayPaymentPollingWorker` polls `GET /api/v1/wallets/checkout/direct/{orderId}`.
- When WalletService reports `"Success"`, CommerceService transitions order to `"Paid"` and commits stock.

---

### Scenario 3: Sub-Order Goods Receipt & Escrow Settlement
**Goal**: Verify automated seller payout upon buyer confirmation.

```http
POST http://localhost:8003/api/v1/sub-orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/confirm-receipt
Content-Type: application/json
x-user-id: 11111111-1111-1111-1111-111111111111
x-user-roles: buyer
```
**Expected Outcome**:
- Status: `200 OK`
- `subOrder.status`: `"Completed"`
- `subOrder.completedAt`: Current UTC timestamp
- Outbound POST to `http://localhost:8008/api/v1/wallets/sub-orders/{subOrderId}/settle` with body:
  `{ "sellerId": "<uuid>", "amount": <amount> }` (No identity headers).

---

### Scenario 4: Seller Rejection & Automated Sub-Order Refund
**Goal**: Verify automated refund upon seller cancellation.

```http
POST http://localhost:8003/api/v1/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6/reject
Content-Type: application/json
x-user-id: 22222222-2222-2222-2222-222222222222
x-user-roles: seller

{
  "subOrderId": "4fa85f64-5717-4562-b3fc-2c963f66afa6",
  "reasonText": "Seller out of stock"
}
```
**Expected Outcome**:
- Status: `200 OK`
- `subOrder.status`: `"Cancelled"`
- Outbound POST to `http://localhost:8008/api/v1/wallets/sub-orders/{subOrderId}/refund`.
- `OrderRefund.status`: `"Completed"`.
- Inventory released exclusively for this sub-order.

---

## 3. Automated Test Suite Execution

Run the complete test suite to verify 0 regressions across unit tests and integration flows:

```powershell
# Run all tests in Release configuration
dotnet test Commerce.sln -c Release --no-build

# Filter specifically for order and checkout tests
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~Orders"
```

**Quality Acceptance Gate**:
- Total tests: 127+
- Failures: 0
- Skipped: 0
