# Quickstart & Verification Guide: Cart Concurrency & State Remediation

**Feature Branch**: `018-cart-concurrency-and-state-remediation`  
**Date**: 2026-11-10  
**Status**: Ready for Verification  

---

## 1. Prerequisites

* **.NET SDK**: 8.0+
* **PostgreSQL** (Optional for local automated tests, required for live container integration): Port `5435`, database `commerce_db`.

---

## 2. Test Execution & Automated Verification

### 2.1 Build Verification
Compile the entire solution in Release configuration:
```bash
dotnet build Commerce.sln -c Release --no-restore -m:1 -nr:false
```
*Expected Result*: Build succeeds with `0 Error(s)`, `0 Warning(s)`.

### 2.2 Unit Test Execution
Execute the Cart test suite to verify domain logic and service state transitions:
```bash
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~Cart" -c Release --no-restore -m:1 -nr:false
```
*Expected Result*: 100% of Cart tests pass.

---

## 3. Manual Scenario Validation

### Scenario A: Adding Item to an Existing Cart (Verifying Fix for 500 Bug)

1. **Setup**: Authenticate as a buyer who already has at least 1 item in their active shopping cart.
2. **Execute**:
   ```http
   POST /api/v1/carts/me/items HTTP/1.1
   Host: localhost:8003
   Authorization: Bearer <valid-jwt>
   Content-Type: application/json

   {
     "skuId": "9d2e1b40-202b-42fa-9486-0dcbe35fce18",
     "quantity": 1
   }
   ```
3. **Verify**:
   * Status code is `200 OK`.
   * Response JSON includes the new item in `items` array.
   * `itemCount` increments by 1.
   * Server logs indicate `INSERT INTO "CartItems"` without `DbUpdateConcurrencyException`.

---

### Scenario B: Guest Cart Merge into Existing Cart

1. **Setup**: Ensure buyer has existing cart.
2. **Execute**:
   ```http
   POST /api/v1/carts/me/merge HTTP/1.1
   Host: localhost:8003
   Authorization: Bearer <valid-jwt>
   Content-Type: application/json

   {
     "guestItems": [
       {
         "skuId": "8c1d0a30-101a-31e9-8375-0cbad24ebf07",
         "quantity": 2
       }
     ]
   }
   ```
3. **Verify**:
   * Status code is `200 OK`.
   * The new guest SKU is cleanly inserted without throwing 500 errors.

---

### Scenario C: Concurrency Conflict Response Validation

1. **Setup**: Simulate a competing write conflict using an outdated `RowVersion`.
2. **Verify**:
   * Status code is `409 Conflict` (NOT `500 Internal Server Error`).
   * Response envelope conforms to:
     ```json
     {
       "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
       "title": "Data concurrency conflict",
       "status": 409,
       "errorCode": "CONCURRENCY_CONFLICT",
       "detail": "The resource was modified or deleted by another operation. Please refresh and try again."
     }
     ```
