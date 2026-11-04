# Research & Architectural Decisions: Fulfillment Internal REST Endpoint Alignment

**Feature Directory**: `specs/013-fulfillment-endpoint-alignment`  
**Phase**: Phase 0 (Technical Context & Research)  
**Date**: 2026-11-04  

---

## 1. Architectural Decisions

### Decision 1: Dedicated Route for Partial Inventory Release (`/release-items`)

- **Context**: When a seller rejects a single sub-order from a multi-shop master checkout, CommerceService must release only that shop's reserved SKUs.
- **Decision**: Target `POST /api/v1/internal/inventories/reservations/{orderId}/release-items` with payload:
  ```json
  {
    "items": [
      {
        "skuId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 2
      }
    ],
    "reason": "SubOrderRejected"
  }
  ```
- **Rationale**: Fulfillment Service Task F04 created a dedicated endpoint in `InternalInventoryReservationsController.cs` for selective item release. The generic `/release` endpoint is strictly reserved for full reservation expiration/cancellation.
- **Alternatives Considered**:
  - `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=SubOrderRejected`: Rejected. In Fulfillment, `/release` wipes out the entire reservation for the order, releasing other shops' held stock.
  - Granular single SKU release calls: Rejected due to N+1 HTTP round-trip latency.

---

### Decision 2: Standardized Internal Route for Shipping Fee Calculation (`/internal/shipping/quote`)

- **Context**: Dynamic carrier shipping rates must be calculated during the checkout quote preview.
- **Decision**: Target `POST /api/v1/internal/shipping/quote` passing:
  ```json
  {
    "sellerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "ghnDistrictId": 1442,
    "ghnWardCode": "20101",
    "items": [
      {
        "skuId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 1,
        "weightGrams": 200
      }
    ]
  }
  ```
- **Rationale**: Aligns with Task F06 in `InternalShippingController.cs`. Fulfillment has standardized all inter-service endpoints under the `/api/v1/internal/` namespace.
- **Alternatives Considered**:
  - `POST /api/v1/fulfillment/shipping/quote`: Rejected. It returns HTTP 404 Not Found on Fulfillment, which under Fail-Closed policies halts customer checkout.
  - Public route `/api/v1/shipping/quote`: Rejected. Internal service key header is required.

---

### Decision 3: Standardized Internal Route for Carrier Shipment Creation (`/internal/shipments`)

- **Context**: When a seller accepts an order, CommerceService requests tracking code and label generation.
- **Decision**: Target `POST /api/v1/internal/shipments` with payload:
  ```json
  {
    "subOrderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  }
  ```
- **Rationale**: Aligns with Task F07 in `InternalShipmentsController.cs`. Provides built-in idempotency (returns existing shipment if already created for `subOrderId`).
- **Alternatives Considered**:
  - `POST /api/v1/shipments`: Rejected. Misses the `/internal/` prefix and produces HTTP 404 Not Found.

---

### Decision 4: Payload Model Serialization & Casing Governance

- **Context**: Ensuring JSON contracts between CommerceService and FulfillmentService deserialize seamlessly without property casing mismatches.
- **Decision**: Enforce explicit camelCase anonymous projection or `JsonNamingPolicy.CamelCase` for all request payloads.
- **Rationale**: Fulfillment's ASP.NET Core Web API controllers use standard camelCase model binding. Explicit anonymous projections (`skuId`, `quantity`) eliminate any PascalCase discrepancies.

---

## 2. Risk & Impact Analysis

| Component | Target Symbol / Method | Risk Level | Mitigation Strategy |
| :--- | :--- | :---: | :--- |
| `FulfillmentClient` | `ReleaseSubOrderStockAsync` | Medium | Update URI to `/release-items` and add `reason` property; verified by unit tests. |
| `FulfillmentClient` | `CalculateShopShippingFeeAsync` | Medium | Update URI to `/internal/shipping/quote`; verify 200, 400, and 503 fail-closed responses. |
| `FulfillmentClient` | `CreateShipmentAsync` | Low | Update URI to `/internal/shipments`; verify tracking code mapping. |
| `FulfillmentClientTests` | Test suite mock assertions | Low | Update expected URIs across all 14 test fixtures to match new routes. |
| `fulfillment-contracts.json` | OpenAPI documentation | Low | Update contract path declarations to reflect `/internal/` routes. |
