# Data Model: Order Financial Lifecycle, Staff-Managed Platform Fee & Voucher Extensibility

**Feature Branch**: `023-order-financial-lifecycle-and-voucher-extensibility`  
**Date**: 2026-11-16  
**Status**: Completed  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Entity Relationships & Schemas (Zero Schema Drift)

No new PostgreSQL tables or column migrations are introduced. All operations leverage existing entities in `Commerce.Domain.Entities`.

```mermaid
erDiagram
    Orders ||--|{ SubOrders : "contains (1:N)"
    SubOrders ||--|{ SubOrderItems : "contains (1:N)"
    SubOrders ||--o{ OrderRefunds : "has (0:N)"
    SubOrders ||--o| OrderCancellations : "has (0:1)"
    Orders ||--o{ DiscountCodeUsages : "applies (0:N)"
    SubOrders ||--o{ DiscountCodeUsages : "applies (0:N)"

    Orders {
        uuid Id PK
        uuid BuyerId
        int Status "0: PendingPayment, 1: PaymentProcessing, 2: Paid, 3: Cancelled, 4: Expired"
        numeric TotalAmount "numeric(18,2)"
        timestamptz PaymentDeadlineAt
        int PaymentAttemptCount
        uuid WalletTransactionId
    }

    SubOrders {
        uuid Id PK
        string SubOrderNo UK
        uuid OrderId FK
        uuid BuyerId
        uuid SellerId
        int Status "0: PendingPayment, 1: Paid, 2: Confirmed, 3: Shipped, 4: Delivered, 5: Completed, 6: Cancelled"
        int PaymentMethod "0: Wallet, 1: VnPay (Deprecated), 2: Cod"
        numeric ItemsAmount "numeric(18,2)"
        numeric ProductDiscountAmount "numeric(18,2)"
        numeric ShopDiscountAmount "numeric(18,2)"
        numeric PlatformMarkupAmount "numeric(18,2)"
        numeric SystemDiscountAmount "numeric(18,2)"
        numeric ShippingFeeAmount "numeric(18,2)"
        numeric TotalAmount "numeric(18,2)"
        numeric SellerReceiveAmount "numeric(18,2)"
        numeric PlatformFeeAmount "numeric(18,2)"
        timestamptz PaymentDeadlineAt
        timestamptz PaidAt
        timestamptz CompletedAt
        timestamptz CancelledAt
        bigint RowVersion
    }

    OrderRefunds {
        uuid Id PK
        uuid SubOrderId FK
        uuid OrderId FK
        int RefundType "0: Full, 1: Partial"
        numeric Amount "numeric(18,2)"
        int Status "0: Requested, 1: Completed, 2: Failed"
        string ReasonCode
        string ReasonNote
        int ActorRole "0: Buyer, 1: Seller, 2: System"
        uuid RequestedBy
        timestamptz RequestedAt
        timestamptz CompletedAt
    }

    DiscountCodeUsages {
        uuid Id PK
        uuid DiscountCodeId FK
        uuid UserId
        uuid OrderId FK
        uuid SubOrderId FK
        numeric DiscountAmount "numeric(18,2)"
        int Status "0: Reserved, 1: Used, 2: Released"
        timestamptz ReservedAt
        timestamptz UsedAt
        timestamptz ReleasedAt
    }
```

---

## 2. Mathematical Invariants & Field Definitions

### 8 Dimensions of SubOrder Financials

| Field Name | Type | Mathematical Formula | Accounting Role |
| :--- | :--- | :--- | :--- |
| `ItemsAmount` | `decimal` | $\sum (\text{Quantity} \times \text{UnitPrice})$ | Gross goods value |
| `ProductDiscountAmount` | `decimal` | $\sum \text{Quantity} \times (\text{UnitPrice} - \text{UnitPriceAfterDiscount})$ | Item-level flash sale subsidy |
| `ShopDiscountAmount` | `decimal` | Value subsidized by Seller voucher | Reduces seller receive amount |
| `SellerReceiveAmount` | `decimal` | `ItemsAmount - ProductDiscountAmount - ShopDiscountAmount` | **Net seller payout on delivery** |
| `PlatformMarkupAmount` | `decimal` | $\text{Round}(\text{SellerReceiveAmount} \times \frac{\text{PlatformMarkupPercent}}{100m}, 2)$ | Gross platform commission |
| `SystemDiscountAmount` | `decimal` | Value subsidized by Platform voucher | Platform marketing subsidy |
| `PlatformFeeAmount` | `decimal` | `PlatformMarkupAmount - SystemDiscountAmount` | **Net platform margin (can be negative)** |
| `ShippingFeeAmount` | `decimal` | Carrier rate quote (GHN dynamic fee) | Payable to logistics carrier |
| `TotalAmount` | `decimal` | `SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount` | **Net amount billed to Buyer** |

### Strict Invariant Constraint
$$\boxed{\text{TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}}$$
- If violated, `SubOrder.CalculateFinancials` throws `DomainException`.
- When `SystemDiscountAmount > PlatformMarkupAmount`, `PlatformFeeAmount` is negative, reflecting platform co-funding without diminishing `SellerReceiveAmount`.

---

## 3. Data Transfer Objects (DTOs) & Contracts

### Platform Fee Management DTOs
```csharp
public sealed record PlatformFeeResponse(
    decimal DefaultPercent,
    string UpdatedByRole,
    DateTimeOffset UpdatedAt);

public sealed record UpdatePlatformFeeRequest(
    [Range(0.0, 100.0, ErrorMessage = "Platform fee rate must be between 0% and 100%.")]
    decimal DefaultPercent);
```

### Voucher Extensibility Abstraction DTOs
```csharp
public sealed record VoucherCalculationResult(
    Dictionary<Guid, decimal> ShopDiscounts,
    Dictionary<Guid, decimal> SystemDiscounts,
    Dictionary<Guid, decimal> ProductDiscounts,
    List<string> AppliedVoucherCodes);
```

---

## 4. Lifecycle State Transitions

### Order Status Lifecycle
```mermaid
stateDiagram-v2
    [*] --> PendingPayment: Order created (PaymentMethod = Wallet / COD)
    PendingPayment --> Paid: Wallet debit succeeds (TotalAmount in Escrow)
    PendingPayment --> Cancelled: Cancelled by Buyer/Seller before payment
    PendingPayment --> Expired: 15-minute deadline expires without payment
    Paid --> Cancelled: Cancelled by Seller rejection before shipment
    Paid --> Completed: All sub-orders completed
```

### SubOrder Status Lifecycle & Financial Triggers
```mermaid
stateDiagram-v2
    [*] --> PendingPayment: Checkout created
    PendingPayment --> Paid: Wallet debit succeeds
    PendingPayment --> Confirmed: COD order placed
    Paid --> Confirmed: Shop accepts order
    Confirmed --> Shipped: Package handed over to carrier
    Shipped --> Delivered: Carrier delivers package
    Delivered --> Completed: Buyer confirms receipt
    note right of Completed
        Financial Trigger:
        SettleSellerSubOrderAsync
        Transfers SellerReceiveAmount to Seller Wallet
    end note
    Paid --> Cancelled: Shop rejects or buyer cancels
    note right of Cancelled
        Financial Trigger:
        Wallet: RefundSubOrderPaymentAsync
        COD: Skip wallet refund (No cashout)
        Release DiscountCodeUsages
    end note
```
