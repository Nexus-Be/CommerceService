# Implementation Plan: QA E2E Remediation, Buy Now Contract Parity, Stock Resilience & Lifecycle Hardening

**Branch**: `036-qa-e2e-remediation-and-lifecycle-hardening` | **Date**: 2026-11-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/036-qa-e2e-remediation-and-lifecycle-hardening/spec.md`

---

## 1. Summary

This plan remediates and hardens CommerceService against the issues discovered during comprehensive Phase 2 E2E testing:
1. **D-14 (Buy Now Contract Parity)**: Make `WinningBidId` nullable in `AuctionSettlementRequest`, `AuctionOrder` entity, responses, and migrations so Buy Now settlements (`source = "BUY_NOW"`) can be ingested without 400 validation errors.
2. **D-01 (Stock Policy Fail-Closed)**: Enforce `_failOpen = false` in `FulfillmentClient` by default in non-dev environments and log `LogError` on reservation failures to stop overselling.
3. **D-07 & D-08 (Delivery Lifecycle & Bom Hàng Penalty)**:
   - Handle `"Returning"` status in `UpdateSubOrderDeliveryStatusAsync`, maintaining order state and logging history.
   - Refactor `UpdateAuctionOrderDeliveryStatusAsync` to only cancel upon `"Returned"` (not on interim `DeliveryFailed`).
   - Call `_userClient.AdjustReputationAsync` (`ScoreDelta: -5`, `Reason: "PackageReturnedDeliveryFailed"`) when a regular sub-order is marked `"Returned"` due to recipient delivery failure.
4. **D-12 (Database Migration FK Fix)**: Fix `20261106065814_FixSubOrderJsonSeed.cs` to delete dependent records in `DiscountCodeUsages`, `OrderInvoices`, and `OrderStatusHistories` before deleting `SubOrders` row `02000000-0000-0000-0000-000000000001`.
5. **D-06 (Internal Dispute Refund Endpoint)**: Add secured internal endpoint `POST /api/v1/internal/sub-orders/{subOrderId}/dispute-refund` allowing `UserService` to execute immediate buyer refunds upon dispute resolution.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0
- **Primary Dependencies**: ASP.NET Core 8.0, EF Core 8.0, Npgsql.EntityFrameworkCore.PostgreSQL
- **Storage**: PostgreSQL (`commerce_db:5435`)
- **Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests`)
- **Target Platform**: Docker port 8003 / Windows Local ports 5105 / 7035
- **Constitutional Guardrails**:
  - No repository pattern over EF Core. Direct `AppDbContext` / `ICommerceDbContext`.
  - Inward-Only: Zero relative paths outside CommerceService.
  - Port 8003, Database 5435.
  - Error protocol: 100% English sentence-case messages, `SCREAMING_SNAKE_CASE` error codes.

---

## 3. Constitution & Quality Gates

| Principle / Gate | Requirement | Status | Evidence / Analysis |
|---|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | 4-layer structure, direct `ICommerceDbContext` / `AppDbContext` usage, NO Repository pattern | **PASS** | Logic co-located in `AuctionOrderService` and `OrderService`. Direct EF Core queries. |
| **II. Synchronous REST Orchestration** | Typed HTTP REST clients (`IWalletClient`, `IFulfillmentClient`, `IUserClient`), fail-closed, no RabbitMQ/gRPC | **PASS** | Direct REST calls with Polly retries; 0 message brokers. |
| **III. Immutable Infrastructure & REST** | Port 8003, DB 5435, RFC 9110 error payload format | **PASS** | Endpoints under `/api/v1/auction-orders/*`, `/api/v1/sub-orders/*`, `/api/v1/internal/*`. |
| **IV. Standardized Error Protocol** | 100% English, Sentence case messages, SCREAMING_SNAKE_CASE codes | **PASS** | Error codes: `VAL_INVALID_SETTLEMENT`, `INSUFFICIENT_STOCK`, `INVALID_REFUND_STATUS`. |
| **V. Inward-Only Boundaries** | No references to external repos or sibling service directories | **PASS** | 100% self-contained within `D:\CommerceService`. |

---

## 4. Phase-by-Phase Implementation Checklist

### Phase 1: Database & Migration Remediation (D-12, D-14)
- [ ] Edit `Commerce.Infrastructure/Persistence/Migrations/20261106065814_FixSubOrderJsonSeed.cs`: add deletes for `DiscountCodeUsages`, `OrderInvoices`, `OrderStatusHistories` before `SubOrders`.
- [ ] Create EF Core migration for `AuctionOrders.WinningBidId` nullable: `20261121000000_MakeAuctionOrderWinningBidIdNullable.cs`.
- [ ] Update `AppDbContextModelSnapshot.cs` and `AuctionOrderConfiguration.cs` if needed.

### Phase 2: Domain & Application Contracts (D-14, D-07, D-06)
- [ ] Update `Commerce.Domain/Entities/AuctionOrder.cs`: `public Guid? WinningBidId { get; set; }`.
- [ ] Update `Commerce.Domain/Enums/SubOrderStatus.cs`: add `Returning = 8`.
- [ ] Update `Commerce.Application/Services/AuctionOrder/AuctionOrderRequests.cs`: `Guid? WinningBidId`.
- [ ] Update `Commerce.Application/Services/AuctionOrder/AuctionOrderResponses.cs`: `Guid? WinningBidId`.
- [ ] Update `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs`: validate `WinningBidId` only required when `source != BuyNow`; handle null comparison in idempotency check.
- [ ] Add DTO `DisputeRefundRequest` and `DisputeRefundResponse` in `Commerce.Application/Services/Order/OrderRequests.cs`.
- [ ] Add `Task<DisputeRefundResponse> DisputeRefundSubOrderAsync(...)` in `IOrderService.cs` and implement in `OrderService.cs`.

### Phase 3: Infrastructure Resilience & Delivery Hardening (D-01, D-07, D-08)
- [ ] Update `FulfillmentClient.cs`: set `_failOpen` default to `false` in non-development environments; change reservation error log to `LogError`.
- [ ] Update `OrderService.UpdateSubOrderDeliveryStatusAsync`:
  - Add handling for `"Returning"` status.
  - Refactor `UpdateAuctionOrderDeliveryStatusAsync`: do not cancel on `DeliveryFailed`, only on `Returned`.
  - In `Returned` branch: call `_userClient.AdjustReputationAsync` for Buyer (`-5` points, `PackageReturnedDeliveryFailed`).

### Phase 4: Api Endpoints & Security (D-06)
- [ ] In `OrdersController.cs`: add `[HttpPost("/api/v1/internal/sub-orders/{subOrderId:guid}/dispute-refund")]` with `InternalServiceKeyGuard.Check(Request, configuration)` and `AllowAnonymous`.

### Phase 5: Verification & Unit Tests
- [ ] Run `dotnet build Commerce.sln -c Release` (verify 0 errors).
- [ ] Add/update unit tests in `Commerce.UnitTests` covering:
  - Buy Now settlement without `WinningBidId`.
  - Non-BuyNow settlement rejecting missing `WinningBidId`.
  - Stock reservation fail-closed behavior.
  - Webhook delivery `"Returning"` status and `"Returned"` buyer reputation penalty.
  - Internal dispute refund endpoint.
- [ ] Run `dotnet test` (verify 100% pass rate).
