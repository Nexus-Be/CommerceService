# Quickstart Guide: Route Alignment & Compiler Warning Hygiene Verification

**Feature**: `037-route-alignment-and-code-hygiene`  
**Date**: 2026-11-26  

---

## 1. Prerequisites

- .NET 8.0 SDK installed
- Working directory: `D:\CommerceService`

---

## 2. Build Verification (Zero Warnings & Zero Errors)

To verify all nullability and member hiding compiler warnings have been resolved:

```powershell
dotnet build Commerce.sln -c Release
```

**Expected Outcome:**
```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 3. Automated Test Verification

Run all unit and integration test suites:

```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release
```

**Expected Outcome:**
```text
Passed!  - Failed:     0, Passed:   696, Skipped:    13, Total:   709
```

---

## 4. API Endpoint Verification (Local Development)

### 4.1 Canonical Route Verification
```bash
curl -X GET "http://localhost:5105/api/v1/orders/{orderId}/shipments" \
  -H "Authorization: Bearer <BUYER_OR_ADMIN_JWT_TOKEN>" \
  -H "Accept: application/json"
```

**Expected Response**: `200 OK` with JSON envelope containing `orderId`, `orderStatus`, and list of `shipments`.

### 4.2 Legacy Alias Route Verification
```bash
curl -X GET "http://localhost:5105/api/v1/shipments/order/{orderId}" \
  -H "Authorization: Bearer <BUYER_OR_ADMIN_JWT_TOKEN>" \
  -H "Accept: application/json"
```

**Expected Response**: `200 OK` with identical payload.

### 4.3 Authorization Gate Verification (Cross-Tenant Access)
```bash
curl -X GET "http://localhost:5105/api/v1/orders/{orderId}/shipments" \
  -H "Authorization: Bearer <DIFFERENT_BUYER_JWT_TOKEN>"
```

**Expected Response**: `403 Forbidden` (`errorCode: "FORBIDDEN"`).
