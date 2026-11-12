# Implementation Plan: WalletService Integration Contract Alignment & Direct VNPay Orchestration

**Branch**: `017-wallet-contract-alignment` | **Date**: 2026-11-10 | **Spec**: [specs/017-wallet-contract-alignment/spec.md](spec.md)

**Input**: Feature specification from `/specs/017-wallet-contract-alignment/spec.md`

---

## Summary

This feature aligns CommerceService's outbound payment integration with the verified WalletService integration contract (dated 2026-11-10). It accomplishes:
1. **Identity & Trust Boundary Alignment**: Replaces obsolete Bearer JWT forwarding with mandatory `x-user-id: <buyer-uuid>` propagation for user endpoints (`/orders/{orderId}/payment`, `/wallets/me`, `/checkout/direct`). Removes identity headers from unauthenticated S2S endpoints (`/settle`, `/refund`).
2. **Problem Details RFC 9110 Error Parsing**: Captures `errorCode: "INSUFFICIENT_BALANCE"` from HTTP 400 responses and handles `409 PAYMENT_ORDER_CONFLICT`, eliminating brittle HTTP status-code guessing.
3. **Business Key Idempotency**: Strips redundant `Idempotency-Key` headers, anchoring safe retries on `{orderId}` and `{subOrderId}` URL route parameters.
4. **Direct VNPay Gateway Orchestration**: Implements `InitiateDirectCheckoutAsync` (`POST /api/v1/wallets/checkout/direct`) to return authentic merchant-signed payment URLs. Adds `VnPayPaymentPollingWorker` to asynchronously poll `GET /api/v1/wallets/checkout/direct/{orderId}` and commit stock upon `Success`.
5. **Automated Escrow Settlement & Refund Wiring**: Connects `SettleSellerSubOrderAsync` to buyer receipt confirmation and `RefundSubOrderPaymentAsync` to sub-order cancellation.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0 (`Commerce.sln` targeting net8.0)  
**Primary Dependencies**:
- `Microsoft.EntityFrameworkCore` & `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x
- `Microsoft.Extensions.Http` (Typed `HttpClient` orchestration)
- `System.Text.Json` (ProblemDetails parsing)
- `xUnit`, `Moq`, `FluentAssertions` (Test projects)  
**Storage**: PostgreSQL `5435` (`commerce_db`), Redis / In-Memory Distributed Cache  
**Testing**: `dotnet test Commerce.sln -c Release` (127+ unit tests)  
**Target Platform**: Linux Container (Port `8003`) & Local Windows Development  
**Project Type**: ASP.NET Core RESTful Web API Service  
**Performance Goals**: Sub-100ms outbound client calls, 30-second reconciliation polling intervals  
**Constraints**:
- Strict 4-Layer Clean Architecture with zero Repository pattern wrappers over EF Core.
- 100% RESTful communication over HTTP/JSON (Port 8008 for WalletService). No gRPC, RabbitMQ, MassTransit, or Saga orchestrators.
- Fail-closed error handling with 10-second client timeouts and W3C `traceparent` propagation.
- Inward-only scope: all references must be self-contained within `CommerceService`.  
**Scale/Scope**: 5 modified core files, 1 new background worker, 2 test suites updated.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-evaluated post-design.*

| Principle / Rule | Compliance Status | Analysis & Verification |
| :--- | :---: | :--- |
| **I. Clean Architecture & Direct EF Core Access** | **PASS** | `OrderService` interacts directly with `ICommerceDbContext` and `DbSet<T>`. Zero repository or Unit of Work layers introduced. |
| **II. Synchronous Typed REST Orchestration** | **PASS** | Outbound calls to WalletService utilize typed `IWalletClient` (`WalletClient.cs`). Zero message brokers, event buses, or sagas. |
| **III. Immutable Infrastructure & REST Standards** | **PASS** | Container port `8003`, database `commerce_db:5435`. All routes follow plural conventions. Client communicates with Wallet at port `8008`. |
| **IV. Universal Error Protocol** | **PASS** | All error messages are 100% English Sentence-case (e.g., `"Order cannot be cancelled in its current status."`). Error codes are `SCREAMING_SNAKE_CASE` (e.g., `INSUFFICIENT_BALANCE`, `PAYMENT_ORDER_CONFLICT`). |
| **V. Inward-Only Boundaries** | **PASS** | Completely self-contained inside `d:\CommerceService`. No outward directory referencing. |
| **DoD Verification Gate** | **PASS** | Solution builds cleanly with 0 warnings and 0 errors in Release mode (`dotnet build Commerce.sln -c Release`). All test suites pass. |

---

## Project Structure

### Documentation & SpecKit Artifacts (this feature)

```text
specs/017-wallet-contract-alignment/
├── spec.md              # Feature specification with 4 prioritized User Stories (P1/P2)
├── plan.md              # This implementation plan file
├── research.md          # Phase 0: 5 technical decisions and unknowns resolved
├── data-model.md        # Phase 1: Entity attributes, finite state machine, and DTO definitions
├── quickstart.md        # Phase 1: Runnable verification scenarios and testing guide
├── contracts/           # Phase 1: OpenAPI 3.0 specification for WalletService endpoints
│   └── wallet-integration-api.yaml
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Concrete Source Code Layout & Blast Radius

```text
Commerce.Application/
├── Interfaces/
│   ├── IWalletClient.cs                            # Updated PayOrderAsync signature, added DirectCheckout methods
│   └── WalletPaymentResult.cs                      # Enhanced with DirectCheckoutResult record
└── Services/
    └── Order/
        ├── OrderService.cs                         # Pass buyerId to PayOrderAsync, wire InitiateDirectCheckoutAsync
        └── OrderResponses.cs                       # Ensure PaymentUrl is cleanly propagated in CreateOrderResponse

Commerce.Infrastructure/
└── Services/
    └── Wallet/
        └── WalletClient.cs                         # Propagate x-user-id, parse Problem Details RFC 9110, strip Idempotency header

Commerce.Api/
├── Workers/
│   └── VnPayPaymentPollingWorker.cs                # NEW: BackgroundService to poll GET .../checkout/direct/{orderId}
└── Program.cs                                      # Register VnPayPaymentPollingWorker in DI

tests/Commerce.UnitTests/
└── Orders/
    ├── OrderCheckoutTests.cs                       # Updated mocks for IWalletClient with buyerId and Problem Details
    └── VnPayPaymentPollingWorkerTests.cs           # NEW: Unit tests covering polling, success commit, and timeout cancellation
```

---

## Structure Decision

The project follows the established Clean Architecture layout:
- `Commerce.Application/Interfaces/IWalletClient.cs` defines the application boundary.
- `Commerce.Infrastructure/Services/Wallet/WalletClient.cs` encapsulates HTTP transport, header management, and Problem Details parsing.
- `Commerce.Api/Workers/VnPayPaymentPollingWorker.cs` manages asynchronous reconciliation without coupling to UI or controllers.

---

## Complexity Tracking

> **No Constitution violations detected.** Direct EF Core access, typed REST clients, and fail-closed error handling are fully maintained.
