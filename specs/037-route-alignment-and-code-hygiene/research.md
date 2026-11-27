# Phase 0 Research: Technical Decisions & Architectural Analysis

**Feature**: `037-route-alignment-and-code-hygiene`  
**Date**: 2026-11-26  
**Status**: Completed  

---

## 1. Decision: Canonical Route Migration vs Alias Preservation

### Context
`OrdersController.cs:246` currently exposes:
```csharp
[HttpGet("/api/v1/shipments/order/{orderId:guid}")]
public async Task<IActionResult> GetShipmentTracking([FromRoute] Guid orderId, CancellationToken ct)
```
On API Gateway (port `8080`), all requests with prefix `/api/v1/shipments/**` are routed to `FulfillmentService` (port `8006`). Consequently, external clients calling this route receive `HTTP 404 Not Found` from FulfillmentService instead of CommerceService.

### Decision
Migrate the endpoint to the canonical RESTful hierarchical route on `OrdersController`:
```csharp
[HttpGet("{orderId:guid}/shipments")]
[HttpGet("/api/v1/shipments/order/{orderId:guid}", Order = 2)] // Legacy alias for backward compatibility
public async Task<IActionResult> GetShipmentTracking([FromRoute] Guid orderId, CancellationToken ct)
```

### Rationale
1. **RESTful Resource Hierarchy**: The `Order` is the Aggregate Root. Shipments and fulfillment carrier progress belong directly to that order (`/api/v1/orders/{orderId}/shipments`).
2. **Gateway Route Compatibility**: The API Gateway already maps `/api/v1/orders/**` directly to CommerceService (port `8003`). No special Gateway prefix override or complex regex matching is required.
3. **Zero Downtime & Zero Broken Callers**: Preserving `[HttpGet("/api/v1/shipments/order/{orderId:guid}")]` as a secondary route ensures existing in-cluster callers or automated integration scripts continue functioning without disruption.

### Alternatives Considered
- *Alternative A: Reconfigure Gateway with high-priority exact match rule for `/api/v1/shipments/order/**` to Commerce*.
  - *Rejected because*: Violates REST conventions (Shipments root controller in Commerce); creates fragile regex precedence rules in Gateway prone to regressions when new shipment endpoints are added in FulfillmentService.
- *Alternative B: Remove the legacy route immediately without alias*.
  - *Rejected because*: Could break existing clients or unmigrated tests before full deployment.

---

## 2. Decision: Compiler Nullability Warnings Remediation Pattern

### Context
During `dotnet build Commerce.sln -c Release`, 5 nullability warnings are emitted:
1. `AuctionOrderService.cs(1374,31)`: `CS8604` - `rawAddress.District` (`string?`) passed to parameter `DistrictName` (`string`) in `ShippingAddressDto`.
2. `OrderService.cs(3900,24)`: `CS8601` - `resolvedAddress.District` (`string?`) assigned to `ShippingAddressSnapshot.District` (`string`).
3. `OrderService.cs(3904,27)`: `CS8601` - `resolvedAddress.GhnWardCode` (`string?`) assigned to `ShippingAddressSnapshot.GhnWardCode` (`string`).
4. `OrderService.cs(4023,28)`: `CS8601` - `resolvedAddress.District` (`string?`) assigned to `ShippingAddressSnapshot.District` (`string`).
5. `OrderService.cs(4027,31)`: `CS8601` - `resolvedAddress.GhnWardCode` (`string?`) assigned to `ShippingAddressSnapshot.GhnWardCode` (`string`).

### Decision
Apply deterministic null-coalescing fallback `?? string.Empty` at data mapping boundaries:
```csharp
// AuctionOrderService.cs
DistrictName: rawAddress.District ?? string.Empty,

// OrderService.cs
District = resolvedAddress.District ?? string.Empty,
Province = resolvedAddress.Province ?? string.Empty,
GhnWardCode = resolvedAddress.GhnWardCode ?? string.Empty,
```

### Rationale
- `ShippingAddressSnapshot` properties are defined as non-nullable `string` with default `= string.Empty;`.
- Fallback to `string.Empty` prevents potential `NullReferenceException` when downstream code formats `FullAddressText` or sends payloads to carrier APIs.
- Domain validation prior to persistence already asserts valid `GhnDistrictId` and `GhnWardCode`, so fallback only handles unpopulated optional text inputs safely.

### Alternatives Considered
- *Alternative A: Null-forgiving operator `!` (`resolvedAddress.District!`)*.
  - *Rejected because*: Suppresses the warning without providing runtime safety. If null occurs, it can cause runtime failures.
- *Alternative B: Changing `ShippingAddressSnapshot` properties to nullable (`string?`)*.
  - *Rejected because*: Changes database mapping contract, potentially requiring EF Core migrations and risking null reference bugs across multiple layers.

---

## 3. Decision: Compiler Member Hiding Remediation (CS0108)

### Context
`AdminRefundFundCasesController.cs(57,38)` emits `CS0108`:
`'AdminRefundFundCasesController.Request(RequestRefundFundCaseRequest, CancellationToken)' hides inherited member 'ControllerBase.Request'. Use the new keyword if hiding was intended.`

### Decision
Add the `new` keyword modifier to the controller action method:
```csharp
[HttpPost]
public new async Task<IActionResult> Request([FromBody] RequestRefundFundCaseRequest request, CancellationToken ct)
```

### Rationale
- The action method `Request` is bound to `POST /api/v1/admin/refund-fund/cases`.
- Adding `new` explicitly tells the C# compiler that hiding the base `ControllerBase.Request` property is intentional.
- Zero change to HTTP routing, action signature, or OpenAPI operation behavior.

### Alternatives Considered
- *Alternative A: Rename action to `CreateRequest` or `SubmitCase`*.
  - *Rejected because*: Unnecessarily changes code identifiers and internal Swagger operation IDs.

---

## 4. Decision: Gateway Routing Artifact Isolation

### Context
Problem 1.2 reported that Gateway lacks routes for Commerce sub-resources (`checkouts`, `sub-orders`, `auction-orders`, `discounts`, `refunds`, `admin`). CommerceService is an Inward-Only repository and cannot modify files outside its root.

### Decision
Provide `specs/037-route-alignment-and-code-hygiene/gateway-routes-spec.json` containing complete YARP route and cluster definitions for CommerceService.

### Rationale
- Maintains 100% adherence to Constitution Principle V (Inward-Only).
- Gives DevOps and Gateway engineers a drop-in, tested JSON configuration snippet to apply directly to `Gateway/appsettings.json`.
