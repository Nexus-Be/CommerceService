# Data Model & Interaction Contracts: Fulfillment Endpoint Alignment

**Feature Directory**: `specs/013-fulfillment-endpoint-alignment`  
**Phase**: Phase 1 (Data Model & Sequences)  
**Date**: 2026-11-04  

---

## 1. Outbound Request & Response DTOs

### 1.1. Partial Stock Release (`POST /api/v1/internal/inventories/reservations/{referenceId}/release-items`)

#### Request: `ReleaseReservationItemsRequest`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `items` | `List<ReleaseReservationItem>` | Yes | Array of items to decrement from the reservation hold |
| `reason` | `string` | No | Rejection context (defaults to `"SubOrderRejected"`) |

#### Item: `ReleaseReservationItem`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `skuId` | `Guid` | Yes | Unique identifier of the product variant |
| `quantity` | `int` | Yes | Quantity to release back to available stock |

#### Response: `ReleaseReservationResponse`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `success` | `bool` | Yes | Indicates successful partial stock restoration |

---

### 1.2. Dynamic Shipping Quote (`POST /api/v1/internal/shipping/quote`)

#### Request: `ShopShippingQuoteRequest`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `sellerId` | `Guid` | Yes | Identifier of the originating shop |
| `ghnDistrictId` | `int` | Yes | Carrier destination district code |
| `ghnWardCode` | `string` | Yes | Carrier destination ward code |
| `items` | `List<ShippingQuoteItem>` | Yes | Package items with physical weights |

#### Response: `ShippingQuoteResponse`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `feeAmount` | `decimal` | Yes | Calculated shipping fee in VND |

---

### 1.3. Carrier Shipment Dispatch (`POST /api/v1/internal/shipments`)

#### Request: `CreateShipmentRequest`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `subOrderId` | `Guid` | Yes | Target sub-order identifier |

#### Response: `ShipmentCreationResult`
| Field | Type | Required | Description |
| :--- | :--- | :---: | :--- |
| `trackingCode` | `string` | Yes | Active carrier tracking code (e.g., `GHN...`) |
| `carrierCode` | `string` | Yes | Carrier code (e.g., `GHN`) |
| `labelPrintUrl` | `string` | Yes | Printable package waybill URL |

---

## 2. Interaction Sequence Diagrams

### 2.1. Partial Stock Release Flow
```mermaid
sequenceDiagram
    autonumber
    actor Seller
    participant Commerce as CommerceService (8003)
    participant DB as Commerce DB
    participant Fulfillment as FulfillmentService (8006)

    Seller->>Commerce: POST /api/v1/orders/{orderId}/sub-orders/reject { subOrderId, reason }
    Commerce->>DB: Update SubOrder.Status = Cancelled
    Commerce->>DB: Add OrderCancellation & OrderRefund (if paid)
    Commerce->>DB: SaveChangesAsync()
    Commerce->>Fulfillment: POST /api/v1/internal/inventories/reservations/{orderId}/release-items { items, reason }
    Fulfillment-->>Commerce: 200 OK (Partial Stock Released)
    Commerce-->>Seller: 200 OK (SubOrder Cancelled)
```

### 2.2. Dynamic Shipping Quote Flow
```mermaid
sequenceDiagram
    autonumber
    actor Buyer
    participant Commerce as CommerceService (8003)
    participant Fulfillment as FulfillmentService (8006)

    Buyer->>Commerce: POST /api/v1/checkout/quote { items, address }
    Commerce->>Fulfillment: POST /api/v1/internal/shipping/quote { sellerId, ghnDistrictId, ghnWardCode, items }
    Fulfillment-->>Commerce: 200 OK { feeAmount: 25000 }
    Commerce-->>Buyer: 200 OK (Quote with Shipping Fee)
```

### 2.3. Shipment Creation Flow
```mermaid
sequenceDiagram
    autonumber
    actor Seller
    participant Commerce as CommerceService (8003)
    participant Fulfillment as FulfillmentService (8006)

    Seller->>Commerce: POST /api/v1/orders/{orderId}/sub-orders/accept { subOrderId }
    Commerce->>Fulfillment: POST /api/v1/internal/shipments { subOrderId }
    Fulfillment-->>Commerce: 200 OK { trackingCode, labelPrintUrl }
    Commerce-->>Seller: 200 OK (Shipment Created)
```
