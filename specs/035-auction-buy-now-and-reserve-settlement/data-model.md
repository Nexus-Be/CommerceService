# Data Model: Auction Buy Now & Reserve Price Settlement Integration

**Feature**: `035-auction-buy-now-and-reserve-settlement`  
**Date**: 2026-11-20  
**Service**: `CommerceService` (PostgreSQL `commerce_db:5435`)  

---

## 1. Domain Entities & Enums

### Enum: `AuctionOrderSource` (`Commerce.Domain/Enums/AuctionOrderSource.cs`)

Represents the acquisition source of the auction order.

```csharp
namespace Commerce.Domain.Enums;

public enum AuctionOrderSource
{
    Winner = 1,
    SecondChance = 2,
    BuyNow = 3 // [NEW] Order created from "Buy Now" (100% held deposit)
}
```

### Extensions: `AuctionOrderSourceExtensions` (`Commerce.Domain/Enums/AuctionOrderSourceExtensions.cs`)

Maps the internal C# enum to the standardized API / JSON wire contract format.

```csharp
namespace Commerce.Domain.Enums;

public static class AuctionOrderSourceExtensions
{
    public static string ToWireValue(this AuctionOrderSource source) =>
        source switch
        {
            AuctionOrderSource.SecondChance => "SECOND_CHANCE",
            AuctionOrderSource.BuyNow => "BUY_NOW",
            _ => "WINNER"
        };
}
```

---

## 2. Entity: `AuctionOrder` (`Commerce.Domain/Entities/AuctionOrder.cs`)

Database table: `auction_orders` in `commerce_db`.

### Key Fields & Invariants for Buy Now:

| Field Name | Type | Persistence / Mapping | Invariant for `source = "BUY_NOW"` |
|---|---|---|---|
| `Id` | `Guid` | Primary Key | Unique UUID |
| `OrderNo` | `string` | Unique, varchar(100) | `AO-{timestamp}-{random}` |
| `AuctionId` | `Guid` | Not Null, indexed | Auction session UUID |
| `BuyerId` | `Guid` | Not Null | UUID of buyer who clicked Buy Now |
| `SellerId` | `Guid` | Not Null | UUID of merchant / seller |
| `WinningAmount` | `decimal` | Precision(18, 2) | Gross BuyNowPrice (e.g. 5,000,000 VND) |
| `DepositAmount` | `decimal` | Precision(18, 2) | Equal to `WinningAmount` (100% held deposit) |
| `RemainingAmount` | `decimal` | Precision(18, 2) | Strictly `0.00 VND` ($\text{WinningAmount} - \text{DepositAmount}$) |
| `ShippingFeeAmount` | `decimal?` | Precision(18, 2) | `null` initially; set after `PUT .../shipping` |
| `TotalAmount` | `decimal?` | Precision(18, 2) | `null` initially; equals `ShippingFeeAmount` after shipping submission |
| `SellerReceiveAmount` | `decimal` | Precision(18, 2) | $\text{WinningAmount} \times 0.95$ (e.g. 4,750,000 VND) |
| `PlatformFeeAmount` | `decimal` | Precision(18, 2) | $\text{WinningAmount} \times 0.05$ (e.g. 250,000 VND) |
| `OrderSource` | `AuctionOrderSource` | Varchar(32), `HasConversion<string>()` | Persisted as `"BuyNow"` |
| `Status` | `SubOrderStatus` | String conversion | Initialized to `PendingPayment` |
| `PaymentDeadlineAt` | `DateTimeOffset` | Timestamp with tz | `UtcNow.AddHours(24)` |

---

## 3. Financial & Lifecycle State Transitions

```
[Auction Settlement Received (source = BUY_NOW)]
       │
       ▼
Status: PendingPayment
RemainingAmount: 0 VND
DepositAmount: 5,000,000 VND
TotalAmount: null
       │
       ├──────────────────────────────────────────────────┐
       │ (Buyer submits delivery address within 24h)      │ (24h Payment Deadline expires)
       ▼                                                  ▼
Status: PendingPayment                            Status: Cancelled
TotalAmount: ShippingFeeAmount (e.g. 35,000 VND)  PaymentTimeoutWorker:
       │                                            - Dispatches callback: PaymentTimedOut
       │                                            - orderSource: "BUY_NOW"
       │ (Buyer pays TotalAmount via Nexus Wallet)  - Zero buyer penalties from Commerce
       ▼
Status: Paid
PaidAt: UtcNow
SellerRespondDeadlineAt: UtcNow.AddHours(24)
Commerce:
  - Dispatches callback: Paid (orderSource: "BUY_NOW")
Auction Service:
  - Captures 100% held deposit into Escrow
       │
       ├──────────────────────────────────────────────────┐
       │ (Seller confirms order within 24h)               │ (Seller rejects or times out)
       ▼                                                  ▼
Status: Confirmed                                 Status: SellerRejected / SellerExpired
Commerce dispatches callback:                     Commerce:
  - SellerAccepted                                  - Refunds TotalAmount (shipping fee)
  - orderSource: "BUY_NOW"                        Auction:
                                                    - Unholds/refunds 100% deposit
```

---

## 4. DTO Contracts (Inbound & Outbound)

### Inbound Settlement Request (`POST /api/v1/auction-orders/settlement`)
```json
{
  "sessionId": "4f9c6d1a-8b2e-4e7a-9a1b-3c5d7e9f0a2b",
  "source": "BUY_NOW",
  "winnerId": "11111111-2222-3333-4444-555555555555",
  "sellerId": "99999999-8888-7777-6666-555555555555",
  "itemId": "aaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "winningAmount": 5000000.0,
  "depositAmount": 5000000.0,
  "paymentDeadlineAt": "2026-11-21T16:00:00Z"
}
```

### Outbound Callback (`POST /api/v1/auctions/{auctionId}/settlement/payment-status`)
```json
{
  "orderId": "7b8c9d0e-1f2a-3b4c-5d6e-7f8a9b0c1d2e",
  "winnerId": "11111111-2222-3333-4444-555555555555",
  "status": "Paid",
  "amount": 5000000.0,
  "paidAt": "2026-11-20T17:30:00Z",
  "eventId": "f1e2d3c4-b5a6-7890-1234-56789abcdef0",
  "occurredAt": "2026-11-20T17:30:00Z",
  "orderSource": "BUY_NOW"
}
```
