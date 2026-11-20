# Implementation Plan: User Service Alignment — Buyer Permission Gate, Seller Reputation Rewards & Violation Penalties

**Branch/Directory**: `specs/016-user-service-alignment` | **Date**: 2026-11-10  
**Spec Reference**: [specs/016-user-service-alignment/spec.md](spec.md)  
**Input**: Feature specification from `spec.md`  

---

## 1. Summary

This feature integrates `CommerceService` (port `8003`) with `UserService` (port `8001`) via synchronous typed REST calls, closing three critical security and business logic gaps:
1. **Buyer Purchasing Permission Gate (`POST /api/v1/orders`)**:
   - Calls `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy` before reserving stock or debiting buyer funds.
   - Blocks banned or restricted accounts (`Buyer:Restriction`, `Account:BanAccount`) with HTTP 403 `BUYER_ACTION_RESTRICTED`.
2. **Order Completion & Partner Reputation Adjustment (`POST /api/v1/orders/{orderId}/complete` & `confirm-receipt`)**:
   - Implements `POST /api/v1/orders/{orderId}/complete` in `OrdersController` and triggers reputation adjustments (`INT-07`) with `+1` score delta for each shop seller upon completion.
   - Realigns `BuyerConfirmSubOrderReceiptAsync` to reward the seller and auto-complete the parent order when all sub-orders finish.
   - Adds `OrderStatus.Completed = 5` to represent completed master orders.
3. **Seller Cancellation Violation Penalties (`POST /api/v1/orders/{orderId}/cancel` & `SellerRejectSubOrderAsync`)**:
   - Expands `POST /api/v1/orders/{orderId}/cancel` to authorize sellers for sub-orders they own.
   - Detects unjustified cancellations on confirmed/paid orders (`CancellationWithoutReason = 7`) and automatically reports penalties (`INT-06`) to deduct 5 points from the offending seller under clause `POLICY-SEC-3.2`.

---

## 2. Technical Context

- **Language / Framework**: C# 12 / .NET 8 LTS (`Commerce.sln` targeting `net8.0`)
- **Primary Dependencies**:
  - `Microsoft.EntityFrameworkCore` & `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x
  - `Microsoft.AspNetCore.Mvc` & `System.Text.Json`
  - `Microsoft.Extensions.Http` & `Microsoft.Extensions.Logging`
  - `FluentAssertions`, `Moq`, `xunit` (test suite)
- **Database / Cache**: PostgreSQL `5435` (`commerce_db`), Redis Distributed Cache
- **Database Migration**: **Zero Migration**. PostgreSQL stores `OrderStatus` and `CancellationReasonCode` as integer conversions or string enums without schema alteration.
- **Target Microservice**: `CommerceService` (Port `8003`)
- **Downstream Dependency**: `UserService` (Port `8001`) via `USER_SERVICE_URL`
- **Blast Radius**:
  - `Commerce.Domain/Enums/OrderStatus.cs`
  - `Commerce.Domain/Enums/CancellationReasonCode.cs`
  - `Commerce.Application/Interfaces/IUserClient.cs`
  - `Commerce.Application/Interfaces/UserClientModels.cs`
  - `Commerce.Application/Services/Order/IOrderService.cs`
  - `Commerce.Application/Services/Order/OrderService.cs`
  - `Commerce.Application/Services/Order/OrderRequests.cs`
  - `Commerce.Application/Services/Order/OrderResponses.cs`
  - `Commerce.Infrastructure/Services/User/UserClient.cs`
  - `Commerce.Infrastructure/DependencyInjection.cs`
  - `Commerce.Api/Controllers/Public/OrdersController.cs`
  - `Commerce.Api/appsettings.json`
  - `Commerce.Api/appsettings.Development.json`
  - `docker-compose.yml`
  - `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs`
  - `tests/Commerce.UnitTests/Infrastructure/UserClientTests.cs`

---

## 3. Constitution Check

*GATE: Must pass before execution.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core Access** | **PASS** | `IUserClient` lives in `Commerce.Application/Interfaces/`. `UserClient` lives in `Commerce.Infrastructure/Services/User/`. `OrderService` interacts directly with `ICommerceDbContext`. Zero repository pattern. |
| **II. Synchronous Typed REST Orchestration** | **PASS** | All communication with `UserService` uses typed `HttpClient` with strict 10s timeout, identity propagation (`x-user-id`, `X-Internal-Service-Key`), and W3C traceparent propagation. Zero message brokers, zero Saga. |
| **III. Immutable Infrastructure & REST Standards** | **PASS** | Runs on port `8003`, connects to `commerce_db:5435`. Inter-service calls target `USER_SERVICE_URL` (`http://user-service:8001`). Strict Idempotency on reference IDs (`ORD-YYYYMMDD-XXXX`). |
| **IV. Universal Error Protocol** | **PASS** | 100% English Sentence-case error messages (`"User has an active restriction prohibiting purchase actions."`). Error codes in `SCREAMING_SNAKE_CASE` (`BUYER_ACTION_RESTRICTED`, `FORBIDDEN`, `ORDER_NOT_FOUND`). |
| **V. Inward-Only Boundaries** | **PASS** | 100% self-contained within `d:\CommerceService`. No cross-repo references. |
| **DoD Verification Gate** | **PASS** | Solution must build cleanly (`dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false`) with 0 warnings, 0 errors, and pass all automated tests. |

---

## 4. Technical Decisions & Architectural Rationale

### Decision 1: Fail-Closed Permission Gate Placement in `CreateOrderAsync`
- **Decision**: Invoke `_userClient.CheckBuyerPermissionAsync(buyerId, "buy", ct)` immediately after retrieving the validated quote from cache and before reserving stock or executing wallet payments.
- **Rationale**: If a buyer is restricted or banned, failing immediately prevents creating orphaned inventory holds in `FulfillmentService` and avoids unnecessary debit-credit rollbacks on the payment ledger.

### Decision 2: Dual-Trigger Reputation Adjustments
- **Decision**: Trigger `_userClient.AdjustReputationAsync` in both:
  1. `BuyerConfirmSubOrderReceiptAsync`: When buyer confirms receipt of an individual sub-order (`SubOrderStatus.Completed`), reward the seller of that specific sub-order.
  2. `CompleteOrderAsync`: When buyer confirms receipt of the entire order (`OrderStatus.Completed`), loop over all delivered sub-orders and ensure each distinct seller is rewarded.
- **Rationale**: E-commerce multi-vendor architecture allows partial delivery and staggered confirmation. UserService provides built-in idempotency deduplication (`isDuplicate: true`) and a 7-day anti-farming cooldown (`cooldownSuppressed: true`), guaranteeing that duplicate calls are safely absorbed without double credits.

### Decision 3: Seller Violation Penalty Policy & Cancellation Handling
- **Decision**: Extend `CancelOrderAsync` to check actor identity. If `currentUserId != order.BuyerId`:
  - Check whether `currentUserId` is the `SellerId` of any sub-order within the order.
  - If the sub-order was already `Confirmed` or `Paid`, and the cancellation is not due to force majeure (code `CancellationWithoutReason`), invoke `_userClient.CreatePenaltyAsync` with `customScoreDeduction: 5` and `policyClauseCode: "POLICY-SEC-3.2"`.
- **Rationale**: Aligns endpoint `POST /api/v1/orders/{orderId}/cancel` with the specification while maintaining backward compatibility with buyer cancellations.

### Decision 4: Non-Breaking Enum Expansion
- **Decision**:
  - Add `Completed = 5` to [`OrderStatus`](file:///d:/CommerceService/Commerce.Domain/Enums/OrderStatus.cs).
  - Add `CancellationWithoutReason = 7` to [`CancellationReasonCode`](file:///d:/CommerceService/Commerce.Domain/Enums/CancellationReasonCode.cs).
- **Rationale**: Values are appended with explicit integer assignments, preventing renumbering of existing persisted enum values in PostgreSQL.

---

## 5. Project Structure

```text
CommerceService/
├── Commerce.Domain/
│   └── Enums/
│       ├── OrderStatus.cs                  # [MODIFIED] Added Completed = 5
│       └── CancellationReasonCode.cs       # [MODIFIED] Added CancellationWithoutReason = 7
├── Commerce.Application/
│   ├── Interfaces/
│   │   ├── IUserClient.cs                  # [NEW] Outbound User Service contract
│   │   └── UserClientModels.cs             # [NEW] DTOs for permission check, reputation, penalty
│   └── Services/Order/
│       ├── IOrderService.cs                # [MODIFIED] Added CompleteOrderAsync, updated CancelOrderAsync
│       ├── OrderRequests.cs                # [MODIFIED] Added CompleteOrderRequest, updated CancelOrderRequest
│       ├── OrderResponses.cs               # [MODIFIED] Added OrderCompleteResponse
│       └── OrderService.cs                 # [MODIFIED] Integrated buyer permission check, rewards, penalties
├── Commerce.Infrastructure/
│   ├── Services/User/
│   │   └── UserClient.cs                   # [NEW] Typed HttpClient calling UserService (:8001)
│   └── DependencyInjection.cs              # [MODIFIED] Registered IUserClient with USER_SERVICE_URL
├── Commerce.Api/
│   ├── Controllers/Public/
│   │   └── OrdersController.cs             # [MODIFIED] Added POST /api/v1/orders/{orderId}/complete,
│   │                                                     extended POST /api/v1/orders/{orderId}/cancel
│   ├── appsettings.json                    # [MODIFIED] Added USER_SERVICE_URL fallback
│   └── appsettings.Development.json        # [MODIFIED] Added USER_SERVICE_URL default
├── docker-compose.yml                      # [MODIFIED] Added USER_SERVICE_URL environment variable
├── docs/
│   └── USER_SERVICE_ALIGNMENT_CONTRACT.md  # [NEW] Service-level technical contract doc
└── tests/Commerce.UnitTests/
    ├── Application/
    │   └── OrderLifecycleTests.cs          # [MODIFIED] Added tests for permission check, complete, penalty
    └── Infrastructure/
        └── UserClientTests.cs              # [NEW] Unit tests for UserClient HTTP interactions
```

---

## 6. Verification Checklist

1. `dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false` compiles with 0 errors and 0 warnings.
2. Unit tests verify:
   - `CreateOrderAsync` throws 403 `BUYER_ACTION_RESTRICTED` when `CheckBuyerPermissionAsync` returns false.
   - `CompleteOrderAsync` and `BuyerConfirmSubOrderReceiptAsync` invoke `AdjustReputationAsync` with `scoreDelta: 1`.
   - `CancelOrderAsync` invokes `CreatePenaltyAsync` with `customScoreDeduction: 5` when seller cancels confirmed order without valid reason.
   - `UserClient` correctly transmits headers `X-Internal-Service-Key` and `x-user-id`.
