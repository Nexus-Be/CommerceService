# Quickstart & Verification Guide: Fulfillment Internal Endpoint Alignment

**Feature Directory**: `specs/013-fulfillment-endpoint-alignment`  
**Phase**: Phase 1 (Validation Guide)  
**Date**: 2026-11-04  

---

## 1. Prerequisites

- **.NET 8.0 SDK** installed (`dotnet --version` >= 8.0.x).
- Solution directory: `d:\CommerceService`.
- Ports reference: CommerceService (`8003`), FulfillmentService (`8006`).

---

## 2. Verification Scenarios

### Scenario 1: Verify Partial Release Endpoint (`/release-items`)
Execute targeted unit tests verifying that when a sub-order is rejected, the client sends an HTTP POST request to `/api/v1/internal/inventories/reservations/{orderId}/release-items`:
```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~ReleaseSubOrderStockAsync"
```
**Expected Outcome**: 
- Request method: `POST`
- URI: `http://localhost:8006/api/v1/internal/inventories/reservations/{orderId}/release-items`
- Request body contains `{ items: [...], reason: "SubOrderRejected" }`
- Test passes with 0 failures.

---

### Scenario 2: Verify Shipping Quote Endpoint (`/internal/shipping/quote`)
Execute targeted unit tests verifying that carrier rate quotes target the internal shipping route:
```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~CalculateShopShippingFeeAsync"
```
**Expected Outcome**:
- Request method: `POST`
- URI: `http://localhost:8006/api/v1/internal/shipping/quote`
- Success: returns calculated fee.
- Failure: throws `CommerceDomainException` (503 `FULFILLMENT_SERVICE_UNAVAILABLE`).
- Test passes with 0 failures.

---

### Scenario 3: Verify Shipment Creation Endpoint (`/internal/shipments`)
Execute targeted unit tests verifying that shipment creation targets the internal shipments route:
```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --filter "FullyQualifiedName~CreateShipmentAsync"
```
**Expected Outcome**:
- Request method: `POST`
- URI: `http://localhost:8006/api/v1/internal/shipments`
- Request body contains `{ subOrderId: "..." }`
- Returns genuine tracking code and label print URL.
- Test passes with 0 failures.

---

### Scenario 4: Full Solution Release Build
Execute clean solution compilation:
```powershell
dotnet build Commerce.sln -c Release
```
**Expected Outcome**:
- Build succeeded.
- 0 Warning(s).
- 0 Error(s).

---

### Scenario 5: Full Automated Test Suite Execution
Execute complete test suite across all feature components:
```powershell
dotnet test Commerce.sln -c Release
```
**Expected Outcome**:
- 100% tests passed (>= 115 tests).
- 0 failures, 0 skipped.
