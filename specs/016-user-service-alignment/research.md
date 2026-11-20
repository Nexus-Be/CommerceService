# Research & Technical Decisions: User Service Alignment

**Feature Directory**: `specs/016-user-service-alignment`  
**Phase**: Phase 0 (Research & Technical Decisions)  
**Status**: Completed  

---

## 1. Research Tasks & Unknowns Resolved

### Research 1: Inter-Service Security & Header Propagation for User Service
* **Context**:
  Calls to `UserService` target internal routes:
  - `GET /api/v1/internal/users/{buyerId}/permission-check?action=buy`
  - `POST /api/v1/internal/users/reputation-adjust`
  - `POST /api/v1/internal/users/penalties`
  These internal endpoints require inter-service authorization.
* **Decision**:
  1. Configure `UserClient` in `Commerce.Infrastructure/DependencyInjection.cs` with the shared secret header:
     `client.DefaultRequestHeaders.Add("X-Internal-Service-Key", internalServiceKey);`
  2. Attach `.AddHttpMessageHandler<Services.Common.IdentityPropagationHandler>()` to propagate ambient user context (`x-user-id`, `x-user-roles`) and W3C distributed trace context (`traceparent`).
* **Rationale**:
  Consistent with existing clients (`CatalogClient`, `FulfillmentClient`, `WalletClient`). Ensures auditing and trace correlation across Grafana Tempo and OpenTelemetry.
* **Alternatives Considered**:
  - *Sending standard JWT Bearer token*: Internal daemon or system operations (e.g. timeout worker) may lack ambient JWT, whereas `X-Internal-Service-Key` guarantees authorized service-to-service execution.

---

### Research 2: Buyer Permission Check Handling & Failure Modes
* **Context**:
  When a buyer calls `POST /api/v1/orders`, `OrderService` must verify purchasing privileges at `UserService`. If the buyer has an active restriction (`Buyer:Restriction` or `Account:BanAccount`) or the account is inactive, `UserService` returns HTTP 403.
* **Decision**:
  - In `UserClient.CheckBuyerPermissionAsync`:
    - On HTTP 200 OK: return `new UserPermissionCheckResult(IsAllowed: true, ErrorCode: null, Message: null)`.
    - On HTTP 403 Forbidden: deserialize response problem details or default to `errorCode: "BUYER_ACTION_RESTRICTED"` and `message: "User has an active restriction prohibiting purchase actions."`.
    - On HTTP 5xx / Network Timeout: log warning and fail closed by throwing `CommerceDomainException("USER_SERVICE_UNAVAILABLE", "User service is temporarily unavailable. Please try again later.", 503)`.
  - In `OrderService.CreateOrderAsync`:
    - If `!permResult.IsAllowed`, throw:
      `new CommerceDomainException("BUYER_ACTION_RESTRICTED", "User has an active restriction prohibiting purchase actions.", 403);`
* **Rationale**:
  Strict fail-closed architecture protects merchants and the platform from unauthorized or fraudulent transactions.
* **Alternatives Considered**:
  - *Fail-open (allowing purchase when UserService is down)*: Rejected per Constitution Principle II (Fail-Closed Governance: never mock or bypass security checks).

---

### Research 3: Dual-Trigger Reputation Adjustment & Idempotency Guarantees
* **Context**:
  Commerce orders are structured as a master `Order` containing one or more shop-specific `SubOrders`. Sub-orders may be delivered at different times.
* **Decision**:
  1. **Sub-Order Confirmation (`confirm-receipt`)**: When a buyer confirms receipt of a specific `SubOrder`, immediately call `AdjustReputationAsync` for that seller using `referenceId: so.SubOrderNo`.
  2. **Master Order Completion (`orders/{orderId}/complete`)**: When the master order is completed, verify all sub-orders are delivered, complete any pending sub-orders, and call `AdjustReputationAsync` for any seller not yet rewarded, using `referenceId: orderNo`.
  3. **Idempotency & Anti-Farming Shield**: `UserService` handles deduplication (`isDuplicate: true, appliedDelta: 0`) and 7-day anti-farming cooldown (`cooldownSuppressed: true, appliedDelta: 0`). `CommerceService` safely logs these responses without throwing errors.
* **Rationale**:
  Provides immediate gratification for sellers upon delivery while ensuring master order completion remains fully idempotent and resilient.
* **Alternatives Considered**:
  - *Only rewarding on master order completion*: Creates poor UX for sellers whose items arrive days before other items in a multi-shop basket.

---

### Research 4: Seller Cancellation Violation Detection
* **Context**:
  When a seller cancels a sub-order or rejects an order after having accepted/confirmed it, if the cancellation is unjustified (code `CancellationWithoutReason`), `UserService` must be notified to penalize the seller (-5 score).
* **Decision**:
  1. Expand `CancellationReasonCode` with `CancellationWithoutReason = 7`.
  2. In `OrderService.CancelOrderAsync`:
     - If the initiator is the seller of a sub-order within the order:
       - Check if `so.Status == SubOrderStatus.Confirmed || so.Status == SubOrderStatus.Paid`.
       - If `request.ReasonCode == CancellationReasonCode.CancellationWithoutReason` or `request.ReasonCode == CancellationReasonCode.SellerRejected`:
         Invoke `_userClient.CreatePenaltyAsync` with:
         - `targetRole: "Seller"`
         - `violationType: "CancellationWithoutReason"`
         - `customScoreDeduction: 5`
         - `policyClauseCode: "POLICY-SEC-3.2"`
         - `referenceType: "Order"`
         - `referenceId: so.SubOrderNo`
* **Rationale**:
  Automates enforcement of Platform Seller Quality Policies without requiring manual support interventions.
