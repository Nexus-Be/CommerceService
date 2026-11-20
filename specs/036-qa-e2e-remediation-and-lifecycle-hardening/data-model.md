# Data Model & Schema Modifications: SPEC-036

**Feature**: `036-qa-e2e-remediation-and-lifecycle-hardening`  
**Date**: 2026-11-21  
**Service**: `CommerceService` (PostgreSQL `commerce_db:5435`)  

---

## 1. Domain Entities & Enums

### 1.1 `AuctionOrder` Entity (`Commerce.Domain/Entities/AuctionOrder.cs`)

Field `WinningBidId` changed from non-nullable `required Guid` to nullable `Guid?`:

```csharp
namespace Commerce.Domain.Entities;

public class AuctionOrder : AuditableEntity
{
    public required Guid Id { get; set; }
    public required string OrderNo { get; set; }
    public required Guid AuctionId { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }

    // [MODIFIED]: Nullable for Buy Now settlements where no bids exist in the auction ledger
    public Guid? WinningBidId { get; set; }

    public required Guid ProductId { get; set; }
    public Guid? SkuId { get; set; }
    // ... remaining fields unchanged
}
```

### 1.2 `SubOrderStatus` Enum (`Commerce.Domain/Enums/SubOrderStatus.cs`)

Expanded with `Returning = 8` to model package return in-flight:

```csharp
namespace Commerce.Domain.Enums;

public enum SubOrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Confirmed = 2,
    SellerAccepted = 2,
    Shipping = 3,
    Shipped = 3,
    Delivered = 4,
    Completed = 5,
    Cancelled = 6,
    Refunded = 7,
    Returning = 8 // [NEW]: Package failed delivery 3 times, in-transit back to merchant warehouse
}
```

---

## 2. Database Migrations

### 2.1 Remediation Migration for D-12: `FixSubOrderJsonSeed`

File: `Commerce.Infrastructure/Persistence/Migrations/20261106065814_FixSubOrderJsonSeed.cs`

Before:
```csharp
migrationBuilder.DeleteData(table: "SubOrderItems", keyColumn: "Id", keyValue: new Guid("03000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "SubOrders", keyColumn: "Id", keyValue: new Guid("02000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "Orders", keyColumn: "Id", keyValue: new Guid("01000000-0000-0000-0000-000000000001"));
```

Remediated:
```csharp
// Step 1: Delete dependent child records referencing SubOrderId 02000000-0000-0000-0000-000000000001
migrationBuilder.DeleteData(table: "DiscountCodeUsages", keyColumn: "Id", keyValue: new Guid("d2000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "OrderInvoices", keyColumn: "Id", keyValue: new Guid("05000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "OrderStatusHistories", keyColumn: "Id", keyValue: new Guid("04000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "SubOrderItems", keyColumn: "Id", keyValue: new Guid("03000000-0000-0000-0000-000000000001"));

// Step 2: Safely delete parent SubOrders and Orders records without FK violations
migrationBuilder.DeleteData(table: "SubOrders", keyColumn: "Id", keyValue: new Guid("02000000-0000-0000-0000-000000000001"));
migrationBuilder.DeleteData(table: "Orders", keyColumn: "Id", keyValue: new Guid("01000000-0000-0000-0000-000000000001"));
```

### 2.2 Schema Migration: Nullable `WinningBidId` on `AuctionOrders`

```csharp
migrationBuilder.AlterColumn<Guid>(
    name: "WinningBidId",
    table: "AuctionOrders",
    type: "uuid",
    nullable: true,
    oldClrType: typeof(Guid),
    oldType: "uuid");
```

---

## 3. Application Contracts & DTOs

### 3.1 `AuctionOrderRequests.cs`

```csharp
public sealed record AuctionSettlementRequest(
    [Required] Guid AuctionId,
    [Required] Guid WinnerId,
    [Required] Guid SellerId,
    Guid? WinningBidId, // [MODIFIED]: Nullable without [Required]
    [Required] Guid ProductId,
    Guid? SkuId,
    [Required, StringLength(255, MinimumLength = 1)] string ProductName,
    string? ProductImageUrl,
    string? SkuAttributes,
    [Range(0.01, double.MaxValue)] decimal WinningAmount,
    [Required] DateTimeOffset PaymentDeadlineAt,
    decimal? DepositAmount = 0,
    string? Source = "WINNER",
    ShippingAddressDto? ShippingAddress = null,
    string? ContactPhone = null,
    string? ImageUrl = null,
    Guid? DepositId = null
);
```

### 3.2 `DisputeRefundRequests.cs` (New)

```csharp
namespace Commerce.Application.Services.Order;

public sealed record DisputeRefundRequest(
    [Required] Guid DisputeId,
    [Range(0.01, double.MaxValue)] decimal RefundAmount,
    [Required, StringLength(255, MinimumLength = 1)] string Reason,
    string? Note = null
);

public sealed record DisputeRefundResponse(
    Guid SubOrderId,
    Guid RefundId,
    decimal Amount,
    string Status,
    DateTimeOffset CompletedAt
);
```
