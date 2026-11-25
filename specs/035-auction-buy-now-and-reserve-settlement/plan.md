# Implementation Plan: Auction Buy Now & Reserve Price Settlement Integration

**Branch**: `035-auction-buy-now-and-reserve-settlement` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/035-auction-buy-now-and-reserve-settlement/spec.md`

---

## Summary

This feature upgrades the CommerceService Auction Order subsystem to integrate with AuctionService v0.4 contract (`COMMERCE_HANDOFF_BUY_NOW_AND_RESERVE.md`):
1. **Buy Now Ingestion (`source: "BUY_NOW"`)**: Accept Buy Now settlements where 100% of the merchandise amount has been locked by AuctionService in Nexus Wallet (`depositAmount == winningAmount`). Initialize the order with `RemainingAmount = 0 VND`, `Status = PendingPayment`, and compute seller net payout (95%) and platform fee (5%).
2. **Shipping & Net Payment Flow**: Buyer submits physical delivery destination within 24 hours (`PUT .../shipping`). GHN calculates shipping fee, setting `TotalAmount = ShippingFeeAmount`. Buyer pays solely the carrier shipping fee via Nexus Wallet (`POST .../pay`), moving order to `Paid` and triggering outbound callback `Paid` with `orderSource: "BUY_NOW"`.
3. **Payment Timeout & Dispute Safeguards**: If shipping is unpaid after 24h, `PaymentTimeoutWorker` cancels the order, dispatches callback `PaymentTimedOut` with `orderSource: "BUY_NOW"`, and avoids duplicate penalties on the buyer.
4. **Reserve Price Architecture**: Maintain zero code branching for reserve price; AuctionService absorbs below-reserve approval logic and pushes standard `source: "WINNER"` settlements upon seller authorization.
5. **Contract Wire Value Standardization**: Standardize all callback serialization points across `AuctionOrderService` and background workers to use `.ToWireValue()` (producing `"BUY_NOW"`, `"SECOND_CHANCE"`, `"WINNER"` without string truncation).

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, Microsoft.EntityFrameworkCore 8.0, Npgsql.EntityFrameworkCore.PostgreSQL  
**Storage**: PostgreSQL (`commerce_db:5435`), table `auction_orders`  
**Testing**: xUnit, FluentAssertions, Moq (`tests/Commerce.UnitTests`)  
**Target Platform**: Linux Container (Docker port 8003) / Windows Local (ports 5105 / 7035)  
**Project Type**: Clean Architecture Web API & Background Processing Service  
**Performance Goals**: Settlement ingestion response < 200ms p95  
**Constraints**: Zero repository pattern over EF Core; strict inward-only referencing; 100% English sentence-case error messages  
**Scale/Scope**: 5 touchpoint files in `Commerce.Domain`, `Commerce.Application`, `Commerce.Api`, and `Commerce.UnitTests`  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Gate | Requirement | Status | Evidence / Analysis |
|---|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | 4-layer structure, direct `ICommerceDbContext` / `AppDbContext` usage, NO Repository pattern | **PASS** | Domain enum `AuctionOrderSource` added; `AuctionOrderService` directly queries `ICommerceDbContext.AuctionOrders`. |
| **II. Synchronous REST Orchestration** | Typed HTTP REST clients (`IAuctionClient`, `IWalletClient`, `IFulfillmentClient`), fail-closed, no RabbitMQ/gRPC | **PASS** | Uses existing typed clients with Polly resilience; 0 message brokers. |
| **III. Immutable Infrastructure & REST** | Port 8003, DB 5435, RFC 9110 error payload format | **PASS** | Follows established REST endpoints under `/api/v1/auction-orders/*`. |
| **IV. Standardized Error Protocol** | 100% English, Sentence case messages, SCREAMING_SNAKE_CASE codes | **PASS** | Error codes: `VAL_INVALID_SETTLEMENT`, `DEPOSIT_EXCEEDS_WINNING_AMOUNT`, `PAYMENT_DEADLINE_EXPIRED`. |
| **V. Inward-Only Boundaries** | No references to external repos or sibling service directories | **PASS** | 100% self-contained within `D:\CommerceService`. |

**Gate Result**: ALL GATES PASSED (5/5). No constitutional violations.

---

## Project Structure

### Documentation (this feature)

```text
specs/035-auction-buy-now-and-reserve-settlement/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Technical research & architectural decisions
├── data-model.md        # Entity attributes, invariants & state transitions
├── quickstart.md        # Validation & verification guide
├── contracts/           # API contract definitions
│   └── auction-buy-now-api.yaml
└── checklists/
    └── requirements.md  # Spec quality review checklist
```

### Source Code Touches

```text
Commerce.Domain/
└── Enums/
    ├── AuctionOrderSource.cs                 # Add BuyNow = 3
    └── AuctionOrderSourceExtensions.cs       # Map BuyNow => "BUY_NOW"

Commerce.Application/
└── Services/AuctionOrder/
    └── AuctionOrderService.cs                # Accept "BUY_NOW", map enum, standardize .ToWireValue()

Commerce.Api/
└── Workers/
    └── PaymentTimeoutWorker.cs               # Standardize .ToWireValue() on seller timeout loop

tests/Commerce.UnitTests/
├── AuctionOrderSourceContractTests.cs        # Add BuyNow enum and wire value test cases
└── AuctionOrderServiceTests.cs               # Add BuyNow settlement, 0 remaining, and shipping tests
```

**Structure Decision**: Standard Clean Architecture layout with Feature Co-location under `Commerce.Application/Services/AuctionOrder/`.

---

## Complexity Tracking

> **Status**: No constitutional violations. Zero unjustified complexity.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| *None* | N/A | N/A |
