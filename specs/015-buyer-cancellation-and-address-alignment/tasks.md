---
description: "Task list for Buyer Order Cancellation with Automated Wallet Refund & 2-Tier Administrative Address Normalization"
---

# Tasks: Buyer Order Cancellation & 2-Tier Address Normalization

**Input**: Design documents from `/specs/015-buyer-cancellation-and-address-alignment/`  
**Prerequisites**: `spec.md` (required), `plan.md` (required)  
**Tests**: Automated Unit Tests are required per Constitution Principle V and Quality Gate DoD.  
**Organization**: Tasks are grouped by phase and user story to enable independent implementation and testing.

## Format: `[TaskID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: User story identifier ([US1], [US2], [US3], [US4])
- Exact file paths are specified for every task.

---

## Phase 1: Foundational Domain & Contract Adjustments

**Purpose**: Update domain value objects and request records to support nullable district and smart address formatting.

- [X] T001 [P] [US2] Update `ShippingAddressSnapshot.cs` constructor to accept `string? district`, default to `string.Empty`, and concatenate `FullAddressText` using `string.Join(", ", parts.Where(!string.IsNullOrWhiteSpace))` in `Commerce.Domain/ValueObjects/ShippingAddressSnapshot.cs`
- [X] T002 [P] [US2] Update `ShippingAddressInput` record to declare `string? District = null` with backward-compatible default parameters in `Commerce.Application/Services/Order/OrderRequests.cs`

---

## Phase 2: Application Layer Logic Implementation

**Purpose**: Realign checkout validation and expand order cancellation logic with automated wallet refund.

- [X] T003 [US2] Update `QuoteCheckoutAsync` address validation in `Commerce.Application/Services/Order/OrderService.cs` to remove the mandatory check for `District` and validate only 5 required fields (`ReceiverName`, `ContactPhone`, `Street`, `Province`, `Ward`)
- [X] T004 [US2] Update `CreateOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to pass `cachedQuote.Request.ShippingAddress.District ?? string.Empty` when instantiating `ShippingAddressSnapshot`
- [X] T005 [US1] Update `CancelOrderAsync` in `Commerce.Application/Services/Order/OrderService.cs` to:
  - Allow cancellation when `order.Status == OrderStatus.PendingPayment || order.Status == OrderStatus.Paid`
  - Reject cancellation with HTTP 400 `ORDER_CANNOT_BE_CANCELLED` if any sub-order has `Status == Shipping`, `Delivered`, or `Completed`
  - Set `SubOrder.Status = Cancelled`, record `OrderCancellation` (`WasPaid = wasPaid`, `InventoryReleased = true`)
  - When `wasPaid == true`, create `OrderRefund` (Status: `Requested`, `ReasonCode = "BUYER_CANCELLED"`) and invoke `_walletClient.RefundSubOrderPaymentAsync(...)` with `idempotencyKey: $"refund-suborder-cancel-{so.Id}"`, updating to `Completed` on success
  - Release warehouse stock via `_fulfillmentClient.ReleaseStockAsync(order.Id, ct)`

---

## Phase 3: API Layer & Controller Ergonomics

**Purpose**: Expand HTTP verbs and ergonomic request body defaults on public endpoints.

- [X] T006 [US3] Add `[HttpPut("{orderId:guid}/cancel")]` attribute alongside `[HttpPost]`, support optional `CancelOrderRequest? request = null`, and supply default `BuyerChangedMind` request in `Commerce.Api/Controllers/Public/OrdersController.cs`

---

## Phase 4: Unit Test Suite Realignment & Verification

**Purpose**: Update automated test assertions and verify solution stability.

- [X] T007 [US4] Update `tests/Commerce.UnitTests/Application/OrderLifecycleTests.cs`:
  - Replace `CancelOrderAsync_ShouldThrowException_WhenOrderIsAlreadyPaid` with `CancelOrderAsync_ShouldCancelOrderAndRefund_WhenOrderIsPaidAndNotShipped` (asserting `RefundSubOrderPaymentAsync` and `ReleaseStockAsync` are called)
  - Add test `CancelOrderAsync_ShouldThrowException_WhenSubOrderIsAlreadyShipped`
- [X] T008 [US4] Verify solution compilation with `dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false` (0 warnings, 0 errors)
- [X] T009 [US4] Execute test suite and confirm 100% pass rate
