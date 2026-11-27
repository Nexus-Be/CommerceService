# Tasks: Route Alignment, Gateway Parity & Compiler Warning Hygiene

**Input**: Design documents from `specs/037-route-alignment-and-code-hygiene/`  
**Prerequisites**: [plan.md](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/plan.md), [spec.md](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/spec.md), [research.md](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/research.md), [data-model.md](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/data-model.md), [contracts/](file:///d:/CommerceService/specs/037-route-alignment-and-code-hygiene/contracts/)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (`[US1]`, `[US2]`, `[US3]`)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Baseline verification of repository state and active branch

- [x] T001 Verify branch status and build baseline in `Commerce.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Baseline safety checks and external Gateway routing artifact preparation

- [x] T002 [P] Verify carrier cancellation unit test suite passes in `tests/Commerce.UnitTests/Infrastructure/FulfillmentClientCancelTests.cs`
- [x] T003 [P] Verify YARP Gateway reverse proxy routing specification in `specs/037-route-alignment-and-code-hygiene/gateway-routes-spec.json`

**Checkpoint**: Foundation ready - User story implementation can begin in parallel.

---

## Phase 3: User Story 1 - Canonical Order Shipments Route Alignment (Priority: P1) 🎯 MVP

**Goal**: Migrate master order shipment tracking to hierarchical REST route `/api/v1/orders/{orderId}/shipments` to resolve API Gateway collision with FulfillmentService, while keeping a backward-compatible alias.

**Independent Test**: Call `GET /api/v1/orders/{orderId}/shipments` with a valid buyer JWT token and verify HTTP 200 OK with `ShipmentTrackingDto` payload; call `GET /api/v1/shipments/order/{orderId}` and verify identical response.

### Tests for User Story 1
- [x] T004 [P] [US1] Add unit tests verifying route mapping and response handling for `GetShipmentTracking` in `tests/Commerce.UnitTests/Controllers/OrdersControllerShipmentTrackingRouteTests.cs`

### Implementation for User Story 1
- [x] T005 [US1] Update `OrdersController.cs` line 246 with canonical route `[HttpGet("{orderId:guid}/shipments")]` and secondary legacy alias `[HttpGet("/api/v1/shipments/order/{orderId:guid}", Order = 2)]` in `Commerce.Api/Controllers/Public/OrdersController.cs`

**Checkpoint**: User Story 1 complete. Shipment tracking can be called without 404 collisions on Gateway.

---

## Phase 4: User Story 2 - Compiler Warnings Elimination & Code Hygiene (Priority: P1)

**Goal**: Eliminate all 6 compiler warnings (CS8604, CS8601, CS0108) across `AuctionOrderService`, `OrderService`, and `AdminRefundFundCasesController`.

**Independent Test**: Run `dotnet build Commerce.sln -c Release` and assert 0 Warnings, 0 Errors.

### Implementation for User Story 2
- [x] T006 [P] [US2] Resolve CS8604 in `AuctionOrderService.cs` line 1374 by applying null-coalescing `DistrictName: rawAddress.District ?? string.Empty` in `Commerce.Application/Services/AuctionOrder/AuctionOrderService.cs`
- [x] T007 [P] [US2] Resolve CS8601 in `OrderService.cs` lines 3900, 3904, 4023, 4027 by applying null-coalescing `?? string.Empty` for `District` and `GhnWardCode` in `Commerce.Application/Services/Order/OrderService.cs`
- [x] T008 [P] [US2] Resolve CS0108 in `AdminRefundFundCasesController.cs` line 57 by adding `new` modifier to `Request` action method in `Commerce.Api/Controllers/Admin/AdminRefundFundCasesController.cs`

**Checkpoint**: User Story 2 complete. Solution compiles with zero warnings.

---

## Phase 5: User Story 3 - API Documentation Synchronization (Priority: P2)

**Goal**: Update all internal documentation and contract catalogs to reflect the canonical `/api/v1/orders/{orderId}/shipments` endpoint.

**Independent Test**: Search for `/api/v1/shipments/order/{orderId}` across `docs/` and verify canonical references point to `/api/v1/orders/{orderId}/shipments`.

### Implementation for User Story 3
- [x] T009 [P] [US3] Update route endpoint and descriptions in `docs/api/Orders/GET_SHIPMENTS_ORDER_TRACKING.md`
- [x] T010 [P] [US3] Update API summary table row for master order shipments tracking in `docs/api/README.md`
- [x] T011 [P] [US3] Update endpoint catalog entry ORD-08 in `docs/api_catalog.md`
- [x] T012 [P] [US3] Update contract table entry ORD-08 / R87 in `docs/COMMERCE_API_CONTRACT.md`
- [x] T013 [P] [US3] Update architecture specification table in `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md`

**Checkpoint**: User Story 3 complete. 100% documentation parity achieved.

---

## Phase 6: Polish & Verification

**Purpose**: Solution-wide build and regression verification

- [x] T014 Execute full Release build `dotnet build Commerce.sln -c Release` and assert 0 Warnings and 0 Errors
- [x] T015 Execute full automated test suite `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release` and assert 100% pass rate (698 passed, 0 failed)

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: Independent.
- **Foundational (Phase 2)**: Depends on Phase 1; verifies pre-existing safety nets.
- **User Story 1 (Phase 3)**: Independent implementation of canonical route.
- **User Story 2 (Phase 4)**: Independent code hygiene fixes (can run in parallel with US1).
- **User Story 3 (Phase 5)**: Documentation sync (runs after US1 endpoint finalized).
- **Polish (Phase 6)**: Depends on all user stories completed.

### Parallel Opportunities
- Tasks T002, T003 in Phase 2 ran in parallel.
- Tasks T006, T007, T008 in Phase 4 touched distinct files and ran in parallel.
- Tasks T009 through T013 in Phase 5 touched distinct documentation files and ran in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 & 2)
1. Executed T004 - T005 to unblock shipment tracking through Gateway.
2. Executed T006 - T008 to clean all compiler warnings.
3. Validated build with T014 (0 warnings, 0 errors).
4. Executed T009 - T013 for documentation synchronization.
5. Final regression validation with T015 (698 passed, 0 failed).
