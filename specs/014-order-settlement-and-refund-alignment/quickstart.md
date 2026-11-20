# Quickstart Validation Guide: Order Settlement, Automated Refund, and Seed Data Alignment

**Feature Directory**: `specs/014-order-settlement-and-refund-alignment`  
**Phase**: Phase 1 (Quickstart & Validation Guide)  
**Status**: Completed  

---

## 1. Prerequisites & Environment Setup

Ensure the following tools and environment variables are active before running validation scenarios:
* **.NET SDK**: `8.0` (`dotnet --version`)
* **PostgreSQL Database**: Port `5435` running database `commerce_db`
* **Docker Compose Network**: `nexus-network`

```bash
# Verify environment configuration
set COMMERCE_SERVICE_PORT=8003
set ASPNETCORE_ENVIRONMENT=Development
set WALLET_SERVICE_URL=http://localhost:8008
set FULFILLMENT_SERVICE_URL=http://localhost:8006
set CATALOG_SERVICE_URL=http://localhost:8002
```

---

## 2. Validation Scenario 1: Database Seed & Migration Cleanliness

**Objective**: Verify that EF Core migrations and snapshot apply without JSON ownership errors (`HasData` conflict with `ToJson()`).

```bash
# 1. Build solution in Release configuration
dotnet build Commerce.sln -c Release

# 2. Verify migration compilation
dotnet build Commerce.Infrastructure/Commerce.Infrastructure.csproj
```
* **Expected Outcome**: Build succeeds with 0 warnings and 0 errors. Snapshot compiles cleanly without `SeedOrders` JSON mapping exceptions.

---

## 3. Validation Scenario 2: Seller Rejection & Automated Wallet Refund

**Objective**: Verify that when a seller rejects a paid sub-order, funds are automatically refunded to the buyer and stock is released for only that sub-order's SKUs.

```bash
# Execute Order Lifecycle automated test suite
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~OrderLifecycleTests"
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~EndToEndFlowTests.E2E_Flow8"
```
* **Expected Outcome**:
  - `SubOrder.Status` transitions to `Cancelled`.
  - `_walletClient.RefundSubOrderPaymentAsync` is called with the sub-order amount and idempotency key.
  - `_fulfillmentClient.ReleaseSubOrderStockAsync` is called with the exact items of the rejected sub-order.
  - `OrderRefund.Status` is marked as `Completed`.

---

## 4. Validation Scenario 3: Buyer Receipt Confirmation & Escrow Settlement

**Objective**: Verify that when a buyer confirms receipt of a delivered sub-order, status transitions to `Completed` and funds are settled to the seller's wallet.

```bash
# Test buyer confirmation endpoint and settlement logic
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~BuyerConfirm"
```
* **Expected API Flow**:
  1. `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` with Bearer token of Buyer.
  2. Sub-order transitions to `Completed` with timestamp `completedAt`.
  3. `_walletClient.SettleSellerSubOrderAsync` triggers settlement of `sellerReceiveAmount` to `sellerId`.
  4. Response returns HTTP 200 OK with `ConfirmReceiptResponse`.

---

## 5. Validation Scenario 4: Seller Sub-Orders Listing Query

**Objective**: Verify database pagination and status filtering for seller order management.

```bash
# Test seller order listing query
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~ListSellerSubOrders"
```
* **Expected API Flow**:
  1. `GET /api/v1/sub-orders?page=1&pageSize=20&status=Paid` with Bearer token of Seller.
  2. SQL query executes `.Where(s => s.SellerId == sellerId && s.Status == Paid).Skip(0).Take(20)`.
  3. Response returns HTTP 200 with `PagedSubOrdersResponse` envelope.

---

## 6. Full Solution Verification Gate

Run the mandatory quality gate before considering the feature complete:

```bash
# Compile solution
dotnet build Commerce.sln -c Release

# Run entire test suite
dotnet test Commerce.sln -c Release --no-build
```
* **Quality Gate Requirement**: 100% Pass Rate across all tests; 0 Errors; 0 Warnings.
