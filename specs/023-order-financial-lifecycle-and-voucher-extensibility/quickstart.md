# Quickstart: Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility

**Feature Branch**: `023-order-financial-lifecycle-and-voucher-extensibility`  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Quick Verification Commands

### Step 1: Verify Staff-Only Platform Fee Management

#### 1.1 Retrieve Active Fee Rate (Staff Only)
```bash
curl -X GET "http://localhost:8003/api/v1/admin/platform-fee" \
  -H "Authorization: Bearer <STAFF_JWT>" \
  -H "x-user-id: 11111111-1111-1111-1111-111111111111" \
  -H "x-user-roles: staff"
```
**Expected Response (HTTP 200 OK):**
```json
{
  "defaultPercent": 0.0,
  "updatedByRole": "SystemDefault",
  "updatedAt": "2026-11-16T00:00:00Z"
}
```

#### 1.2 Update Platform Fee Rate to 5.0% (Staff Only)
```bash
curl -X PUT "http://localhost:8003/api/v1/admin/platform-fee" \
  -H "Authorization: Bearer <STAFF_JWT>" \
  -H "x-user-id: 11111111-1111-1111-1111-111111111111" \
  -H "x-user-roles: staff" \
  -H "Content-Type: application/json" \
  -d '{
    "defaultPercent": 5.0
  }'
```
**Expected Response (HTTP 200 OK):**
```json
{
  "defaultPercent": 5.0,
  "updatedByRole": "staff",
  "updatedAt": "2026-11-16T12:00:00Z"
}
```

#### 1.3 Verify RBAC Block on Buyer / Seller Calls
```bash
curl -X PUT "http://localhost:8003/api/v1/admin/platform-fee" \
  -H "Authorization: Bearer <BUYER_JWT>" \
  -H "x-user-id: 22222222-2222-2222-2222-222222222222" \
  -H "x-user-roles: buyer" \
  -H "Content-Type: application/json" \
  -d '{ "defaultPercent": 10.0 }'
```
**Expected Response (HTTP 403 Forbidden):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "Forbidden",
  "status": 403,
  "errorCode": "FORBIDDEN",
  "detail": "Access denied: Only staff members can adjust platform fee rates."
}
```

---

### Step 2: Verify Total Decommissioning of VNPay

```bash
curl -X POST "http://localhost:8003/api/v1/orders" \
  -H "Authorization: Bearer <BUYER_JWT>" \
  -H "x-user-id: 22222222-2222-2222-2222-222222222222" \
  -H "x-user-roles: buyer" \
  -H "Content-Type: application/json" \
  -d '{
    "quoteId": "33333333-3333-3333-3333-333333333333",
    "paymentMethod": "VnPay",
    "shippingAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
    "contactPhone": "0901234567"
  }'
```
**Expected Response (HTTP 400 Bad Request):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "errorCode": "UNSUPPORTED_PAYMENT_METHOD",
  "detail": "VNPay payment method is no longer supported. Please use Wallet or COD."
}
```

---

### Step 3: Verify Automated Financial Invariant & Escrow Payout

1. **Buyer Confirms Sub-Order Receipt**:
```bash
curl -X POST "http://localhost:8003/api/v1/sub-orders/{subOrderId}/confirm-receipt" \
  -H "Authorization: Bearer <BUYER_JWT>" \
  -H "x-user-id: {buyerId}" \
  -H "x-user-roles: buyer"
```
**Expected Outcome**:
- SubOrder status transitions to `Completed`.
- An outbound HTTP S2S call `POST /api/v1/wallets/sub-orders/{subOrderId}/settle` disburses exact `SellerReceiveAmount` to seller.
- System Escrow retains `PlatformFeeAmount + ShippingFeeAmount`.

2. **Sub-Order Cancellation with COD**:
```bash
curl -X POST "http://localhost:8003/api/v1/orders/{orderId}/cancel" \
  -H "Authorization: Bearer <BUYER_JWT>" \
  -H "x-user-id: {buyerId}" \
  -H "x-user-roles: buyer"
```
**Expected Outcome**:
- SubOrder status transitions to `Cancelled`.
- Stock is released.
- Zero wallet refund calls are executed for COD orders (no money leaked).

---

## 2. Solution Build and Test Execution

```powershell
# 1. Clean Release build
dotnet build Commerce.sln -c Release

# 2. Execute full automated test suite
dotnet test Commerce.sln -c Release --no-build
```
- Compilation target: **0 errors, 0 warnings**.
- Test pass rate target: **100% pass**.
