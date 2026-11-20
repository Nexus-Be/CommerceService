# Inter-Service API & Resilience Quality Checklist: Catalog and Commerce Contract Alignment

**Purpose**: Reviewer-owned requirements-quality checklist validating the completeness, clarity, consistency, and measurability of inter-service API integration and resilience contracts between CatalogService and CommerceService.  
**Created**: 2026-11-03  
**Feature**: [spec.md](../spec.md)  
**Target Reviewer**: Code Reviewer / Tech Lead (PR Review Gate)  
**Review Depth**: Standard Review  

**Review Ownership**: This checklist is a reviewer-owned requirements-quality review artifact. Mark an item `[x]` only when the reviewer determines the requirements-quality criterion is satisfied.  
**Marker Semantics**: `[x]` means the criterion has been reviewed and satisfied for requirements quality. It does not mean implementation work is complete.  

---

## 1. Requirement Completeness (API Contracts & Failure Modes)

- [ ] CHK001 Are exact endpoint paths, HTTP verbs, and payload schema structures documented for all inter-service Catalog calls? [Completeness, Spec §FR-004, §FR-007, §FR-008]
- [ ] CHK002 Are explicit requirements documented for handling nullable prices (`decimal? Price`) from Catalog without throwing deserialization or null reference exceptions? [Completeness, Spec §FR-002, Edge Cases]
- [ ] CHK003 Are RFC 9110 Problem Details response structures (`type`, `title`, `status`, `errorCode`, `detail`, `traceId`) defined for all Catalog failure modes? [Completeness, Spec §FR-004]
- [ ] CHK004 Are chunking boundaries specified for batch SKU lookups exceeding 100 items? [Completeness, Spec §FR-008, Edge Cases]
- [ ] CHK005 Are fallback-free fail-closed requirements specified for all service consumer touchpoints (cart retrieval, validate cart, checkout quote, order creation)? [Completeness, Spec §FR-003, §FR-004]

---

## 2. Requirement Clarity (Resilience Thresholds & Contract Semantics)

- [ ] CHK006 Is the Fail-Closed HTTP status code explicitly quantified as HTTP 503 `CATALOG_SERVICE_UNAVAILABLE` rather than generic 500? [Clarity, Spec §FR-004, Clarifications]
- [ ] CHK007 Is the fallback value for unpriced items (`null` price) explicitly defined as `0m` VND rather than left to language defaults? [Clarity, Spec §FR-002]
- [ ] CHK008 Is the Single Source of Truth rule for `IsPurchasable` explicitly defined to forbid local price-based overrides (`price > 0`)? [Clarity, Spec §FR-001]
- [ ] CHK009 Is the variant switch rejection contract unambiguous regarding parent product identity and seller identity validation? [Clarity, Spec §FR-007, Spec §SC-003]
- [ ] CHK010 Is the exact error code for parent product mismatch specified as `CATALOG_VARIANT_MISMATCH` with HTTP 400? [Clarity, Spec §FR-007]

---

## 3. Requirement Consistency (Cross-Service Alignment & Governance)

- [ ] CHK011 Do the error handling requirements for `CatalogClient` align with system-wide 3-part rules (100% English sentence case, SCREAMING_SNAKE_CASE)? [Consistency, Constitution §IV, Spec §FR-004]
- [ ] CHK012 Is the precedence hierarchy for cart item availability (`PRODUCT_NOT_FOUND` > `PRODUCT_INACTIVE` > `OUT_OF_STOCK`) consistently specified across single cart, grouped cart, and cart validation? [Consistency, Spec §FR-006, Clarifications]
- [ ] CHK013 Does the variant switch validation specification consistently forbid cross-seller variant switching across both CatalogService and CommerceService? [Consistency, Spec §FR-007]
- [ ] CHK014 Do the batch variant retrieval requirements align with the OpenAPI specification in `catalog_swagger.json`? [Consistency, Spec §FR-008, Contracts]

---

## 4. Scenario & Edge Case Coverage (Network, State & Concurrency)

- [ ] CHK015 Are requirements specified for CatalogService connection timeout, connection refused, and HTTP 5xx responses? [Coverage, Spec §FR-004, Edge Cases]
- [ ] CHK016 Are requirements defined for partial batch responses where some requested SKU IDs are missing from the Catalog response? [Coverage, Spec §FR-006, Edge Cases]
- [ ] CHK017 Are requirements specified for concurrent execution of Fulfillment stock checking and Catalog purchasability verification? [Coverage, Spec §FR-005, Spec §SC-003]
- [ ] CHK018 Are requirements specified for instant database persistence of price snapshots during cart validation (`POST /api/v1/carts/me/validate`)? [Coverage, Spec §FR-009, Clarifications]
- [ ] CHK019 Are requirements specified for asymmetric price drift (silent auto-apply for price drops vs. 409 `PRICE_CHANGED` for price increases) during order checkout? [Coverage, Spec §FR-010, Spec §SC-004]
- [ ] CHK020 Are requirements specified for auction order items having 0 or null initial price snapshots without triggering standard price drift blocks? [Coverage, Spec §FR-010, Assumptions]

---

## 5. Acceptance Criteria Measurability (Verification & Testability)

- [ ] CHK021 Can the "0% synthetic mock data" requirement be objectively verified through automated contract and unit tests? [Measurability, Spec §SC-002, Spec §SC-005]
- [ ] CHK022 Is the "concurrent check" requirement measurable in terms of parallel `Task.WhenAll` invocation rather than sequential blocking calls? [Measurability, Spec §SC-003, Spec §FR-005]
- [ ] CHK023 Are the acceptance criteria for real-time cart gray-out testable without requiring live external network infrastructure? [Measurability, Spec §SC-001, Spec §SC-005]

---

## 6. Dependencies & Assumptions (Service Boundaries & Protocols)

- [ ] CHK024 Is the boundary responsibility between CatalogService (product master, pricing, status) and FulfillmentService (warehouse stock, reservations) clearly documented? [Dependencies, Assumptions]
- [ ] CHK025 Are authentication header requirements (`X-Internal-Service-Key` or public route conventions) documented for all inter-service REST clients? [Dependencies, Assumptions, Constitution §III]
- [ ] CHK026 Is the circuit-breaking or retry expectation for outbound Catalog HTTP calls documented to prevent cascading failure? [Gap, Non-Functional]

---

## Notes

- Mark items `[x]` only after review confirms the requirement-quality criterion is satisfied.
- Leave items unchecked (`[ ]`) when they still require clarification, correction, or reviewer evaluation.
- `/speckit-implement` reads checklist checkbox state as a gate and must not modify markers.
- `checklists/requirements.md` has a separate built-in lifecycle maintained by `/speckit-specify` and `/speckit-clarify`.
- Add comments or findings inline.
- Items are numbered sequentially (`CHK001` - `CHK026`) for easy reference.
