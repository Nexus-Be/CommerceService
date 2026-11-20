# Quickstart & Verification Guide: Auction Buy Now & Reserve Price Integration

**Feature**: `035-auction-buy-now-and-reserve-settlement`  
**Date**: 2026-11-20  

---

## 1. Prerequisites & Environment Setup

- `.NET SDK 8.0` installed.
- PostgreSQL database accessible (e.g. `localhost:5435`, database `commerce_db`).
- Working directory: `D:\CommerceService`.

---

## 2. Build Verification

Verify that the entire solution compiles with 0 warnings and 0 errors:

```powershell
dotnet build Commerce.sln -c Release
```

---

## 3. Automated Test Verification

Run all unit and integration tests across the test suite:

```powershell
dotnet test Commerce.sln -c Release
```

Run specifically the contract and auction order test suites:

```powershell
dotnet test tests/Commerce.UnitTests --filter "FullyQualifiedName~AuctionOrder"
```

---

## 4. End-to-End Simulation Flow

### Step 1: Auction Service Sends Buy Now Settlement
```bash
curl -X POST http://localhost:8003/api/v1/auction-orders/settlement \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: <CommerceInternalKey>" \
  -d '{
    "sessionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
    "source": "BUY_NOW",
    "winnerId": "11111111-2222-3333-4444-555555555555",
    "sellerId": "99999999-8888-7777-6666-555555555555",
    "itemId": "aaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    "winningAmount": 5000000.0,
    "depositAmount": 5000000.0,
    "paymentDeadlineAt": "2026-11-21T18:00:00Z"
  }'
```
*Expected Response (201 Created):*
- `status`: `"PendingPayment"`
- `orderSource`: `"BUY_NOW"`

### Step 2: Buyer Selects Delivery Address
```bash
curl -X PUT http://localhost:8003/api/v1/auction-orders/{orderId}/shipping \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <BuyerJwtToken>" \
  -d '{
    "recipientName": "Nguyen Van A",
    "phone": "0987654321",
    "province": "Ho Chi Minh",
    "district": "Quan 1",
    "ward": "Ben Nghe",
    "detailAddress": "123 Le Loi"
  }'
```
*Expected Response (200 OK):*
- `remainingAmount`: `0.0`
- `shippingFeeAmount`: `35000.0`
- `totalAmount`: `35000.0`

### Step 3: Buyer Pays Shipping Fee via Digital Wallet
```bash
curl -X POST http://localhost:8003/api/v1/auction-orders/{orderId}/pay \
  -H "Authorization: Bearer <BuyerJwtToken>"
```
*Expected Response (200 OK):*
- `status`: `"Paid"`
- Outbound callback dispatched to `POST http://localhost:8005/api/v1/auctions/{auctionId}/settlement/payment-status` containing `status: "Paid"` and `orderSource: "BUY_NOW"`.

### Step 4: Verification of 24h Timeout Sweep
When a Buy Now order is not paid within 24 hours:
- `PaymentTimeoutWorker` cancels the order (`Status = Cancelled`).
- Outbound callback dispatched to Auction Service: `status: "PaymentTimedOut"`, `orderSource: "BUY_NOW"`.
- 0 penalty calls made to `UserService`.
