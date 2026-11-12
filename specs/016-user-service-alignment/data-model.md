# Data Model & State Machine: User Service Alignment

**Feature Directory**: `specs/016-user-service-alignment`  
**Phase**: Phase 1 (Data Model & State Transitions)  
**Status**: Completed  

---

## 1. Domain Entities & Enum Modifications

### 1.1 `OrderStatus` Enum
Represents the lifecycle state of the master `Order` aggregate.

| Value | Name | Persistence (Integer) | JSON Representation | Description |
| :--- | :--- | :---: | :--- | :--- |
| `0` | `PendingPayment` | `0` | `"PendingPayment"` | Initial state awaiting payment authorization |
| `1` | `PaymentProcessing` | `1` | `"PaymentProcessing"` | Intermediate state during gateway processing |
| `2` | `Paid` | `2` | `"Paid"` | Payment settled in full |
| `3` | `Cancelled` | `3` | `"Cancelled"` | Order cancelled before shipment |
| `4` | `Expired` | `4` | `"Expired"` | Payment window elapsed without settlement |
| **`5`** | **`Completed`** | **`5`** | **`"Completed"`** | **[NEW] All sub-orders fulfilled and verified by buyer** |

---

### 1.2 `CancellationReasonCode` Enum
Categorizes order and sub-order cancellation rationales.

| Value | Name | Persistence (Integer) | JSON Representation | Penalty Action |
| :--- | :--- | :---: | :--- | :--- |
| `0` | `BuyerChangedMind` | `0` | `"BuyerChangedMind"` | None (Buyer initiated) |
| `1` | `OutOfStock` | `1` | `"OutOfStock"` | None (Inventory discrepancy) |
| `2` | `PaymentTimeout` | `2` | `"PaymentTimeout"` | None (System passive expiration) |
| `3` | `SellerRejected` | `3` | `"SellerRejected"` | Conditional (If confirmed previously) |
| `4` | `Other` | `4` | `"Other"` | None |
| `5` | `DeliveryFailed` | `5` | `"DeliveryFailed"` | None (Carrier exception) |
| `6` | `PackageReturned` | `6` | `"PackageReturned"` | None (Carrier return) |
| **`7`** | **`CancellationWithoutReason`** | **`7`** | **`"CancellationWithoutReason"`** | **Triggers INT-06 Seller Penalty (-5 pts)** |

---

## 2. Cross-Service Contract Models (`Commerce.Application/Interfaces/UserClientModels.cs`)

### 2.1 Permission Check Models
```csharp
public sealed record UserPermissionCheckResult(
    bool IsAllowed,
    string? ErrorCode,
    string? Message);
```

### 2.2 Reputation Adjustment Models (`INT-07`)
```csharp
public sealed record ReputationAdjustRequest(
    Guid UserId,
    string TargetRole,
    Guid PartnerUserId,
    int ScoreDelta,
    string Reason,
    string ReferenceType,
    string ReferenceId,
    string Note);

public sealed record ReputationAdjustResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    bool CooldownSuppressed,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);
```

### 2.3 Violation Penalty Models (`INT-06`)
```csharp
public sealed record UserPenaltyRequest(
    Guid UserId,
    string TargetRole,
    string ViolationType,
    string ReferenceType,
    string ReferenceId,
    string Reason,
    int CustomScoreDeduction,
    string PolicyClauseCode);

public sealed record UserPenaltyResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    Guid? PenaltyId,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);
```

### 2.4 Application Service Response Models
```csharp
public sealed record OrderCompleteResponse(
    Guid OrderId,
    string OrderNo,
    string Status,
    DateTimeOffset CompletedAt,
    int CompletedSubOrderCount,
    List<Guid> CompletedSubOrderIds);
```

---

## 3. State Machine & Event Transitions

```
[POST /api/v1/orders]
       │
       ▼
(Check Buyer Permission) ──[Banned / Restricted]──▶ Throw 403 BUYER_ACTION_RESTRICTED
       │
       ▼ [Allowed]
[PendingPayment / Paid]
       │
       ├──[Carrier: Delivered]──▶ [Delivered]
       │                                │
       │                                ▼
       │                    [POST /api/v1/orders/{id}/complete]
       │                    (or confirm-receipt per sub-order)
       │                                │
       │                                ▼
       │                    [OrderStatus.Completed]
       │                                │
       │                                ▼
       │                    [POST reputation-adjust (INT-07)]
       │                    (Reward Seller +1 Score)
       │
       └──[Seller Cancels Confirmed Order without Reason]
                                │
                                ▼
                    [OrderStatus.Cancelled]
                                │
                                ▼
                    [POST penalties (INT-06)]
                    (Deduct Seller -5 Score)
```
