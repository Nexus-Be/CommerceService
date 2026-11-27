# Phase 1 Data Model: Value Object & Contract Invariants

**Feature**: `037-route-alignment-and-code-hygiene`  
**Date**: 2026-11-26  
**Status**: Completed  

---

## 1. Entities & Value Objects Involved

### 1.1 `ShippingAddressSnapshot` (Domain Value Object)
Persisted as JSON / owned entity on `SubOrder` representing the buyer's delivery destination snapshot at checkout.

| Property | Type | Nullability | Invariant / Default |
| :--- | :--- | :---: | :--- |
| `ReceiverName` | `string` | Non-null | `= string.Empty;` |
| `ContactPhone` | `string` | Non-null | `= string.Empty;` |
| `Street` | `string` | Non-null | `= string.Empty;` |
| `Ward` | `string` | Non-null | `= string.Empty;` |
| `District` | `string` | Non-null | `= string.Empty;` *(Guaranteed via `resolvedAddress.District ?? string.Empty`)* |
| `Province` | `string` | Non-null | `= string.Empty;` *(Guaranteed via `resolvedAddress.Province ?? string.Empty`)* |
| `ProvinceId` | `int?` | Nullable | GHN province ID; null for legacy orders |
| `GhnDistrictId`| `int` | Non-null | Carrier routing district ID |
| `GhnWardCode` | `string` | Non-null | `= string.Empty;` *(Guaranteed via `resolvedAddress.GhnWardCode ?? string.Empty`)* |
| `FullAddressText` | `string` | Non-null | Formatted summary: `"{ReceiverName}, {Street}, {Ward}, {District}, {Province}"` |

### 1.2 `ShippingAddressDto` (Application Contract DTO)
Cross-boundary DTO used when resolving addresses via `IUserClient` and mapping to carrier quotes.

| Parameter | Type | Nullability | Invariant / Default |
| :--- | :--- | :---: | :--- |
| `FullName` | `string` | Non-null | Receiver full name |
| `PhoneNumber` | `string` | Non-null | Validated contact phone |
| `AddressLine` | `string` | Non-null | Street address |
| `ProvinceName` | `string` | Non-null | Standardized province name |
| `DistrictName` | `string` | Non-null | Standardized district name *(Guaranteed via `rawAddress.District ?? string.Empty`)* |
| `WardName` | `string` | Non-null | Standardized ward name |
| `GhnDistrictId`| `int` | Non-null | Carrier district code (> 0) |
| `GhnWardCode` | `string` | Non-null | Carrier ward code (non-empty string) |
| `ProvinceId` | `int?` | Nullable | Optional GHN province ID |

---

## 2. API Contract Model: Shipment Tracking

### 2.1 `ShipmentTrackingDto` (Response DTO)
Consolidated shipment tracking response returned by `GET /api/v1/orders/{orderId}/shipments`.

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderStatus": "Shipping",
  "createdAt": "2026-11-26T07:00:00Z",
  "shipments": [
    {
      "subOrderId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "sellerId": "d3b07384-d113-467f-94d3-0d321d58ee6e",
      "shopName": "Official Tech Store",
      "trackingCode": "GHN-HCM-8829103",
      "shippingFee": 32000.0,
      "status": "Shipping",
      "timeline": [
        {
          "status": "PendingPayment",
          "description": "Order created, awaiting payment.",
          "timestamp": "2026-11-26T07:00:00Z"
        },
        {
          "status": "Confirmed",
          "description": "Payment confirmed, seller preparing goods.",
          "timestamp": "2026-11-26T07:05:00Z"
        },
        {
          "status": "Shipping",
          "description": "Carrier GHN picked up package.",
          "timestamp": "2026-11-26T08:30:00Z"
        }
      ]
    }
  ]
}
```
