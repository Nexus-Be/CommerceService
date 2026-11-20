# Data Model: Real-World Carrier Logistics and Shipment Tracking

**Feature**: `021-real-world-shipping-and-tracking`  
**Date**: 2026-11-12  

---

## 1. Entity Changes

### 1.1. `SubOrder` Entity (`Commerce.Domain/Entities/SubOrder.cs`)

| Column Name | Data Type | Nullable | Description | Index |
|:---|:---|:---:|:---|:---:|
| `CarrierCode` | `varchar(50)` | YES | Identifier of logistics provider (e.g., `"GHN"`). | No |
| `TrackingCode` | `varchar(100)` | YES | Official carrier tracking code (e.g., `"GHN-SGN-1029384"`). | **YES (IX_SubOrders_TrackingCode)** |
| `LabelPrintUrl` | `varchar(500)` | YES | URL to printable PDF shipping label / barcode sticker. | No |
| `EstimatedDeliveryAt` | `timestamptz` | YES | Estimated delivery deadline calculated by carrier. | No |
| `ShipmentPendingCreation`| `boolean` | NO | Flag indicating shipment creation retry is required (default `false`). | No |

### 1.2. `AuctionOrder` Entity (`Commerce.Domain/Entities/AuctionOrder.cs`)

| Column Name | Data Type | Nullable | Description | Index |
|:---|:---|:---:|:---|:---:|
| `CarrierCode` | `varchar(50)` | YES | Identifier of logistics provider (e.g., `"GHN"`). | No |
| `TrackingCode` | `varchar(100)` | YES | Official carrier tracking code. | **YES (IX_AuctionOrders_TrackingCode)** |
| `LabelPrintUrl` | `varchar(500)` | YES | URL to printable PDF shipping label. | No |
| `EstimatedDeliveryAt` | `timestamptz` | YES | Estimated delivery deadline. | No |

### 1.3. `OrderStatusHistory` Entity (`Commerce.Domain/Entities/OrderStatusHistory.cs`)
*Reused existing schema*:
- `SubOrderId`: FK to `SubOrders.Id`
- `AuctionOrderId`: FK to `AuctionOrders.Id`
- `FromStatus`: `SubOrderStatus` nullable
- `ToStatus`: `SubOrderStatus`
- `ActorRole`: `OrderActorRole.System`
- `Reason`: Carrier status note, hub transit location, or delivery attempt reason
- `CreatedAt`: Timestamp of event

---

## 2. DTO Contracts (`Commerce.Application/Services/Order/`)

### 2.1. Inbound Webhook: `UpdateDeliveryStatusRequest`
```csharp
public sealed record UpdateDeliveryStatusRequest(
    string Status,
    DateTimeOffset? DeliveredAt = null,
    string? CarrierCode = null,
    string? TrackingCode = null,
    string? Note = null,
    string? Location = null);
```

### 2.2. Query Response: `ShipmentTimelineResponse`
```csharp
public sealed record TimelineEventDto(
    string Status,
    string Title,
    string Description,
    DateTimeOffset OccurredAt,
    string? Location = null);

public sealed record ShipmentTimelineResponse(
    Guid SubOrderId,
    string SubOrderNo,
    Guid OrderId,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingCode,
    string? LabelPrintUrl,
    string CurrentStatus,
    DateTimeOffset? EstimatedDeliveryAt,
    ShippingAddressSnapshot ShippingAddress,
    List<TimelineEventDto> Timeline);
```

### 2.3. Retry Response: `RetryShipmentResponse`
```csharp
public sealed record RetryShipmentResponse(
    Guid SubOrderId,
    string SubOrderNo,
    string TrackingCode,
    string CarrierCode,
    string LabelPrintUrl,
    DateTimeOffset CreatedAt);
```
