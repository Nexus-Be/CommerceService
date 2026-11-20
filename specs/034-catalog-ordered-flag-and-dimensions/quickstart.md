# Quickstart & Verification Guide: Catalog Ordered Flag & GHN Volumetric Dimensions

**Branch**: `034-catalog-ordered-flag-and-dimensions`  
**Date**: 2026-11-19  
**Spec**: [spec.md](spec.md)

---

## 1. Prerequisites & Solution Setup

Ensure .NET 8.0 SDK is installed.

```bash
# Verify build from repository root
dotnet build Commerce.sln -c Release -nodeReuse:false -m:1 --no-restore
```

---

## 2. Automated Test Verification

Run all unit tests covering the new functionality:

```bash
# Run the complete test suite
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~CatalogClientTests|FullyQualifiedName~OrderServiceTests|FullyQualifiedName~AuctionOrderServiceTests"
```

---

## 3. End-to-End Validation Scenarios

### Scenario 1: GHN Volumetric Weight Calculation during Checkout Quote
- **Test Objective**: Verify that bulky items compute chargeable weight based on volumetric formula $(L \times W \times H) / 5$ instead of defaulting to 200g.
- **Input**:
  - Item 1: `LengthCm = 30`, `WidthCm = 25`, `HeightCm = 20`, `WeightGrams = 300`.
- **Expected Outcome**:
  - $\text{VolumetricWeight} = (30 \times 25 \times 20) / 5 = 3,000\text{g}$.
  - $\text{ChargeableWeight} = \max(300, 3,000) = 3,000\text{g}$.
  - Fulfillment Service receives `3,000g` per unit in `ShippingQuoteItem.WeightGrams`.

---

### Scenario 2: Synchronize Ordered Variants (`mark-ordered`) on Order Creation
- **Test Objective**: Verify that creating an order sends distinct variant IDs to Catalog Service.
- **Input**:
  - Order with 2 line items having `SkuId` $A$ and $B$.
- **Expected Outcome**:
  - Order is committed in PostgreSQL.
  - `ICatalogClient.MarkVariantsOrderedAsync([A, B])` is invoked with `POST /api/v1/internal/variants/mark-ordered`.
  - Order creation succeeds and returns `201 Created` with `CreateOrderResponse`.

---

### Scenario 3: Synchronize Ordered Variants on Auction Order Settlement
- **Test Objective**: Verify that auction settlement with a valid `SkuId` marks the variant as ordered.
- **Input**:
  - `ReceiveSettlementAsync` called with `SkuId` $C$.
- **Expected Outcome**:
  - Auction order is saved in PostgreSQL.
  - `ICatalogClient.MarkVariantsOrderedAsync([C])` is invoked.
  - Returns `AuctionSettlementResult` with `IsNewlyCreated: true`.

---

### Scenario 4: Non-Blocking Fail-Safe during Catalog Downtime
- **Test Objective**: Verify that network failure or HTTP 500 from Catalog Service during `mark-ordered` does NOT rollback order creation or payment.
- **Input**:
  - Mock Catalog Service returning HTTP 500 Internal Server Error or throwing `HttpRequestException`.
- **Expected Outcome**:
  - Warning is logged (`_logger.LogWarning`).
  - Order remains committed in database.
  - Buyer receives valid `CreateOrderResponse`.
