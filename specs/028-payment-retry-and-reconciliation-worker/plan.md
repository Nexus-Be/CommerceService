# Implementation Plan: Payment Self-Healing, Order Repay & Financial Reconciliation Engine

**Branch**: `028-payment-retry-and-reconciliation-worker` | **Date**: 2026-11-17 | **Spec**: [specs/028-payment-retry-and-reconciliation-worker/spec.md](spec.md)

**Input**: Feature specification from `/specs/028-payment-retry-and-reconciliation-worker/spec.md`

---

## Summary

Deliver a self-healing financial reconciliation and repayment mechanism for CommerceService:
1. Provide `POST /api/v1/orders/{orderId}/pay` allowing buyers to retry payment for an order in `PendingPayment` status within its 15-minute deadline.
2. Extend `PaymentTimeoutWorker.cs` with an automated reconciliation sweep that retries pending refunds (`Status == RefundStatus.Requested`) and unsettled seller escrows (`SubOrder.Status == Completed && !IsEscrowSettled`).
3. Add `bool IsEscrowSettled` to `SubOrder` to track seller payout status persistently.
4. Expose financial transparency endpoints: `GET /api/v1/orders/{orderId}/refunds` and `GET /api/v1/sub-orders/{subOrderId}/financial-breakdown`.
5. Provide comprehensive unit tests across edge scenarios (expired deadline, insufficient balance, COD safety, automated self-healing retry).

---

## Technical Context

**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Entity Framework Core 8, Polly, Microsoft.Extensions.Hosting  
**Storage**: PostgreSQL (port 5435, `commerce_db`) via EF Core `AppDbContext`  
**Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests/`)  
**Target Platform**: Windows / Linux Docker container (Port 8003 runtime, local 5105/7035)  
**Project Type**: RESTful Web API Microservice (Clean Architecture 4-layer)  
**Performance Goals**: Repay sub-second response (< 300ms p95); Background worker iteration non-blocking (< 5s per cycle)  
**Constraints**: Zero message broker (RabbitMQ/MassTransit prohibited); synchronous Typed HTTP REST only (`_walletClient`, `_fulfillmentClient`); 100% English Sentence-case error messages  
**Scale/Scope**: Up to 50 pending refund records and 50 unsettled sub-orders per reconciliation iteration  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **I. Clean Architecture & Direct EF Core Access**: 4-layer structure maintained (`Domain`, `Application`, `Infrastructure`, `Api`). Zero Repository anti-patterns; direct `ICommerceDbContext` / `AppDbContext` usage.
- [x] **II. Synchronous Typed HTTP REST Orchestration**: Inter-service communication strictly via `IWalletClient` and `IFulfillmentClient`. Zero RabbitMQ/MassTransit.
- [x] **III. Immutable Infrastructure & REST Ports**: Port 8003 runtime, Port 5435 PostgreSQL, RFC 9110 Problem Details responses.
- [x] **IV. Standardized System-Wide Error Protocol**: 100% English sentence-case messages (`"Payment deadline for this order has expired."`, `"Your wallet balance is insufficient to pay for this order."`), SCREAMING_SNAKE_CASE codes (`INSUFFICIENT_WALLET_BALANCE`, `ORDER_PAYMENT_DEADLINE_EXPIRED`, `INVALID_PAYMENT_METHOD`).
- [x] **V. Self-Contained Repository & Inward-Only Boundaries**: Zero references outside `CommerceService`.
- [x] **Quality Gates**: Clean Build (0 warnings, 0 errors), 100% unit tests pass.

---

## Project Structure

### Documentation (this feature)

```text
specs/028-payment-retry-and-reconciliation-worker/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── payment-retry-reconciliation-api.yaml
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code Touches

```text
Commerce.Domain/
└── Entities/
    └── SubOrder.cs                      # Add IsEscrowSettled property

Commerce.Infrastructure/
└── Persistence/
    └── Configurations/
        └── SubOrderConfiguration.cs     # Configure IsEscrowSettled default false

Commerce.Application/
├── DTOs/
│   └── Order/
│       ├── RepayOrderResponse.cs        # Repay response contract
│       ├── OrderRefundResponse.cs       # Refund query contract
│       └── SubOrderFinancialBreakdownResponse.cs # Financial breakdown contract
├── Interfaces/
│   └── ICommerceDbContext.cs           # Ensure DbSets accessible
└── Services/
    └── Order/
        ├── IOrderService.cs             # Add RepayOrderAsync, GetOrderRefundsAsync, GetSubOrderFinancialBreakdownAsync
        └── OrderService.cs              # Implement repay, financial queries, and update IsEscrowSettled

Commerce.Api/
├── Controllers/
│   └── Public/
│       └── OrdersController.cs          # Add POST pay, GET refunds, GET financial-breakdown endpoints
└── Workers/
    └── PaymentTimeoutWorker.cs          # Add ReconcilePendingRefundsAsync and ReconcileUnsettledEscrowsAsync

tests/
└── Commerce.UnitTests/
    └── Application/
        ├── OrderRepayTests.cs           # Tests for repaying pending orders
        ├── OrderReconciliationTests.cs  # Tests for worker reconciliation of refunds & escrow
        └── OrderFinancialQueryTests.cs  # Tests for financial transparency endpoints
```

**Structure Decision**: Standard 4-layer Clean Architecture with Feature Co-location under `Order`.

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|:---|:---|:---|
| None | N/A | Clean Architecture and Constitution adhered to 100% |
