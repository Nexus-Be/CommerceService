# Implementation Plan: Wallet Escrow Contract Alignment & Advanced Financial Error Handling

**Branch**: `026-wallet-escrow-contract-alignment` | **Date**: 2026-11-17 | **Spec**: [specs/026-wallet-escrow-contract-alignment/spec.md](file:///d:/CommerceService/specs/026-wallet-escrow-contract-alignment/spec.md)

**Input**: Feature specification from `/specs/026-wallet-escrow-contract-alignment/spec.md`

---

## Summary

Align `CommerceService` with the latest `WalletService` integration contract (`commerce-wallet-handoff.md`). 
Key deliverables:
1. When a buyer cancels an entire master order, call `POST /api/v1/wallets/orders/{orderId}/refund` once with `order.TotalAmount`, drawing down directly from the parent `OrderEscrow` rather than iterating through sub-orders.
2. Upgrade `WalletClient.ParseProblemDetails` to parse new financial collision codes (`ORDER_ESCROW_INSUFFICIENT`, `REFUND_EXCEEDS_PAYMENT`, `SETTLEMENT_CONFLICT`, `FEATURE_DISABLED`).
3. Formally freeze and withhold direct Stripe checkout on Commerce until Wallet completes webhooks, cancel, and refund capabilities.
4. Enforce the invariant that all sales are final upon delivery (`Delivered`/`Completed`), strictly barring automated buyer refunds post-delivery.

---

## Technical Context

**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, Entity Framework Core 8.0, Polly (Resilience), FluentValidation  
**Storage**: PostgreSQL 16 (`commerce_db:5435`)  
**Testing**: xUnit, FluentAssertions, Moq (`dotnet test Commerce.sln -c Release`)  
**Target Platform**: Linux / Windows container runtime (port 8003)  
**Project Type**: RESTful Web API Microservice  
**Performance Goals**: Sub-order cancellation and refund response < 500ms p95  
**Constraints**: Inward-only referencing, no Repository pattern over EF Core, RFC 9110 ProblemDetails, 100% English sentence-case error messages  
**Scale/Scope**: Multi-vendor marketplace orders, escrow payouts to sellers, immediate refunds to buyer wallets  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **I. Clean Architecture & Direct EF Core Access**: 4-layer structure maintained (`Api`, `Application`, `Domain`, `Infrastructure`). No repository pattern. Direct access to `ICommerceDbContext` in Application services.
- [x] **II. Synchronous Typed REST Orchestration**: Uses typed `IWalletClient` HTTP client. No MassTransit, RabbitMQ, Saga, or Outbox tables. Fail-closed error handling.
- [x] **III. Immutable Infrastructure & REST Ports**: Port 8003 runtime, Port 5435 PostgreSQL, RFC 9110 Problem Details responses.
- [x] **IV. Standardized System-Wide Error Protocol**: 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes (`ORDER_ESCROW_INSUFFICIENT`, `ORDER_CANNOT_BE_CANCELLED`).
- [x] **V. Self-Contained Repository & Inward-Only Boundaries**: Zero references outside `CommerceService`.
- [x] **Quality Gates**: Plan-first rule satisfied. Verification via `dotnet build -c Release` and `dotnet test`.

---

## Project Structure

### Documentation (this feature)

```text
specs/026-wallet-escrow-contract-alignment/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── wallet-escrow-alignment-api.yaml
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code Touches

```text
Commerce.Application/
├── Interfaces/
│   ├── IWalletClient.cs               # Confirm signatures for PayOrder, Settle, RefundOrder, RefundSubOrder
│   └── WalletPaymentResult.cs         # Ensure ErrorCode & IsTransientFailure fields
├── Services/
│   └── Order/
│       └── OrderService.cs            # Refactor CancelOrderAsync to call RefundOrderPaymentAsync on master order cancel

Commerce.Infrastructure/
└── Services/
    └── Wallet/
        └── WalletClient.cs            # Upgrade ParseProblemDetails to parse specific financial collision codes

tests/
└── Commerce.UnitTests/
    └── Application/
        ├── OrderServiceTests.cs       # Add test for master order refund calling RefundOrderPaymentAsync
        └── WalletClientTests.cs       # Verify ProblemDetails parsing for 409 & 503 codes
```

**Structure Decision**: Standard 4-layer Clean Architecture without additions.

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None      | N/A        | Clean Architecture adhered to 100%  |
