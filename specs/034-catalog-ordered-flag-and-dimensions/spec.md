# Feature Specification: Catalog Ordered Flag & GHN Volumetric Dimensions Integration

**Feature Branch**: `034-catalog-ordered-flag-and-dimensions`  
**Created**: 2026-11-19  
**Status**: Draft  
**Input**: User description: "Tích hợp cờ đơn hàng (mark-ordered) và kích thước đóng gói chuẩn GHN từ CatalogService theo tài liệu CATALOG_HANDOFF_TO_COMMERCE.md"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-World Carrier Shipping Fee Calculation during Checkout (Priority: P1)

As a Buyer purchasing products from multiple shops,  
I want my shipping fees to be accurately calculated based on the actual physical packaging dimensions and weights of the items,  
So that I pay fair, predictable shipping rates and Sellers do not suffer delivery undercharging penalties from carriers like Giao Hàng Nhanh (GHN).

**Why this priority**: Directly impacts financial correctness and buyer transparency. Eliminates the hardcoded 200g dummy fallback that previously led to undercharging on bulky goods (e.g. helmets, winter coats, electronics).

**Independent Test**: Can be tested by invoking `QuoteCheckoutAsync` with items having known 3D dimensions (e.g. $30 \times 25 \times 20\text{ cm}$, 300g) and verifying that the computed chargeable weight sent to carrier fee calculation is $3,000\text{g}$ rather than $200\text{g}$ or $300\text{g}$.

**Acceptance Scenarios**:
1. **Given** a cart item with length $30\text{ cm}$, width $25\text{ cm}$, height $20\text{ cm}$, and actual weight $300\text{g}$, **When** the Buyer requests a checkout quote, **Then** the system computes the volumetric weight as $(30 \times 25 \times 20) / 5 = 3,000\text{g}$, selects the maximum between actual and volumetric weight ($3,000\text{g}$), and calculates the shipping fee based on $3,000\text{g}$.
2. **Given** a compact heavy item with length $10\text{ cm}$, width $10\text{ cm}$, height $5\text{ cm}$, and actual weight $1,500\text{g}$, **When** the Buyer requests a checkout quote, **Then** the system computes volumetric weight as $100\text{g}$, selects the actual weight ($1,500\text{g}$), and calculates the shipping fee based on $1,500\text{g}$.
3. **Given** an item where dimensions are missing or zero from Catalog, **When** the Buyer requests a checkout quote, **Then** the system falls back to the item's actual weight (or a safe minimum of $200\text{g}$ if actual weight is also zero) without failing the checkout quote.

---

### User Story 2 - Lock Ordered SKU Deletion via `mark-ordered` Synchronization (Priority: P1)

As a Platform Operator and Seller,  
I want variants that have been placed in an order to be permanently flagged as `HasOrder = true` in Catalog Service,  
So that Sellers cannot accidentally or intentionally delete SKUs tied to existing financial and purchase records, while still being able to deactivate them.

**Why this priority**: Protects auditability, invoice validity, and order history integrity across the platform.

**Independent Test**: Can be tested by placing a regular order via `CreateOrderAsync` or receiving an auction settlement via `ReceiveSettlementAsync`, and verifying that Catalog's `POST /api/v1/internal/variants/mark-ordered` endpoint is invoked with the distinct SKU IDs.

**Acceptance Scenarios**:
1. **Given** a successful order containing multiple items from one or more shops, **When** the order is committed to the database in `CreateOrderAsync`, **Then** the system sends the distinct list of variant IDs to Catalog Service's `POST /api/v1/internal/variants/mark-ordered`.
2. **Given** an auction order settlement received with a valid `SkuId`, **When** the auction order is created in `ReceiveSettlementAsync`, **Then** the system sends the variant ID to Catalog Service's `mark-ordered` endpoint.
3. **Given** Catalog Service is temporarily unreachable or returns a network error, **When** an order is created, **Then** the system logs a warning, does NOT abort or rollback the order creation or wallet payment, and completes the purchase flow successfully.

---

### User Story 3 - Up-to-Date Variant Dimensions during Cart Variant Switch (Priority: P2)

As a Buyer adjusting item configurations in my cart,  
I want to change a product variant (e.g. changing size from M to XL) and receive updated package dimensions and weight,  
So that my cart remains synchronized with Catalog master data and ready for accurate checkout quoting.

**Why this priority**: Ensures seamless cart management and accurate weight awareness before checkout.

**Independent Test**: Can be tested by calling `ChangeCartItemVariantAsync` and verifying that Catalog's standardized endpoint `POST /api/v1/internal/variants/validate-switch` is called and returns valid dimensions and weight.

**Acceptance Scenarios**:
1. **Given** an active cart item, **When** the Buyer switches to a different variant of the same product, **Then** the system calls `POST /api/v1/internal/variants/validate-switch` and receives updated dimension and weight attributes.

---

### Edge Cases

- **Volumetric weight rounding**: When $(Length \times Width \times Height) / 5.0$ yields a fractional gram value, the system rounds up using ceiling or standard integer arithmetic to ensure the carrier does not reject the quotation.
- **Multiple items per shop in checkout**: When a shop order contains multiple items and quantities, the chargeable weight is calculated per item unit and aggregated appropriately for the shop package quote.
- **Catalog Service transient failure during checkout quote**: If Catalog batch lookup fails during `QuoteCheckoutAsync`, the system applies a defensive fallback (e.g., standard fallback weight) or surfaces a clean 503 error if catalog data is strictly required.
- **Duplicate variant IDs in an order**: An order containing multiple lines of the same variant or same variant across different context bundles must deduplicate IDs before sending to `mark-ordered`.
- **Zero or negative dimensions**: Any dimension $\le 0$ is ignored, and the system relies on `WeightGrams`.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST implement `MarkVariantsOrderedAsync` in `ICatalogClient` and `CatalogClient` targeting `POST /api/v1/internal/variants/mark-ordered` with payload `{ "variantIds": [Guid] }`.
- **FR-002**: The system MUST invoke `MarkVariantsOrderedAsync` immediately after order persistence succeeds in `OrderService.CreateOrderAsync` with all distinct variant IDs from the order.
- **FR-003**: The system MUST invoke `MarkVariantsOrderedAsync` immediately after auction order persistence succeeds in `AuctionOrderService.ReceiveSettlementAsync` when `SkuId` is present and non-empty.
- **FR-004**: The system MUST treat all calls to `MarkVariantsOrderedAsync` as non-blocking side effects, wrapping invocations in `try-catch` blocks with warning logs so failures never abort or rollback order creation or payment transactions.
- **FR-005**: The system MUST update `CatalogProductCheckResult` to include package dimensions `LengthCm`, `WidthCm`, `HeightCm` with default values of `0`.
- **FR-006**: The system MUST update `CatalogVariantSwitchResult` to include package dimensions `LengthCm`, `WidthCm`, `HeightCm` with default values of `0`.
- **FR-007**: The system MUST deserialize `lengthCm`, `widthCm`, `heightCm` in `CatalogClient` for batch variant queries (`POST /api/v1/internal/variants`) and variant switch validation (`POST /api/v1/internal/variants/validate-switch`).
- **FR-008**: The system MUST standardize the variant switch route in `CatalogClient` to `api/v1/internal/variants/validate-switch`.
- **FR-009**: The system MUST calculate item chargeable weight in `OrderService.QuoteCheckoutAsync` using the formula:
  $$\text{VolumetricWeight} = (\text{LengthCm} \times \text{WidthCm} \times \text{HeightCm}) / 5$$
  $$\text{ChargeableWeight} = \max(\text{WeightGrams}, \text{VolumetricWeight})$$
  falling back to `WeightGrams` or $200\text{g}$ minimum if dimensions are not available.
- **FR-010**: The system MUST pass the calculated `ChargeableWeight` into `ShippingQuoteItem` for carrier shipping fee calculation via `IFulfillmentClient.CalculateShopShippingFeeAsync`.
- **FR-011**: The system MUST NOT modify the PostgreSQL database schema; all packaging dimension computations MUST remain in-memory and transient during the checkout quoting lifecycle.

---

### Key Entities

- **CatalogVariantDimension**: Transient packaging dimension model representing `LengthCm`, `WidthCm`, `HeightCm`, and `WeightGrams` returned by Catalog Service.
- **ShippingQuoteItem**: Value object sent to Fulfillment Service holding `SkuId`, `Quantity`, and unit `ChargeableWeight` in grams.
- **CatalogMarkOrderedPayload**: Contract payload `{ "variantIds": [Guid] }` sent to Catalog Service.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of newly placed regular orders and auction settlements with valid SKUs trigger a `mark-ordered` sync request with deduplicated variant IDs.
- **SC-002**: 100% of checkout shipping quotes utilize real SKU dimensions and the GHN volumetric formula when dimensions are returned by Catalog Service, replacing the static 200g dummy fallback.
- **SC-003**: 0% order creation or auction settlement failures caused by Catalog Service unavailability or timeouts during `mark-ordered` execution.
- **SC-004**: 100% test pass rate across all unit and integration test suites in `Commerce.UnitTests`.

---

## Assumptions

- Catalog Service endpoint `POST /api/v1/internal/variants/mark-ordered` is idempotent and safe for repeated calls.
- Catalog Service provides dimensions in centimeters ($\text{cm}$) and weight in grams ($\text{g}$).
- Fulfillment Service consumes the unit chargeable weight in grams and calculates the total parcel weight multiplying by quantity.
- Database schema changes are zero; no EF Core migrations are required.
