# Implementation Plan: Auction Deposit Ingestion, Runner-Up Settlement & Split Reversal

**Branch**: `022-auction-deposit-and-runner-up-settlement` | **Date**: 2026-11-15 | **Spec**: [specs/022-auction-deposit-and-runner-up-settlement/spec.md](spec.md)

**Input**: Feature specification from `specs/022-auction-deposit-and-runner-up-settlement/spec.md`

---

## 1. Summary

Implement the comprehensive upgrade for Auction Orders (`AuctionOrder`) in `CommerceService` based on the Auction Deposit Integration v0.4 contract (Option C: Nexus Wallet Balance Hold):
1. Upgrade `POST /api/v1/auction-orders/settlement` to ingest `depositAmount` and `source` (`"WINNER"` | `"SECOND_CHANCE"`).
2. Calculate and persist financial invariants:
   $$\text{RemainingAmount} = \text{WinningAmount} - \text{DepositAmount}$$
   $$\text{TotalAmount} = \text{RemainingAmount} + \text{ShippingFeeAmount}$$
   $$\text{SellerReceiveAmount} = \text{WinningAmount} \times 95\% \quad \mid \quad \text{PlatformFeeAmount} = \text{WinningAmount} \times 5\%$$
3. Implement strict financial validation: HTTP 422 `DEPOSIT_EXCEEDS_WINNING_AMOUNT` and `INVALID_AMOUNT`, HTTP 409 `SETTLEMENT_CONFLICT`.
4. Support runner-up (`SECOND_CHANCE`) order ingestion with zero deposit when the previous winner order is `Cancelled`.
5. Ensure buyer wallet payments debit strictly `TotalAmount` (net cash balance).
6. Enrich outbound callback `IAuctionClient.NotifyPaymentStatusAsync` with `orderSource: "WINNER" | "SECOND_CHANCE"` across all lifecycle events (`Paid`, `SellerAccepted`, `SellerRejected`, `PaymentTimedOut`, `SellerExpired`).
7. Enforce independent reversal split on seller rejection or timeout: Commerce refunds only `TotalAmount` (merchandise + shipping paid through Commerce), while Auction Service independently refunds captured deposit via Wallet W5.
8. Update `GET /api/v1/auction-orders/{id}` to return `depositAmount`, `remainingAmount`, and `orderSource`.
9. Maintain 100% test coverage with zero warnings and 100% pass rate.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0 LTS
- **Primary Dependencies**: Microsoft.EntityFrameworkCore 8.0.x, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.x, FluentAssertions, Moq, xUnit
- **Storage**: PostgreSQL 16 (database `commerce_db:5435`)
- **Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)
- **Target Platform**: Linux Docker Container / Windows local development (`ASPNETCORE_URLS=http://+:8003`)
- **Project Type**: Web API RESTful microservice
- **Performance Goals**: Sub-500ms p95 settlement ingestion, non-blocking background timeout worker (60s tick interval)
- **Constraints**: 100% English Sentence-case error messages, SCREAMING_SNAKE_CASE error codes, zero MassTransit/RabbitMQ, fail-closed external client integrations, strict Inward-Only reference boundary

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Analysis | Status |
|---|---|---|
| **I. Clean Architecture & Direct EF Core Access** | 4-layer architecture strictly maintained. Directly queries `ICommerceDbContext` / `AppDbContext.AuctionOrders`. Zero repository anti-pattern. Feature Co-location applied in `Commerce.Application/Services/AuctionOrder/`. | **PASS** |
| **II. Synchronous Typed REST Orchestration** | Outbound calls coordinate via typed clients (`IAuctionClient`, `IWalletClient`, `IFulfillmentClient`, `IUserClient`). Zero message brokers, Saga state machines, or transactional outbox tables. Financial fail-safe applied. | **PASS** |
| **III. Immutable Infrastructure & REST Standards** | Operates on container port `8003` (DB `5435`). Auction Service targeted at `8005`. REST plural routes used. ProblemDetails returned on errors. | **PASS** |
| **IV. Standardized System-Wide Error Protocol** | 100% English Sentence-case messages (`"Deposit amount cannot exceed winning amount."`). Error codes formatted in `SCREAMING_SNAKE_CASE` (`DEPOSIT_EXCEEDS_WINNING_AMOUNT`, `INVALID_AMOUNT`, `SETTLEMENT_CONFLICT`). | **PASS** |
| **V. Self-Contained Repository & Inward-Only** | All paths, namespaces, and tests strictly internal to `Commerce.sln`. Zero external sibling references. | **PASS** |

---

## 4. Project Structure

### Documentation (this feature)

```text
specs/022-auction-deposit-and-runner-up-settlement/
├── spec.md              # Feature specification & 10 User Stories
├── plan.md              # Implementation plan (this file)
├── research.md          # Phase 0 technical decisions & deposit ledger model
├── data-model.md        # Phase 1 data model & state transition rules
├── quickstart.md        # Phase 1 end-to-end verification guide
├── contracts/           # Phase 1 OpenAPI contracts
│   └── auction-deposit-api.yaml
└── checklists/
    └── requirements.md  # Spec quality checklist (100% passed)
```

### Source Code Touches & Additions

```text
Commerce.Domain/
├── Entities/
│   └── AuctionOrder.cs                                         # [MODIFY] Add DepositAmount, RemainingAmount, OrderSource
└── Enums/
    └── AuctionOrderSource.cs                                   # [NEW] Enum Winner = 1, SecondChance = 2

Commerce.Infrastructure/
├── Persistence/
│   ├── Configurations/
│   │   └── AuctionOrderConfiguration.cs                        # [MODIFY] Configure Decimal precision and Enum conversion
│   └── Migrations/
│       └── 20261115000000_AddAuctionDepositAndSourceFields.cs  # [NEW] EF Core migration for PostgreSQL
└── Services/
    └── Auction/
        └── AuctionClient.cs                                    # [MODIFY] Include orderSource in outbound callback payload

Commerce.Application/
├── Interfaces/
│   └── IAuctionClient.cs                                       # [MODIFY] Add OrderSource property to callback DTO
└── Services/
    └── AuctionOrder/
        ├── AuctionOrderRequests.cs                             # [MODIFY] Add DepositAmount, Source to AuctionSettlementRequest
        ├── AuctionOrderResponses.cs                            # [MODIFY] Add DepositAmount, RemainingAmount, OrderSource to detail DTO
        └── AuctionOrderService.cs                              # [MODIFY] Deposit formulas, 422 validations, runner-up logic

Commerce.Api/
├── Controllers/
│   └── Public/
│       └── AuctionOrdersController.cs                          # [VERIFY] Verify thin controller delegation
└── Workers/
    └── PaymentTimeoutWorker.cs                                 # [MODIFY] Dispatch orderSource in timeout callbacks

tests/Commerce.UnitTests/
└── Application/
    ├── AuctionOrderServiceTests.cs                             # [MODIFY] Add test cases for deposit, 422 validations, runner-up
    └── PaymentTimeoutWorkerTests.cs                            # [MODIFY] Verify orderSource dispatched in callbacks
```

---

## 5. Implementation Phases

- **Phase 1: Domain & EF Core Schema**
  - Create `AuctionOrderSource` enum.
  - Update `AuctionOrder` entity with `DepositAmount`, `RemainingAmount`, `OrderSource`.
  - Update `AuctionOrderConfiguration` and generate EF Core migration.
- **Phase 2: Application DTOs & Service Logic**
  - Update `AuctionSettlementRequest` and `AuctionOrderDetailResponse`.
  - Implement financial boundary validations (422 `DEPOSIT_EXCEEDS_WINNING_AMOUNT`, `INVALID_AMOUNT`, `INVALID_RUNNER_UP_DEPOSIT`).
  - Implement financial calculations: `RemainingAmount`, `TotalAmount = RemainingAmount + ShippingFeeAmount`.
  - Implement multi-winner runner-up idempotency: `(AuctionId, OrderSource, BuyerId)` with 409 `SETTLEMENT_CONFLICT`.
  - Verify seller rejection refunds only `TotalAmount` (independent reversal split).
- **Phase 3: Outbound IAuctionClient Enrichment**
  - Add `string? OrderSource = null` to `AuctionPaymentStatusCallbackRequest`.
  - Update `AuctionClient.cs` to include `orderSource` in JSON payload.
  - Pass `auctionOrder.OrderSource` in all service callback invocations.
- **Phase 4: Background Timeout Worker Enrichment**
  - Update `PaymentTimeoutWorker.cs` to pass `orderSource` in `PaymentTimedOut` and `SellerExpired` loops.
- **Phase 5: Automated Verification & Test Suite**
  - Update existing unit tests and add comprehensive unit test cases covering all new acceptance scenarios.
  - Execute `dotnet build Commerce.sln -c Release` (0 warnings, 0 errors).
  - Execute `dotnet test Commerce.sln -c Release` (100% pass rate).
