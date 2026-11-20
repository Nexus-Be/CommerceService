# Quickstart Validation Guide: Auction Deposit Ingestion & Runner-Up Settlement

**Feature**: `022-auction-deposit-and-runner-up-settlement`  
**Target Service**: `CommerceService` (Port `8003`, Database `commerce_db:5435`)  
**Date**: 2026-11-15  

---

## 1. Prerequisites & Environment Setup

- .NET 8.0 SDK installed (`dotnet --version` $\ge 8.0.x$)
- PostgreSQL running at port `5435` with database `commerce_db` (or in-memory mock for unit tests)
- Environment Variables:
  - `ASPNETCORE_ENVIRONMENT=Development`
  - `INTERNAL_SERVICE_SECRET=eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
  - `AUCTION_SERVICE_URL=http://localhost:8005/`

---

## 2. Automated Test Suite Execution

Run the complete test suite to verify 100% pass rate:

```bash
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuctionOrder"
```

Verify clean compilation of the entire solution:

```bash
dotnet build Commerce.sln -c Release
```

---

## 3. End-to-End API Validation Scenarios

### Scenario 1: Primary Winner Settlement with 10% Deposit

1. **Ingest Settlement**:
   ```http
   POST /api/v1/auction-orders/settlement HTTP/1.1
   Host: localhost:8003
   X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e
   X-Caller-Service: auction-service
   Content-Type: application/json

   {
     "auctionId": "b1111111-1111-1111-1111-111111111111",
     "winnerId": "c2222222-2222-2222-2222-222222222222",
     "sellerId": "d3333333-3333-3333-3333-333333333333",
     "winningBidId": "e4444444-4444-4444-4444-444444444444",
     "productId": "f5555555-5555-5555-5555-555555555555",
     "productName": "Antique Bronze Watch",
     "winningAmount": 1000000,
     "depositAmount": 100000,
     "source": "WINNER",
     "paymentDeadlineAt": "2026-11-16T10:00:00Z"
   }
   ```
   *Expected Response*: `201 Created` with `orderId`, `orderNo: "AO-..."`, `status: "PendingPayment"`.

2. **Submit Shipping Address**:
   ```http
   PUT /api/v1/auction-orders/{orderId}/shipping HTTP/1.1
   Authorization: Bearer <BuyerToken>
   Content-Type: application/json

   {
     "shippingAddress": {
       "fullName": "Nguyen Van A",
       "phoneNumber": "0912345678",
       "addressLine": "123 Le Loi",
       "provinceName": "TP. Ho Chi Minh",
       "districtName": "Quan 1",
       "wardName": "Ben Nghe",
       "ghnDistrictId": 1442,
       "ghnWardCode": "20101"
     },
     "contactPhone": "0912345678"
   }
   ```
   *Expected Response*: `200 OK` with `shippingFeeAmount: 30000`, `remainingAmount: 900000`, and `totalAmount: 930000`.

3. **Pay Net Remaining Balance via Nexus Wallet**:
   ```http
   POST /api/v1/auction-orders/{orderId}/pay HTTP/1.1
   Authorization: Bearer <BuyerToken>
   ```
   *Expected Response*: `200 OK` with `status: "Paid"`.  
   *Expected Outbound Callback*: `POST http://localhost:8005/api/v1/auctions/{auctionId}/settlement/payment-status` with `orderSource: "WINNER"`, `status: "Paid"`.

---

### Scenario 2: Runner-Up Second Chance Ingestion

1. **Precondition**: Order from Scenario 1 cancelled due to payment timeout.
2. **Ingest Runner-Up Settlement**:
   ```http
   POST /api/v1/auction-orders/settlement HTTP/1.1
   Host: localhost:8003
   X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e
   X-Caller-Service: auction-service
   Content-Type: application/json

   {
     "auctionId": "b1111111-1111-1111-1111-111111111111",
     "winnerId": "c8888888-8888-8888-8888-888888888888",
     "sellerId": "d3333333-3333-3333-3333-333333333333",
     "winningBidId": "e9999999-9999-9999-9999-999999999999",
     "productId": "f5555555-5555-5555-5555-555555555555",
     "productName": "Antique Bronze Watch",
     "winningAmount": 950000,
     "depositAmount": 0,
     "source": "SECOND_CHANCE",
     "paymentDeadlineAt": "2026-11-17T10:00:00Z"
   }
   ```
   *Expected Response*: `201 Created` with a new `orderId`, `depositAmount: 0`, `remainingAmount: 950000`, `orderSource: "SecondChance"`.

---

### Scenario 3: Financial Boundary Rejection (422)

1. **Attempt Settlement where Deposit > Winning Amount**:
   ```http
   POST /api/v1/auction-orders/settlement HTTP/1.1
   Content-Type: application/json

   {
     "auctionId": "b2222222-2222-2222-2222-222222222222",
     "winnerId": "c3333333-3333-3333-3333-333333333333",
     "sellerId": "d4444444-4444-4444-4444-444444444444",
     "winningBidId": "e5555555-5555-5555-5555-555555555555",
     "productId": "f6666666-6666-6666-6666-666666666666",
     "productName": "Rare Painting",
     "winningAmount": 500000,
     "depositAmount": 600000,
     "source": "WINNER",
     "paymentDeadlineAt": "2026-11-16T10:00:00Z"
   }
   ```
   *Expected Response*: `422 Unprocessable Entity`
   ```json
   {
     "type": "https://httpstatuses.com/422",
     "title": "Unprocessable Entity",
     "status": 422,
     "errorCode": "DEPOSIT_EXCEEDS_WINNING_AMOUNT",
     "detail": "Deposit amount cannot exceed winning amount."
   }
   ```

---

### Scenario 4: Seller Rejection & Independent Reversal

1. **Seller Rejection Request**:
   ```http
   POST /api/v1/auction-orders/{orderId}/reject HTTP/1.1
   Authorization: Bearer <SellerToken>
   Content-Type: application/json

   {
     "reasonNote": "Item damaged during storage inspection."
   }
   ```
   *Expected Response*: `200 OK` with `status: "Cancelled"`, `action: "Rejected"`, and `refundAmount: 930000`.  
   *Verified Invariants*:
   - Wallet refund executed for 930,000 VND (Commerce does not refund the 100,000 VND deposit).
   - Seller penalty executed via `IUserClient.CreatePenaltyAsync` (`POLICY-AUC-SELLER-REJECT`).
   - Callback dispatched with `status: "SellerRejected"`, `orderSource: "WINNER"`.
