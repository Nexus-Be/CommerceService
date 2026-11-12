# API Contract: Cart Item Mutations & Concurrency Protocols

**Feature Branch**: `018-cart-concurrency-and-state-remediation`  
**Date**: 2026-11-10  
**Target Service**: `CommerceService` (Port 8003)  
**Security Boundary**: Requires `Authorization: Bearer <JWT>` containing role `buyer`, `admin`, or `staff`.

---

## 1. Endpoints

### 1.1 `POST /api/v1/carts/me/items` (CART-04)

Adds a product SKU to the buyer's active shopping cart, or increments quantity if already present.

#### Request Headers
* `Authorization`: `Bearer <token>` (Required)
* `Content-Type`: `application/json`

#### Request Body
```json
{
  "skuId": "9d2e1b40-202b-42fa-9486-0dcbe35fce18",
  "quantity": 2,
  "productId": "7a30cfb1-127e-4091-87a3-5c7ffc26da42"
}
```

#### Success Response (HTTP 200 OK)
* `Content-Type`: `application/json`
```json
{
  "id": "c1f1982b-65c9-4a7b-b0b3-1e24749622d1",
  "buyerId": "a9b8c7d6-e5f4-4321-abcd-ef0123456789",
  "status": "Active",
  "itemCount": 1,
  "subtotalAmount": 100000.00,
  "items": [
    {
      "id": "e2f3a4b5-c6d7-4e8f-9a0b-1c2d3e4f5a6b",
      "productId": "7a30cfb1-127e-4091-87a3-5c7ffc26da42",
      "skuId": "9d2e1b40-202b-42fa-9486-0dcbe35fce18",
      "sellerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "productName": "Wireless Noise Cancelling Headphones",
      "quantity": 2,
      "unitPriceSnapshot": 50000.00,
      "lineTotal": 100000.00,
      "addedAt": "2026-11-10T09:00:00Z",
      "isAvailable": true,
      "disabledReason": null
    }
  ],
  "lastActivityAt": "2026-11-10T09:00:00Z"
}
```

---

### 1.2 `POST /api/v1/carts/me/merge` (CART-12)

Merges items from temporary unauthenticated guest storage into the authenticated buyer's cart upon login.

#### Request Body
```json
{
  "guestItems": [
    {
      "skuId": "9d2e1b40-202b-42fa-9486-0dcbe35fce18",
      "quantity": 1,
      "productId": "7a30cfb1-127e-4091-87a3-5c7ffc26da42"
    },
    {
      "skuId": "8c1d0a30-101a-31e9-8375-0cbad24ebf07",
      "quantity": 3
    }
  ]
}
```

#### Success Response (HTTP 200 OK)
Returns the consolidated `CartDto`.

---

## 2. Universal Error Matrix (RFC 9110 Problem Details)

All error responses return `Content-Type: application/problem+json` formatted according to RFC 9110:

| HTTP Status | Error Code (`errorCode`) | Title | Example Detail Message | Trigger Condition |
| :--- | :--- | :--- | :--- | :--- |
| **400 Bad Request** | `VAL_INVALID_INPUT` | `Invalid argument` | `Quantity must be greater than zero.` | `request.Quantity <= 0` |
| **400 Bad Request** | `CART_ITEM_LIMIT_EXCEEDED` | `Domain error` | `Total quantity for a single product cannot exceed 99.` | Cumulative quantity for SKU > 99 |
| **400 Bad Request** | `CART_LIMIT_EXCEEDED` | `Domain error` | `Cart cannot exceed maximum limit of 50 different items.` | Adding 51st distinct SKU |
| **400 Bad Request** | `CATALOG_SKU_INACTIVE` | `Domain error` | `The specified product SKU is unavailable or out of stock.` | Catalog SKU has `IsPurchasable == false` |
| **400 Bad Request** | `INSUFFICIENT_STOCK` | `Domain error` | `Insufficient stock available in warehouse.` | Fulfillment inventory check fails |
| **404 Not Found** | `CATALOG_SKU_NOT_FOUND` | `Resource not found` | `The specified product SKU was not found in catalog.` | Catalog Service does not recognize SKU |
| **409 Conflict** | `CONCURRENCY_CONFLICT` | `Data concurrency conflict` | `The resource was modified or deleted by another operation. Please refresh and try again.` | Optimistic concurrency token mismatch (`DbUpdateConcurrencyException`) |
| **500 Internal Server Error** | `INTERNAL_SERVER_ERROR` | `An unexpected error occurred` | `An internal server error occurred. Please try again later.` | Unexpected fatal infrastructure crash |

---

## 3. RFC 9110 Concurrency Problem Details Envelope

When an optimistic locking collision occurs:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Data concurrency conflict",
  "status": 409,
  "errorCode": "CONCURRENCY_CONFLICT",
  "detail": "The resource was modified or deleted by another operation. Please refresh and try again.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
  "timestamp": "2026-11-10T09:15:30Z"
}
```
