# Research & Architecture Decisions: Catalog Ordered Flag & GHN Volumetric Dimensions

**Branch**: `034-catalog-ordered-flag-and-dimensions`  
**Date**: 2026-11-19  
**Spec**: [spec.md](spec.md)

---

## 1. Context & Objectives

Catalog Service has finalized technical handover `CATALOG_HANDOFF_TO_COMMERCE.md` specifying two primary integration goals:
1. **Synchronize Order Flag (`mark-ordered`)**:
   - Route: `POST /api/v1/internal/variants/mark-ordered` (Port 8002).
   - Mark `HasOrder = true` for all purchased variant IDs so Seller cannot permanently delete active SKUs.
2. **Package 3D Dimensions & GHN Volumetric Weight Calculation**:
   - Ingest `LengthCm`, `WidthCm`, `HeightCm`, and `WeightGrams` from Catalog Service internal endpoints (`/api/v1/internal/variants`, `/api/v1/internal/variants/check-status`, `/api/v1/internal/variants/validate-switch`).
   - Replace the legacy 200g hardcoded dummy fallback in `QuoteCheckoutAsync` with the official carrier formula:
     $$\text{VolumetricWeight (g)} = \frac{\text{LengthCm} \times \text{WidthCm} \times \text{HeightCm}}{5000} \times 1000 = \frac{L \times W \times H}{5}$$
     $$\text{ChargeableWeight (g)} = \max\left(\text{WeightGrams}, \text{VolumetricWeight}\right)$$

---

## 2. Research Decisions & Findings

### Decision 1: Idempotent `mark-ordered` Endpoint & Resilient Non-Blocking Flow

- **Decision**:
  - Implement `MarkVariantsOrderedAsync(IEnumerable<Guid> variantIds, CancellationToken cancellationToken = default)` in `ICatalogClient` and `CatalogClient`.
  - Target URL: `api/v1/internal/variants/mark-ordered`.
  - Deduplicate `variantIds` in-memory using `.Where(id => id != Guid.Empty).Distinct().ToList()`. If empty, return `true` immediately without network overhead.
  - Wrap downstream call in `try-catch (Exception ex)`: log warning on error or HTTP non-success and return `false`, ensuring zero impact on the primary transaction.
- **Rationale**:
  - Order creation and financial balance deductions in `CommerceService` are mission-critical. External catalog flags are auxiliary side effects; network partitions or temporary Catalog downtime must never abort order creation or trigger rollback.
  - Catalog's API is 100% idempotent: duplicate invocations for the same `variantIds` return `200 OK` safely.
- **Alternatives Considered**:
  - *Outbox Table / RabbitMQ*: Strictly rejected by Constitution Principle II & III (Zero message broker, zero MassTransit, zero Outbox tables).
  - *Blocking fail-closed*: Rejected because failing an order creation after inventory is reserved and wallet balance is deducted just because of a secondary metadata flag would cause severe financial and user experience inconsistencies.

---

### Decision 2: Ingestion of 3D Package Dimensions in `ICatalogClient`

- **Decision**:
  - Add optional parameters with default values `int LengthCm = 0, int WidthCm = 0, int HeightCm = 0` to `CatalogProductCheckResult` and `CatalogVariantSwitchResult`.
  - Update `CatalogVariantRawItem` and `CatalogSwitchRawResponse` in `CatalogClient.cs` to deserialize `LengthCm`, `WidthCm`, `HeightCm`.
- **Rationale**:
  - Using default parameter values `= 0` in C# positional records ensures 100% backward compatibility for all existing unit tests and callers throughout the solution without breaking compilation.
- **Alternatives Considered**:
  - *Nullable ints (`int?`)*: Rejected in application records; domain calculations benefit from clean non-negative integers where `0` cleanly represents unspecified/negligible dimension.

---

### Decision 3: Standard GHN Volumetric Weight Calculation in `QuoteCheckoutAsync`

- **Decision**:
  - Define a reusable calculation helper or static method `CalculateGhnChargeableWeight(int weightGrams, int lengthCm, int widthCm, int heightCm)`:
    ```csharp
    public static int CalculateGhnChargeableWeight(int weightGrams, int lengthCm, int widthCm, int heightCm)
    {
        int actual = Math.Max(0, weightGrams);
        int volumetric = 0;
        if (lengthCm > 0 && widthCm > 0 && heightCm > 0)
        {
            volumetric = (int)Math.Ceiling((double)(lengthCm * widthCm * heightCm) / 5.0);
        }
        int chargeable = Math.Max(actual, volumetric);
        return chargeable > 0 ? chargeable : 200; // Safe minimum fallback
    }
    ```
  - In `OrderService.QuoteCheckoutAsync`:
    - Collect all `selectedItems.Select(i => i.SkuId).Distinct().ToList()`.
    - Batch-fetch SKU details via `await _catalogClient.GetSkuBatchDetailsAsync(allSkuIds, ct)`.
    - For each item, compute the unit `chargeableWeight` and supply it to `new ShippingQuoteItem(i.SkuId, i.Quantity, chargeableWeight)`.
- **Rationale**:
  - In `FulfillmentClient.CalculateShopShippingFeeAsync`:
    `weightInGrams = Math.Max(1, items.Sum(i => Math.Max(1, i.WeightGrams) * Math.Max(1, i.Quantity)))`.
    By providing the unit `chargeableWeight` in `ShippingQuoteItem.WeightGrams`, `FulfillmentClient` automatically aggregates the total chargeable weight for the entire parcel and sends it to GHN API.
  - Eliminates the previous hardcoded `Math.Max(100, i.Quantity * 200)` dummy calculation.
- **Alternatives Considered**:
  - *Calling Fulfillment with dimensions directly*: Fulfillment's existing API contract `ShopShippingQuoteRequest` only accepts `ShippingQuoteItem(Guid SkuId, int Quantity, int WeightGrams = 200)`. Calculating chargeable weight in Commerce before invoking Fulfillment preserves the existing contract while achieving 100% accurate carrier quotes.

---

### Decision 4: Standardization of Variant Switch Endpoint Route

- **Decision**:
  - Update `CatalogClient.ValidateVariantSwitchAsync` endpoint from `api/v1/internal/catalog/skus/validate-variant-switch` to `api/v1/internal/variants/validate-switch`.
- **Rationale**:
  - Catalog Service standardized all internal variant endpoints under `api/v1/internal/variants/*` (`CAT-47`).
- **Alternatives Considered**:
  - *Keep legacy path*: Legacy path may be deprecated or subject to routing redirects in Catalog Service.

---

### Decision 5: Zero Database Schema Changes (Database Isolation)

- **Decision**:
  - No database migration or entity modifications for `Carts`, `CartItems`, `Orders`, `SubOrders`, `SubOrderItems`.
- **Rationale**:
  - Catalog is the Single Source of Truth for SKU packaging attributes.
  - The final shipping fee amount (`ShippingFeeAmount`) is already persisted in `SubOrders`. Storing transient parcel dimensions in Commerce database tables is redundant.
