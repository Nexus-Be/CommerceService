# Data Model & State Alignment: Purchase Record Synchronization

**Feature Branch**: `020-catalog-purchase-sync`  
**Date**: 2026-11-11  

---

## 1. Domain Entities & Field Mappings

No schema alterations or database migrations are required for `CommerceService`. All necessary identifier fields already exist on primary persistence models in PostgreSQL (`commerce_db:5435`).

### Entity: `SubOrderItem` (Source for Standard Order Items)
* **Table**: `SubOrderItems`
* **Relevant Fields**:
  - `Id`: `Guid` (Primary Key)
  - `SubOrderId`: `Guid` (Foreign Key $\rightarrow$ `SubOrders.Id`)
  - `ProductId`: `Guid` (Cross-service reference $\rightarrow$ Catalog Service parent Product ID)
  - `SkuId`: `Guid` (Variant ID - NOT transmitted to Catalog purchase sync)
  - `SellerId`: `Guid` (Seller reference)
  - `ProductNameSnapshot`: `string`

### Entity: `SubOrder` (Source for Order Context)
* **Table**: `SubOrders`
* **Relevant Fields**:
  - `Id`: `Guid` (Transmitted as `orderId`)
  - `OrderId`: `Guid` (Master Order ID)
  - `BuyerId`: `Guid` (Transmitted as `buyerId`)
  - `Status`: `SubOrderStatus` (Transitions to `Completed = 5`)
  - `CompletedAt`: `DateTimeOffset?` (Transmitted as `completedAt`)

### Entity: `AuctionOrder` (Source for Won Auction Orders)
* **Table**: `AuctionOrders`
* **Relevant Fields**:
  - `Id`: `Guid` (Transmitted as `orderId`)
  - `BuyerId`: `Guid` (Transmitted as `buyerId`)
  - `ProductId`: `Guid` (Transmitted as `productId`)
  - `Status`: `SubOrderStatus` (Transitions to `Completed = 5`)
  - `CompletedAt`: `DateTimeOffset?` (Transmitted as `completedAt`)

---

## 2. Application Layer Contracts & DTOs

### Contract: `CatalogPurchaseSyncRequest`
Defined in `Commerce.Application/Interfaces/ICatalogClient.cs`:

```csharp
namespace Commerce.Application.Interfaces;

public sealed record CatalogPurchaseSyncRequest(
    Guid BuyerId,
    Guid OrderId,
    Guid ProductId,
    DateTimeOffset? CompletedAt = null);
```

### Raw Internal Response Model (Private in `CatalogClient.cs`)

```csharp
private sealed class CatalogPurchaseSyncRawResponse
{
    public Guid Id { get; set; }
    public Guid BuyerId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

---

## 3. Order Completion & Synchronization Lifecycle Flow

```mermaid
sequenceDiagram
    autonumber
    actor Buyer as Authenticated Buyer
    participant API as Commerce.Api (OrdersController / AuctionOrdersController)
    participant SVC as Commerce.Application (OrderService / AuctionOrderService)
    participant DB as PostgreSQL (commerce_db:5435)
    participant WAL as WalletService (Port 8008)
    participant CAT as CatalogService (Port 8002)

    Buyer->>API: POST /api/v1/sub-orders/{subOrderId}/confirm-receipt
    API->>SVC: BuyerConfirmSubOrderReceiptAsync(buyerId, subOrderId)
    
    rect rgb(240, 248, 255)
        Note over SVC, DB: 1. Core State Transition & Escrow Settlement
        SVC->>DB: Query SubOrder with .Include(s => s.Items)
        SVC->>SVC: Verify buyer ownership & status == Delivered/Shipped
        SVC->>SVC: Update status = Completed, CompletedAt = UtcNow
        SVC->>WAL: SettleSellerSubOrderAsync(subOrderId, sellerId, amount)
        SVC->>DB: SaveChangesAsync()
    end

    rect rgb(245, 255, 250)
        Note over SVC, CAT: 2. Secondary Side-Effects (Non-Blocking)
        SVC->>SVC: Deduplicate distinct ProductIds from subOrder.Items
        loop In Parallel (Task.WhenAll)
            SVC->>CAT: POST /api/v1/internal/purchases { buyerId, orderId, productId, completedAt }
            CAT-->>SVC: 201 Created / 200 OK
        end
        Note over SVC: If CAT fails: catch, log warning, do NOT abort
    end

    SVC-->>API: ConfirmReceiptResponse
    API-->>Buyer: 200 OK
```

---

## 4. Invariants & Deduplication Rules

1. **Product Deduplication Rule**:
   $$\text{SyncSet} = \{ p \in \text{Items} \mid p.\text{ProductId} \neq \emptyset \}.\text{DistinctBy}(p \implies p.\text{ProductId})$$
   Never transmit multiple requests for the same `ProductId` within a single order completion invocation.

2. **Transaction Isolation**:
   The call to `_dbContext.SaveChangesAsync()` occurs prior to or alongside secondary side-effects. Catalog synchronization errors MUST NOT trigger database rollback or throw `500 Internal Server Error` to the buyer.
