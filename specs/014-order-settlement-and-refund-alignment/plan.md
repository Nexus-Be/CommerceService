# Implementation Plan: Order Settlement, Automated Refund, and Seed Data Alignment

**Branch**: `014-order-settlement-and-refund-alignment` | **Date**: 2026-11-06 | **Spec**: [specs/014-order-settlement-and-refund-alignment/spec.md](spec.md)

**Input**: Feature specification from `/specs/014-order-settlement-and-refund-alignment/spec.md`

---

## Summary

This feature resolves critical cross-service integration and database conflicts identified in the backend services review (2026-11-05). It delivers:
1. **Database & Entity Fixes**: Resolves EF Core `CartItem` tracking on add and removes `Order`/`SubOrder` seed data from `CommerceSeedData.HasData` to eliminate JSON ownership metadata conflicts with `ShippingAddress.ToJson()`, accompanied by migration `FixSubOrderJsonSeed` and snapshot alignment.
2. **Automated Wallet Refund**: Transitions `WalletClient` from stubbed responses to live synchronous REST calls to WalletService (`RefundOrderPaymentCommand`), executing automatic 100% refunds when shops reject orders or DB compensation occurs.
3. **Buyer Receipt & Escrow Settlement**: Exposes `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`, transitioning `SubOrder.Status` to `Completed` and releasing escrowed funds to the seller via WalletService (`SettleSellerOrderCommand`).
4. **Seller Order Listing**: Exposes `GET /api/v1/sub-orders` with server-side database pagination and status filtering for merchants.
5. **Quality & Container Alignment**: Adds `FULFILLMENT_SERVICE_URL` to `docker-compose.yml`, enforces 100% English Sentence-case error messages, and updates E2E/Checkout test suites to achieve 100% pass rate.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`Commerce.sln` targeting net8.0)  
**Primary Dependencies**: 
- `Microsoft.EntityFrameworkCore` & `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x
- `Microsoft.AspNetCore.Mvc` & `System.Text.Json`
- `FluentAssertions`, `Moq`, `xunit` (test projects)  
**Storage**: PostgreSQL `5435` (`commerce_db`), Redis / In-Memory Distributed Cache  
**Testing**: `dotnet test` with xUnit, Moq, FluentAssertions, `TestCommerceDbContext` in-memory provider  
**Target Platform**: Linux Container (Docker) & Windows Development  
**Project Type**: ASP.NET Core RESTful Web API Service (Port `8003`)  
**Performance Goals**: Sub-50ms latency for database-paginated queries (`GET /api/v1/sub-orders`), zero in-memory full-table scans  
**Constraints**: 
- No Repository Pattern: Direct `ICommerceDbContext` / `AppDbContext` usage in Application layer
- Fail-Closed HTTP Orchestration: No mock fallbacks, mandatory `Idempotency-Key` headers on financial mutations
- Strict Inward-Only Scope: Zero references outside CommerceService repository root  
**Scale/Scope**: 5 modified core files, 1 new migration, 2 modified test suites, 2 new REST endpoints  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked post-design.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core Access** | **PASS** | Logic co-located in `Commerce.Application/Services/Order/` and `Cart/`. Application consumes `ICommerceDbContext` directly. No repositories or Unit of Work wrappers created. |
| **II. Synchronous Typed REST Orchestration** | **PASS** | Outbound calls to Wallet (`IWalletClient`) and Fulfillment (`IFulfillmentClient`) use typed HTTP clients. Zero message brokers (RabbitMQ/MassTransit) or saga orchestrators. |
| **III. Immutable Infrastructure & REST Standards** | **PASS** | Operates on port `8003`, connects to `commerce_db:5435`. All routes follow plural resource conventions (`/api/v1/sub-orders`, `/api/v1/orders`). Idempotency keys enforced on state mutations. |
| **IV. Universal Error Protocol** | **PASS** | All new error messages are 100% English Sentence-case (e.g. `"Sub-order not found."`, `"Sub-order cannot be confirmed in its current status."`). Error codes are `SCREAMING_SNAKE_CASE` (e.g. `FORBIDDEN`, `INVALID_STATUS`). |
| **V. Inward-Only Boundaries** | **PASS** | 100% self-contained within `d:\CommerceService`. No cross-repo pathing. |
| **DoD Verification Gate** | **PASS** | Solution builds in Release configuration with 0 warnings and 0 errors. All tests passing. |

---

## Project Structure

### Documentation & SpecKit Artifacts (this feature)

```text
specs/014-order-settlement-and-refund-alignment/
├── spec.md              # Feature specification with 5 User Stories (P1/P2)
├── plan.md              # This implementation plan file
├── research.md          # Phase 0: 5 technical decisions and unknowns resolved
├── data-model.md        # Phase 1: Entity attributes, finite state machine, and DTO definitions
├── quickstart.md        # Phase 1: Runnable scenarios and verification checklist
└── contracts/           # Phase 1: OpenAPI 3.0 specification for new endpoints
    └── order-settlement-refund-api.yaml
```

### Concrete Source Code Layout & Blast Radius

```text
Commerce.Domain/
└── Enums/
    └── SubOrderStatus.cs                           # Reference: Completed (5), Cancelled (6)

Commerce.Application/
├── Interfaces/
    └── IWalletClient.cs                            # Bổ sung SettleSellerSubOrderAsync & Refund contracts
├── Services/
    ├── Cart/
    │   └── CartService.cs                          # Sửa tracking CartItem mới trong AddItemAsync
    └── Order/
        ├── IOrderService.cs                        # Khai báo ListSellerSubOrdersAsync & BuyerConfirmSubOrderReceiptAsync
        ├── OrderService.cs                         # Triển khai auto-refund, escrow settlement, seller listing query
        ├── OrderRequests.cs                        # Request records (ConfirmReceipt, SellerOrderQuery)
        └── OrderResponses.cs                       # Response records (SubOrderSummaryResponse, ConfirmReceiptResponse)

Commerce.Infrastructure/
├── Persistence/
│   ├── Configurations/
│   │   └── SubOrderConfiguration.cs                # OwnsOne(ShippingAddress).ToJson()
│   ├── Migrations/
│   │   ├── 20261104150739_FixSubOrderJsonSeed.cs    # Migration gỡ seed HasData đơn mẫu
│   │   ├── 20261104150739_FixSubOrderJsonSeed.Designer.cs
│   │   └── AppDbContextModelSnapshot.cs            # Snapshot đồng bộ
│   └── Seeds/
│       └── CommerceSeedData.cs                     # Bỏ SeedOrders khỏi SeedAll
└── Services/
    └── Wallet/
        └── WalletClient.cs                         # Triển khai HTTP POST Settle & Refund sang WalletService

Commerce.Api/
└── Controllers/Public/
    └── OrdersController.cs                         # Thêm GET /api/v1/sub-orders & POST /api/v1/sub-orders/{id}/confirm-receipt

docker-compose.yml                                  # Thêm FULFILLMENT_SERVICE_URL: http://fulfillment-service:8006

tests/Commerce.UnitTests/
└── Application/
    ├── OrderLifecycleTests.cs                      # Bổ sung test Seller Reject -> Auto Refund
    ├── OrderCheckoutTests.cs                       # Bổ sung test DB compensation rollback
    └── EndToEndFlowTests.cs                        # Cập nhật E2E Flow 8 và E2E Flow xác nhận nhận hàng
```

---

## Detailed Implementation Work Breakdown

### Phase A: Database & Cart Tracking Stabilization (COMM-01, COMM-02)
1. **`CartService.cs`**:
   - In `AddItemAsync`: After `cart.AddOrUpdateItem(...)`, verify if the returned item is not in `_dbContext.CartItems.Local`. If newly created, call `_dbContext.CartItems.Add(newItem)`.
2. **`CommerceSeedData.cs`**:
   - Comment out or remove `SeedOrders(modelBuilder)` call inside `SeedAll(...)`.
3. **`Migrations`**:
   - Add migration `FixSubOrderJsonSeed` and synchronize `AppDbContextModelSnapshot.cs`.

### Phase B: Outbound Wallet Integration & Container Config (COMM-03, COMM-04, COMM-05)
1. **`IWalletClient.cs`**:
   - Add: `Task<bool> SettleSellerSubOrderAsync(Guid subOrderId, Guid sellerId, decimal amount, string? idempotencyKey = null, CancellationToken ct = default);`
   - Ensure `RefundSubOrderPaymentAsync` and `RefundOrderPaymentAsync` contracts accept idempotency keys.
2. **`WalletClient.cs`**:
   - Implement `SettleSellerSubOrderAsync`: `POST api/v1/wallets/sub-orders/{subOrderId}/settle` with `{ "sellerId": sellerId, "amount": amount }`, `Idempotency-Key: settle-{subOrderId}`.
   - Implement `RefundSubOrderPaymentAsync` & `RefundOrderPaymentAsync`: `POST api/v1/wallets/orders/{orderId}/refund` with `{ "amount": amount, "reason": reason }`, `Idempotency-Key: refund-{subOrderId}`.
   - Forward Bearer token from `_httpContextAccessor` or `IdentityPropagationHandler`.
3. **`docker-compose.yml`**:
   - Add `FULFILLMENT_SERVICE_URL: "${FULFILLMENT_SERVICE_URL:-http://fulfillment-service:8006}"`.

### Phase C: Core Order State Machine & Settlement (COMM-07, COMM-08)
1. **`OrderService.SellerRejectSubOrderAsync`**:
   - Remove "Pending manual refund" stub.
   - When `wasPaid == true`, execute `await _walletClient.RefundSubOrderPaymentAsync(so.Id, so.BuyerId.ToString(), so.TotalAmount, request.ReasonText ?? "Shop rejected", $"refund-{so.Id}", ct)`.
   - If refund succeeds, set `refund.Status = RefundStatus.Completed` and `refund.CompletedAt = DateTimeOffset.UtcNow`.
   - Release only rejected sub-order SKUs via `_fulfillmentClient.ReleaseSubOrderStockAsync`.
2. **`OrderService.BuyerConfirmSubOrderReceiptAsync`**:
   - Validate `currentUserId == so.BuyerId`. Return 403 `FORBIDDEN` if mismatched.
   - Validate `so.Status == SubOrderStatus.Delivered` (or `Shipped`). Return 400 `INVALID_STATUS` if invalid.
   - Transition `so.Status = SubOrderStatus.Completed` and set `so.CompletedAt = DateTimeOffset.UtcNow`.
   - Trigger `await _walletClient.SettleSellerSubOrderAsync(so.Id, so.SellerId, so.SellerReceiveAmount, $"settle-{so.Id}", ct)`.

### Phase D: REST Controller Exposing (COMM-06, COMM-08)
1. **`OrdersController.cs`**:
   - Add `GET /api/v1/sub-orders`: Extracts `sellerId` from `this.TryGetUserId()`. Calls `_orderService.ListSellerSubOrdersAsync(sellerId, page, pageSize, status, ct)`. Returns 200 with `PagedData<SubOrderSummaryResponse>`.
   - Add `POST /api/v1/sub-orders/{subOrderId:guid}/confirm-receipt`: Extracts `buyerId` from `this.TryGetUserId()`. Calls `_orderService.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrderId, ct)`. Returns 200 with `ConfirmReceiptResponse`.

### Phase E: Test Modernization & Quality Verification (COMM-10)
1. **`OrderLifecycleTests.cs`**:
   - Assert `_mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(...), Times.Once)` on seller rejection.
   - Add test case for `BuyerConfirmSubOrderReceiptAsync`: validates status transition to `Completed`, escrow settlement invocation, and 403 / 400 guards.
2. **`EndToEndFlowTests.cs`**:
   - Update `E2E_Flow8` to verify `refund.Status.Should().Be(RefundStatus.Completed)`.
   - Add `E2E_Flow9` demonstrating complete multi-shop delivery, buyer receipt confirmation, and seller escrow payout.
3. **Build & Test Gate**:
   - Run `dotnet build Commerce.sln -c Release` (0 errors, 0 warnings).
   - Run `dotnet test Commerce.sln -c Release` (100% pass rate).
