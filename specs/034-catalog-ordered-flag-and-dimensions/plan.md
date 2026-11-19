# Implementation Plan: Catalog Ordered Flag & GHN Volumetric Dimensions

**Branch**: `034-catalog-ordered-flag-and-dimensions` | **Date**: 2026-11-19 | **Spec**: [specs/034-catalog-ordered-flag-and-dimensions/spec.md](spec.md)

**Input**: Feature specification from `specs/034-catalog-ordered-flag-and-dimensions/spec.md`

---

## 1. Summary

Implement synchronization of the ordered flag (`mark-ordered`) to Catalog Service and standard GHN volumetric shipping fee calculations based on technical handover `CATALOG_HANDOFF_TO_COMMERCE.md`:
1. **Packaging Dimensions Ingestion**: Extend `CatalogProductCheckResult` and `CatalogVariantSwitchResult` in `ICatalogClient.cs` to include 3D dimensions (`LengthCm`, `WidthCm`, `HeightCm`). Update `CatalogClient.cs` to deserialize these attributes from Catalog internal APIs (`/api/v1/internal/variants`, `/api/v1/internal/variants/check-status`, `/api/v1/internal/variants/validate-switch`).
2. **GHN Volumetric Weight Calculation in Checkout**: Replace the static 200g dummy fallback in `OrderService.QuoteCheckoutAsync` with the standard carrier formula:
   $$\text{VolumetricWeight (g)} = \frac{\text{LengthCm} \times \text{WidthCm} \times \text{HeightCm}}{5000} \times 1000 = \frac{L \times W \times H}{5}$$
   $$\text{ChargeableWeight (g)} = \max\left(\text{WeightGrams}, \text{VolumetricWeight}\right)$$
   Batch-fetch SKU packaging dimensions in `QuoteCheckoutAsync` and pass the unit `ChargeableWeight` to `IFulfillmentClient.CalculateShopShippingFeeAsync`.
3. **Synchronize Ordered Variants (`mark-ordered`)**: Implement `MarkVariantsOrderedAsync` in `ICatalogClient` and `CatalogClient` invoking `POST /api/v1/internal/variants/mark-ordered`.
4. **Trigger Points**:
   - `OrderService.CreateOrderAsync`: Synchronize distinct variant IDs immediately after successful database commit.
   - `AuctionOrderService.ReceiveSettlementAsync`: Synchronize the variant ID if `SkuId` is present on newly created auction orders.
5. **Non-Blocking Fail-Safe**: Wrap `mark-ordered` calls in `try-catch` blocks with warning logs so Catalog network errors never rollback order creation or wallet payment transactions.
6. **Standardize Route**: Update variant switch route in `CatalogClient.cs` to `api/v1/internal/variants/validate-switch`.
7. **Zero DB Migrations**: Maintain 100% database schema isolation; dimension processing is strictly transient and in-memory.
8. **Unit Tests**: Implement comprehensive test suites in `tests/Commerce.UnitTests/`.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0 LTS
- **Primary Dependencies**: Microsoft.EntityFrameworkCore 8.0.x, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.x, FluentAssertions, Moq, xUnit
- **Storage**: PostgreSQL 16 (database `commerce_db`, port `5435`) — Zero schema alterations required (0 migration)
- **Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)
- **Target Platform**: Linux Docker Container / Windows local development (ASPNETCORE_URLS `http://+:8003`)
- **Downstream Targets**:
  - `CatalogService` (Port `8002`, `http://nexus-catalog-service:8002/` / `http://localhost:8002/`)
  - `FulfillmentService` (Port `8006`, `http://nexus-fulfillment-service:8006/` / `http://localhost:8006/`)
- **Project Type**: Web API RESTful microservice
- **Performance Goals**: Single batch roundtrip in `QuoteCheckoutAsync`, sub-200ms `mark-ordered` overhead, zero latency impact on downstream failure
- **Constraints**: 100% English Sentence-case error messages, SCREAMING_SNAKE_CASE error codes, zero MassTransit/RabbitMQ, non-blocking auxiliary side-effects, strict Inward-Only reference boundary

---

## 3. Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Compliance Analysis | Status |
|---|---|---|
| **I. Clean Architecture & Direct EF Core Access** | Respects 4-layer structure inward. Uses `ICommerceDbContext` / `AppDbContext` directly. Zero repository abstractions. Feature Co-location preserved. | **PASS** |
| **II. Synchronous Typed REST Orchestration** | Integrates via existing `ICatalogClient` and `CatalogClient` typed HTTP client. Zero message brokers or Saga state machines. Resilient non-blocking side-effect pattern. | **PASS** |
| **III. Immutable Infrastructure & REST Standards** | Operates on port `8003` (DB 5435). Catalog Service targeted at `8002`. Standard RESTful internal routes used (`api/v1/internal/variants/mark-ordered`). | **PASS** |
| **IV. Standardized System-Wide Error Protocol** | 100% English Sentence-case messages and logs. Error codes formatted in `SCREAMING_SNAKE_CASE`. | **PASS** |
| **V. Self-Contained Repository & Inward-Only** | All paths, references, and mock tests strictly relative to `Commerce.sln`. Zero external sibling directory references. | **PASS** |

---

## 4. Project Structure

### Documentation (this feature)

```text
specs/034-catalog-ordered-flag-and-dimensions/
├── spec.md              # Feature specification & requirements
├── plan.md              # Implementation plan (this file)
├── research.md          # Technical research & architecture decisions
├── data-model.md        # Data model, contracts, and lifecycle sequence
├── quickstart.md        # Verification guide & testing scenarios
├── contracts/           # OpenAPI API contract
│   └── catalog-internal-api.yaml
└── checklists/
    └── requirements.md  # Spec quality checklist (100% passed)
```

### Source Code Touches & Additions

```text
Commerce.Application/
├── Interfaces/
│   └── ICatalogClient.cs                                       # [MODIFY] Add LengthCm/WidthCm/HeightCm to results & declare MarkVariantsOrderedAsync
└── Services/
    ├── Order/
    │   └── OrderService.cs                                     # [MODIFY] GHN volumetric formula in QuoteCheckoutAsync & trigger mark-ordered in CreateOrderAsync
    └── AuctionOrder/
        └── AuctionOrderService.cs                              # [MODIFY] Trigger mark-ordered in ReceiveSettlementAsync when SkuId is present

Commerce.Infrastructure/
└── Services/
    └── Catalog/
        └── CatalogClient.cs                                    # [MODIFY] Implement MarkVariantsOrderedAsync, deserialize dimensions, standardize validate-switch route

tests/Commerce.UnitTests/
├── Infrastructure/
│   └── CatalogClientTests.cs                                   # [MODIFY] Test MarkVariantsOrderedAsync, dimension deserialization, and route updates
└── Application/
    ├── OrderServiceTests.cs                                    # [MODIFY] Test GHN volumetric weight calculation in QuoteCheckoutAsync and mark-ordered trigger
    └── AuctionOrderServiceTests.cs                             # [MODIFY] Test mark-ordered trigger on auction order settlement
```

---

## 5. Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| None | N/A | Fully compliant with Constitution and Architecture rules |
