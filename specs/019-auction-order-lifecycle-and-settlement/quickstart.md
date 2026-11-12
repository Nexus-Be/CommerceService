# Quickstart Validation Guide: Auction Order Lifecycle, Settlement & Automated Reversal

**Feature**: `019-auction-order-lifecycle-and-settlement`  
**Date**: 2026-11-11  
**Status**: Ready for Verification  

---

## 1. Prerequisites & Environment

1. **Active Configuration**:
   - `AUCTION_SERVICE_URL`: `http://auction-service:8005` (or mock HTTP server).
   - `WALLET_SERVICE_URL`: `http://wallet-service:8008`.
   - `FULFILLMENT_SERVICE_URL`: `http://fulfillment-service:8006`.
   - `InternalSecurity__ServiceKey`: `eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`.
2. **Database**: PostgreSQL `commerce_db:5435` running with current schema.
3. **Build Tool**: .NET 8.0 SDK installed.

---

## 2. End-to-End Validation Scenarios

### Scenario 1: Ingest Settlement & Verify Idempotency (SPEC-AUC-01)
1. Send initial settlement:
   ```bash
   curl -X POST http://localhost:8003/api/v1/auction-orders/settlement \
     -H "Content-Type: application/json" \
     -H "X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e" \
     -d '{
       "auctionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
       "winningBidId": "4ba85f64-5717-4562-b3fc-2c963f66afa7",
       "winnerId": "5ca85f64-5717-4562-b3fc-2c963f66afa8",
       "sellerId": "6da85f64-5717-4562-b3fc-2c963f66afa9",
       "productId": "7ea85f64-5717-4562-b3fc-2c963f66afb0",
       "skuId": "8fa85f64-5717-4562-b3fc-2c963f66afb1",
       "productName": "Đồng hồ Vintage Omega Seamaster 1968",
       "winningAmount": 15000000.00,
       "paymentDeadlineAt": "2026-11-12T20:00:00Z"
     }'
   ```
   *Expected*: HTTP 201 Created with `{ orderId, orderNo: "AO-...", status: "PendingPayment" }`.
2. Resend the exact same payload:
   *Expected*: HTTP 200 OK with the identical `{ orderId, orderNo }` (no duplicates in database).

---

### Scenario 2: Submit Shipping Address & Quote Total (SPEC-AUC-02)
1. Winner submits shipping address:
   ```bash
   curl -X PUT http://localhost:8003/api/v1/auction-orders/{orderId}/shipping \
     -H "Content-Type: application/json" \
     -H "X-User-Id: 5ca85f64-5717-4562-b3fc-2c963f66afa8" \
     -d '{
       "receiverName": "Nguyễn Văn A",
       "contactPhone": "0901234567",
       "street": "123 Lê Lợi",
       "ward": "Bến Nghé",
       "district": "Quận 1",
       "province": "TP Hồ Chí Minh",
       "ghnDistrictId": 1442,
       "ghnWardCode": "20101"
     }'
   ```
   *Expected*: HTTP 200 OK with `totalAmount = 15000000.00 + shippingFeeAmount`.

---

### Scenario 3: Pay Auction Order & Verify Callback (SPEC-AUC-03)
1. Winner pays via wallet:
   ```bash
   curl -X POST http://localhost:8003/api/v1/auction-orders/{orderId}/pay \
     -H "X-User-Id: 5ca85f64-5717-4562-b3fc-2c963f66afa8"
   ```
   *Expected*: HTTP 200 OK with `{ status: "Paid", paidAt, sellerRespondDeadlineAt }`.
   *Assert*: Outbound mock for Auction Service records call `POST /api/v1/auctions/{auctionId}/settlement/payment-status` with `{ status: "Paid", amount: 15000000.00, paidAt }`.

---

### Scenario 4: Seller Accepts Paid Order (SPEC-AUC-04)
1. Seller accepts within 24 hours:
   ```bash
   curl -X POST http://localhost:8003/api/v1/auction-orders/{orderId}/accept \
     -H "X-User-Id: 6da85f64-5717-4562-b3fc-2c963f66afa9" \
     -d '{ "note": "Packing goods" }'
   ```
   *Expected*: HTTP 200 OK with `{ status: "Confirmed" }`.
   *Assert*: Outbound mock for Auction Service records call with `{ status: "SellerAccepted" }`.

---

### Scenario 5: Seller Rejects Paid Order with 100% Refund (SPEC-AUC-05)
1. Seller rejects a paid order:
   ```bash
   curl -X POST http://localhost:8003/api/v1/auction-orders/{orderId}/reject \
     -H "Content-Type: application/json" \
     -H "X-User-Id: 6da85f64-5717-4562-b3fc-2c963f66afa9" \
     -d '{ "reasonCode": "SellerRejected", "reasonText": "Defective item in warehouse" }'
   ```
   *Expected*: HTTP 200 OK with `{ status: "Cancelled" }`.
   *Assert*: `OrderCancellation` and `OrderRefund` are persisted in PostgreSQL with `AuctionOrderId`.
   *Assert*: Outbound mock for Wallet Service records refund call for 100% of `TotalAmount`.
   *Assert*: Outbound mock for Auction Service records call with `{ status: "SellerRejected" }`.

---

### Scenario 6: Background Worker Auto-Cancels Expired Payment (SPEC-AUC-06)
1. Seed an `AuctionOrder` with `Status == PendingPayment` and `PaymentDeadlineAt = UtcNow.AddHours(-1)`.
2. Run `PaymentTimeoutWorker.ProcessExpiredOrdersAsync`.
3. Assert:
   - Order `Status` transitions to `Cancelled`.
   - `OrderCancellation` has `AuctionOrderId` populated and `SubOrderId` is NULL (validating FK fix).
   - Outbound mock records call to Auction Service with `{ status: "PaymentTimedOut" }`.

---

### Scenario 7: Background Worker Auto-Cancels Expired Seller Response (SPEC-AUC-07)
1. Seed an `AuctionOrder` with `Status == Paid` and `SellerRespondDeadlineAt = UtcNow.AddHours(-1)`.
2. Run `PaymentTimeoutWorker.ProcessExpiredOrdersAsync`.
3. Assert:
   - Order `Status` transitions to `Cancelled`.
   - 100% wallet refund is triggered for the buyer.
   - Outbound mock records call to Auction Service with `{ status: "SellerExpired" }`.

---

## 3. Automated Test Verification Commands

```powershell
# 1. Build solution in Release configuration (0 errors, 0 warnings)
dotnet build Commerce.sln -c Release

# 2. Run unit and integration tests for Auction Order lifecycle
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~Auction"

# 3. Run full test suite to guarantee zero regression
dotnet test Commerce.sln -c Release
```
