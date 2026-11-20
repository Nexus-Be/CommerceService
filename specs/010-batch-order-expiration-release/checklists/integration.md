# Inter-Service API Contract & Integration Checklist: 010-batch-order-expiration-release

**Purpose**: Validate requirements quality, contract clarity, schema parity, and distributed exception coverage for CommerceService ↔ FulfillmentService integration (Phương án 3: The Golden Standard).  
**Created**: 2026-11-03  
**Feature**: [spec.md](../spec.md)  
**Target Audience**: PR Reviewers, System Architects & Inter-Service Handoff Leads  
**Review Ownership**: This checklist is a reviewer-owned requirements-quality review artifact. Mark an item `[x]` only when the reviewer determines the requirements-quality criterion is satisfied.  
**Marker Semantics**: `[x]` means the criterion has been reviewed and satisfied for requirements quality. It does NOT mean implementation work is complete.

---

## 1. Requirement Completeness

- [ ] CHK001 Are exact HTTP methods, URL route templates, and content-type headers explicitly specified for all 4 outbound fulfillment endpoints (`by-skus`, `reservations`, `commit`, `release`)? [Completeness, Spec §US1, §TR-003]
- [ ] CHK002 Does the spec specify whether `holdMinutes` has an upper or lower bounding constraint (e.g., minimum 1 minute, maximum 60 minutes) when creating inventory reservations? [Completeness, Gap, Spec §US1 AC2]
- [ ] CHK003 Are the expected HTTP response status codes and body schemas documented for both success and business rejection on `POST api/v1/internal/inventories/reservations` (e.g., `400 Bad Request` vs `409 Conflict / Insufficient Stock`)? [Completeness, Spec §US1 AC2]
- [ ] CHK004 Does the specification document the required behavior when the input list of `skuIds` is empty for `BatchCheckStockAsync`? [Completeness, Spec §US1 AC1]
- [ ] CHK005 Are the exact payload requirements and allowed values for the `reason` query parameter documented for both order-level and suborder-level releases (`OrderCancelled`, `SubOrderRejected`)? [Completeness, Spec §US1 AC5]

## 2. Requirement Clarity & Precision

- [ ] CHK006 Is the model binding format for `POST api/v1/inventories/by-skus` explicitly clarified as a raw JSON array (`["guid1", "guid2"]`) rather than an object wrapper (`{ "skuIds": [...] }`)? [Clarity, Spec §Problem Statement, §US1 AC1]
- [ ] CHK007 Is the commit endpoint route explicitly defined with `orderId` in the URL path (`/{orderId}/commit`) and an empty HTTP request body to eliminate body-binding ambiguity? [Clarity, Spec §US1 AC3]
- [ ] CHK008 Is the header key name `X-Internal-Service-Key` specified with exact case-sensitivity and delivery mechanism across all internal routes? [Clarity, Spec §TR-001, §TR-002]
- [ ] CHK009 Are the exact RFC 9110 error codes (`STOCK_CHECK_UNAVAILABLE`, `STOCK_RESERVATION_FAILED`, `STOCK_RELEASE_UNAVAILABLE`) and HTTP status codes (`503 Service Unavailable`) explicitly specified when warehouse communication fails in fail-closed mode? [Clarity, Spec §US1 AC6, §TR-006]

## 3. Requirement Consistency & Schema Parity

- [ ] CHK010 Do the request DTO property names in Commerce (`referenceId`, `holdMinutes`, `items.skuId`, `items.quantity`) match the exact JSON property naming expected by Fulfillment's `InternalInventoryReservationsController`? [Consistency, Spec §US1 AC2, Plan §Phase 2]
- [ ] CHK011 Is the term `referenceId` consistently defined across both services as the Commerce `OrderId` (UUID string format)? [Consistency, Spec §US1 AC2, DataModel §1.2]
- [ ] CHK012 Does the delivery status webhook contract define consistent enum values (`Delivered`, `Shipped`, `Cancelled`) between Commerce's `SubOrderStatus` and Fulfillment's shipment event lifecycle? [Consistency, Spec §US3 AC2, Contract §delivery-status-contract]
- [ ] CHK013 Are the architectural boundaries between Commerce and Fulfillment consistent with the project's Inward-Only and Zero-Broker governance rules (no gRPC, no MassTransit/RabbitMQ)? [Consistency, Spec §TR-006, Constitution §V]

## 4. Distributed Exception & Failure Recovery Coverage

- [ ] CHK014 Are retry policies for `CommitStockAsync` specified with explicit retry counts (3 attempts), backoff intervals (200ms, 400ms, 800ms), and qualifying exception filters (transient network drops vs non-retryable 4xx client errors)? [Recovery, Spec §US1 AC4, Clarifications Q3]
- [ ] CHK015 Is the safe idempotency expectation on Fulfillment's commit endpoint documented so that redundant retries on an already-committed reservation return `200 OK` rather than throwing errors? [Resilience, Spec §Clarifications Q3]
- [ ] CHK016 Does the specification clearly define the boundary between transient commit retry (handled synchronously in request/job) and permanent failure handling (flagged for background reconciliation)? [Recovery, Ambiguity, Spec §Clarifications Q3]
- [ ] CHK017 Is the behavior of `PaymentTimeoutWorker` explicitly defined to perform zero HTTP calls on payment expiration, relying solely on Fulfillment's independent `AutoExpireReservationsAsync` background job? [Resilience, Spec §US2 AC3, Clarifications Q1]
- [ ] CHK018 Does the spec define what happens if a delivery status callback arrives out of order (e.g., `Delivered` received before `Shipped`) or if a duplicate webhook request is delivered multiple times? [Edge Case, Gap, Spec §US3]
- [ ] CHK019 Does the specification define whether the delivery status webhook endpoint should reject requests for SubOrders that are already in a terminal state (e.g., `Cancelled` or `Refunded`)? [Edge Case, Gap, Spec §US3]

## 5. Security & Authorization Governance

- [ ] CHK020 Is the HTTP status code (`403 Forbidden`) and error response body format (`FORBIDDEN` error code and English message) explicitly defined when `X-Internal-Service-Key` is missing or invalid on the delivery callback? [Security, Spec §US3 AC1, §TR-005]
- [ ] CHK021 Is the storage and injection mechanism for `X-Internal-Service-Key` specified using environment variables and configuration files (`appsettings.json`, `InternalSecurity:ServiceKey`) rather than hardcoded credentials? [Security, Spec §TR-001, §TR-002]
- [ ] CHK022 Does the specification define whether the delivery callback endpoint requires user JWT authentication or strictly relies on the internal service key? [Security, Spec §US3, Contract §delivery-status-contract]

## 6. Acceptance Criteria Quality & Measurability

- [ ] CHK023 Can the "Zero-HTTP calls on timeout" requirement for `PaymentTimeoutWorker` be objectively verified via test assertions (e.g., verifying mock HTTP client invocations are `Times.Never`)? [Measurability, Spec §US2 AC3, §TR-007]
- [ ] CHK024 Can the single database transaction requirement (`await dbContext.SaveChangesAsync()` called exactly once per batch) be objectively measured under test? [Measurability, Spec §US2 AC2]
- [ ] CHK025 Are the performance boundaries for batch order cancellation specified with a concrete batch size upper bound (e.g., $N \le 50$)? [Clarity, Spec §US2 AC1]

---

## Notes

- Mark items `[x]` only after reviewer confirms the requirements-quality criterion is satisfied.
- Leave items unchecked (`[ ]`) when they still require clarification, correction, or reviewer evaluation.
- `/speckit-implement` reads checklist checkbox state as a review gate and must not modify markers.
- `checklists/requirements.md` has a separate built-in lifecycle maintained by `/speckit-specify` and `/speckit-clarify`.
- Traceability: 100% of items (25/25) include domain quality dimensions, section references, or gap markers.
